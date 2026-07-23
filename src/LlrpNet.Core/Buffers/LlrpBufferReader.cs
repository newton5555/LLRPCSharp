using System.Buffers.Binary;

namespace LlrpNet.Core.Buffers;

/// <summary>
/// Reads primitive values from an LLRP byte buffer in network byte order.
/// </summary>
public ref struct LlrpBufferReader
{
    private readonly ReadOnlySpan<byte> _buffer;
    private int _position;

    /// <summary>
    /// Initializes a new reader over <paramref name="buffer"/>.
    /// </summary>
    /// <param name="buffer">The buffer to read. The buffer is not copied.</param>
    public LlrpBufferReader(ReadOnlySpan<byte> buffer)
    {
        _buffer = buffer;
        _position = 0;
    }

    /// <summary>
    /// Gets the total number of bytes in the buffer.
    /// </summary>
    public readonly int Length => _buffer.Length;

    /// <summary>
    /// Gets the zero-based offset of the next byte to read.
    /// </summary>
    public readonly int Position => _position;

    /// <summary>
    /// Gets the number of unread bytes.
    /// </summary>
    public readonly int Remaining => _buffer.Length - _position;

    /// <summary>
    /// Gets a value indicating whether the reader has consumed the complete buffer.
    /// </summary>
    public readonly bool End => _position == _buffer.Length;

    /// <summary>
    /// Reads one unsigned byte.
    /// </summary>
    /// <returns>The byte at the current position.</returns>
    /// <exception cref="EndOfStreamException">The buffer is truncated.</exception>
    public byte ReadByte()
    {
        EnsureAvailable(sizeof(byte));
        return _buffer[_position++];
    }

    /// <summary>
    /// Reads one signed byte.
    /// </summary>
    /// <returns>The signed byte at the current position.</returns>
    /// <exception cref="EndOfStreamException">The buffer is truncated.</exception>
    public sbyte ReadSByte()
    {
        return unchecked((sbyte)ReadByte());
    }

    /// <summary>
    /// Reads a 16-bit unsigned integer in network byte order.
    /// </summary>
    /// <returns>The decoded value.</returns>
    /// <exception cref="EndOfStreamException">The buffer is truncated.</exception>
    public ushort ReadUInt16()
    {
        return BinaryPrimitives.ReadUInt16BigEndian(ReadSpan(sizeof(ushort)));
    }

    /// <summary>
    /// Reads a 16-bit signed integer in network byte order.
    /// </summary>
    /// <returns>The decoded value.</returns>
    /// <exception cref="EndOfStreamException">The buffer is truncated.</exception>
    public short ReadInt16()
    {
        return BinaryPrimitives.ReadInt16BigEndian(ReadSpan(sizeof(short)));
    }

    /// <summary>
    /// Reads a 32-bit unsigned integer in network byte order.
    /// </summary>
    /// <returns>The decoded value.</returns>
    /// <exception cref="EndOfStreamException">The buffer is truncated.</exception>
    public uint ReadUInt32()
    {
        return BinaryPrimitives.ReadUInt32BigEndian(ReadSpan(sizeof(uint)));
    }

    /// <summary>
    /// Reads a 32-bit signed integer in network byte order.
    /// </summary>
    /// <returns>The decoded value.</returns>
    /// <exception cref="EndOfStreamException">The buffer is truncated.</exception>
    public int ReadInt32()
    {
        return BinaryPrimitives.ReadInt32BigEndian(ReadSpan(sizeof(int)));
    }

    /// <summary>
    /// Reads a 64-bit unsigned integer in network byte order.
    /// </summary>
    /// <returns>The decoded value.</returns>
    /// <exception cref="EndOfStreamException">The buffer is truncated.</exception>
    public ulong ReadUInt64()
    {
        return BinaryPrimitives.ReadUInt64BigEndian(ReadSpan(sizeof(ulong)));
    }

    /// <summary>
    /// Reads a 64-bit signed integer in network byte order.
    /// </summary>
    /// <returns>The decoded value.</returns>
    /// <exception cref="EndOfStreamException">The buffer is truncated.</exception>
    public long ReadInt64()
    {
        return BinaryPrimitives.ReadInt64BigEndian(ReadSpan(sizeof(long)));
    }

    /// <summary>
    /// Reads a contiguous sequence of bytes without allocating or copying it.
    /// </summary>
    /// <param name="length">The number of bytes to read.</param>
    /// <returns>A view over the requested bytes.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="length"/> is negative.</exception>
    /// <exception cref="EndOfStreamException">The buffer is truncated.</exception>
    public ReadOnlySpan<byte> ReadBytes(int length)
    {
        if (length < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length), length, "The byte count cannot be negative.");
        }

        return ReadSpan(length);
    }

    /// <summary>
    /// Advances the reader by the specified number of bytes.
    /// </summary>
    /// <param name="length">The number of bytes to skip.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="length"/> is negative.</exception>
    /// <exception cref="EndOfStreamException">The buffer is truncated.</exception>
    public void Advance(int length)
    {
        if (length < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length), length, "The byte count cannot be negative.");
        }

        EnsureAvailable(length);
        _position += length;
    }

    private ReadOnlySpan<byte> ReadSpan(int length)
    {
        EnsureAvailable(length);
        ReadOnlySpan<byte> result = _buffer.Slice(_position, length);
        _position += length;
        return result;
    }

    private readonly void EnsureAvailable(int length)
    {
        if (length > Remaining)
        {
            throw new EndOfStreamException(
                $"The LLRP buffer is truncated at byte {_position}: requested {length} byte(s), but only {Remaining} remain.");
        }
    }
}
