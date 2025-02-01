using Steamworks;
using System;

namespace NuclearVOIP
{
    internal interface INetworkSystem
    {
        public event Action<CSteamID>? NewConnection;
        public event Action<CSteamID, byte[]>? OnPacket;
        public event Action<CSteamID>? ConnectionLost;
        public event Action<NetworkStatus, NetworkStatus>? OnNetworkMeasurement;

        void Disconnect(CSteamID player);
        void SendToTeam(byte[] data);
        void SendToAll(byte[] data);
        void SendTo(CSteamID player, byte[] data);
    }
}
