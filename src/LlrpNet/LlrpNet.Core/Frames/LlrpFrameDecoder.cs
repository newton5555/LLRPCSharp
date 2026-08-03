using System.Buffers;
using LlrpNet.Core.Protocol;

namespace LlrpNet.Core.Frames;

/// <summary>
/// Splits a stream buffer into complete LLRP messages using the length in the common header.
/// </summary>
public sealed class LlrpFrameDecoder
{
    /// <summary>
    /// The default defensive limit for one LLRP message: 16 MiB.
    /// </summary>
    public const uint DefaultMaximumFrameLength = 16 * 1024 * 1024;

    /// <summary>
    /// Initializes a new instance of the <see cref="LlrpFrameDecoder"/> class.
    /// </summary>
    /// <param name="maximumFrameLength">The largest accepted complete LLRP message.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The configured limit is smaller than the fixed LLRP message header.
    /// </exception>
    public LlrpFrameDecoder(uint maximumFrameLength = DefaultMaximumFrameLength)
    {
        if (maximumFrameLength < LlrpMessageHeader.EncodedLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumFrameLength),
                maximumFrameLength,
                $"The maximum frame length must be at least {LlrpMessageHeader.EncodedLength} octets.");
        }

        MaximumFrameLength = maximumFrameLength;
    }

    /// <summary>
    /// Gets the largest accepted complete LLRP message.
    /// </summary>
    public uint MaximumFrameLength { get; }

    /// <summary>
    /// Attempts to remove one complete frame from the beginning of <paramref name="buffer"/>.
    /// </summary>
    /// <param name="buffer">
    /// The available stream buffer. On success it is advanced past the returned frame; when more data is
    /// required it is unchanged.
    /// </param>
    /// <param name="frame">The complete frame when this method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when a complete frame was available; otherwise <see langword="false"/>.</returns>
    /// <exception cref="LlrpProtocolException">The header is malformed or the frame exceeds the configured limit.</exception>
    /// <remarks>
    /// The returned sequence references the same memory as <paramref name="buffer"/>. A pipeline consumer must
    /// finish observing or copy it before advancing the underlying <c>PipeReader</c>.
    /// </remarks>
    public bool TryReadFrame(ref ReadOnlySequence<byte> buffer, out LlrpFrame frame)
    {
        if (buffer.Length < LlrpMessageHeader.EncodedLength)
        {
            frame = default;
            return false;
        }

        Span<byte> headerBytes = stackalloc byte[LlrpMessageHeader.EncodedLength];
        buffer.Slice(0, LlrpMessageHeader.EncodedLength).CopyTo(headerBytes);
        LlrpMessageHeader header = LlrpMessageHeader.Decode(headerBytes);

        if (header.MessageLength > MaximumFrameLength)
        {
            throw new LlrpProtocolException(
                LlrpProtocolErrorCode.FrameTooLarge,
                $"The encoded frame length {header.MessageLength} exceeds the configured limit {MaximumFrameLength}.");
        }

        if (buffer.Length < header.MessageLength)
        {
            frame = default;
            return false;
        }

        ReadOnlySequence<byte> frameBytes = buffer.Slice(0, header.MessageLength);
        buffer = buffer.Slice(header.MessageLength);
        frame = new LlrpFrame(header, frameBytes);
        return true;
    }
}

