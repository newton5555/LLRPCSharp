using LlrpNet.Core.Frames;
using LlrpNet.Core.Protocol;

namespace LlrpNet.Core.Transport;

/// <summary>
/// Configures an LLRP TCP client transport.
/// </summary>
public sealed record LlrpTcpTransportOptions
{
    private static readonly TimeSpan MaximumTimerTimeout =
        TimeSpan.FromMilliseconds(uint.MaxValue - 1d);

    /// <summary>
    /// The standard IANA-assigned LLRP TCP port.
    /// </summary>
    public const int DefaultPort = 5084;

    /// <summary>
    /// Gets the reader hostname or IP address.
    /// </summary>
    public required string Host { get; init; }

    /// <summary>
    /// Gets the reader TCP port.
    /// </summary>
    public int Port { get; init; } = DefaultPort;

    /// <summary>
    /// Gets the maximum duration of one TCP connection attempt.
    /// </summary>
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Gets the maximum time allowed to assemble a frame after its first octet arrives.
    /// </summary>
    /// <remarks>
    /// This does not impose an idle timeout between frames. Use <see cref="Timeout.InfiniteTimeSpan"/> to disable
    /// protection against peers that start a header or body and then stop transmitting.
    /// </remarks>
    public TimeSpan FrameAssemblyTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Gets the defensive upper bound for one complete LLRP message.
    /// </summary>
    public uint MaximumFrameLength { get; init; } = LlrpFrameDecoder.DefaultMaximumFrameLength;

    /// <summary>
    /// Gets a value indicating whether complete frame hex may be emitted at Trace level.
    /// </summary>
    /// <remarks>
    /// Frames can contain access passwords and other sensitive values. This option is therefore disabled by default.
    /// </remarks>
    public bool LogFrameHex { get; init; }

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Host))
        {
            throw new ArgumentException("An LLRP reader hostname or IP address is required.", nameof(Host));
        }

        if (Port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(Port), Port, "The TCP port must be from 1 through 65535.");
        }

        if ((ConnectTimeout <= TimeSpan.Zero || ConnectTimeout > MaximumTimerTimeout) &&
            ConnectTimeout != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ConnectTimeout),
                ConnectTimeout,
                $"The connection timeout must be positive, no greater than {MaximumTimerTimeout}, or infinite.");
        }

        if ((FrameAssemblyTimeout <= TimeSpan.Zero || FrameAssemblyTimeout > MaximumTimerTimeout) &&
            FrameAssemblyTimeout != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(
                nameof(FrameAssemblyTimeout),
                FrameAssemblyTimeout,
                $"The frame assembly timeout must be positive, no greater than {MaximumTimerTimeout}, or infinite.");
        }

        if (MaximumFrameLength is < LlrpMessageHeader.EncodedLength or > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaximumFrameLength),
                MaximumFrameLength,
                $"The maximum frame length must be from {LlrpMessageHeader.EncodedLength} through {int.MaxValue} octets.");
        }
    }
}

