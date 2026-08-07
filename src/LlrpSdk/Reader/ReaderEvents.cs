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
    /// <param name="deviceInitiatedClose">Whether the reader itself requested the connection close.</param>
    internal ReaderConnectionChangedEventArgs(
        ReaderConnectionState previousState,
        ReaderConnectionState currentState,
        Exception? error,
        bool deviceInitiatedClose = false)
    {
        PreviousState = previousState;
        CurrentState = currentState;
        Error = error;
        DeviceInitiatedClose = deviceInitiatedClose;
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
    /// Gets whether the reader itself requested the connection close by sending a CLOSE_CONNECTION message.
    /// </summary>
    /// <remarks>
    /// When <see langword="true"/> the transition was caused by a device-initiated graceful close (for example a
    /// reader restart or an administrative action) rather than by a network failure; applications can use this to
    /// distinguish intentional device shutdown from unexpected link loss.
    /// </remarks>
    public bool DeviceInitiatedClose { get; }

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

/// <summary>
/// Describes a GPI pin state change notification from the reader.
/// </summary>
public sealed class GpiChangedEventArgs : EventArgs
{
    internal GpiChangedEventArgs(ushort portNumber, bool state)
    {
        PortNumber = portNumber;
        State = state;
        Timestamp = DateTimeOffset.UtcNow;
    }

    /// <summary>Gets the GPI port number that changed state.</summary>
    public ushort PortNumber { get; }

    /// <summary>Gets the new electrical state of the GPI port (true = High, false = Low).</summary>
    public bool State { get; }

    /// <summary>Gets the UTC time of the state change event.</summary>
    public DateTimeOffset Timestamp { get; }
}

/// <summary>Describes an antenna connection-state notification from the reader.</summary>
public sealed class AntennaChangedEventArgs : EventArgs
{
    internal AntennaChangedEventArgs(ushort antennaId, bool isConnected)
    {
        AntennaId = antennaId;
        IsConnected = isConnected;
        Timestamp = DateTimeOffset.UtcNow;
    }

    /// <summary>Gets the physical antenna port that changed state.</summary>
    public ushort AntennaId { get; }

    /// <summary>Gets whether the antenna is now connected.</summary>
    public bool IsConnected { get; }

    /// <summary>Gets when the SDK observed the event.</summary>
    public DateTimeOffset Timestamp { get; }
}

/// <summary>
/// Describes a reader-internal exception notification (ReaderExceptionEvent) from the reader.
/// </summary>
public sealed class ReaderExceptionEventArgs : EventArgs
{
    internal ReaderExceptionEventArgs(
        string message,
        uint? roSpecId,
        ushort? specIndex,
        ushort? inventoryParameterSpecId,
        ushort? antennaId,
        uint? accessSpecId,
        ushort? opSpecId)
    {
        Message = message;
        ROSpecId = roSpecId;
        SpecIndex = specIndex;
        InventoryParameterSpecId = inventoryParameterSpecId;
        AntennaId = antennaId;
        AccessSpecId = accessSpecId;
        OpSpecId = opSpecId;
        Timestamp = DateTimeOffset.UtcNow;
    }

    /// <summary>Gets the reader-supplied exception description.</summary>
    public string Message { get; }

    /// <summary>Gets the ROSpec the exception occurred in, when the reader supplied one.</summary>
    public uint? ROSpecId { get; }

    /// <summary>Gets the spec index within the ROSpec, when supplied.</summary>
    public ushort? SpecIndex { get; }

    /// <summary>Gets the inventory parameter spec identifier, when supplied.</summary>
    public ushort? InventoryParameterSpecId { get; }

    /// <summary>Gets the antenna the exception is associated with, when supplied.</summary>
    public ushort? AntennaId { get; }

    /// <summary>Gets the access spec identifier, when supplied.</summary>
    public uint? AccessSpecId { get; }

    /// <summary>Gets the op spec identifier, when supplied.</summary>
    public ushort? OpSpecId { get; }

    /// <summary>Gets the UTC time at which the SDK observed the exception.</summary>
    public DateTimeOffset Timestamp { get; }
}

/// <summary>Describes a reader tag-report buffer level warning.</summary>
public sealed class ReportBufferWarningEventArgs : EventArgs
{
    internal ReportBufferWarningEventArgs(byte percentageFull)
    {
        PercentageFull = percentageFull;
        Timestamp = DateTimeOffset.UtcNow;
    }

    /// <summary>Gets the reader-reported percentage of the report buffer currently in use.</summary>
    public byte PercentageFull { get; }

    /// <summary>Gets when the SDK observed the warning.</summary>
    public DateTimeOffset Timestamp { get; }
}

/// <summary>Describes an opt-in SDK keepalive liveness timeout.</summary>
public sealed class KeepaliveTimeoutEventArgs : EventArgs
{
    internal KeepaliveTimeoutEventArgs(TimeSpan timeout, DateTimeOffset lastReceivedAt)
    {
        Timeout = timeout;
        LastReceivedAt = lastReceivedAt;
        Timestamp = DateTimeOffset.UtcNow;
    }

    /// <summary>Gets the configured maximum silence between reader keepalives.</summary>
    public TimeSpan Timeout { get; }

    /// <summary>Gets the last keepalive observation time, or the start of ready state when none arrived.</summary>
    public DateTimeOffset LastReceivedAt { get; }

    /// <summary>Gets when the SDK detected the timeout.</summary>
    public DateTimeOffset Timestamp { get; }
}

/// <summary>Describes dropped tag reports on the SDK connection-level report stream.</summary>
public sealed class TagReportOverflowEventArgs : EventArgs
{
    internal TagReportOverflowEventArgs(int bufferCapacity, long totalDropped)
    {
        BufferCapacity = bufferCapacity;
        TotalDropped = totalDropped;
        Timestamp = DateTimeOffset.UtcNow;
    }

    /// <summary>Gets the bounded capacity of the connection-level tag-report buffer.</summary>
    public int BufferCapacity { get; }

    /// <summary>Gets the total number of tag reports dropped since the reader was created.</summary>
    public long TotalDropped { get; }

    /// <summary>Gets when the SDK dropped the report.</summary>
    public DateTimeOffset Timestamp { get; }
}
