namespace LlrpNet.Core.Session;

/// <summary>
/// Indicates that an in-flight LLRP transaction could not complete because its session disconnected.
/// </summary>
public sealed class LlrpSessionDisconnectedException : IOException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LlrpSessionDisconnectedException"/> class.
    /// </summary>
    /// <param name="connectionId">The transport connection identifier.</param>
    /// <param name="message">A description of the disconnection.</param>
    /// <param name="innerException">The transport or receive failure, when available.</param>
    public LlrpSessionDisconnectedException(
        string connectionId,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        ConnectionId = connectionId;
    }

    /// <summary>
    /// Gets the identifier of the transport connection that was interrupted.
    /// </summary>
    public string ConnectionId { get; }
}
