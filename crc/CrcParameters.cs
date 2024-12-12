namespace Crc
{
    public class CrcParameters(byte size, ulong polynomial, ulong initialValue, ulong finalXorValue, bool reflectInput, bool reflectOutput, ulong? expectedCheck = null)
    {
        public byte Size { get; } = size;
        public ulong Polynomial { get; } = polynomial;
        public ulong InitialValue { get; } = initialValue;
        public ulong FinalXorValue { get; } = finalXorValue;
        public bool ReflectInput { get; } = reflectInput;
        public bool ReflectOutput { get; } = reflectOutput;
        public ulong? ExpectedCheck { get; } = expectedCheck;

        protected bool Equals(CrcParameters other)
        {
            return Size == other.Size && Polynomial == other.Polynomial && InitialValue == other.InitialValue && FinalXorValue == other.FinalXorValue && ReflectInput == other.ReflectInput && ReflectOutput == other.ReflectOutput && ExpectedCheck == other.ExpectedCheck;
        }

        public override bool Equals(object obj)
        {
            if (obj is null) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((CrcParameters)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = Size.GetHashCode();
                hashCode = (hashCode * 397) ^ Polynomial.GetHashCode();
                hashCode = (hashCode * 397) ^ InitialValue.GetHashCode();
                hashCode = (hashCode * 397) ^ FinalXorValue.GetHashCode();
                hashCode = (hashCode * 397) ^ ReflectInput.GetHashCode();
                hashCode = (hashCode * 397) ^ ReflectOutput.GetHashCode();
                hashCode = (hashCode * 397) ^ ExpectedCheck.GetHashCode();
                return hashCode;
            }
        }
    }
}