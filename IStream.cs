using System;

namespace NuclearVOIP
{
    internal interface IStream<I, O>
    {
        public event Action<StreamArgs<O>>? OnData;

        public abstract void Write(I data);
        public abstract void Write(I[] data);
        public abstract O? Read();
        public abstract O[]? Read(int num);
        public abstract bool Empty();
        public abstract int Count();
    }
}
