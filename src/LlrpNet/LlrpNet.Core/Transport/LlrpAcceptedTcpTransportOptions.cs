using LlrpNet.Core.Frames;
using LlrpNet.Core.Protocol;

namespace LlrpNet.Core.Transport;

/// <summary>
/// Configures the server side of one already accepted LLRP TCP connection.
/// </summary>
public sealed record LlrpAcceptedTcpTransportOptions
{
    private static readonly TimeSpan MaximumTimerTimeout =
        TimeSpan.FromMilliseconds(uint.MaxValue - 1d);

    /// <summary>Gets the maximum duration allowed to assemble one frame after its first octet arrives.</summary>
    public TimeSpan FrameAssemblyTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Gets the maximum time allowed between complete frames.</summary>
    public TimeSpan IdleTimeout { get; init; } = Timeout.InfiniteTimeSpan;

    /// <summary>Gets the defensive upper bound for one complete LLRP frame.</summary>
    public uint MaximumFrameLength { get; init; } = LlrpFrameDecoder.DefaultMaximumFrameLength;

    /// <summary>Gets whether complete frame hex is logged at Trace level.</summary>
    public bool LogFrameHex { get; init; }

    internal void Validate()
    {
        ValidateTimeout(FrameAssemblyTimeout, nameof(FrameAssemblyTimeout));
        ValidateTimeout(IdleTimeout, nameof(IdleTimeout));
        if (MaximumFrameLength is < LlrpMessageHeader.EncodedLength or > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaximumFrameLength),
                MaximumFrameLength,
                $"The maximum frame length must be from {LlrpMessageHeader.EncodedLength} through {int.MaxValue} octets.");
        }
    }

    private static void ValidateTimeout(TimeSpan timeout, string parameterName)
    {
        if ((timeout <= TimeSpan.Zero || timeout > MaximumTimerTimeout) &&
            timeout != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                timeout,
                $"The timeout must be positive, no greater than {MaximumTimerTimeout}, or infinite.");
        }
    }
}
