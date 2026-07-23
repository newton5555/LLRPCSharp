namespace LlrpSdk;

/// <summary>
/// Describes one observable reader connection-state transition.
/// </summary>
public sealed class ReaderConnectionChangedEventArgs : EventArgs
{
    /// <summary>
    /// Initializes connection transition event data.
    /// </summary>
    /// <param name="previousState">The state before the transition.</param>
    /// <param name="currentState">The state after the transition.</param>
    /// <param name="error">The failure that caused the transition, when applicable.</param>
    internal ReaderConnectionChangedEventArgs(
        ReaderConnectionState previousState,
        ReaderConnectionState currentState,
        Exception? error)
    {
        PreviousState = previousState;
        CurrentState = currentState;
        Error = error;
        Timestamp = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Gets the state before the transition.
    /// </summary>
    public ReaderConnectionState PreviousState { get; }

    /// <summary>
    /// Gets the state after the transition.
    /// </summary>
    public ReaderConnectionState CurrentState { get; }

    /// <summary>
    /// Gets the failure that caused the transition, if any.
    /// </summary>
    public Exception? Error { get; }

    /// <summary>
    /// Gets the UTC time at which the SDK recorded the transition.
    /// </summary>
    public DateTimeOffset Timestamp { get; }
}

/// <summary>
/// Describes a reader lifecycle or background-pump error.
/// </summary>
public sealed class ReaderErrorEventArgs : EventArgs
{
    /// <summary>
    /// Initializes reader error event data.
    /// </summary>
    /// <param name="error">The observed failure.</param>
    /// <param name="connectionState">The connection state recorded for the failure.</param>
    internal ReaderErrorEventArgs(Exception error, ReaderConnectionState connectionState)
    {
        Error = error;
        ConnectionState = connectionState;
        Timestamp = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Gets the observed failure.
    /// </summary>
    public Exception Error { get; }

    /// <summary>
    /// Gets the connection state recorded for the failure.
    /// </summary>
    public ReaderConnectionState ConnectionState { get; }

    /// <summary>
    /// Gets the UTC time at which the SDK recorded the failure.
    /// </summary>
    public DateTimeOffset Timestamp { get; }
}
