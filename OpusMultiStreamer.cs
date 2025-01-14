using Steamworks;
using System;
using System.Collections.Generic;
using System.IO;

namespace NuclearVOIP
{
    internal class OpusMultiStreamer
    {
        private enum Commands: byte
        {
            START,
            DATA,
            STOP,
            LOSS
        }

        public enum Target
        {
            STOPPED,
            TEAM,
            GLOBAL
        }

        private readonly CommSystem comms;
        private readonly INetworkSystem networking;
        private readonly Dictionary<CSteamID, OpusNetworkStream> streams = [];
        private ushort pos = 0;

        private Target target;
        public Target CurTarget { 
            get
            {
                return target;
            }
            set
            {
                if (target == value)
                    return;

                Target was = target;

                target = value;

                if (was == Target.STOPPED)
                    StartOutbound();
                else if (value == Target.STOPPED)
                    StopOutbound(was);
                else
                {
                    StopOutbound(was);
                    StartOutbound();
                }
            }
        }

        public OpusMultiStreamer(CommSystem comms, INetworkSystem networking)
        {
            this.comms = comms;
            this.networking = networking;

            comms.OnData += OnData;
            comms.OnTarget += (Target target) => { CurTarget = target; };
            networking.OnPacket += Parse;
        }

        private void StartOutbound()
        {
            switch (target)
            {
                case Target.TEAM:
                    networking.SendToTeam([(byte)Commands.START]);
                    break;
                case Target.GLOBAL:
                    networking.SendToAll([(byte)Commands.START]);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            pos = 0;
        }

        private void StopOutbound(Target was)
        {
            switch(was)
            {
                case Target.TEAM:
                    networking.SendToTeam([(byte)Commands.STOP]);
                    break;
                case Target.GLOBAL:
                    networking.SendToAll([(byte)Commands.STOP]);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private void OnData(byte[][] packets)
        {
            if (target == Target.STOPPED)
                return;

            int totalLen = (packets.Length << 1) + 3;
            foreach (byte[] packet in packets)
                totalLen += packet.Length;

            byte[] encoded = new byte[totalLen];

            MemoryStream stream = new(encoded);
            BinaryWriter writer = new(stream);

            writer.Write((byte)Commands.DATA);
            writer.Write(pos++);
            foreach (byte[] packet in packets)
            {
                writer.Write((ushort)packet.Length);
                writer.Write(packet);
            }

            writer.Flush();

            if (target == Target.TEAM)
                networking.SendToTeam(encoded);
            else 
                networking.SendToAll(encoded);
        }

        private void Parse(CSteamID player, byte[] data)
        {
            Commands cmd = (Commands)data[0];

            switch (cmd)
            {
                case Commands.START:
                    if (streams.ContainsKey(player))
                    {
                        Plugin.Logger.LogWarning("Peer trying to start stream with active stream");
                        EndStream(player);
                    }

                    streams[player] = new(comms.NewStream(player));
                    break;
                case Commands.DATA:
                    if (!streams.TryGetValue(player, out OpusNetworkStream stream))
                    {
                        Plugin.Logger.LogError("Peer tried to send data on stopped stream");
                        return;
                    }

                    stream.Parse(data[1..]);
                    break;
                case Commands.STOP:
                    EndStream(player);
                    break;
                case Commands.LOSS:
                    // TODO
                    break;
                default:
                    Plugin.Logger.LogError("Unknown command received from peer");
                    break;
            }
        }

        private void EndStream(CSteamID player)
        {
            if (!streams.TryGetValue(player, out OpusNetworkStream stream))
            {
                Plugin.Logger.LogWarning("Peer tried to stop stopped stream");
                return;
            }

            stream.Flush();
            byte perc = stream.Loss;
            comms.DestroyStream(player);

            streams.Remove(player);

            networking.SendTo(player, [(byte)Commands.LOSS, perc]);
        }
    }
}
