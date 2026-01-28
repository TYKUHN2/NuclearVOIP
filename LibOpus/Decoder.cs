using System;
using System.Linq;
using System.Runtime.InteropServices;

namespace LibOpus
{
    public class Decoder
    {
        private enum DecoderCtl
        {
            SET_COMPLEXITY = 4010,
            GET_COMPLEXITY = 4011,
            SET_GAIN = 4034,
            GET_GAIN = 4045,
            SET_OSCE_BWE = 4054,
            GET_OSCE_BWE = 4055
        }

        private readonly IntPtr decoder;
        private readonly int freq; // in kHz
        private readonly int max_frame_size;

        public int Gain
        {
            get
            {
                return GetCtl(DecoderCtl.GET_GAIN);
            }
            set
            {
                SetCtl(DecoderCtl.SET_GAIN, value);
            }
        }

        public int Complexity
        {
            get
            {
                return GetCtl(DecoderCtl.GET_COMPLEXITY);
            }
            set
            {
                SetCtl(DecoderCtl.SET_COMPLEXITY, value);
            }
        }

        public bool BWE
        {
            get
            {
                return GetCtl(DecoderCtl.GET_OSCE_BWE) == 1;
            }
            set
            {
                SetCtl(DecoderCtl.SET_OSCE_BWE, value ? 1 : 0);
            }
        }

        public bool LACE
        {
            get
            {
                return Complexity == 6;
            }
        }

        public bool NoLACE
        {
            get
            {
                return Complexity >= 7;
            }
        }

        public Decoder(int frequency, int channels)
        {
            if (!OpusTypes.GOOD_FREQS.Contains(frequency))
                throw new ArgumentException("LibOpus.Decoder: Frequency must be 8000, 12000, 16000, 24000, 48000");

            freq = frequency / 1000;
            max_frame_size = freq * 120;

            decoder = Marshal.AllocHGlobal(OpusTypes.opus_decoder_get_size(1));
            int err = OpusTypes.opus_decoder_init(decoder, frequency, channels);
            if (err != 0)
                throw new OpusTypes.OpusException(err);

            BWE = true;
        }

        ~Decoder()
        {
            Marshal.FreeHGlobal(decoder);
        }

        public float[] Decode(byte[] packet)
        {
            float[] decoded = new float[max_frame_size];

            int err = OpusTypes.opus_decode_float(decoder, packet, packet.Length, decoded, max_frame_size, 0);
            if (err < 0)
                throw new OpusTypes.OpusException(err);

            Array.Resize(ref decoded, err);
            return decoded;
        }

        public float[] DecodeLoss(byte[] packet, float duration)
        {
            int frame_size = (int)(freq * duration);

            float[] decoded = new float[frame_size];

            int fec = OpusTypes.opus_packet_has_lbrr(packet, packet.Length);
            int err;

            if (fec == 1)
                err = OpusTypes.opus_decode_float(decoder, packet, packet.Length, decoded, frame_size, 1);
            else
                err = OpusTypes.opus_decode_float(decoder, null, 0, decoded, frame_size, 0);

            if (err < 0)
                throw new OpusTypes.OpusException(err);

            Array.Resize(ref decoded, err);

            return decoded;
        }

        private void SetCtl(DecoderCtl ctl, int val)
        {
            int err = OpusTypes.opus_decoder_ctl(decoder, (int)ctl, val);
            if (err != 0)
            {
                Marshal.FreeHGlobal(decoder);
                throw new OpusTypes.OpusException(err);
            }
        }

        private int GetCtl(DecoderCtl ctl)
        {
            int err = OpusTypes.opus_decoder_ctl(decoder, (int)ctl, out int result);

            if (err != 0)
            {
                Marshal.FreeHGlobal(decoder);
                throw new OpusTypes.OpusException(err);
            }

            return result;
        }
    }
}
