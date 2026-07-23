namespace LlrpNet.Core.Diagnostics;

/// <summary>
/// Observes complete, unmodified LLRP frames at the transport boundary.
/// </summary>
/// <remarks>
/// <para>
/// Implementations should avoid unbounded blocking. An implementation that queues work must copy borrowed frame
/// bytes before this method completes unless it finishes consuming those bytes synchronously.
/// </para>
/// <para>
/// Frame observation is best-effort diagnostics, not part of network success. Transport implementations isolate
/// observer exceptions so a frame that was already written or read is not reported as an I/O failure. Direct callers
/// and observer compositions can still receive exceptions from this method.
/// </para>
/// <para>
/// A transport may invoke transmit and receive observations concurrently. Implementations must therefore be safe for
/// concurrent calls. Observations for serialized transmit writes are delivered in the same order as those writes.
/// </para>
/// </remarks>
public interface ILlrpFrameObserver
{
    /// <summary>
    /// Observes one complete frame.
    /// </summary>
    /// <param name="observation">The borrowed frame and its transport metadata.</param>
    /// <param name="cancellationToken">A token that requests cancellation of observation.</param>
    /// <returns>An operation that completes when the observer no longer needs the borrowed frame memory.</returns>
    public ValueTask ObserveAsync(
        LlrpFrameObservation observation,
        CancellationToken cancellationToken = default);
}
