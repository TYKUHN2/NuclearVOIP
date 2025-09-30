using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
//using UnityEngine;

namespace NuclearVOIP
{
    internal class OpusDecoder: AbstractTransform<byte[], float>
    {
        private readonly IntPtr decoder;
        private bool packetLost = false;

        //private int frame = 0;

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
            Stopwatch sw = Stopwatch.StartNew();

            float[][] decoded = new float[data.Length][];
            for (int i = 0; i < data.Length; i++)
                decoded[i] = DoDecode(data[i]);

            sw.Stop();

            if (sw.ElapsedMilliseconds > 20)
                Plugin.Logger.LogDebug("Decoder exceeded RT threshold.");

            return [.. decoded.SelectMany(a => a)];
        }

        private float[] DoDecode(byte[] packet)
        {
            /*if (frame > (Time.frameCount + 5)) // Only update every 6 frames, supports roll over.
            {
                int curComplexity = Complexity; // Avoid repeatedly fetching
                int framerate = Plugin.Instance.FrameRate;

                if (QualitySettings.vSyncCount == 1)
                {
                    int difference = ((int)Math.Round(Screen.currentResolution.refreshRateRatio.value)) - framerate;

                    if (difference >= 3 && curComplexity > 0)
                        Complexity = curComplexity - 1;
                    else if (difference <= 1 && curComplexity < 10)
                        Complexity = curComplexity + 1;
                }
                else if (framerate < 20 && curComplexity > 1)
                    Complexity = curComplexity - 2;
                else if (framerate < 50 && curComplexity > 0)
                    Complexity = curComplexity - 1;
                else if (framerate > 70 && curComplexity < 10)
                    Complexity = curComplexity + 1;

                frame = Time.frameCount;
            }*/

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

                int err = LibOpus.opus_decode_float(decoder, packet, packet.Length, decoded, 5760, 0);
                if (err < 0)
                {
                    Marshal.FreeHGlobal(decoder);
                    throw new LibOpus.OpusException(err);
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
