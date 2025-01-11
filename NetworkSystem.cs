﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using NuclearOption.Networking;
using Steamworks;
using UnityEngine;

namespace NuclearVOIP
{
    internal class NetworkSystem: MonoBehaviour
    {
        private readonly int channel = Plugin.Instance.configVOIPPort.Value;
        private readonly List<SteamNetworkingIdentity> connections = [];
        private float elapsed = 0;

        private readonly Callback<SteamNetworkingMessagesSessionRequest_t> sessionReq;
        private readonly Callback<SteamNetworkingMessagesSessionFailed_t> messageFailed;

        public event Action<CSteamID, byte[]>? OnPacket;
        public event Action<CSteamID>? ConnectionLost;

        NetworkSystem()
        {
            sessionReq = Callback<SteamNetworkingMessagesSessionRequest_t>.Create(OnSession);
            messageFailed = Callback<SteamNetworkingMessagesSessionFailed_t>.Create(OnDisconnect);

            Player[] players = [..UnitRegistry.playerLookup.Values];
            foreach (Player player in players)
            {
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

        private unsafe void FixedUpdate()
        {
            elapsed += Time.fixedDeltaTime;

            if (elapsed > 10)
            {
                elapsed -= 10;
                for (int i = 0; i < connections.Count; i++)
                {
                    SteamNetworkingIdentity identity = connections[i];
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

                    // Later I'll add something about packet loss and ping here.
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

                        if (message->m_cbSize == 0) // Just a stupid handshake
                            continue;

                        byte[] packet = new byte[message->m_cbSize];
                        Marshal.Copy(message->m_pData, packet, 0, packet.Length);

                        packets.Add((message->m_identityPeer, packet));

                        message->Release();
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
            bool found = NetworkManagerNuclearOption.i.GamePlayers.Any(a => a.SteamID == request.m_identityRemote.GetSteamID64());
            if (found)
                SteamNetworking.AcceptP2PSessionWithUser(request.m_identityRemote.GetSteamID());
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