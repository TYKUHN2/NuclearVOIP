using System;
using System.Collections.Generic;
using System.IO;

namespace NuclearVOIP
{
    internal class OpusNetworkStream(Action<byte[][]> handler)
    {
        private static readonly byte[] deadPacket = []; // DTX / Lost packet

        private readonly Action<byte[][]> handler = handler;

        private ushort pos = 0;
        private ushort lost = 0;

        private byte[][]? delayed;

        public byte Loss
        {
            get
            {
                if (pos == 0 || lost == 0)
                    return 0;

                return (byte)((100 * lost) / pos);
            }
        }

        public void Parse(byte[] data)
        {
            MemoryStream stream = new(data, false);
            BinaryReader reader = new(stream);

            ushort id = reader.ReadUInt16();

            LinkedList<byte[]> packets = new();

            while (stream.Position != stream.Length)
            {
                if (stream.Position + 2 <= stream.Length)
                {
                    Plugin.Logger.LogWarning("VOIP packet truncated");
                    break;
                }

                ushort len = reader.ReadUInt16();
                if (len == 0)
                {
                    packets.AddLast(deadPacket);
                    continue;
                }

                byte[] packet = reader.ReadBytes(len);
                if (packet.Length < len)
                {
                    Plugin.Logger.LogWarning("VOIP packet truncated");
                    break;
                }

                packets.AddLast(packet);
            }

            if (packets.Count == 0)
            {
                return;
            }

            byte[][] packetArr = [..packets];

            lock (this)
            {
                short diff = (short)(id - pos);
                switch (diff)
                {
                    default: // More than 1 packet behind
                        break;
                    case -1: // One packet behind
                        if (delayed != null) // Caught up
                        {
                            handler([..packetArr, ..delayed]);
                            delayed = null;
                        }
                        break;
                    case 0: // On time
                        if (delayed == null)
                            handler(packetArr);
                        else // Stop waiting
                        {
                            handler([deadPacket, ..delayed, .. packetArr]);
                            delayed = null;
                            lost++;
                        }

                        pos++;
                        break;
                    case > 0: // Ahead of time
                        delayed = packetArr;
                        lost += (ushort)(diff - 1);
                        byte[][] backfill = new byte[diff - 1][];
                        Array.Fill(backfill, deadPacket);
                        handler(backfill);
                        pos = (ushort)(id + 1);
                        break;
                }
            }
        }

        public void Flush()
        {
            if (delayed == null)
                return;

            handler(delayed);
            delayed = null;
            lost++;
        }
    }
}
