namespace Crc
{
    public class Crc32Base(uint polynomial, uint initialValue, uint finalXorValue, bool reflectInput, bool reflectOutput, uint? check = null): 
        CrcBase(32, polynomial, initialValue, finalXorValue, reflectInput, reflectOutput, check)
    {
    }
}