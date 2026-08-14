namespace LlrpSdk;

/// <summary>
/// Version-neutral LLRP bit-vector conversion shared by the version-specific protocol compilers.
/// </summary>
internal static class LlrpWireBits
{
    /// <summary>
    /// Expands packed bytes into a bit list; a zero bit length uses every packed bit
    /// (standard semantics: array length == bit count). The first bit of the first byte
    /// becomes the first element.
    /// </summary>
    public static IReadOnlyList<bool> ToBits(ReadOnlySpan<byte> bytes, ushort bitLength)
    {
        int length = bitLength == 0 ? checked(bytes.Length * 8) : bitLength;
        var bits = new bool[length];
        for (int i = 0; i < length; i++)
        {
            bits[i] = (bytes[i / 8] & (1 << (7 - (i % 8)))) != 0;
        }

        return bits;
    }
}
