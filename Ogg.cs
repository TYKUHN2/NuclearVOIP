using Crc;
using System;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;

namespace NuclearVOIP
{
    internal class Ogg
    {
        static readonly Crc32Base hasher = new(0x04C11DB7, 0x0, 0x0, true, true, 0x89A1897F);

        private int streams = 0;

        public class Stream(Ogg file)
        {
            private readonly int serial = Interlocked.Increment(ref file.streams) - 1;

            private int _sequence = 0;
            public int Sequence
            {
                get { return _sequence; }
            }

            private int _finalized = 0;
            public bool Finalized
            {
                get { return _finalized == 0; }
            }

            public byte[] EncodePage(byte[][] segments, long granule, bool continued = false, bool final = false)
            {
                if (segments.Length > 255)
                    throw new ArgumentException("OggStream too many segments");

                int totalSegmentSize = 0;
                byte[] sizes = new byte[segments.Length];
                for (int i = 0; i < segments.Length; i++)
                {
                    int length = segments[i].Length;
                    if (length > 255)
                        throw new ArgumentException("OggStream segments cannot be larger than 255 bytes");

                    totalSegmentSize += length;
                    sizes[i] = (byte)length;
                }

                if (sizes.Length == 255)
                {
                    if (final && sizes[255] == 255)
                        throw new ArgumentException("OggStream final but continues");
                } else
                {
                    if (sizes[sizes.Length - 1] == 255)
                        //throw new ArgumentException("OggStream last segment is continued but there is spare space");
                        Debug.LogWarning("OggStream last segment is continued but there is spare space");
                }
                

                if ((final && Interlocked.Exchange(ref _finalized, int.MaxValue) == int.MaxValue) || Finalized)
                    throw new InvalidOperationException("Ogg.Stream finalized");

                int seq = Interlocked.Increment(ref _sequence) - 1;
                byte type = (byte)(seq == 0 ? 0x2 : 0x0);
                if (final)
                    type |= 0x4;
                if (continued)
                    type |= 0x1;

                byte[] page = new byte[27 + segments.Length + totalSegmentSize];
                MemoryStream stream = new(page);
                BinaryWriter writer = new(stream);

                writer.Write(Encoding.ASCII.GetBytes("OggS"));
                writer.Write((byte)0);
                writer.Write(type);
                writer.Write(granule);
                writer.Write(serial);
                writer.Write(seq);
                writer.Write(0);
                writer.Write((byte)segments.Length);
                writer.Flush();

                long pos = stream.Position;
                foreach (byte[] segment in segments)
                {
                    segment.CopyTo(page, pos);
                    pos += segment.Length;
                }

                writer.Seek(22, SeekOrigin.Begin);
                writer.Write(hasher.ComputeHash(page));
                writer.Close();

                return page;
            }

            public static byte[][] SplitSegments(byte[][] frames)
            {
                int nSegments = 0;
                foreach (byte[] frame in frames)
                    nSegments += (frame.Length / 255) + 1;

                int pos = 0;
                byte[][] segments = new byte[nSegments][];
                for (int i = 0; i < frames.Length; i++)
                {
                    byte[][] segment = SplitSegment(frames[i]);
                    segment.CopyTo(segments, pos);
                    pos += segment.Length;
                }

                return segments;
            }

            public static byte[][] SplitSegment(byte[] frame)
            {
                int nSegments = (frame.Length / 255) + 1;
                byte[][] segments = new byte[nSegments][];
                int pos = 0;
                for (int i = 0; i < nSegments; i++)
                {
                    if (pos == frame.Length)
                    {
                        segments[i] = [];
                        break;
                    }

                    segments[i] = new byte[i == nSegments ? frame.Length - pos : 255];
                    Array.Copy(frame, pos, segments[i], 0, segments[i].Length);
                    pos += segments[i].Length;
                }

                return segments;
            }
        }
    }
}
