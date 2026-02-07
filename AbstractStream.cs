using System;
using System.Threading;

namespace NuclearVOIP
{
    public class AbstractStream<T> : InStream<T>, OutStream<T>
    {
        private InStream<T>? consumer;

        protected class Node(T data)
        {
            public readonly T data = data;
            public Node? next;
        }

        protected Node? head;
        protected Node? tail;

        public event Action<StreamArgs<T>>? OnData;

        public virtual void Write(T data)
        {
            Write([data]);
        }

        public virtual void Write(T[] data)
        {
            if (data.Length == 0)
                return;

            if (consumer != null)
            {
                consumer.Write(data);
                return;
            }

            StreamArgs<T> args = new(data);
            OnData?.Invoke(args);

            if (!args.Handled)
                _Write(data);
        }

        /// <summary>
        /// Write to the storage without invoking the event system.
        /// Designed to be called from derived classes which monitor the event system.
        /// </summary>
        /// <param name="data">Data to write</param>
        protected void _Write(T[] data)
        {
            Node ourHead = new(data[0]);
            Node curNode = ourHead;
            for (int i = 1; i < data.Length; i++)
            {
                curNode.next = new(data[i]);
                curNode = curNode.next;
            }

            Node? old = Interlocked.Exchange(ref tail, curNode);
            Interlocked.CompareExchange(ref head, ourHead, null);
            if (old != null)
                old.next = ourHead;
        }

        public virtual T? Read()
        {
            if (head == null)
                return default;

            Node? ourNode = head;
            while (ourNode != null && ourNode != Interlocked.CompareExchange(ref head, ourNode.next, ourNode))
                ourNode = head;

            if (ourNode != null)
            {
                Interlocked.CompareExchange(ref tail, null, ourNode);

                return ourNode.data;
            }
            else
                return default;
        }

        public virtual T[]? Read(int num)
        {
            if (num == 0)
                return null;

            loop:
            if (head == null)
                return null;

            Node ourHead = head;
            Node? endNode = head;
            for (int i = 0; i < num; i++)
            {
                if (endNode == null)
                    break;
                else
                    endNode = endNode.next;
            }

            if (Interlocked.CompareExchange(ref head, endNode, ourHead) != ourHead)
                goto loop;

            if (endNode == null)
                Interlocked.CompareExchange(ref tail, null, null);

            Node curNode = ourHead;
            T[] values = new T[num];
            for (int i = 0; i < num; i++)
            {
                values[i] = curNode.data;
                curNode = curNode.next!;
            }

            return values;
        }

        public bool Empty()
        {
            return head == null;
        }

        public int Count()
        {
            int count = 0;
            for (Node? curNode = head; curNode != null; curNode = curNode.next)
                count++;

            return count;
        }

        public void Pipe(InStream<T>? stream)
        {
            if (stream == null)
                consumer = null;
            else
            {
                T[]? prefix = Read(Count());
                if (prefix != null)
                    stream.Write(prefix);

                consumer = stream;
            }
        }
    }
}
