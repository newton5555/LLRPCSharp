namespace LlrpNet.Core.Session;

/// <summary>
/// Reports that the bounded unsolicited-frame queue could not accept another reader-initiated message.
/// </summary>
public sealed class LlrpSessionBackpressureException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LlrpSessionBackpressureException"/> class.
    /// </summary>
    public LlrpSessionBackpressureException(string connectionId, int capacity)
        : base(
            $"LLRP session {connectionId} received more unsolicited frames than its bounded " +
            $"queue capacity of {capacity} can retain.")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        ConnectionId = connectionId;
        Capacity = capacity;
    }

    /// <summary>
    /// Gets the transport connection identifier.
    /// </summary>
    public string ConnectionId { get; }

    /// <summary>
    /// Gets the configured unsolicited-frame queue capacity.
    /// </summary>
    public int Capacity { get; }
}
