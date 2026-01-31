//#define DECODER_TEST

using System;
using System.Linq;
using System.Runtime.InteropServices;

namespace LibOpus
{
    public class Encoder
    {
        private enum EncoderCtl
        {
            SET_BITRATE = 4002,
            GET_BITRATE = 4003,
            SET_BANDWIDTH = 4008,
            GET_BANDWIDTH = 4009,
            SET_COMPLEXITY = 4010,
            GET_COMPLEXITY = 4011,
            SET_INBAND_FEC = 4012,
            GET_INBAND_FEC = 4013,
            SET_PACKET_LOSS_PERC = 4014,
            GET_PACKET_LOSS_PERC = 4015,
            SET_DTX = 4016,
            GET_DTX = 4017,
            SET_SIGNAL = 4024,
            GET_SIGNAL = 4025,
            GET_LOOKAHEAD = 4027,
            SET_LSB_DEPTH = 4036,
            GET_LSB_DEPTH = 4037,
        }

        public class ValidationException : Exception
        {
            internal ValidationException() : base("encoder decoder length disagreement")
            {
            }

            internal ValidationException(float encoded, float decoded) : base($"encoder decoder sample disagreement ({encoded}, {decoded})")
            {
            }
        };

        private static readonly float[] GOOD_DURATIONS = [2.5f, 5, 10, 20, 40, 60];

        private readonly IntPtr encoder;

#if DECODER_TEST
        private readonly IntPtr decoder;
        private bool decoder_good = true;
#endif

        public int BitRate
        {
            get
            {
                return GetCtl(EncoderCtl.GET_BITRATE);
            }
            set
            {
                SetCtl(EncoderCtl.SET_BITRATE, value == -1000 ? value : Math.Clamp(value, 500, 512000));
            }
        }

        public int PacketLoss
        {
            get
            {
                return GetCtl(EncoderCtl.GET_PACKET_LOSS_PERC);
            }
            set
            {
                SetCtl(EncoderCtl.SET_PACKET_LOSS_PERC, Math.Clamp(value, 0, 100));
            }
        }

        public OpusTypes.FEC FEC
        {
            get
            {
                return (OpusTypes.FEC)GetCtl(EncoderCtl.GET_INBAND_FEC);
            }
            set
            {
                SetCtl(EncoderCtl.SET_INBAND_FEC, (int)value);
            }
        }

        public bool DTX
        {
            get
            {
                return GetCtl(EncoderCtl.GET_DTX) == 1;
            }
            set
            {
                SetCtl(EncoderCtl.SET_DTX, value ? 1 : 0);
            }
        }

        public OpusTypes.Signal Signal
        {
            get
            {
                return (OpusTypes.Signal)GetCtl(EncoderCtl.GET_SIGNAL);
            }
            set
            {
                SetCtl(EncoderCtl.SET_SIGNAL, (int)value);
            }
        }

        public int LSB_Depth
        {
            get
            {
                return GetCtl(EncoderCtl.GET_LSB_DEPTH);
            }
            set
            {
                SetCtl(EncoderCtl.SET_LSB_DEPTH, Math.Clamp(value, 8, 24));
            }
        }

        public int Complexity
        {
            get
            {
                return GetCtl(EncoderCtl.GET_COMPLEXITY);
            }
            set
            {
                SetCtl(EncoderCtl.SET_COMPLEXITY, Math.Clamp(value, 0, 10));
            }
        }

        public OpusTypes.Bandwidth Bandwidth
        {
            get
            {
                return (OpusTypes.Bandwidth)GetCtl(EncoderCtl.GET_BANDWIDTH);
            }
            set
            {
                SetCtl(EncoderCtl.SET_BANDWIDTH, (int)value);
            }
        }

        public readonly int LookAhead;
        public readonly int FrameSize;

        public Encoder(int frequency, int channels, float frameDuration, OpusTypes.Modes mode)
        {
            if (!OpusTypes.GOOD_FREQS.Contains(frequency))
                throw new ArgumentException("LibOpus.Encoder: Frequency must be 8000, 12000, 16000, 24000, 48000");

            if (channels != 1 && channels != 2)
                throw new ArgumentException("LibOpus.Encoder: Channels must be 1 or 2");

            FrameSize = (int)(frameDuration * frequency) / 1000;

            encoder = Marshal.AllocHGlobal(OpusTypes.opus_encoder_get_size(channels));
            int err = OpusTypes.opus_encoder_init(encoder, frequency, channels, (int)mode);
            if (err != 0)
                throw new OpusTypes.OpusException(err);

            LookAhead = GetCtl(EncoderCtl.GET_LOOKAHEAD);

#if DECODER_TEST
            decoder = Marshal.AllocHGlobal(OpusTypes.opus_decoder_get_size(1));
            err = OpusTypes.opus_decoder_init(decoder, frequency, 1);
            if (err != 0)
                throw new OpusTypes.OpusException(err);
#endif
        }

        ~Encoder()
        {
            Marshal.FreeHGlobal(encoder);

#if DECODER_TEST
            Marshal.FreeHGlobal(decoder);
#endif
        }

        public byte[] Encode(float[] samples)
        {
            if (samples.Length != FrameSize)
                throw new ArgumentException($"LibOpus.Encoder: Samples must be of size {FrameSize}");

            byte[] frame = new byte[4000];

            int err = OpusTypes.opus_encode_float(encoder, samples, FrameSize, frame, 4000);
            if (err < 0)
                throw new OpusTypes.OpusException(err);

            Array.Resize(ref frame, err);

#if DECODER_TEST
            if (decoder_good)
            {
                float[] decoded = new float[5760];
                err = OpusTypes.opus_decode_float(decoder, frame, frame.Length, decoded, 5760, 0);

                if (err < 0)
                    decoder_good = false;
                else
                {
                    if (err != FrameSize)
                        throw new ValidationException();

                    Array.Resize(ref decoded, err);

                    decoded = decoded[LookAhead..];

                    for (int i = 0; i < FrameSize - LookAhead; i++)
                    {
                        float error = decoded[i] - samples[i];

                        if (Math.Abs(error) > 0.2)
                        {
                            ValidationException exception = new(samples[i], decoded[i]);
                            decoder_good = false;

                            throw exception;
                        }
                    }
                }
            }
#endif

            return frame;
        }

        private void SetCtl(EncoderCtl ctl, int val)
        {
            int err = OpusTypes.opus_encoder_ctl(encoder, (int)ctl, val);
            if (err != 0)
            {
                Marshal.FreeHGlobal(encoder);
                throw new OpusTypes.OpusException(err);
            }
        }

        private int GetCtl(EncoderCtl ctl)
        {
            int err = OpusTypes.opus_encoder_ctl(encoder, (int)ctl, out int result);

            if (err != 0)
            {
                Marshal.FreeHGlobal(encoder);
                throw new OpusTypes.OpusException(err);
            }

            return result;
        }
    }
}
