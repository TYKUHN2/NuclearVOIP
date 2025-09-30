using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

namespace NuclearVOIP
{
    internal class OpusDecoder: AbstractTransform<byte[], float>
    {
        private readonly IntPtr decoder;
        private bool packetLost = false;

        private byte costi = 0;
        private readonly int[] costs = new int[6];

        public int Gain
        {
            get
            {
                return GetCtl(LibOpus.DecoderCtl.GET_GAIN);
            }
            set
            {
                SetCtl(LibOpus.DecoderCtl.SET_GAIN, value);
            }
        }

        // Use StopWatch to ensure we stay under 20ms, adjust complexity as needed
        public int Complexity
        {
            get
            {
                return GetCtl(LibOpus.DecoderCtl.GET_COMPLEXITY);
            }
            set
            {
                SetCtl(LibOpus.DecoderCtl.SET_COMPLEXITY, value);
            }
        }

        public OpusDecoder()
        {
            decoder = Marshal.AllocHGlobal(LibOpus.opus_decoder_get_size(1));
            int err = LibOpus.opus_decoder_init(decoder, 48000, 1);
            if (err != 0)
            {
                Marshal.FreeHGlobal(decoder);
                throw new LibOpus.OpusException(err);
            }
        }

        ~OpusDecoder()
        {
            Marshal.FreeHGlobal(decoder);
        }

        protected override float[] Transform(byte[][] data)
        {
            float[][] decoded = new float[data.Length][];
            for (int i = 0; i < data.Length; i++)
                decoded[i] = DoDecode(data[i]);

            return [.. decoded.SelectMany(a => a)];
        }

        private float[] DoDecode(byte[] packet)
        {
            if (packet.Length == 1 && packet[0] == 13) // DTX/Lost packet
            {
                packetLost = true;
                return [];
            } 
            else
            {
                float[] prefix = [];
                float[] decoded = new float[5760];

                if (packetLost)
                    prefix = RecoverPacket(packet);

                Stopwatch sw = Stopwatch.StartNew();

                int err = LibOpus.opus_decode_float(decoder, packet, packet.Length, decoded, 5760, 0);
                if (err < 0)
                {
                    Marshal.FreeHGlobal(decoder);
                    throw new LibOpus.OpusException(err);
                }

                sw.Stop();

                if (sw.ElapsedMilliseconds > 20)
                    Plugin.Logger.LogDebug("Decoder exceeded RT threshold.");

                costs[costi++] = (int)sw.ElapsedMilliseconds;

                if (costi == 6)
                {
                    costi = 0;

                    int avgCost = (int)Math.Ceiling(costs.Average());

                    if (avgCost > 40) // On average we are two or more packets too slow
                        Complexity -= 2;
                    else if (avgCost > 20) // On average we are one packet too slow
                        Complexity -= 1;
                }

                Array.Resize(ref decoded, err);
                return [..prefix, ..decoded];
            }
        }

        private float[] RecoverPacket(byte[] packet)
        {
            float[] decoded = new float[5760];

            int fec = LibOpus.opus_packet_has_lbrr(packet, packet.Length);
            int err;

            if (fec == 1)
                err = LibOpus.opus_decode_float(decoder, packet, packet.Length, decoded, 5760, 1);
            else
                err = LibOpus.opus_decode_float(decoder, null, 0, decoded, 5760, 0);

            if (err < 0)
            {
                Marshal.FreeHGlobal(decoder);
                throw new LibOpus.OpusException(err);
            }

            Array.Resize(ref decoded, err);

            return decoded;
        }

        private void SetCtl(LibOpus.DecoderCtl ctl, int val)
        {
            int err = LibOpus.opus_decoder_ctl(decoder, (int)ctl, val);
            if (err != 0)
            {
                Marshal.FreeHGlobal(decoder);
                throw new LibOpus.OpusException(err);
            }
        }

        private unsafe int GetCtl(LibOpus.DecoderCtl ctl)
        {
            int err = LibOpus.opus_decoder_ctl(decoder, (int)ctl, out int result);

            if (err != 0)
            {
                Marshal.FreeHGlobal(decoder);
                throw new LibOpus.OpusException(err);
            }

            return result;
        }
    }
}
