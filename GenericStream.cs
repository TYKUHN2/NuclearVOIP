using System.Threading;

namespace NuclearVOIP
{
    internal class GenericStream<T>: IStream<T, T>
    {
        protected class Node(T data)
        {
            public readonly T data = data;
            public Node? next;
        }

        protected Node? head;
        protected Node? tail;

        public event IStream<T, T>.DataHandler? OnData;
        
        public virtual void Write(T data)
        {
            Node node = new(data);
            Node? old = Interlocked.Exchange(ref tail, node);
            Interlocked.CompareExchange(ref head, node, null);
            if (old != null)
                old.next = node;

            OnData?.Invoke(this);
        }

        public virtual void Write(T[] data)
        {
            if (data.Length == 0)
                return;

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

            OnData?.Invoke(this);
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

        public virtual bool Empty()
        {
            return head == null;
        }

        public virtual int Count()
        {
            int count = 0;
            for (Node? curNode = head; curNode != null; curNode = curNode.next)
                count++;

            return count;
        }
    }
}
