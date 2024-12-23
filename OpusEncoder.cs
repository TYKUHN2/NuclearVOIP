using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace NuclearVOIP
{
    internal class OpusEncoder : GenericStream<byte[]>
    {
        private readonly SampleStream parent;
        private readonly IntPtr encoder;

        private readonly IntPtr decoder;
        private bool decoder_good = true;

        private readonly int frameSize;

        private readonly Mutex readLock = new(); // Currently not internally reordered so locking is needed

        public readonly int lookahead;
        public readonly SampleStream original;

        public int BitRate
        {
            get
            {
                return GetCtl(LibOpus.EncoderCtl.GET_BITRATE);
            }
            set
            {
                SetCtl(LibOpus.EncoderCtl.SET_BITRATE, value);
            }
        }

        public int PacketLoss
        {
            get
            {
                return GetCtl(LibOpus.EncoderCtl.GET_PACKET_LOSS_PERC);
            }
            set
            {
                SetCtl(LibOpus.EncoderCtl.SET_PACKET_LOSS_PERC, value);
            }
        }

        public LibOpus.FEC FEC
        {
            get
            {
                return (LibOpus.FEC)GetCtl(LibOpus.EncoderCtl.GET_INBAND_FEC);
            }
            set
            {
                SetCtl(LibOpus.EncoderCtl.SET_INBAND_FEC, (int)value);
            }
        }

        public bool DTX
        {
            get
            {
                return GetCtl(LibOpus.EncoderCtl.GET_DTX) == 1;
            }
            set
            {
                SetCtl(LibOpus.EncoderCtl.SET_DTX, value ? 1 : 0);
            }
        }

        public LibOpus.Signal Signal
        {
            get
            {
                return (LibOpus.Signal)GetCtl(LibOpus.EncoderCtl.GET_SIGNAL);
            }
            set
            {
                SetCtl(LibOpus.EncoderCtl.SET_SIGNAL, (int)value);
            }
        }

        public int LSB_Depth
        {
            get
            {
                return GetCtl(LibOpus.EncoderCtl.GET_LSB_DEPTH);
            }
            set
            {
                SetCtl(LibOpus.EncoderCtl.SET_LSB_DEPTH, value);
            }
        }

        unsafe public OpusEncoder(SampleStream parent)
        {
            this.parent = parent;
            original = new(parent.frequency);

            frameSize = (int)(0.02 * parent.frequency);

            encoder = Marshal.AllocHGlobal(LibOpus.opus_encoder_get_size(1));
            int err = LibOpus.opus_encoder_init(encoder, parent.frequency, 1, (int)LibOpus.Modes.VOIP);
            if (err != 0)
            {
                Marshal.FreeHGlobal(encoder);
                throw new Exception("libopus: " + Marshal.PtrToStringAnsi(LibOpus.opus_strerror(err)));
            }

            decoder = Marshal.AllocHGlobal(LibOpus.opus_decoder_get_size(1));
            err = LibOpus.opus_decoder_init(decoder, parent.frequency, 1);
            if (err != 0)
            {
                Marshal.FreeHGlobal(encoder);
                throw new Exception("libopus: " + Marshal.PtrToStringAnsi(LibOpus.opus_strerror(err)));
            }

            BitRate = 24000;
            //FEC = LibOpus.FEC.AGGRESSIVE;
            //DTX = true;
            Signal = LibOpus.Signal.VOICE;
            lookahead = GetCtl(LibOpus.EncoderCtl.GET_LOOKAHEAD);

            parent.OnData += DoEncode;
        }

        ~OpusEncoder()
        {
            parent.OnData -= DoEncode;
            Marshal.FreeHGlobal(encoder);
            Marshal.FreeHGlobal(decoder);
        }

        private void DoEncode(SampleStream parent)
        {
            if (!readLock.WaitOne(0))
                return;

            try
            {
                LinkedList<byte[]> frames = new();
                while (parent.Count() > frameSize)
                {
                    float[]? rawFrame = parent.Read(frameSize);
                    if (rawFrame == null)
                        return;

                    original.Write(rawFrame);
                    frames.AddLast(EncodeFrame(rawFrame));
                }

                if (frames.Count() > 0)
                {
                    Plugin.Instance.Logger.LogDebug("Writing Opus frame(s)");
                    Write(frames.ToArray());
                }
                else
                    Plugin.Instance.Logger.LogDebug("No Opus frames to write");
            }
            finally
            {
                readLock.ReleaseMutex();
            }
        }

        public void Close()
        {
            readLock.WaitOne();

            try
            {
                LinkedList<byte[]> frames = new();
                int count;
                while ((count = parent.Count()) > 0)
                {
                    float[]? rawFrame = parent.Read(count > frameSize ? frameSize : count);
                    if (rawFrame == null)
                        return;

                    original.Write(rawFrame);
                    if (rawFrame.Length < frameSize)
                        Array.Resize(ref rawFrame, frameSize);

                    frames.AddLast(EncodeFrame(rawFrame));
                }

                if (frames.Count() > 0)
                {
                    Plugin.Instance.Logger.LogDebug("Writing Opus frame(s)");
                    Write(frames.ToArray());
                }
                else
                    Plugin.Instance.Logger.LogDebug("No Opus frames to write");
            }
            finally
            {
                readLock.ReleaseMutex();
            }
        }

        private unsafe byte[] EncodeFrame(float[] samples)
        {
            byte[] frame = new byte[4000];

            int err = LibOpus.opus_encode_float(encoder, samples, frameSize, frame, 4000);
            if (err < 0)
                throw new Exception("libopus: " + Marshal.PtrToStringAnsi(LibOpus.opus_strerror(err)));

            Array.Resize(ref frame, err);

            if (decoder_good)
            {
                float[] decoded = new float[5760];
                err = LibOpus.opus_decode_float(decoder, frame, frame.Length, decoded, 5760, 0);

                if (err < 0)
                {
                    Plugin.Instance.Logger.LogWarning("WARNING: Decoder failure when verifying encoding. Disabling verification.");
                    decoder_good = false;
                }
                else
                {
                   if (err != frameSize)
                        throw new ValidationException();

                    Array.Resize(ref decoded, err);
                    for (int i = 0; i < frameSize; i++)
                    {
                        float error = decoded[i] - samples[i];

                        if (Math.Abs(error) > 0.2)
                            throw new ValidationException(samples[i], decoded[i]);
                    }
                }
            }

            return frame;
        }

        private void SetCtl(LibOpus.EncoderCtl ctl, int val)
        {
            int err = LibOpus.opus_encoder_ctl(encoder, (int)ctl, val);
            if (err != 0)
            {
                Marshal.FreeHGlobal(encoder);
                throw new Exception("libopus: " + Marshal.PtrToStringAnsi(LibOpus.opus_strerror(err)));
            }
        }

        private unsafe int GetCtl(LibOpus.EncoderCtl ctl)
        {
            int result = 0;
            int err = LibOpus.opus_encoder_ctl(encoder, (int)ctl, result);

            if (err != 0)
            {
                Marshal.FreeHGlobal(encoder);
                throw new Exception("libopus: " + Marshal.PtrToStringAnsi(LibOpus.opus_strerror(err)));
            }

            return result;
        }

        public class ValidationException: Exception
        {
            internal ValidationException(): base("encoder decoder length disagreement")
            {
            }

            internal ValidationException(float encoded, float decoded): base($"encoder decoder sample disagreement ({encoded}, {decoded})")
            {
            }
        };
    }
}
