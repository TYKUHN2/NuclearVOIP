using System;
using System.Linq;
using System.Runtime.InteropServices;

namespace LibOpus
{
    public class Decoder
    {
        private const int SAMPLES_PER_FRAME = 20 * 48;

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
        private readonly IntPtr dred;
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

        public Decoder(int frequency, int channels, bool useDRED)
        {
            if (!OpusTypes.GOOD_FREQS.Contains(frequency))
                throw new ArgumentException("LibOpus.Decoder: Frequency must be 8000, 12000, 16000, 24000, 48000");

            freq = frequency / 1000;
            max_frame_size = freq * 120;

            decoder = Marshal.AllocHGlobal(OpusTypes.opus_decoder_get_size(1));
            int err = OpusTypes.opus_decoder_init(decoder, frequency, channels);
            if (err != 0)
                throw new OpusTypes.OpusException(err);

            if (useDRED)
            {
                dred = Marshal.AllocHGlobal(OpusTypes.opus_dred_decoder_get_size());
                err = OpusTypes.opus_dred_decoder_init(dred);
                if (err != 0)
                    throw new OpusTypes.OpusException(err);
            }

            BWE = true;
        }

        ~Decoder()
        {
            Marshal.FreeHGlobal(decoder);
            Marshal.FreeHGlobal(dred);
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

        public float[] DecodeLoss(byte[]? packet, int frames, float duration)
        {
            int frame_size = (int)(freq * duration);
            float[] samples = new float[frame_size * frames];
            int lastPos = 0;

            IntPtr dpack;
            int din = 0;
            int end;

            if (packet != null && dred != IntPtr.Zero)
            {
                dpack = Marshal.AllocHGlobal(OpusTypes.opus_dred_get_size());
                
                din = OpusTypes.opus_dred_parse(dred, dpack, packet, packet.Length, frame_size * frames, freq * 1000, out end, 0);
                if (din < 1)
                    dpack = IntPtr.Zero;
            }
            else
            {
                dpack = IntPtr.Zero;
                end = 0;
            }

            BWE = false;

            int loc = frames * frame_size;
            for (int i = 0; i < frames; i++)
            {
                float[] frame;

                if (loc <= din && loc > end)
                    frame = DecodeLossFrame(packet, frame_size, i == frames - 1, dpack, loc);
                else
                    frame = DecodeLossFrame(packet, frame_size, i == frames - 1, IntPtr.Zero, 0);

                loc -= frame_size;

                frame.CopyTo(samples, lastPos);
                lastPos += frame.Length;
            }

            BWE = true;

            if (dpack != IntPtr.Zero)
                Marshal.FreeHGlobal(dpack);

            Array.Resize(ref samples, lastPos);

            return samples;
        }

        private float[] DecodeLossFrame(byte[]? packet, int frame_size, bool fec, IntPtr dpack, int loc)
        {
            float[] decoded = new float[frame_size];

            if (fec)
            {
                int err1 = packet == null ? 0 : OpusTypes.opus_packet_has_lbrr(packet, packet.Length);
                if (err1 < 0)
                    throw new OpusTypes.OpusException(err1);

                fec = err1 == 1;
            }

            int err;
            if (fec)
                err = OpusTypes.opus_decode_float(decoder, packet, packet!.Length, decoded, frame_size, 1);
            else if (dpack != IntPtr.Zero)
                err = OpusTypes.opus_decoder_dred_decode_float(decoder, dpack, loc, decoded, frame_size);
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
