using BepInEx;
using BepInEx.Configuration;
using System;
using System.Threading;
using BepInEx.Logging;
using Steamworks;
using UnityEngine;
using AtomicFramework;
using NuclearOption.Networking;
using LibOpus;

#if BEP6
using BepInEx.Unity.Mono.Configuration;
#endif

namespace NuclearVOIP
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    public class Plugin: Mod
    {
        private static readonly Options options = new()
        {
            multiplayerOptions = Options.Multiplayer.CLIENT_ONLY,
            repository = "TYKUHN2/NuclearVOIP"
        };

        private static Plugin? _Instance;
        internal static Plugin Instance
        {
            get
            {
                if (_Instance == null)
                    throw new InvalidOperationException("Plugin not initialized");

                return _Instance;
            }
        }

        internal new NetworkAPI? Networking => base.Networking;

        internal new static ManualLogSource Logger => Instance._Logger;

        private ManualLogSource _Logger => base.Logger;

        internal OpusMultiStreamer? streamer;

        internal readonly ConfigEntry<KeyboardShortcut> configTalkKey;
        internal readonly ConfigEntry<KeyboardShortcut> configAllTalkKey;
        internal readonly ConfigEntry<KeyboardShortcut> configChannelKey;

        internal readonly ConfigEntry<float> configInputGain;
        internal readonly ConfigEntry<float> configOutputGain;

        private float[] deltas = new float[10];

        public int FrameRate
        {
            get
            {
                double sum = 0;
                foreach (var delta in deltas)
                    sum += delta;

                sum /= deltas.Length;

                return (int)Math.Round(sum);
            }
        }

        Plugin(): base(options)
        {
            if (Interlocked.CompareExchange(ref _Instance, this, null) != null) // I like being thread safe okay?
                throw new InvalidOperationException($"Reinitialization of Plugin {MyPluginInfo.PLUGIN_GUID}");

            configTalkKey = Config.Bind(
                    "General",
                    "Talk Key",
                    new KeyboardShortcut(KeyCode.V),
                    "Push to talk key"
                );

            configAllTalkKey = Config.Bind(
                    "General",
                    "All Talk Key",
                    new KeyboardShortcut(KeyCode.C),
                    "Push to talk to all key"
                );

            configChannelKey = Config.Bind(
                    "General",
                    "Change Channel",
                    new KeyboardShortcut(KeyCode.Slash),
                    "Change talk channel"
                );

            configInputGain = Config.Bind(
                    "General",
                    "Microphone Gain",
                    2.0f,
                    "A (linear) multiplier applied to microphone readings"
                );

            configOutputGain = Config.Bind(
                    "General",
                    "Output Gain",
                    1.0f,
                    "A (in dB) multiplier applied to incoming voice"
                );
        }

        ~Plugin()
        {
            _Instance = null;
        }

        private void Awake()
        {
            Logger.LogInfo($"Loaded {MyPluginInfo.PLUGIN_GUID}");

            LoadingManager.NetworkReady += LateLoad;
        }

        private void Update()
        {
            float[] copy = new float[10];
            copy[9] = Time.deltaTime;
            Array.Copy(deltas, 1, copy, 0, 9);
            deltas = copy;
        }

        private void LateLoad()
        {
            Logger.LogInfo($"LateLoading {MyPluginInfo.PLUGIN_GUID}");
            if (!SteamManager.ClientInitialized)
            {
                Logger.LogWarning("Disabling VOIP: steam is not initalized");
                return;
            }

            LoadingManager.MissionUnloaded += MissionUnload;
            LoadingManager.MissionLoaded += LoadingFinished;

            SteamNetworking.AllowP2PPacketRelay(true);

            if (SteamNetworkingSockets.GetAuthenticationStatus(out _) == ESteamNetworkingAvailability.k_ESteamNetworkingAvailability_NeverTried)
                SteamNetworkingSockets.InitAuthentication();
            
            if (SteamNetworkingUtils.GetRelayNetworkStatus(out _) == ESteamNetworkingAvailability.k_ESteamNetworkingAvailability_NeverTried)
                SteamNetworkingUtils.InitRelayNetworkAccess();

            Logger.LogInfo($"LateLoaded {MyPluginInfo.PLUGIN_GUID}");
        }

        private void OnDestroy()
        {
            Logger.LogInfo($"Unloaded {MyPluginInfo.PLUGIN_GUID}");
        }

        private void MissionUnload()
        {
            streamer = null;
        }

        private void LoadingFinished()
        {
            GameManager.GetLocalPlayer(out Player localPlayer);
            GameObject host = localPlayer.gameObject;
            NetworkSystem networkSystem = host.AddComponent<NetworkSystem>();
            CommSystem comms = host.AddComponent<CommSystem>();

            streamer = new(comms, networkSystem);
        }
    }
}
