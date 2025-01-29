using NuclearVOIP.UI;
using Steamworks;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

#if BEP6
using BepInEx.Unity.Mono.Configuration;
#elif BEP5
using BepInEx.Configuration;
#endif

namespace NuclearVOIP
{
    internal class CommSystem: MonoBehaviour
    {
        private MicrophoneListener? listener;
        private OpusEncoder? encoder;
        private KeyboardShortcut? activeKey;
        private TalkingList? talkingList;

        private readonly Dictionary<CSteamID, StreamPlayer> players = [];

        public event Action<byte[][]>? OnData;
        public event Action<OpusMultiStreamer.Target>? OnTarget;

        public void Awake()
        {
            listener = gameObject.AddComponent<MicrophoneListener>();

            GameObject go = new("TalkingList");
            talkingList = go.AddComponent<TalkingList>();

            GameObject host = GameObject.Find("/SceneEssentials/Canvas/ChatCanvas/TopPanel/LeftSpace");
            go.transform.SetParent(host.transform, false);
        }

        private void Update()
        {
            KeyboardShortcut talkKey = Plugin.Instance.configTalkKey.Value;
            KeyboardShortcut allTalkKey = Plugin.Instance.configAllTalkKey.Value;

            if (!listener!.enabled && (talkKey.IsDown() || allTalkKey.IsDown()))
            {
                Plugin.Instance.Config.Reload();

                encoder = new(listener.frequency);
                encoder.OnData += _OnData;

                listener.OnData += (StreamArgs<float> args) =>
                {
                    args.Handle();

                    float[] boosted = args.data.Select(a => a * Plugin.Instance.configInputGain.Value).ToArray();

                    encoder.Write(boosted);
                };

                listener.enabled = true;

                activeKey = talkKey.IsDown() ? talkKey : allTalkKey;

                talkingList!.AddPlayer(GameManager.LocalPlayer);

                OnTarget?.Invoke(talkKey.IsDown() ? OpusMultiStreamer.Target.TEAM : OpusMultiStreamer.Target.GLOBAL);
            }
            else if (activeKey?.IsUp() == true)
            {
                listener!.enabled = false;
                encoder = null;

                talkingList!.RemovePlayer(GameManager.LocalPlayer);

                listener.Pipe(null);
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

            StreamPlayer sPlayer = gameObject.AddComponent<StreamPlayer>();
            sPlayer.decoder.Gain = (int)Math.Round(Plugin.Instance.configOutputGain.Value * 256);

            players[player] = sPlayer;

            Player playerObj = UnitRegistry.playerLookup
                .Where(a => a.Value.SteamID == player.m_SteamID)
                .First()
                .Value;

            if (playerObj != GameManager.LocalPlayer) // DebugNetworkingSystem will error otherwise
                talkingList!.AddPlayer(playerObj);

            return (byte[][] opusPackets) =>
            {
                sPlayer.decoder.Write(opusPackets);
            };
        }

        internal void DestroyStream(CSteamID player)
        {
            if (!players.TryGetValue(player, out StreamPlayer sPlayer))
            {
                Plugin.Logger.LogWarning("Nonexistent stream was closed");
                return;
            }

            players.Remove(player);
            Destroy(sPlayer);

            Player playerObj = UnitRegistry.playerLookup
                .Where(a => a.Value.SteamID == player.m_SteamID)
                .First()
                .Value;

            if (playerObj != GameManager.LocalPlayer) // DebugNetworkingSystem will error otherwise
                talkingList!.RemovePlayer(playerObj);
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
    }
}
