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

        public event Action<CSteamID, byte[]>? OnPacket;
        public event Action<CSteamID>? ConnectionLost;

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
                if (received > 0)
                    Plugin.Logger.LogDebug($"DebugNetworkSystem: Received {received} messages");

                for (int i = 0; i < received; i++)
                {
                    unsafe
                    {
                        SteamNetworkingMessage_t* message = (SteamNetworkingMessage_t*)pointers[i];

                        if (message->m_cbSize == 0) // Just a stupid handshake
                            continue;

                        byte[] packet = new byte[message->m_cbSize];
                        Marshal.Copy(message->m_pData, packet, 0, packet.Length);

                        packets.Add(packet);

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

        public void SendToTeam(byte[] data)
        {
            Plugin.Logger.LogDebug("DebugNetworkSystem: SendToTeam");
            Send(data);
        }

        public void SendToAll(byte[] data)
        {
            Plugin.Logger.LogDebug("DebugNetworkSystem: SendToAll");
            Send(data);
        }

        public void SendTo(CSteamID target, byte[] data)
        {
            Plugin.Logger.LogDebug($"DebugNetworkSystem: SendTo {target}");
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
