namespace NuclearVOIP
{
    public interface InStream<in T>
    {
        abstract void Write(T data);
        abstract void Write(T[] data);
    }
}
