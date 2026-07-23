namespace LlrpNet.Core.Session;

/// <summary>
/// Configures request handling for an <see cref="LlrpSession"/>.
/// </summary>
public sealed class LlrpSessionOptions
{
    private static readonly TimeSpan MaximumTimerTimeout =
        TimeSpan.FromMilliseconds(uint.MaxValue - 1d);

    /// <summary>
    /// Gets the timeout applied when a transaction does not provide an explicit timeout.
    /// </summary>
    /// <remarks>
    /// Use <see cref="Timeout.InfiniteTimeSpan"/> to disable the manager-enforced timeout.
    /// </remarks>
    public TimeSpan DefaultRequestTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Gets the safety window during which a timed-out or caller-cancelled message identifier cannot be reused on
    /// the same connection generation.
    /// </summary>
    public TimeSpan MessageIdReuseQuarantine { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets the maximum number of complete reader-initiated frames retained for consumers.
    /// </summary>
    public int UnsolicitedFrameCapacity { get; init; } = 1024;

    /// <summary>
    /// Gets the action taken when <see cref="UnsolicitedFrameCapacity"/> is exhausted.
    /// </summary>
    public LlrpUnsolicitedFrameOverflowPolicy UnsolicitedFrameOverflowPolicy { get; init; } =
        LlrpUnsolicitedFrameOverflowPolicy.FaultConnection;

    internal void Validate()
    {
        if ((DefaultRequestTimeout < TimeSpan.Zero ||
             DefaultRequestTimeout > MaximumTimerTimeout) &&
            DefaultRequestTimeout != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(
                nameof(DefaultRequestTimeout),
                DefaultRequestTimeout,
                $"The default request timeout must be non-negative, no greater than {MaximumTimerTimeout}, or Timeout.InfiniteTimeSpan.");
        }

        if (UnsolicitedFrameCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(UnsolicitedFrameCapacity),
                UnsolicitedFrameCapacity,
                "The unsolicited-frame capacity must be positive.");
        }

        if (MessageIdReuseQuarantine < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MessageIdReuseQuarantine),
                MessageIdReuseQuarantine,
                "The message identifier reuse quarantine cannot be negative.");
        }

        if (!Enum.IsDefined(UnsolicitedFrameOverflowPolicy))
        {
            throw new ArgumentOutOfRangeException(
                nameof(UnsolicitedFrameOverflowPolicy),
                UnsolicitedFrameOverflowPolicy,
                "The unsolicited-frame overflow policy is not defined.");
        }
    }
}
