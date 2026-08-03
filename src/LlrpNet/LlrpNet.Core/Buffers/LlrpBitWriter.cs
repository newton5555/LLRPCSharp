namespace LlrpNet.Core.Buffers;

/// <summary>
/// Writes MSB-first bit fields to an LLRP byte buffer.
/// </summary>
public ref struct LlrpBitWriter
{
    private readonly Span<byte> _buffer;
    private long _bitPosition;

    /// <summary>
    /// Initializes a new bit writer at the first, most-significant bit of <paramref name="buffer"/>.
    /// </summary>
    /// <param name="buffer">The destination buffer. The buffer is not copied.</param>
    public LlrpBitWriter(Span<byte> buffer)
        : this(buffer, 0)
    {
    }

    /// <summary>
    /// Initializes a new bit writer at a specified bit offset.
    /// </summary>
    /// <param name="buffer">The destination buffer. The buffer is not copied.</param>
    /// <param name="bitOffset">The zero-based offset of the first bit to write.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="bitOffset"/> is outside the inclusive range from zero through the buffer length in bits.
    /// </exception>
    public LlrpBitWriter(Span<byte> buffer, long bitOffset)
    {
        long capacityInBits = (long)buffer.Length * 8;
        if ((ulong)bitOffset > (ulong)capacityInBits)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bitOffset),
                bitOffset,
                "The bit offset must identify a position within the destination or its end.");
        }

        _buffer = buffer;
        _bitPosition = bitOffset;
    }

    /// <summary>
    /// Gets the total capacity of the destination in bits.
    /// </summary>
    public readonly long CapacityInBits => (long)_buffer.Length * 8;

    /// <summary>
    /// Gets the zero-based offset at which the next bit will be written.
    /// </summary>
    public readonly long BitPosition => _bitPosition;

    /// <summary>
    /// Gets the number of bits that can still be written.
    /// </summary>
    public readonly long RemainingBits => CapacityInBits - _bitPosition;

    /// <summary>
    /// Gets the number of bytes touched by bits from the start of the buffer through the current position.
    /// </summary>
    public readonly int BytesWritten => checked((int)((_bitPosition + 7) / 8));

    /// <summary>
    /// Gets a view over the bytes touched so far.
    /// </summary>
    public readonly ReadOnlySpan<byte> WrittenSpan => _buffer[..BytesWritten];

    /// <summary>
    /// Writes one bit at the current position.
    /// </summary>
    /// <param name="value"><see langword="true"/> to write one; otherwise, <see langword="false"/>.</param>
    /// <exception cref="InvalidOperationException">The destination has insufficient capacity.</exception>
    public void WriteBit(bool value)
    {
        EnsureCapacity(1);
        WriteBitUnchecked(value);
    }

    /// <summary>
    /// Writes up to 64 bits of an unsigned value in MSB-first order.
    /// </summary>
    /// <param name="value">The unsigned value to write.</param>
    /// <param name="bitCount">The field width in bits, from zero through 64.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="bitCount"/> is outside the range from zero through 64, or <paramref name="value"/> does not fit in the requested width.
    /// </exception>
    /// <exception cref="InvalidOperationException">The destination has insufficient capacity.</exception>
    public void WriteBits(ulong value, int bitCount)
    {
        ValidateValue(value, bitCount);
        EnsureCapacity(bitCount);

        for (int bitIndex = bitCount - 1; bitIndex >= 0; bitIndex--)
        {
            WriteBitUnchecked(((value >> bitIndex) & 1) != 0);
        }
    }

    /// <summary>
    /// Advances the writer by the specified number of bits without changing their contents.
    /// </summary>
    /// <param name="bitCount">The number of bits to skip.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="bitCount"/> is negative.</exception>
    /// <exception cref="InvalidOperationException">The destination has insufficient capacity.</exception>
    public void Advance(long bitCount)
    {
        if (bitCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bitCount), bitCount, "The bit count cannot be negative.");
        }

        EnsureCapacity(bitCount);
        _bitPosition += bitCount;
    }

    private void WriteBitUnchecked(bool value)
    {
        int byteIndex = (int)(_bitPosition >> 3);
        int bitIndex = (int)(_bitPosition & 7);
        byte mask = (byte)(0x80 >> bitIndex);

        if (value)
        {
            _buffer[byteIndex] |= mask;
        }
        else
        {
            _buffer[byteIndex] &= (byte)~mask;
        }

        _bitPosition++;
    }

    private static void ValidateValue(ulong value, int bitCount)
    {
        if ((uint)bitCount > 64)
        {
            throw new ArgumentOutOfRangeException(nameof(bitCount), bitCount, "The bit count must be from zero through 64.");
        }

        if (bitCount < 64 && (value >> bitCount) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "The value does not fit in the requested bit width.");
        }
    }

    private readonly void EnsureCapacity(long bitCount)
    {
        if (bitCount > RemainingBits)
        {
            throw new InvalidOperationException(
                $"The LLRP bit destination has insufficient capacity at bit {_bitPosition}: requested {bitCount} bit(s), but only {RemainingBits} remain.");
        }
    }
}
