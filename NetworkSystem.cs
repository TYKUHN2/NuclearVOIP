using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using NuclearOption.Networking;
using Steamworks;
using UnityEngine;

namespace NuclearVOIP
{
    internal class NetworkSystem: MonoBehaviour, INetworkSystem
    {
        private const float INTERVAL = 0.02f;

        private readonly int channel = Plugin.Instance.configVOIPPort.Value;
        private readonly List<SteamNetworkingIdentity> connections = [];
        private float elapsed = 0;

        private readonly Callback<SteamNetworkingMessagesSessionRequest_t> sessionReq;
        private readonly Callback<SteamNetworkingMessagesSessionFailed_t> messageFailed;

        public event Action<CSteamID>? NewConnection;
        public event Action<CSteamID, byte[]>? OnPacket;
        public event Action<CSteamID>? ConnectionLost;
        public event Action<NetworkStatus, NetworkStatus>? OnNetworkMeasurement;

        NetworkSystem()
        {
            sessionReq = Callback<SteamNetworkingMessagesSessionRequest_t>.Create(OnSession);
            messageFailed = Callback<SteamNetworkingMessagesSessionFailed_t>.Create(OnDisconnect);

            Player[] players = [..UnitRegistry.playerLookup.Values];
            foreach (Player player in players)
            {
                if (player == GameManager.LocalPlayer)
                    continue;

                SteamNetworkingIdentity target = new()
                {
                    m_eType = ESteamNetworkingIdentityType.k_ESteamNetworkingIdentityType_SteamID
                };
                target.SetSteamID64(player.SteamID);

                SteamNetworkingMessages.SendMessageToUser(ref target, IntPtr.Zero, 0, 9, channel);
                connections.Add(target);

                player.Identity.OnStopClient.AddListener(() => { OnDisconnect(target); });
            }
        }

        ~NetworkSystem()
        {
            foreach (SteamNetworkingIdentity identity in connections)
            {
                SteamNetworkingIdentity copy = identity;
                SteamNetworkingMessages.CloseSessionWithUser(ref copy);
            }
        }

        private void FixedUpdate()
        {
            elapsed += Time.fixedDeltaTime;

            if (elapsed >= INTERVAL)
            {
                elapsed -= INTERVAL;

                List<float> qualities = new(connections.Count);
                List<int> pings = new(connections.Count);
                List<int> bandwidths = new(connections.Count);

                List<float> teamQualities = [];
                List<int> teamPings = [];
                List<int> teamBandwidths = [];

                for (int i = 0; i < connections.Count; i++)
                {
                    SteamNetworkingIdentity identity = connections[i];
                    Player peer = UnitRegistry.playerLookup
                        .Where(a => a.Value.SteamID == identity.GetSteamID64())
                        .First()
                        .Value;

                    if (ChatManager.IsMuted(peer))
                    {
                        SteamNetworkingMessages.CloseChannelWithUser(ref identity, channel);

                        ConnectionLost?.Invoke(identity.GetSteamID());
                    } 
                    else
                    {
                        ESteamNetworkingConnectionState state = SteamNetworkingMessages.GetSessionConnectionInfo(
                        ref identity,
                        out SteamNetConnectionInfo_t info,
                        out SteamNetConnectionRealTimeStatus_t status
                        );

                        if (state == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer ||
                            state == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally)
                        {
                            OnDisconnect(identity);

                            continue;
                        }

                        if (peer.HQ == GameManager.LocalFactionHQ)
                        {
                            teamQualities.Add(Math.Min(status.m_flConnectionQualityLocal, status.m_flConnectionQualityRemote));
                            teamPings.Add(status.m_nPing);
                            teamBandwidths.Add(status.m_nSendRateBytesPerSecond);
                        }

                        qualities.Add(Math.Min(status.m_flConnectionQualityLocal, status.m_flConnectionQualityRemote));
                        pings.Add(status.m_nPing);
                        bandwidths.Add(status.m_nSendRateBytesPerSecond);
                    }

                    if (teamQualities.Count == 0)
                    {
                        teamQualities.Add(1);
                        teamPings.Add(0);
                        teamBandwidths.Add(0);
                    }

                    if (qualities.Count == 0)
                    {
                        qualities.Add(1);
                        pings.Add(0);
                        bandwidths.Add(0);
                    }

                    NetworkStatus teamStatus = new()
                    {
                        avgQuality = teamQualities.Average(),
                        minQuality = teamQualities.Min(),

                        avgPing = (int)teamPings.Average(),
                        maxPing = teamPings.Max(),

                        avgBandwidth = (int)teamBandwidths.Average(),
                        minBandwidth = teamBandwidths.Min()
                    };

                    NetworkStatus allStatus = new()
                    {
                        avgQuality = qualities.Average(),
                        minQuality = qualities.Min(),

                        avgPing = (int)pings.Average(),
                        maxPing = pings.Max(),

                        avgBandwidth = (int)bandwidths.Average(),
                        minBandwidth = bandwidths.Min()
                    };

                    OnNetworkMeasurement?.Invoke(allStatus, teamStatus);
                }
            }

            int cPlayers = UnitRegistry.playerLookup.Count;
            IntPtr[] pointers = new IntPtr[cPlayers];

            while (true)
            {
                List<(SteamNetworkingIdentity, byte[])> packets = [];
                int received = SteamNetworkingMessages.ReceiveMessagesOnChannel(channel, pointers, cPlayers);
                for (int i = 0; i < received; i++)
                {
                    unsafe
                    {
                        SteamNetworkingMessage_t* message = (SteamNetworkingMessage_t*)pointers[i];

                        if (message->m_cbSize != 0) // Just a stupid handshake
                        {
                            byte[] packet = new byte[message->m_cbSize];
                            Marshal.Copy(message->m_pData, packet, 0, packet.Length);

                            packets.Add((message->m_identityPeer, packet));
                        }

                        SteamNetworkingMessage_t.Release((IntPtr)message);
                    }
                }

                if (OnPacket == null)
                    return;

                foreach ((SteamNetworkingIdentity from, byte[] packet) in packets)
                    OnPacket.Invoke(from.GetSteamID(), packet);

                if (received < cPlayers)
                    break;
            }
        }

        public void Disconnect(CSteamID player)
        {
            SteamNetworkingIdentity conn = connections.Find(a => a.GetSteamID() == player);

            if (!conn.Equals(default))
            {
                connections.Remove(conn);
                SteamNetworkingMessages.CloseChannelWithUser(ref conn, channel);
            }
        }

        public void SendToTeam(byte[] data)
        {
            FactionHQ ourHQ = GameManager.LocalFactionHQ;
            Player[] team = [..ourHQ.GetPlayers(false)];

            foreach (SteamNetworkingIdentity identity in connections)
                if (team.Any(a => a.SteamID == identity.GetSteamID64()))
                    SendTo(identity, data);
        }

        public void SendToAll(byte[] data)
        {
            foreach (SteamNetworkingIdentity identity in connections)
                SendTo(identity, data);
        }

        public void SendTo(CSteamID target, byte[] data)
        {
            SteamNetworkingIdentity ident = new()
            {
                m_eType = ESteamNetworkingIdentityType.k_ESteamNetworkingIdentityType_SteamID
            };
            ident.SetSteamID(target);

            SendTo(ident, data);
        }

        private unsafe void SendTo(SteamNetworkingIdentity target, byte[] data)
        {
            fixed (byte* buf = data)
            {
                EResult result = SteamNetworkingMessages.SendMessageToUser(ref target, (IntPtr)buf, (uint)data.Length, 5, channel);
                if (result == EResult.k_EResultNoConnection)
                    SteamNetworkingMessages.CloseSessionWithUser(ref target);
            }
        }

        private void OnSession(SteamNetworkingMessagesSessionRequest_t request)
        {
            Player? player = NetworkManagerNuclearOption.i.GamePlayers
                .Where(a => a.SteamID == request.m_identityRemote.GetSteamID64())
                .FirstOrDefault();

            if (player != null && !ChatManager.IsMuted(player)) // TODO: When a player is unmuted (might need a patch) retry connection
            {
                SteamNetworking.AcceptP2PSessionWithUser(request.m_identityRemote.GetSteamID());
                NewConnection?.Invoke(request.m_identityRemote.GetSteamID());
            }
            else
                Plugin.Logger.LogWarning("Received P2P request from random user");
        }

        private void OnDisconnect(SteamNetworkingIdentity target)
        {
            SteamNetworkingMessages.CloseSessionWithUser(ref target);

            if (connections.Remove(target))
                ConnectionLost?.Invoke(target.GetSteamID());
        }

        private void OnDisconnect(SteamNetworkingMessagesSessionFailed_t failure)
        {
            OnDisconnect(failure.m_info.m_identityRemote);
        }
    }
}
