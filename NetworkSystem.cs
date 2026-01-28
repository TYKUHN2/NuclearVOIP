using AtomicFramework;
using NuclearOption.Networking;
using NuclearOption.Chat;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace NuclearVOIP
{
    internal class NetworkSystem: MonoBehaviour
    {
        private const float INTERVAL = 1f;

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
            Plugin.Logger.LogDebug("Initializing NetworkSystem");

            if (Plugin.Instance.Networking == null)
            {
                Destroy(this);
                return;
            }

            chan = Plugin.Instance.Networking.OpenChannel(0);

            chan.OnConnection += OnSession;
            chan.OnDisconnect += OnDisconnect;
            chan.OnConnected += player =>
            {
                connections.Add(player);
                NewConnection?.Invoke(player);
            };

            Discovery discovery = NetworkingManager.instance!.discovery;

            List<ulong> awaiting = [.. discovery.Players];

            foreach (ulong player in discovery.Players)
            {
                if (discovery.GetMods(player).Contains(MyPluginInfo.PLUGIN_GUID))
                {
                    chan.Connect(player);
                    awaiting.Remove(player);
                }
            }

            void OnMods(ulong player)
            {
                if (discovery.GetMods(player).Contains(MyPluginInfo.PLUGIN_GUID))
                {
                    chan.Connect(player);
                    awaiting.Remove(player);
                }
            }

            discovery.ModsAvailable += OnMods;

            chan.OnMessage += OnMessage;
        }

        private void FixedUpdate()
        {
            elapsed += Time.fixedDeltaTime;

            if (elapsed >= INTERVAL)
            {
                elapsed -= INTERVAL;

                List<float> losses = new(connections.Count);
                List<int> pings = new(connections.Count);
                List<int> bandwidths = new(connections.Count);

                List<float> teamLosses = [];
                List<int> teamPings = [];
                List<int> teamBandwidths = [];

                for (int i = 0; i < connections.Count; i++)
                {
                    ulong identity = connections[i];
                    Player? peer = NetworkingManager.GetPlayer(identity);

                    if (peer != null && ChatManager.IsMuted(peer))
                    {
                        chan.Disconnect(identity);
                        ConnectionLost?.Invoke(identity);
                    } 
                    else
                    {
                        NetworkStatistics state = chan.GetStatistics(identity);

                        GameManager.GetLocalHQ(out FactionHQ localHq);
                        if (peer?.HQ == localHq)
                        {
                            teamLosses.Add(state.packetLoss);
                            teamPings.Add(state.ping);
                            teamBandwidths.Add(state.bandwidth);
                        }

                        losses.Add(state.packetLoss);
                        pings.Add(state.ping);
                        bandwidths.Add(state.bandwidth);
                    }

                    if (teamLosses.Count == 0)
                    {
                        teamLosses.Add(1);
                        teamPings.Add(0);
                        teamBandwidths.Add(0);
                    }

                    if (losses.Count == 0)
                    {
                        losses.Add(1);
                        pings.Add(0);
                        bandwidths.Add(0);
                    }

                    NetworkStatus teamStatus = new()
                    {
                        avgLoss = teamLosses.Average(),
                        maxLoss = teamLosses.Max(),

                        avgPing = (int)teamPings.Average(),
                        maxPing = teamPings.Max(),

                        avgBandwidth = (int)teamBandwidths.Average(),
                        minBandwidth = teamBandwidths.Min()
                    };

                    NetworkStatus allStatus = new()
                    {
                        avgLoss = losses.Average(),
                        maxLoss = losses.Max(),

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
            GameManager.GetLocalHQ(out FactionHQ ourHQ);
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

        public void SendToSlow(ulong target, byte[] data)
        {
            chan.Send(target, data);
        }

        private bool OnSession(ulong player)
        {
            Player? playerObj = NetworkingManager.GetPlayer(player);

            if (playerObj != null && !ChatManager.IsMuted(playerObj)) // TODO: When a player is unmuted (might need a patch) retry connection
                return true;
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
