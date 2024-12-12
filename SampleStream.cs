using System;
using System.Threading;

namespace NuclearVOIP
{
    internal sealed class SampleStream(int freq) // Cannot be GenericStream due to weird processing requirements
    {
        private class Node(float[] samples)
        {
            private static int lastId = 0;
            public readonly int id = Interlocked.Increment(ref lastId);
            public readonly float[] samples = samples;
            public volatile Node? next;
        }

        private Node? head;
        private Node? tail;

        public delegate void DataHandler(SampleStream listener);
        public event DataHandler? OnData;

        public readonly int frequency = freq;

        public void Write(float[] samples)
        {
            if (samples.Length == 0)
                return;

            Node node = new(samples);
            Node? old = Interlocked.Exchange(ref tail, node);
            Node? oldHead = Interlocked.CompareExchange(ref head, node, null);
            if (old != null)
                old.next = node;

            OnData?.Invoke(this);
        }

        public float[]? Read(int numSamples)
        {
            float[] buf = new float[numSamples];

            loop:
            if (head == null)
                return null;
            Node ourHead = head;

            Node curNode = ourHead;
            int lastPos = 0;
            int pos = 0;
            while (true)
            {
                int toRead = Math.Min(numSamples - pos, curNode.samples.Length);
                Array.Copy(curNode.samples, 0, buf, pos, toRead);

                lastPos = pos;
                pos += toRead;

                if (pos < numSamples)
                {
                    if (curNode.next != null)
                        curNode = curNode.next;
                    else
                        return null;
                }
                else
                    break;
            }

            int wasRead = pos - lastPos;
            Node? newHead;
            if (wasRead < curNode.samples.Length) // Entire buffer not read
            {
                float[] newArr = new float[curNode.samples.Length - wasRead];
                Array.Copy(curNode.samples, wasRead, newArr, 0, newArr.Length);

                newHead = new(newArr);
                newHead.next = curNode.next;
            }
            else
                newHead = curNode.next;

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
    }
}
