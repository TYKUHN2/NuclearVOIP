using System.IO;
using System.Text;
using System;
using System.Threading;

namespace NuclearVOIP
{
    internal class OggOpus: GenericStream<byte[]>, IDisposable
    {
        private static ReadOnlySpan<byte> EncoderVendor => "NUCLEARVOIP"u8;
        private readonly FrameStream frames = new();
        private readonly Ogg.Stream oggStream;
        private readonly int samplesPerFrame;
        private int granulePos = 0;

        public readonly OpusEncoder encoder;

        public OggOpus(Ogg file, SampleStream parent, byte channels, int msPerPacket)
        {
            oggStream = new Ogg.Stream(file);
            samplesPerFrame = msPerPacket * 48;
            encoder = new(parent);
            base.Write(EncodeOpusHeaders(channels, (short)encoder.lookahead));

            frames.OnData += OnFrame;
            encoder.OnData += OnParent;
        }

        ~OggOpus()
        {
            encoder.OnData -= OnParent;
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

        public void Dispose() => Flush();

        public void Close() 
        {
            encoder.Close();
            byte[][]? data = frames.Read(frames.Count());
            if (data == null)
                return;

            base.Write(EncodeOpus(data, true));
        }

        private void OnParent(IStream<byte[], byte[]> _)
        {
            byte[][]? pFrames = encoder.Read(encoder.Count());
            if (pFrames == null) 
                return;

            frames.Write(pFrames);
        }

        private void OnFrame(IStream<byte[], byte[]> _)
        {
            int count = frames.Count() - 1; // Leave last frame for closing reasons
            if (count < 50)
                return;

            byte[][]? buf = frames.Read(count);
            if (buf == null)
                return;

            base.Write(EncodeOpus(buf, false));
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

        private class FrameStream: GenericStream<byte[]>
        {
            public int SafeBytes()
            {
                Node? curNode = head;
                int count = 0;
                while (curNode?.next != null)
                {
                    count += curNode.data.Length;
                    curNode = curNode.next;
                }

                return count;
            }
        }
    }
}
