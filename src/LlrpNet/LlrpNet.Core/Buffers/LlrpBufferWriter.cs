using System.Buffers.Binary;

namespace LlrpNet.Core.Buffers;

/// <summary>
/// Writes primitive values to an LLRP byte buffer in network byte order.
/// </summary>
public ref struct LlrpBufferWriter
{
    private readonly Span<byte> _buffer;
    private int _position;

    /// <summary>
    /// Initializes a new writer over <paramref name="buffer"/>.
    /// </summary>
    /// <param name="buffer">The destination buffer. The buffer is not copied.</param>
    public LlrpBufferWriter(Span<byte> buffer)
    {
        _buffer = buffer;
        _position = 0;
    }

    /// <summary>
    /// Gets the total capacity of the destination buffer in bytes.
    /// </summary>
    public readonly int Capacity => _buffer.Length;

    /// <summary>
    /// Gets the zero-based offset at which the next byte will be written.
    /// </summary>
    public readonly int Position => _position;

    /// <summary>
    /// Gets the number of bytes that can still be written.
    /// </summary>
    public readonly int Remaining => _buffer.Length - _position;

    /// <summary>
    /// Gets a view over the bytes written so far.
    /// </summary>
    public readonly ReadOnlySpan<byte> WrittenSpan => _buffer[.._position];

    /// <summary>
    /// Writes one unsigned byte.
    /// </summary>
    /// <param name="value">The value to write.</param>
    /// <exception cref="InvalidOperationException">The destination has insufficient capacity.</exception>
    public void WriteByte(byte value)
    {
        EnsureCapacity(sizeof(byte));
        _buffer[_position++] = value;
    }

    /// <summary>
    /// Writes one signed byte.
    /// </summary>
    /// <param name="value">The value to write.</param>
    /// <exception cref="InvalidOperationException">The destination has insufficient capacity.</exception>
    public void WriteSByte(sbyte value)
    {
        WriteByte(unchecked((byte)value));
    }

    /// <summary>
    /// Writes a 16-bit unsigned integer in network byte order.
    /// </summary>
    /// <param name="value">The value to write.</param>
    /// <exception cref="InvalidOperationException">The destination has insufficient capacity.</exception>
    public void WriteUInt16(ushort value)
    {
        BinaryPrimitives.WriteUInt16BigEndian(GetDestination(sizeof(ushort)), value);
    }

    /// <summary>
    /// Writes a 16-bit signed integer in network byte order.
    /// </summary>
    /// <param name="value">The value to write.</param>
    /// <exception cref="InvalidOperationException">The destination has insufficient capacity.</exception>
    public void WriteInt16(short value)
    {
        BinaryPrimitives.WriteInt16BigEndian(GetDestination(sizeof(short)), value);
    }

    /// <summary>
    /// Writes a 32-bit unsigned integer in network byte order.
    /// </summary>
    /// <param name="value">The value to write.</param>
    /// <exception cref="InvalidOperationException">The destination has insufficient capacity.</exception>
    public void WriteUInt32(uint value)
    {
        BinaryPrimitives.WriteUInt32BigEndian(GetDestination(sizeof(uint)), value);
    }

    /// <summary>
    /// Writes a 32-bit signed integer in network byte order.
    /// </summary>
    /// <param name="value">The value to write.</param>
    /// <exception cref="InvalidOperationException">The destination has insufficient capacity.</exception>
    public void WriteInt32(int value)
    {
        BinaryPrimitives.WriteInt32BigEndian(GetDestination(sizeof(int)), value);
    }

    /// <summary>
    /// Writes a 64-bit unsigned integer in network byte order.
    /// </summary>
    /// <param name="value">The value to write.</param>
    /// <exception cref="InvalidOperationException">The destination has insufficient capacity.</exception>
    public void WriteUInt64(ulong value)
    {
        BinaryPrimitives.WriteUInt64BigEndian(GetDestination(sizeof(ulong)), value);
    }

    /// <summary>
    /// Writes a 64-bit signed integer in network byte order.
    /// </summary>
    /// <param name="value">The value to write.</param>
    /// <exception cref="InvalidOperationException">The destination has insufficient capacity.</exception>
    public void WriteInt64(long value)
    {
        BinaryPrimitives.WriteInt64BigEndian(GetDestination(sizeof(long)), value);
    }

    /// <summary>
    /// Copies a contiguous sequence of bytes into the destination buffer.
    /// </summary>
    /// <param name="value">The bytes to write.</param>
    /// <exception cref="InvalidOperationException">The destination has insufficient capacity.</exception>
    public void WriteBytes(ReadOnlySpan<byte> value)
    {
        Span<byte> destination = GetDestination(value.Length);
        value.CopyTo(destination);
    }

    /// <summary>
    /// Advances the writer by the specified number of bytes without changing their contents.
    /// </summary>
    /// <param name="length">The number of bytes to skip.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="length"/> is negative.</exception>
    /// <exception cref="InvalidOperationException">The destination has insufficient capacity.</exception>
    public void Advance(int length)
    {
        if (length < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length), length, "The byte count cannot be negative.");
        }

        EnsureCapacity(length);
        _position += length;
    }

    private Span<byte> GetDestination(int length)
    {
        EnsureCapacity(length);
        Span<byte> result = _buffer.Slice(_position, length);
        _position += length;
        return result;
    }

    private readonly void EnsureCapacity(int length)
    {
        if (length > Remaining)
        {
            throw new InvalidOperationException(
                $"The LLRP destination buffer has insufficient capacity at byte {_position}: requested {length} byte(s), but only {Remaining} remain.");
        }
    }
}
