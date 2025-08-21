using AtomicFramework;
using NuclearOption.Networking;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace NuclearVOIP
{
    internal class NetworkSystem: MonoBehaviour
    {
        private const float INTERVAL = 0.02f;

        private readonly List<ulong> connections = [];
        private float elapsed = 0;

        public event Action<ulong>? NewConnection;
        public event Action<ulong, byte[]>? OnPacket;
        public event Action<ulong>? ConnectionLost;
        public event Action<NetworkStatus, NetworkStatus>? OnNetworkMeasurement;

        private NetworkChannel chan;

        ~NetworkSystem()
        {
            Plugin.Instance.Networking?.CloseChannel(0);
        }

        private void Awake()
        {
            if (Plugin.Instance.Networking == null)
            {
                Destroy(this);
                return;
            }

            chan = Plugin.Instance.Networking.OpenChannel(0);

            chan.OnConnection += OnSession;
            chan.OnDisconnect += OnDisconnect;

            Discovery discovery = NetworkingManager.instance!.discovery;

            discovery.Ready += () =>
            {
                List<ulong> awaiting = [.. discovery.Players];

                void OnMods(ulong player)
                {
                    if (discovery.GetMods(player).Contains(MyPluginInfo.PLUGIN_GUID))
                    {
                        chan.Connect(player);
                        awaiting.Remove(player);
                    }
                }

                foreach (ulong player in discovery.Players)
                {
                    if (discovery.GetMods(player).Contains(MyPluginInfo.PLUGIN_GUID))
                    {
                        chan.Connect(player);
                        awaiting.Remove(player);
                    }
                }

                discovery.ModsAvailable += OnMods;
            };

            chan.OnMessage += OnMessage;
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
                    ulong identity = connections[i];
                    Player peer = NetworkingManager.instance!.GetPlayer(identity)!;

                    if (ChatManager.IsMuted(peer))
                    {
                        chan.Disconnect(identity);
                        ConnectionLost?.Invoke(identity);
                    } 
                    else
                    {
                        NetworkStatistics state = chan.GetStatistics(identity);

                        if (peer.HQ == GameManager.LocalFactionHQ)
                        {
                            teamQualities.Add(1 - state.packetLoss);
                            teamPings.Add(state.ping);
                            teamBandwidths.Add(state.bandwidth);
                        }

                        qualities.Add(1 - state.packetLoss);
                        pings.Add(state.ping);
                        bandwidths.Add(state.bandwidth);
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
        }

        public void Disconnect(ulong player)
        {
            connections.Remove(player);
            OnDisconnect(player);
        }

        public void SendToTeam(byte[] data)
        {
            FactionHQ ourHQ = GameManager.LocalFactionHQ;
            Player[] team = [..ourHQ.GetPlayers(false)];

            foreach (ulong identity in connections)
                if (team.Any(a => a.SteamID == identity))
                    chan.Send(identity, data, true);
        }

        public void SendToAll(byte[] data)
        {
            foreach (ulong identity in connections)
                chan.Send(identity, data, true);
        }

        public void SendTo(ulong target, byte[] data)
        {
            chan.Send(target, data, true);
        }

        private bool OnSession(ulong player)
        {
            Player? playerObj = NetworkManagerNuclearOption.i.GamePlayers
                .Where(a => a.SteamID == player)
                .FirstOrDefault();

            if (playerObj != null && !ChatManager.IsMuted(playerObj)) // TODO: When a player is unmuted (might need a patch) retry connection
            {
                NewConnection?.Invoke(player);
                return true;
            }
            else
            {
                Plugin.Logger.LogWarning("Received P2P request from random user");
                return false;
            }
        }

        private void OnDisconnect(ulong player)
        {
            if (connections.Remove(player))
                ConnectionLost?.Invoke(player);
        }

        private void OnMessage(NetworkMessage message)
        {
            OnPacket?.Invoke(message.player, message.data);
        }
    }
}
