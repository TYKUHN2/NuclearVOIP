#pragma warning disable CS0067

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Steamworks;
using UnityEngine;

namespace NuclearVOIP
{
    internal class DebugNetworkSystem: MonoBehaviour, INetworkSystem
    {
        private readonly CSteamID steamID = new(GameManager.LocalPlayer.SteamID);
        private readonly int channel = Plugin.Instance.configVOIPPort.Value;

        private readonly HSteamNetConnection send;
        private readonly HSteamNetConnection recv;

        public event Action<CSteamID>? NewConnection;
        public event Action<CSteamID, byte[]>? OnPacket;
        public event Action<CSteamID>? ConnectionLost;
        public event Action<NetworkStatus, NetworkStatus>? OnNetworkMeasurement;

        DebugNetworkSystem()
        {
            Plugin.Logger.LogDebug("DebugNetworkSystem initalized");

            SteamNetworkingIdentity localhost = new();
            localhost.SetLocalHost();

            SteamNetworkingSockets.CreateSocketPair(
                    out send, 
                    out recv, 
                    false,
                    ref localhost,
                    ref localhost
                );
        }

        ~DebugNetworkSystem()
        {
            SteamNetworkingSockets.CloseConnection(send, 0, null, false);
            SteamNetworkingSockets.CloseConnection(recv, 0, null, false);
        }

        private void FixedUpdate()
        {
            IntPtr[] pointers = new IntPtr[2];

            while (true)
            {
                List<byte[]> packets = [];
                int received = SteamNetworkingSockets.ReceiveMessagesOnConnection(recv, pointers, 2);

                for (int i = 0; i < received; i++)
                {
                    unsafe
                    {
                        SteamNetworkingMessage_t* message = (SteamNetworkingMessage_t*)pointers[i];

                        if (message->m_cbSize != 0) // Just a stupid handshake
                        {
                            byte[] packet = new byte[message->m_cbSize];
                            Marshal.Copy(message->m_pData, packet, 0, packet.Length);

                            packets.Add(packet);
                        }

                        SteamNetworkingMessage_t.Release((IntPtr)message);
                    }
                }

                if (OnPacket == null)
                    return;

                foreach (byte[] packet in packets)
                    OnPacket.Invoke(steamID, packet);

                if (received < 2)
                    break;
            }
        }

        public void Disconnect(CSteamID steamID)
        {

        }

        public void SendToTeam(byte[] data)
        {
            Send(data);
        }

        public void SendToAll(byte[] data)
        {
            Send(data);
        }

        public void SendTo(CSteamID target, byte[] data)
        {
            Send(data);
        }

        private unsafe void Send(byte[] data)
        {
            fixed(byte* ptr = data)
            {
                SteamNetworkingSockets.SendMessageToConnection(send, (IntPtr)ptr, (uint)data.Length, 5, out var _);
            }
        }
    }
}
