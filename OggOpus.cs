using System.IO;
using System.Text;
using System;

namespace NuclearVOIP
{
    internal class OggOpus: GenericStream<byte[]>, IDisposable
    {
        private readonly GenericStream<byte[]> segments = new();
        private readonly GenericStream<byte[]> completeSegments = new();
        private readonly Ogg.Stream oggStream;
        private readonly int samplesPerFrame;

        public OggOpus(Ogg file, byte channels, short preskip, int msPerPacket)
        {
            oggStream = new Ogg.Stream(file);
            samplesPerFrame = msPerPacket * 48;

            completeSegments.OnData += OnSegsAvail;
            Write(EncodeOpusHeaders(channels, preskip));
        }

        public override void Write(byte[] data)
        {
            WriteInner(Ogg.Stream.SplitSegment(data));
        }

        public override void Write(byte[][] data)
        {
            WriteInner(Ogg.Stream.SplitSegments(data));
        }

        // UPDATE, NEEDS THREAD SAFETY
        private void WriteInner(byte[][] segs)
        {
            for (int i = segs.Length - 1; ; i--)
            {
                if (segs[i].Length != 255)
                {
                    if (i != segs.Length - 1)
                    {
                        byte[][] incomplete = new byte[segs.Length - i - 1][];
                        Array.Copy(segs[i], i, incomplete, 0, incomplete.Length);

                        segments.Write(incomplete);

                        Array.Resize(ref segs, i + 1);
                        completeSegments.Write(segs);
                    }
                    else
                        completeSegments.Write(segs);

                    break;
                }
            }

            segments.Write(segs);
        }

        // Could use better thread safety
        public void Flush()
        {
            byte[][]? segs = completeSegments.Read(completeSegments.Count());
            if (segs == null)
                return;

            Write(EncodeOpus(segs, false));
        }

        public void Dispose() => Close();

        // UPDATE, NEEDS THREAD SAFETY
        public void Close()
        {
            completeSegments.OnData -= OnSegsAvail;
            byte[][]? iSegs = segments.Read(segments.Count());
            if (iSegs != null)
                completeSegments.Write(iSegs);

            byte[][]? segs = completeSegments.Read(completeSegments.Count());
            if (segs == null)
                return;

            Write(EncodeOpus(segs, true));
        }

        private void OnSegsAvail(IStream<byte[], byte[]> _)
        {
            int count = completeSegments.Count();
            if (count < 255)
                return;

            byte[][]? segs = completeSegments.Read(count / 255 + 1);
            if (segs == null)
                return;

            Write(EncodeOpus(segs, false));
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

                headers[1] = oggStream.EncodePage([buf], 0);
            }

            {
                byte[] buf = new byte[27];
                MemoryStream mem = new(buf);
                BinaryWriter writer = new(mem);

                writer.Write(Encoding.ASCII.GetBytes("OpusTags"));
                writer.Write("NUCLEARVOIP".Length);
                writer.Write(Encoding.UTF8.GetBytes("NUCLEARVOIP"));
                writer.Write(0);
                writer.Close();

                headers[2] = oggStream.EncodePage([buf], 0);
            }

            return headers;
        }

        private long GetGranule(byte[][] segments)
        {
            int frames = 0;
            for (int i = segments.Length - 1; i >= 0; i--)
                if (segments[i].Length != 255)
                    frames++;

            return frames == 0 ? -1 : frames * samplesPerFrame;
        }

        private byte[][] EncodeOpus(byte[][] segments, bool end)
        {
            byte[][] pages = new byte[(segments.Length / 255) + 1][];
            bool continuing = false;
            for (int i = 0; i < pages.Length; i++)
            {
                int nSegments = i == pages.Length - 1 ? segments.Length % 255 : 255;
                byte[][] subarray = new byte[nSegments][];
                Array.Copy(segments, i * 255, subarray, 0, nSegments);

                pages[i] = oggStream.EncodePage(subarray, GetGranule(segments), continuing, end && i == pages.Length);
                continuing = subarray[subarray.Length - 1].Length == 255;
            }

            return pages;
        }
    }
}
