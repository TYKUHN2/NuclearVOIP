using System;

namespace NuclearVOIP
{
    public abstract class AbstractTransform<I, O>: InStream<I>, OutStream<O>
    {
        private readonly AbstractStream<O> storage = new();

        public event Action<StreamArgs<O>>? OnData;

        public AbstractTransform()
        {
            storage.OnData += _OnData;
        }

        private void _OnData(StreamArgs<O> args)
        {
            OnData?.Invoke(args);
        }

        public virtual void Write(I data)
        {
            Write([data]);
        }

        public virtual void Write(I[] data)
        {
            _Write(Transform(data));
        }

        /// <summary>
        /// Writes to the underlying storage.
        /// Designed to be called from a derived class to write outside of Transform.
        /// </summary>
        /// <param name="data">Data to write</param>
        protected virtual void _Write(O[] data)
        {
            storage.Write(data);
        }

        public virtual O? Read()
        {
            return storage.Read();
        }

        public virtual O[]? Read(int num)
        {
            return storage.Read(num);
        }

        public bool Empty()
        {
            return storage.Empty();
        }

        public int Count()
        {
            return storage.Count();
        }

        public void Pipe(InStream<O>? stream)
        {
            storage.Pipe(stream);
        }

        protected abstract O[] Transform(I[] data);
    }
}
