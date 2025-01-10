using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.Mono;
using BepInEx.Unity.Mono.Configuration;
using System;
using System.Threading;
using NuclearOption.SavedMission;
using BepInEx.Logging;
using NuclearOption.Networking;
using Mirage;
using Steamworks;
using UnityEngine;

namespace NuclearVOIP
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    [BepInProcess("NuclearOption.exe")]
    public class Plugin : BaseUnityPlugin
    {
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

        internal new static ManualLogSource Logger
        {
            get { return Instance._Logger; }
        }

        private ManualLogSource _Logger
        {
            get { return base.Logger; }
        }

        private OpusMultiStreamer? streamer;

        internal readonly ConfigEntry<KeyboardShortcut> configTalkKey;
        internal readonly ConfigEntry<KeyboardShortcut> configAllTalkKey;

        internal readonly ConfigEntry<int> configVOIPPort;

        Plugin()
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

            configVOIPPort = Config.Bind(
                    "General",
                    "VOIP Port",
                    5000,
                    "The port to discover and transmit voice chat on"
                );

            LoadingManager.NetworkReady += LateLoad;
        }

        ~Plugin()
        {
            _Instance = null;
        }

        private void Awake()
        {
            Logger.LogInfo($"Loaded {MyPluginInfo.PLUGIN_GUID}");
        }

        private void LateLoad()
        {
            Logger.LogInfo($"LateLoading {MyPluginInfo.PLUGIN_GUID}");
            if (!SteamManager.Initialized)
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
            GameObject host = GameManager.LocalPlayer.gameObject;
            NetworkSystem networking = host.AddComponent<NetworkSystem>();
            CommSystem comms = host.AddComponent<CommSystem>();

            streamer = new(comms, networking);
        }
    }
}
