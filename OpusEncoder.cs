using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using LibOpus;

namespace NuclearVOIP
{
    internal class OpusEncoder: AbstractTransform<float, byte[]>
    {
        //private int frame = 0;
        private readonly Encoder encoder;

        private readonly Mutex readLock = new(); // Currently not internally reordered so locking is needed
        private bool closed = false;

        private byte costi = 0;
        private readonly int[] costs = new int[6];

        private float[]? leftover;

        public int LookAhead
        {
            get => encoder.LookAhead;
        }

        public int BitRate
        {
            get => encoder.BitRate;
            set => encoder.BitRate = value;
        }

        public OpusTypes.FEC FEC
        {
            get => encoder.FEC;
            set => encoder.FEC = value;
        }

        public int PacketLoss
        {
            get => encoder.PacketLoss;
            set => encoder.PacketLoss = value;
        }

        public int DREDDuration
        {
            get => encoder.DREDDuration;
            set => encoder.DREDDuration = value;
        }

        public OpusEncoder(int frequency)
        {
            encoder = new Encoder(frequency, 1, 20, OpusTypes.Modes.VOIP)
            {
                //DTX = true,
                BitRate = 32000,
                Signal = OpusTypes.Signal.VOICE, // We only care about voice, use SILK and benefit from BWE=
                Bandwidth = OpusTypes.Bandwidth.WIDE, // BWE means we don't need to encode the full bandwidth
                Complexity = 10
            };
        }

        private byte[][] DoEncode(StreamArgs<float> args)
        {
            if (!readLock.WaitOne(0))
                return [];

            try
            {
                args.Handle();

                float[] rawFrames = leftover == null ? args.data : [..leftover, ..args.data];

                int mod = rawFrames.Length % encoder.FrameSize;
                if (mod == 0)
                    leftover = null;
                else
                {
                    leftover = rawFrames[^mod..];
                    Array.Resize(ref rawFrames, rawFrames.Length - mod);
                }

                if (rawFrames.Length == 0)
                    return [];

                byte[][] encoded = new byte[rawFrames.Length / encoder.FrameSize][];
                int offset = -encoder.FrameSize;
                for (int i = 0; i < encoded.Length; i++)
                {
                    offset += encoder.FrameSize;
                    encoded[i] = EncodeFrame(rawFrames[offset..(offset + encoder.FrameSize)]);
                }

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
                    Array.Resize(ref leftover, encoder.FrameSize); // Should never be a frame size or bigger, else would have already encoded

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

            Stopwatch sw = Stopwatch.StartNew();

            byte[] frame = encoder.Encode(samples);

            sw.Stop();

            costs[costi++] = (int)sw.ElapsedMilliseconds;

            if (costi == costs.Length)
            {
                costi = 0;

                int avgCost = (int)Math.Ceiling(costs.Average());

                if (avgCost > 40) // On average we are two or more packets too slow
                    encoder.Complexity -= 2;
                else if (avgCost > 20) // On average we are one packet too slow
                    encoder.Complexity -= 1;
            }

            return frame;
        }
    }
}
