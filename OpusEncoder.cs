using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace NuclearVOIP
{
    internal class OpusEncoder: AbstractTransform<float, byte[]>
    {
        private const bool DECODER_TEST = true;
        private readonly IntPtr encoder;
        private readonly IntPtr decoder;
        private bool decoder_good = true;
        private readonly int frameSize;
        private readonly Mutex readLock = new(); // Currently not internally reordered so locking is needed

        float[] leftover;

        public readonly int lookahead;

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

        unsafe public OpusEncoder(int frequency)
        {
            frameSize = (int)(0.02 * frequency);

            encoder = Marshal.AllocHGlobal(LibOpus.opus_encoder_get_size(1));
            int err = LibOpus.opus_encoder_init(encoder, frequency, 1, (int)LibOpus.Modes.VOIP);
            if (err != 0)
            {
                Marshal.FreeHGlobal(encoder);
                throw new LibOpus.OpusException(err);
            }

            if (DECODER_TEST)
            {
                decoder = Marshal.AllocHGlobal(LibOpus.opus_decoder_get_size(1));
                err = LibOpus.opus_decoder_init(decoder, frequency, 1);
                if (err != 0)
                {
                    Marshal.FreeHGlobal(encoder);
                    Marshal.FreeHGlobal(decoder);
                    throw new LibOpus.OpusException(err);
                }
            }

            BitRate = 24000;
            //FEC = LibOpus.FEC.AGGRESSIVE;
            //DTX = true;
            Signal = LibOpus.Signal.VOICE;
            lookahead = GetCtl(LibOpus.EncoderCtl.GET_LOOKAHEAD);
        }

        ~OpusEncoder()
        {
            Marshal.FreeHGlobal(encoder);

            if (DECODER_TEST)
                Marshal.FreeHGlobal(decoder);
        }

        private void DoEncode(StreamArgs<float> args)
        {
            if (!readLock.WaitOne(0))
                return;

            try
            {
                LinkedList<byte[]> frames = new();
                while (parent.Count() >= frameSize)
                {
                    float[]? rawFrame = parent.Read(frameSize);
                    if (rawFrame == null)
                        return;

                    frames.AddLast(EncodeFrame(rawFrame));
                }

                if (parent.Count() + args.data.Length >= frameSize)
                {
                    args.Handle();

                    float[]? prefix = parent.Read(parent.Count());
                    float[][] rawFrames = new float[args.data.Length / frameSize + 1][];
                    rawFrames[0] = [..prefix, ..args.data.Take(frameSize - (prefix?.Length ?? 0))];

                    for (int i = 1; i < rawFrames.Length; i++)
                        rawFrames[i] = args.data.Skip((i * frameSize) - (prefix?.Length ?? 0)).Take(frameSize).ToArray();

                    if (rawFrames[^1].Length != frameSize)
                    {
                        parent.Write(rawFrames[^1]);
                        Array.Resize(ref rawFrames, rawFrames.Length - 1);
                    }

                    foreach (float[] rawFrame in rawFrames)
                        frames.AddLast(EncodeFrame(rawFrame));
                }

                if (frames.Count() > 0)
                {
                    Plugin.Logger.LogDebug("Writing Opus frame(s)");
                    Write(frames.ToArray());
                }
                else
                    Plugin.Logger.LogDebug("No Opus frames to write");
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

                    if (rawFrame.Length < frameSize)
                        Array.Resize(ref rawFrame, frameSize);

                    frames.AddLast(EncodeFrame(rawFrame));
                }

                if (frames.Count() > 0)
                {
                    Plugin.Logger.LogDebug("Writing Opus frame(s)");
                    Write(frames.ToArray());
                }
                else
                    Plugin.Logger.LogDebug("No Opus frames to write");
            }
            finally
            {
                readLock.ReleaseMutex();
            }
        }

        protected override byte[][] Transform(float[] data)
        {
            float[] rawFrame = [.. leftover, ..data];
            byte[][] frames = new byte[rawFrame.Length / frameSize][];

            for (int i = 0; i < frames.Length; i++)
            {
                int offset = i * frameSize;
                frames[i] = EncodeFrame(rawFrame[offset..(offset + frameSize)]);
            }

            int leftoverSize = rawFrame.Length % frameSize;
            if (leftoverSize != 0)
                leftover = rawFrame[^(leftoverSize + 1)..];

            return frames;
        }

        private unsafe byte[] EncodeFrame(float[] samples)
        {
            byte[] frame = new byte[4000];

            int err = LibOpus.opus_encode_float(encoder, samples, frameSize, frame, 4000);
            if (err < 0)
                throw new LibOpus.OpusException(err);

            Array.Resize(ref frame, err);

            if (DECODER_TEST && decoder_good)
            {
                float[] decoded = new float[5760];
                err = LibOpus.opus_decode_float(decoder, frame, frame.Length, decoded, 5760, 0);

                if (err < 0)
                {
                    Plugin.Logger.LogWarning("WARNING: Decoder failure when verifying encoding. Disabling verification.");
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
                        {
                            ValidationException exception = new ValidationException(samples[i], decoded[i]);
                            //throw exception;
                            Plugin.Logger.LogWarning($"Error: {exception.Message} error = {error}. Disabling verification.");
                            decoder_good = false;
                        }
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
                throw new LibOpus.OpusException(err);
            }
        }

        private unsafe int GetCtl(LibOpus.EncoderCtl ctl)
        {
            int err = LibOpus.opus_encoder_ctl(encoder, (int)ctl, out int result);

            if (err != 0)
            {
                Marshal.FreeHGlobal(encoder);
                throw new LibOpus.OpusException(err);
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
