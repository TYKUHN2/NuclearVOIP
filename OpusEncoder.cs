using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace NuclearVOIP
{
    internal class OpusEncoder: GenericStream<byte[]>
    {
        private readonly SampleStream parent;
        private readonly IntPtr encoder;
        private readonly int frameSize;

        private readonly Mutex readLock = new(); // Currently not internally reordered so locking is needed

        public readonly int lookahead;

        public OpusEncoder(SampleStream parent)
        {
            this.parent = parent;
            frameSize = (int)(0.02 * parent.frequency);

            encoder = Marshal.AllocHGlobal(LibOpus.opus_encoder_get_size(1));
            int err = LibOpus.opus_encoder_init(encoder, parent.frequency, 1, (int)LibOpus.Modes.VOIP);
            if (err != 0)
            {
                Marshal.FreeHGlobal(encoder);
                throw new Exception("libopus: " + Marshal.PtrToStringAnsi(LibOpus.opus_strerror(err)));
            }

            SetCtl(LibOpus.EncoderCtl.SET_BITRATE, 24000);

            SetCtl(LibOpus.EncoderCtl.SET_INBAND_FEC, 1);

            SetCtl(LibOpus.EncoderCtl.SET_DTX, 1);

            SetCtl(LibOpus.EncoderCtl.SET_SIGNAL, (int)LibOpus.Signal.VOICE);

            GetCtl(LibOpus.EncoderCtl.GET_LOOKAHEAD, ref lookahead);

            parent.OnData += DoEncode;
        }

        ~OpusEncoder()
        {
            parent.OnData -= DoEncode;
            Marshal.FreeHGlobal(encoder);
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

                    byte[] frame = new byte[60]; // 20ms at 24kbps

                    //IntPtr buf = Marshal.AllocHGlobal(60);

                    int err = LibOpus.opus_encode_float(encoder, rawFrame, frameSize, frame, 60);

                    if (err < 0)
                    {
                        //Marshal.FreeHGlobal(buf);
                        throw new Exception("libopus: " + Marshal.PtrToStringAnsi(LibOpus.opus_strerror(err)));
                    }
                    else
                    {
                        //Marshal.Copy(buf, frame, 0, err);
                        //Marshal.FreeHGlobal(buf);
                    }

                    Array.Resize(ref frame, err);

                    if (frame.Length > 0)
                        frames.AddLast(frame);
                }

                if (frames.Count() > 0)
                {
                    Plugin.Instance!.Logger.LogDebug("Writing Opus frame(s)");
                    Write(frames.ToArray());
                }
                else
                    Plugin.Instance!.Logger.LogDebug("No Opus frames to write");
            }
            finally
            {
                readLock.ReleaseMutex();
            }
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
        private void GetCtl(LibOpus.EncoderCtl ctl, ref int val)
        {
            int err = LibOpus.opus_encoder_ctl(encoder, (int)ctl, ref val);
            if (err != 0)
            {
                Marshal.FreeHGlobal(encoder);
                throw new Exception("libopus: " + Marshal.PtrToStringAnsi(LibOpus.opus_strerror(err)));
            }
        }
    }
}
