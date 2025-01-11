using System;

namespace NuclearVOIP
{
    public interface OutStream<T>
    {
        event Action<StreamArgs<T>>? OnData;

        abstract T? Read();
        abstract T[]? Read(int num);
        bool Empty()
        {
            return Count() == 0;
        }

        abstract int Count();
        abstract void Pipe(InStream<T>? stream);
    }
}
