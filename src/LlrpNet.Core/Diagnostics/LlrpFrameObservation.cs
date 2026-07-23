namespace LlrpNet.Core.Diagnostics;

/// <summary>
/// Describes one complete LLRP frame observed at the transport boundary.
/// </summary>
/// <param name="Direction">The direction in which the frame crossed the boundary.</param>
/// <param name="Timestamp">The time at which the frame was observed.</param>
/// <param name="ConnectionId">The identifier of the transport connection.</param>
/// <param name="FrameBytes">The complete LLRP frame exactly as sent or received.</param>
/// <remarks>
/// <para>
/// <paramref name="FrameBytes"/> is borrowed memory. It is valid only until the
/// <see cref="ILlrpFrameObserver.ObserveAsync"/> operation that receives this value completes. An observer
/// that retains the bytes or processes them in background work must copy them before completing that operation.
/// </para>
/// <para>
/// The caller retains ownership of the backing memory and must not mutate, recycle, or release it while the
/// observation operation is incomplete. This contract lets a transport pass its existing frame buffer without
/// copying it, including when observation is disabled.
/// </para>
/// </remarks>
public readonly record struct LlrpFrameObservation(
    LlrpFrameDirection Direction,
    DateTimeOffset Timestamp,
    string ConnectionId,
    ReadOnlyMemory<byte> FrameBytes)
{
    /// <summary>
    /// Gets the monotonically increasing connection generation within the transport instance.
    /// </summary>
    /// <remarks>
    /// A reconnect keeps <see cref="ConnectionId"/> stable for log correlation and increments this value,
    /// allowing capture implementations to distinguish separate TCP byte streams.
    /// </remarks>
    public long ConnectionGeneration { get; init; }
}
