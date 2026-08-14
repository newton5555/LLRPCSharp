namespace LlrpSdk;

/// <summary>
/// Version-neutral LLRP bit-vector conversion shared by the version-specific protocol compilers.
/// </summary>
internal static class LlrpWireBits
{
    /// <summary>
    /// Expands packed bytes into a bit list; a zero bit length uses every packed bit
    /// (standard semantics: array length == bit count). The first bit of the first byte
    /// becomes the first element. The inverse of <see cref="BitsToBytes"/>.
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

    /// <summary>
    /// Packs an LLRP bit list into big-endian bytes; the first bit becomes the MSB of the first byte.
    /// The inverse of <see cref="ToBits"/>.
    /// </summary>
    public static byte[] BitsToBytes(IReadOnlyList<bool> bits) => bits.Chunk(8)
        .Select(group => Convert.ToByte(group.Select((bit, index) => bit ? 1 << (7 - index) : 0).Sum())).ToArray();
}
