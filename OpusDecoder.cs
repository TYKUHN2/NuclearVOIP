using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace NuclearVOIP
{
    internal class OpusDecoder: IStream<byte[], float>
    {
        private readonly IntPtr decoder;

        private readonly SampleStream samples = new(48000);

        public event Action<StreamArgs<float>>? OnData;

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

        public void Write(byte[] packet)
        {
            float[] decoded = DoDecode(packet);
            StreamArgs<float> args = new(decoded);
            OnData?.Invoke(args);

            if (!args.Handled)
                samples.Write(decoded);
        }

        public void Write(byte[][] packets)
        {
            LinkedList<float[]> list = new();
            foreach (byte[] packet in packets)
                list.AddLast(DoDecode(packet));

            float[] decoded = list.SelectMany(a => a).ToArray();
            StreamArgs<float> args = new(decoded);
            OnData?.Invoke(args);

            if (!args.Handled)
                samples.Write(decoded);
        }

        public float Read()
        {
            throw new NotSupportedException();
        }

        public float[]? Read(int length) => samples.Read(length);

        public bool Empty() => samples.Empty();

        public int Count() => samples.Count();

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
    }
}
