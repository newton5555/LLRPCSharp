namespace LlrpNet.Core.Session;

/// <summary>
/// Describes why one connected generation of an <see cref="LlrpSession"/> ended.
/// </summary>
public sealed class LlrpSessionTermination
{
    private LlrpSessionTermination(bool wasRequested, Exception? error)
    {
        WasRequested = wasRequested;
        Error = error;
    }

    /// <summary>
    /// Gets a value indicating whether an explicit disconnect or disposal ended the connection.
    /// </summary>
    public bool WasRequested { get; }

    /// <summary>
    /// Gets the unexpected session failure, or <see langword="null"/> for an explicit shutdown.
    /// </summary>
    public Exception? Error { get; }

    internal static LlrpSessionTermination Requested { get; } = new(wasRequested: true, error: null);

    internal static LlrpSessionTermination Unexpected(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new LlrpSessionTermination(wasRequested: false, error);
    }
}
