using HarmonyLib;
using Mirage;
using NuclearOption.Networking;
using System;

namespace NuclearVOIP
{
    public class LoadingManager
    {
        public static event Action? GameLoaded;
        public static event Action? NetworkReady;

        public static event Action? MissionLoaded;
        public static event Action? MissionUnloaded;

        static LoadingManager()
        {
            Type thisType = typeof(LoadingManager);

            Harmony harmony = new("xyz.tyknet.NuclearOption");

            Type netManager = typeof(NetworkManagerNuclearOption);
            harmony.Patch(netManager.GetMethod("Awake"), null, new(thisType.GetMethod(nameof(NetworkManagerPostfix))));

            Type mainMenu = typeof(MainMenu);
            harmony.Patch(mainMenu.GetMethod("Start"), null, new(thisType.GetMethod(nameof(MainMenuPostfix))));
        }

        private static void MainMenuPostfix()
        {
            GameLoaded?.Invoke();
        }

        private static void NetworkManagerPostfix()
        {
            NetworkManagerNuclearOption.i.Client.Connected.AddListener(ClientConnectCallback);
            NetworkManagerNuclearOption.i.Client.Disconnected.AddListener(ClientDisconectCallback);

            NetworkReady?.Invoke();
        }

        private static void MissionLoadCallback()
        {
            MissionLoaded?.Invoke();
        }

        private static void OnIdentity(NetworkIdentity identity)
        {
            identity.OnStartLocalPlayer.AddListener(MissionLoadCallback);
        }

        private static void ClientConnectCallback(INetworkPlayer player)
        {
            player.OnIdentityChanged += OnIdentity;
        }

        private static void ClientDisconectCallback(ClientStoppedReason reason)
        {
            MissionUnloaded?.Invoke();
        }
    }
}
