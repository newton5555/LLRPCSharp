namespace LlrpNet.Core.Diagnostics;

/// <summary>
/// An observer that discards frames without inspecting or copying them.
/// </summary>
public sealed class NullLlrpFrameObserver : ILlrpFrameObserver
{
    private NullLlrpFrameObserver()
    {
    }

    /// <summary>
    /// Gets the shared null observer instance.
    /// </summary>
    public static NullLlrpFrameObserver Instance { get; } = new();

    /// <inheritdoc />
    public ValueTask ObserveAsync(
        LlrpFrameObservation observation,
        CancellationToken cancellationToken = default)
    {
        return cancellationToken.IsCancellationRequested
            ? ValueTask.FromCanceled(cancellationToken)
            : ValueTask.CompletedTask;
    }
}
