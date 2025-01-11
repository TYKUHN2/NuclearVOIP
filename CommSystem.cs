using BepInEx.Unity.Mono.Configuration;
using Steamworks;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace NuclearVOIP
{
    internal class CommSystem: MonoBehaviour
    {
        private MicrophoneListener? listener;
        private OpusEncoder? encoder;
        private KeyboardShortcut? activeKey;

        private readonly Dictionary<CSteamID, StreamPlayer> players = [];

        public event Action<byte[][]>? OnData;

        public void Awake()
        {
            listener = gameObject.AddComponent<MicrophoneListener>();
        }

        private void Update()
        {
            KeyboardShortcut talkKey = Plugin.Instance.configTalkKey.Value;
            KeyboardShortcut allTalkKey = Plugin.Instance.configAllTalkKey.Value;

            if (listener!.enabled && (talkKey.IsDown() || allTalkKey.IsDown()))
            {
                encoder = new(listener.frequency);
                encoder.OnData += _OnData;
                listener.Pipe(encoder);

                listener.enabled = true;

                activeKey = talkKey.IsDown() ? talkKey : allTalkKey;
            }
            else if (activeKey?.IsUp() == true)
            {
                listener!.enabled = false;
                encoder = null;

                listener.Pipe(null);
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
            if (players.ContainsKey(player))
                Plugin.Logger.LogWarning("Received a new stream from a client with an already open stream");

            StreamPlayer sPlayer = new();
            players[player] = sPlayer;

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
