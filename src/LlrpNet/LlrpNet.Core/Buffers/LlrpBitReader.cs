namespace LlrpNet.Core.Buffers;

/// <summary>
/// Reads MSB-first bit fields from an LLRP byte buffer.
/// </summary>
public ref struct LlrpBitReader
{
    private readonly ReadOnlySpan<byte> _buffer;
    private long _bitPosition;

    /// <summary>
    /// Initializes a new bit reader at the first, most-significant bit of <paramref name="buffer"/>.
    /// </summary>
    /// <param name="buffer">The buffer to read. The buffer is not copied.</param>
    public LlrpBitReader(ReadOnlySpan<byte> buffer)
        : this(buffer, 0)
    {
    }

    /// <summary>
    /// Initializes a new bit reader at a specified bit offset.
    /// </summary>
    /// <param name="buffer">The buffer to read. The buffer is not copied.</param>
    /// <param name="bitOffset">The zero-based offset of the first bit to read.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="bitOffset"/> is outside the inclusive range from zero through the buffer length in bits.
    /// </exception>
    public LlrpBitReader(ReadOnlySpan<byte> buffer, long bitOffset)
    {
        long lengthInBits = (long)buffer.Length * 8;
        if ((ulong)bitOffset > (ulong)lengthInBits)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bitOffset),
                bitOffset,
                "The bit offset must identify a position within the buffer or its end.");
        }

        _buffer = buffer;
        _bitPosition = bitOffset;
    }

    /// <summary>
    /// Gets the total number of bits in the buffer.
    /// </summary>
    public readonly long LengthInBits => (long)_buffer.Length * 8;

    /// <summary>
    /// Gets the zero-based offset of the next bit to read.
    /// </summary>
    public readonly long BitPosition => _bitPosition;

    /// <summary>
    /// Gets the number of unread bits.
    /// </summary>
    public readonly long RemainingBits => LengthInBits - _bitPosition;

    /// <summary>
    /// Gets a value indicating whether the reader has consumed all available bits.
    /// </summary>
    public readonly bool End => _bitPosition == LengthInBits;

    /// <summary>
    /// Reads one bit.
    /// </summary>
    /// <returns><see langword="true"/> for one; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="EndOfStreamException">The buffer contains no unread bits.</exception>
    public bool ReadBit()
    {
        EnsureAvailable(1);

        int byteIndex = (int)(_bitPosition >> 3);
        int bitIndex = (int)(_bitPosition & 7);
        bool value = (_buffer[byteIndex] & (0x80 >> bitIndex)) != 0;
        _bitPosition++;
        return value;
    }

    /// <summary>
    /// Reads up to 64 bits in MSB-first order.
    /// </summary>
    /// <param name="bitCount">The number of bits to read, from zero through 64.</param>
    /// <returns>
    /// The decoded unsigned value. The first bit read becomes the most-significant bit of the requested field.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="bitCount"/> is outside the range from zero through 64.</exception>
    /// <exception cref="EndOfStreamException">The buffer does not contain the requested number of bits.</exception>
    public ulong ReadBits(int bitCount)
    {
        ValidateBitCount(bitCount);
        EnsureAvailable(bitCount);

        ulong value = 0;
        for (int index = 0; index < bitCount; index++)
        {
            int byteIndex = (int)(_bitPosition >> 3);
            int bitIndex = (int)(_bitPosition & 7);
            value = (value << 1) | (uint)((_buffer[byteIndex] >> (7 - bitIndex)) & 1);
            _bitPosition++;
        }

        return value;
    }

    /// <summary>
    /// Advances the reader by the specified number of bits.
    /// </summary>
    /// <param name="bitCount">The number of bits to skip.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="bitCount"/> is negative.</exception>
    /// <exception cref="EndOfStreamException">The buffer does not contain the requested number of bits.</exception>
    public void Advance(long bitCount)
    {
        if (bitCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bitCount), bitCount, "The bit count cannot be negative.");
        }

        EnsureAvailable(bitCount);
        _bitPosition += bitCount;
    }

    private static void ValidateBitCount(int bitCount)
    {
        if ((uint)bitCount > 64)
        {
            throw new ArgumentOutOfRangeException(nameof(bitCount), bitCount, "The bit count must be from zero through 64.");
        }
    }

    private readonly void EnsureAvailable(long bitCount)
    {
        if (bitCount > RemainingBits)
        {
            throw new EndOfStreamException(
                $"The LLRP bit buffer is truncated at bit {_bitPosition}: requested {bitCount} bit(s), but only {RemainingBits} remain.");
        }
    }
}
