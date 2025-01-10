namespace NuclearVOIP
{
    public class StreamArgs<O>(O[] data)
    {
        public readonly O[] data = data;

        public bool Handled
        {
            get; private set;
        }

        public void Handle()
        {
            Handled = true;
        }
    }
}