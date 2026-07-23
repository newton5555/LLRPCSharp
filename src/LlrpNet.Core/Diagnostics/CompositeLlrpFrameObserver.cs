namespace LlrpNet.Core.Diagnostics;

/// <summary>
/// Invokes a fixed collection of frame observers in registration order.
/// </summary>
/// <remarks>
/// Observers are awaited one at a time. Caller cancellation stops delivery. An individual observer failure is
/// collected while later observers still run, so a failed capture sink cannot suppress a logging or metrics sink.
/// One failure is rethrown unchanged after delivery; multiple failures are reported as an aggregate. Consequently,
/// completion means that every non-cancelled observer has finished using the borrowed frame memory.
/// </remarks>
public sealed class CompositeLlrpFrameObserver : ILlrpFrameObserver
{
    private readonly ILlrpFrameObserver[] observers;

    /// <summary>
    /// Initializes a new instance of the <see cref="CompositeLlrpFrameObserver"/> class.
    /// </summary>
    /// <param name="observers">The observers to invoke, in order.</param>
    /// <exception cref="ArgumentNullException"><paramref name="observers"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="observers"/> contains a null element.</exception>
    public CompositeLlrpFrameObserver(params ILlrpFrameObserver[] observers)
    {
        ArgumentNullException.ThrowIfNull(observers);

        if (Array.Exists(observers, static observer => observer is null))
        {
            throw new ArgumentException("The observer collection cannot contain null elements.", nameof(observers));
        }

        this.observers = (ILlrpFrameObserver[])observers.Clone();
    }

    /// <inheritdoc />
    public async ValueTask ObserveAsync(
        LlrpFrameObservation observation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        List<Exception>? failures = null;
        foreach (ILlrpFrameObserver observer in observers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await observer.ObserveAsync(observation, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }
        }

        if (failures is [Exception failure])
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }

        if (failures is { Count: > 1 })
        {
            throw new AggregateException("Multiple LLRP frame observers failed.", failures);
        }
    }
}
