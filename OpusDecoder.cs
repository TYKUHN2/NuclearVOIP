using System;
using System.Linq;
using System.Runtime.InteropServices;

namespace NuclearVOIP
{
    internal class OpusDecoder: AbstractTransform<byte[], float>
    {
        private readonly IntPtr decoder;

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

            return decoded.SelectMany(a => a).ToArray();
        }

        private float[] DoDecode(byte[] packet)
        {
            float[] decoded = new float[5760];

            int err = LibOpus.opus_decode_float(decoder, packet, packet.Length, decoded, 5760, 0);
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
