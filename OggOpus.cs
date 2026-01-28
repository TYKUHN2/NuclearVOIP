using System.IO;
using System.Text;
using System;
using System.Threading;

namespace NuclearVOIP
{
    internal class OggOpus: AbstractStream<byte[]>, IDisposable
    {
        private static ReadOnlySpan<byte> EncoderVendor => "NUCLEARVOIP"u8;
        private readonly AbstractStream<byte[]> frames = new();
        private readonly Ogg.Stream oggStream;
        private readonly int samplesPerFrame;
        private int granulePos = 0;

        public readonly OpusEncoder encoder;

        public OggOpus(Ogg file, int frequency, byte channels, int msPerPacket)
        {
            oggStream = new Ogg.Stream(file);
            samplesPerFrame = msPerPacket * 48;
            encoder = new(frequency);
            base.Write(EncodeOpusHeaders(channels, (short)encoder.LookAhead));

            frames.OnData += OnFrame;
            encoder.Pipe(this);
        }

        public override void Write(byte[] data)
        {
            frames.Write(data);
        }

        public override void Write(byte[][] data)
        {
            frames.Write(data);
        }

        // Consider allowing a single frame to stay unflushed
        public void Flush()
        {
            byte[][]? data = frames.Read(frames.Count());
            if (data == null)
                return;

            base.Write(EncodeOpus(data, false));
        }

        public void Dispose() => Close();

        public void Close() 
        {
            encoder.Close();
            byte[][]? data = frames.Read(frames.Count());
            if (data == null)
                return;

            base.Write(EncodeOpus(data, true));
        }

        private void OnFrame(StreamArgs<byte[]> args)
        {
            int count = frames.Count();
            if (count + args.data.Length < 51) // Leave last frame for closing reasons
                return;

            args.Handle();

            byte[][]? prefix = frames.Read(count);
            byte[][]? buf = prefix == null ? args.data[..^1] : [..prefix, ..(args.data[..^1])];
            base.Write(EncodeOpus(buf, false));

            frames.Write(args.data[^1]);
        }

        private byte[][] EncodeOpusHeaders(byte channels, short preskip)
        {
            if (oggStream.Sequence != 0)
                throw new InvalidOperationException("OpusHeaders not first pages");

            byte[][] headers = new byte[2][];
            {
                byte[] buf = new byte[19];
                MemoryStream mem = new(buf);
                BinaryWriter writer = new(mem);

                writer.Write(Encoding.ASCII.GetBytes("OpusHead"));
                writer.Write((byte)1);
                writer.Write(channels);
                writer.Write(preskip);
                writer.Write(48000);
                writer.Write((short)0);
                writer.Write((byte)0);
                writer.Close();

                headers[0] = oggStream.EncodePage([buf], 0);
            }
            {
                byte[] buf = new byte[27];
                MemoryStream mem = new(buf);
                BinaryWriter writer = new(mem);

                writer.Write(Encoding.ASCII.GetBytes("OpusTags"));
                writer.Write(EncoderVendor.Length);
                writer.Write(EncoderVendor);
                writer.Write(0);
                writer.Close();

                headers[1] = oggStream.EncodePage([buf], 0);
            }

            return headers;
        }

        private long GetGranule(byte[][] segments)
        {
            int frames = 0;
            for (int i = segments.Length - 1; i >= 0; i--)
                if (segments[i].Length != 255)
                    frames++;

            if (frames == 0)
                return -1;

            return Interlocked.Add(ref granulePos, frames * samplesPerFrame);
        }

        private byte[][] EncodeOpus(byte[][] frames, bool end)
        {
            /*int endPoint = end ? frames.Length - 1 : frames.Length;
            for (int i = 0; i < endPoint; i++)
            {
                byte[] oldFrame = frames[i];
                int internalLength = oldFrame.Length - 1;
                switch (internalLength)
                {
                    case > 251:
                        frames[i] = new byte[oldFrame.Length + 2];
                        frames[i][0] = oldFrame[0];
                        Array.Copy(oldFrame, 1, frames[i], 3, oldFrame.Length - 1);

                        frames[i][1] = (byte)(internalLength & 0xFF);
                        frames[i][2] = (byte)((internalLength - frames[i][1]) >>> 2);

                        break;
                    case 0:
                        frames[i] = [0x00, 0x00];
                        break;
                    default:
                        frames[i] = new byte[oldFrame.Length + 1];
                        frames[i][0] = oldFrame[0];
                        Array.Copy(oldFrame, 1, frames[i], 2, oldFrame.Length - 1);

                        frames[i][1] = (byte)internalLength;
                        break;
                }
            }*/

            byte[][] segments = Ogg.Stream.SplitSegments(frames);
            byte[][] pages = new byte[(segments.Length / 255) + 1][];
            bool continuing = false;
            for (int i = 0; i < pages.Length; i++)
            {
                int nSegments = i == pages.Length - 1 ? segments.Length % 255 : 255;
                int index = i * 255;
                byte[][] subarray = segments[index..(index + nSegments)];

                pages[i] = oggStream.EncodePage(subarray, GetGranule(segments), continuing, end && (i == pages.Length - 1));
                continuing = subarray[^1].Length == 255;
            }

            return pages;
        }
    }
}
