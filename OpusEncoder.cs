//#define DECODER_TEST

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
//using UnityEngine;

namespace NuclearVOIP
{
    internal class OpusEncoder: AbstractTransform<float, byte[]>
    {
        //private int frame = 0;
        private readonly IntPtr encoder;
        private readonly int frameSize;
        private readonly Mutex readLock = new(); // Currently not internally reordered so locking is needed
        private bool closed = false;

#if DECODER_TEST
        private readonly IntPtr decoder;
        private bool decoder_good = true;
#endif

        private float[]? leftover;

        public readonly int lookahead;

        public int BitRate
        {
            get
            {
                return GetCtl(LibOpus.EncoderCtl.GET_BITRATE);
            }
            set
            {
                SetCtl(LibOpus.EncoderCtl.SET_BITRATE, value == -1000 ? value : Math.Clamp(value, 500, 512000));
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
                SetCtl(LibOpus.EncoderCtl.SET_PACKET_LOSS_PERC, Math.Clamp(value, 0, 100));
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
                SetCtl(LibOpus.EncoderCtl.SET_LSB_DEPTH, Math.Clamp(value, 8, 24));
            }
        }

        // Use StopWatch to ensure we stay under 20ms, adjust complexity as needed
        public int Complexity
        {
            get
            {
                return GetCtl(LibOpus.EncoderCtl.GET_COMPLEXITY);
            }
            set
            {
                SetCtl(LibOpus.EncoderCtl.SET_COMPLEXITY, Math.Clamp(value, 0, 10));
            }
        }

        public OpusEncoder(int frequency)
        {
            frameSize = (int)(0.02 * frequency);

            encoder = Marshal.AllocHGlobal(LibOpus.opus_encoder_get_size(1));
            int err = LibOpus.opus_encoder_init(encoder, frequency, 1, (int)LibOpus.Modes.VOIP);
            if (err != 0)
            {
                Marshal.FreeHGlobal(encoder);
                throw new LibOpus.OpusException(err);
            }

#if DECODER_TEST
            decoder = Marshal.AllocHGlobal(LibOpus.opus_decoder_get_size(1));
            err = LibOpus.opus_decoder_init(decoder, frequency, 1);
            if (err != 0)
            {
                Marshal.FreeHGlobal(encoder);
                Marshal.FreeHGlobal(decoder);
                throw new LibOpus.OpusException(err);
            }
#endif

            BitRate = 24000;
            //FEC = LibOpus.FEC.AGGRESSIVE;
            //DTX = true;
            Signal = LibOpus.Signal.VOICE;
            lookahead = GetCtl(LibOpus.EncoderCtl.GET_LOOKAHEAD);
        }

        ~OpusEncoder()
        {
            Marshal.FreeHGlobal(encoder);

#if DECODER_TEST
            Marshal.FreeHGlobal(decoder);
#endif
        }

        private byte[][] DoEncode(StreamArgs<float> args)
        {
            if (!readLock.WaitOne(0))
                return [];

            try
            {
                args.Handle();

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

                Stopwatch sw = Stopwatch.StartNew();

                float[] rawFrames = leftover == null ? args.data : [..leftover, ..args.data];

                int mod = rawFrames.Length % frameSize;
                if (mod == 0)
                    leftover = null;
                else
                {
                    leftover = rawFrames[^mod..];
                    Array.Resize(ref rawFrames, rawFrames.Length - mod);
                }

                if (rawFrames.Length == 0)
                    return [];

                byte[][] encoded = new byte[rawFrames.Length / frameSize][];
                int offset = -frameSize;
                for (int i = 0; i < encoded.Length; i++)
                {
                    offset += frameSize;
                    encoded[i] = EncodeFrame(rawFrames[offset..(offset + frameSize)]);
                }

                sw.Stop();

                if (sw.ElapsedMilliseconds > 20)
                    Plugin.Logger.LogDebug("Warning! Opus encoding exceeded frametime!");

                return encoded;
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
                closed = true;

                if (leftover != null)
                {
                    Array.Resize(ref leftover, frameSize); // Should never be a frame size or bigger, else would have already encoded

                    _Write([EncodeFrame(leftover)]);
                }
            }
            finally
            {
                readLock.ReleaseMutex();
            }
        }

        override protected byte[][] Transform(float[] samples)
        {
            if (closed)
                throw new InvalidOperationException("OpusEncoder was closed");

            return DoEncode(new(samples));
        }

        private byte[] EncodeFrame(float[] samples)
        {
            byte[] frame = new byte[4000];

            int err = LibOpus.opus_encode_float(encoder, samples, frameSize, frame, 4000);
            if (err < 0)
                throw new LibOpus.OpusException(err);

            Array.Resize(ref frame, err);

#if DECODER_TEST
            if (decoder_good)
            {
                float[] decoded = new float[5760];
                err = LibOpus.opus_decode_float(decoder, frame, frame.Length, decoded, 5760, 0);

                if (err < 0)
                {
                    Plugin.Logger.LogWarning($"Decoder failure when verifying encoding. Disabling verification. {(new LibOpus.OpusException(err)).Message}");
                    decoder_good = false;
                }
                else
                {
                   if (err != frameSize)
                        throw new ValidationException();

                    Array.Resize(ref decoded, err);

                    LibOpus.opus_encoder_ctl(encoder, (int)LibOpus.EncoderCtl.GET_LOOKAHEAD, out int skip);
                    decoded = decoded[skip..];

                    for (int i = 0; i < frameSize - skip; i++)
                    {
                        float error = decoded[i] - samples[i];

                        if (Math.Abs(error) > 0.2)
                        {
                            ValidationException exception = new(samples[i], decoded[i]);
                            //throw exception;
                            Plugin.Logger.LogWarning($"{exception.Message} error = {error}. Disabling verification.");
                            decoder_good = false;
                        }
                    }
                }
            }
#endif

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

        private int GetCtl(LibOpus.EncoderCtl ctl)
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
