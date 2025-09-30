using NuclearVOIP.UI;
using Steamworks;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using System.Reflection;
using NuclearOption.Networking;



#if BEP6
using BepInEx.Unity.Mono.Configuration;
#elif BEP5
using BepInEx.Configuration;
#endif

namespace NuclearVOIP
{
    internal class CommSystem: MonoBehaviour
    {
        private const float MAX_STRENGTH = 0.01f;

        private static readonly FieldInfo accumulation = typeof(Radar).GetField("jamAccumulation", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo tolerance = typeof(Radar).GetField("jamTolerance", BindingFlags.Instance | BindingFlags.NonPublic);

        private MicrophoneListener? listener;
        private OpusEncoder? encoder;
        private KeyboardShortcut? activeKey;
        private TalkingList? talkingList;

        private readonly Dictionary<CSteamID, StreamPlayer> players = [];

        public event Action<byte[][]>? OnData;
        public event Action<OpusMultiStreamer.Target>? OnTarget;

        private NetworkStatus allStatus = new()
        {
            maxPing = 0,
            avgPing = 0,

            minBandwidth = 0,
            avgBandwidth = 0,

            minQuality = 1,
            avgQuality = 1
        };

        private NetworkStatus teamStatus = new()
        {
            maxPing = 0,
            avgPing = 0,

            minBandwidth = 0,
            avgBandwidth = 0,

            minQuality = 1,
            avgQuality = 1
        };

        public void Awake()
        {
            listener = gameObject.AddComponent<MicrophoneListener>();

            listener.OnData += (StreamArgs<float> args) =>
            {
                if (encoder == null)
                    return;

                args.Handle();

                float[] boosted = args.data.Select(a => a * Plugin.Instance.configInputGain.Value).ToArray();

                encoder.Write(boosted);
            };

            GameObject UI = new("VOIPUI");
            UI.AddComponent<UIGroup>();

            GameObject go = new("TalkingList");
            go.transform.SetParent(UI.transform, false);

            talkingList = go.AddComponent<TalkingList>();

            GameObject host = GameObject.Find("/SceneEssentials/Canvas/ChatCanvas/TopPanel/LeftSpace");
            UI.transform.SetParent(host.transform, false);
        }

        private void Update()
        {
            KeyboardShortcut talkKey = Plugin.Instance.configTalkKey.Value;
            KeyboardShortcut allTalkKey = Plugin.Instance.configAllTalkKey.Value;

            if (!listener!.enabled && (talkKey.IsDown() || allTalkKey.IsDown()))
            {
                activeKey = talkKey.IsDown() ? talkKey : allTalkKey;

                Plugin.Instance.Config.Reload();

                encoder = new(listener.frequency);
                UpdateEncoder();

                encoder.OnData += _OnData;

                listener.enabled = true;

                GameManager.GetLocalPlayer(out Player localPlayer);
                talkingList!.AddPlayer(localPlayer);

                OnTarget?.Invoke(talkKey.IsDown() ? OpusMultiStreamer.Target.TEAM : OpusMultiStreamer.Target.GLOBAL);
            }
            else if (activeKey?.IsUp() == true)
            {
                listener!.enabled = false;
                encoder = null;

                GameManager.GetLocalPlayer(out Player localPlayer);
                talkingList!.RemovePlayer(localPlayer);

                OnTarget?.Invoke(OpusMultiStreamer.Target.STOPPED);
            }
        }

        public void OnDestroy()
        {
            Destroy(listener);
            listener = null;
            encoder = null;
        }

        internal Action<byte[][]> NewStream(CSteamID player)
        {
            Plugin.Instance.Config.Reload();

            if (players.ContainsKey(player))
            {
                Plugin.Logger.LogWarning("Received a new stream from a client with an already open stream");
                DestroyStream(player);
            }

            GameObject newObj = new($"OpusStream: {player}");
            newObj.transform.parent = gameObject.transform;

            StreamPlayer sPlayer = newObj.AddComponent<StreamPlayer>();
            sPlayer.decoder.Gain = (int)Math.Round(Plugin.Instance.configOutputGain.Value * 256);

            players[player] = sPlayer;

            Player playerObj = UnitRegistry.playerLookup
                .Where(a => a.Value.SteamID == player.m_SteamID)
                .First()
                .Value;

            GameManager.GetLocalPlayer(out Player localPlayer);
            if (playerObj != localPlayer) // DebugNetworkingSystem will error otherwise
                talkingList!.AddPlayer(playerObj);

            sPlayer.curModifier = JamModifier;
            return sPlayer.decoder.Write;
        }

        internal void DestroyStream(CSteamID player)
        {
            if (!players.TryGetValue(player, out StreamPlayer sPlayer))
            {
                Plugin.Logger.LogWarning("Nonexistent stream was closed");
                return;
            }

            players.Remove(player);
            Destroy(sPlayer.gameObject);

            Player playerObj = UnitRegistry.playerLookup
                .Where(a => a.Value.SteamID == player.m_SteamID)
                .First()
                .Value;

            GameManager.GetLocalPlayer(out Player localPlayer);
            if (playerObj != localPlayer) // DebugNetworkingSystem will error otherwise
                talkingList!.RemovePlayer(playerObj);
        }

        internal void UpdateStatus(NetworkStatus all, NetworkStatus team)
        {
            allStatus = all;
            teamStatus = team;

            UpdateEncoder();
        }

        private void _OnData(StreamArgs<byte[]> args)
        {
            args.Handle();
            
            if (OnData != null)
            {
                for (int i = 0; i < args.data.Length; i++)
                {
                    if (args.data[i].Length <= 2) // DTX
                        Array.Resize(ref args.data[i], 0);
                }

                OnData.Invoke(args.data);
            }
        }

        private void UpdateEncoder()
        {
            if (encoder == null || activeKey == null)
                return;

            NetworkStatus curStatus = activeKey.Equals(Plugin.Instance.configAllTalkKey.Value) ? allStatus : teamStatus;

            encoder.BitRate = curStatus.minBandwidth == 0 ? -1000 : (int)(curStatus.minBandwidth * 7.2); // 8 bits per byte, 90% saturation
            encoder.FEC = curStatus.avgQuality <= 0.75 ? LibOpus.FEC.RELAXED : LibOpus.FEC.DISABLED;
            encoder.PacketLoss = 100 - (int)(curStatus.avgQuality * 100);
        }

        private void JamModifier(ref float[] samples)
        {
            GameManager.GetLocalAircraft(out Aircraft? localAircraft);
            Radar? radar = (Radar?)localAircraft?.radar;

            if (radar == null) return;

            if (radar.IsJammed())
            {
                float jamAccumulation = (float)accumulation.GetValue(radar);
                float jamTolerance = (float)tolerance.GetValue(radar);

                float strength = Math.Min(jamAccumulation / jamTolerance, 1.0f) * MAX_STRENGTH;

                float[] offsets = new float[samples.Length / 10];
                for (int i = 0; i < offsets.Length; i++)
                    offsets[i] = Math.Abs((Util.Random() - 0.5f) * strength);

                for (int i = 0; i < offsets.Length; i++)
                {
                    int index = i * 10;
                    for (int j = 0; j < 10; j++)
                    {
                        float sample = samples[index + j];
                        float neg = -sample;

                        if (MathF.Sign(sample) == -1)
                           (neg, sample) = (sample, neg);

                        samples[index + j] = Math.Clamp(sample - offsets[i], neg, sample);
                    }
                }
            }
        }
    }
}
