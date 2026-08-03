namespace LlrpSdk;

/// <summary>
/// Describes the connection lifecycle of one <see cref="LlrpReader"/>.
/// </summary>
public enum ReaderConnectionState
{
    /// <summary>
    /// No reader session is connected.
    /// </summary>
    Disconnected,

    /// <summary>
    /// The transport and session are being connected.
    /// </summary>
    Connecting,

    /// <summary>
    /// The connected reader and SDK are negotiating a protocol version.
    /// </summary>
    Negotiating,

    /// <summary>
    /// Reader identity, capabilities, configuration, and extensions are being initialized.
    /// </summary>
    Initializing,

    /// <summary>
    /// The reader session can accept operations.
    /// </summary>
    /// <remarks>
    /// In the current M2 implementation this means that an LLRP 1.0.1 session can send and receive typed or raw
    /// messages and that GET_READER_CAPABILITIES(All) successfully initialized general identity and capabilities.
    /// Reader Event handling, true multi-version negotiation, and configuration initialization are not yet implied.
    /// </remarks>
    Ready,

    /// <summary>
    /// A previously connected session is being re-established.
    /// </summary>
    Reconnecting,

    /// <summary>
    /// The active session is being stopped.
    /// </summary>
    Disconnecting,

    /// <summary>
    /// A connection, disconnection, protocol-pump, or receive failure stopped normal operation.
    /// </summary>
    Faulted,
}
