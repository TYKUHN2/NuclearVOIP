using System;
using System.Threading;

namespace NuclearVOIP
{
    internal class SampleStream(int freq): OutStream<float> // Reimplemented for speed
    {
        private class Node(float[] samples)
        {
            public readonly float[] samples = samples;
            public volatile Node? next;
        }

        private Node? head;
        private Node? tail;

        private InStream<float>? consumer;

        public event Action<StreamArgs<float>>? OnData;

        public readonly int frequency = freq;

        public void Write(float[] samples)
        {
            if (consumer != null)
            {
                consumer.Write(samples);
                return;
            }

            StreamArgs<float> args = new(samples);
            OnData?.Invoke(args);

            if (!args.Handled)
            {
                if (samples.Length == 0)
                    return;

                Node node = new(samples);
                Node? old = Interlocked.Exchange(ref tail, node);
                Node? _ = Interlocked.CompareExchange(ref head, node, null);
                if (old != null)
                    old.next = node;
            }
        }

        public float Read()
        {
            float[]? samples = Read(1) ?? throw new InvalidOperationException();
            return samples[0];
        }

        public float[]? Read(int numSamples)
        {
            float[] buf = new float[numSamples];

            loop:
            if (head == null)
                return null;
            Node ourHead = head;

            Node curNode = ourHead;
            int pos = 0;
            Node? newHead;
            while (true)
            {
                int toRead = Math.Min(numSamples - pos, curNode.samples.Length);
                Array.Copy(curNode.samples, 0, buf, pos, toRead);
                pos += toRead;

                if (pos < numSamples)
                {
                    if (curNode.next != null)
                        curNode = curNode.next;
                    else
                        return null;
                }
                else
                {
                    if (toRead < curNode.samples.Length) // Entire buffer not read
                    {
                        newHead = new(curNode.samples[toRead..])
                        {
                            next = curNode.next
                        };
                    }
                    else
                        newHead = curNode.next;

                    break;
                }
            }

            Node? oldHead = Interlocked.CompareExchange(ref head, newHead, ourHead);
            if (oldHead != ourHead)
                goto loop;

            Interlocked.CompareExchange(ref tail, newHead, curNode);

            return buf;
        }

        public bool Empty()
        {
            return head == null;
        }

        public int Count()
        {
            Node? curNode = head;
            int count = 0;
            while (curNode != null)
            {
                count += curNode.samples.Length;
                curNode = curNode.next;
            }

            return count;
        }

        public void Pipe(InStream<float>? stream)
        {
            if (stream == null)
                consumer = null;
            else
            {
                float[]? prefix = Read(Count());
                if (prefix != null)
                    stream.Write(prefix);

                consumer = stream;
            }
        }
    }
}
