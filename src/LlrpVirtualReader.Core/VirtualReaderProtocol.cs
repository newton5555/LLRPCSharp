using System.Net;
using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Messages;
using LlrpNet.Protocol.Registry;

namespace LlrpVirtualReader;

/// <summary>Contributes protocol codecs and device-side handlers to a virtual reader.</summary>
public interface IVirtualReaderProtocolModule
{
    /// <summary>Gets the stable module identifier.</summary>
    public string Id { get; }

    /// <summary>Gets the explicitly supported protocol versions.</summary>
    public IReadOnlySet<LlrpProtocolVersion> SupportedVersions { get; }

    /// <summary>Registers vendor or custom codecs before a host accepts connections.</summary>
    public void RegisterCodecs(LlrpCodecRegistry registry);

    /// <summary>Registers request handlers. Handlers are evaluated in registration order.</summary>
    public void RegisterHandlers(VirtualReaderHandlerRegistry registry);
}

/// <summary>Receives one decoded message and may return a response and asynchronous messages.</summary>
public interface IVirtualReaderMessageHandler
{
    /// <summary>Gets a diagnostic name for the handler.</summary>
    public string Name { get; }

    /// <summary>Returns whether the handler owns the supplied version/message pair.</summary>
    public bool CanHandle(LlrpProtocolVersion version, ILlrpMessage message);

    /// <summary>Handles one request in the isolated client session.</summary>
    public ValueTask<VirtualReaderDispatchResult> HandleAsync(
        VirtualReaderRequestContext context,
        ILlrpMessage message,
        CancellationToken cancellationToken);
}

/// <summary>Provides the shared host state and connection diagnostics to one message handler.</summary>
public sealed class VirtualReaderRequestContext
{
    internal VirtualReaderRequestContext(
        VirtualReaderHost host,
        ILlrpReaderDeviceBackend deviceBackend,
        string connectionId,
        LlrpProtocolVersion version,
        uint messageId)
    {
        Host = host;
        DeviceBackend = deviceBackend;
        ConnectionId = connectionId;
        Version = version;
        MessageId = messageId;
    }

    /// <summary>Gets the owning single-host runtime.</summary>
    public VirtualReaderHost Host { get; }

    /// <summary>Gets the host device backend. This is intentionally not a platform Manager instance directory.</summary>
    internal ILlrpReaderDeviceBackend DeviceBackend { get; }

    /// <summary>Gets the transport connection identifier.</summary>
    public string ConnectionId { get; }

    /// <summary>Gets the explicit protocol version of this request.</summary>
    public LlrpProtocolVersion Version { get; }

    /// <summary>Gets the request message identifier.</summary>
    public uint MessageId { get; }
}

/// <summary>Contains the response and optional device-initiated messages for one request.</summary>
public sealed record VirtualReaderDispatchResult(
    ILlrpMessage? Response,
    IReadOnlyList<ILlrpMessage> AdditionalMessages,
    bool CloseConnection = false,
    LlrpProtocolVersion? ResponseVersion = null,
    LlrpProtocolVersion? NextProtocolVersion = null)
{
    /// <summary>Creates a response-only result.</summary>
    public static VirtualReaderDispatchResult FromResponse(ILlrpMessage response) => new(response, []);

    /// <summary>Creates a response with messages sent immediately afterwards.</summary>
    public static VirtualReaderDispatchResult WithMessages(
        ILlrpMessage response,
        params ILlrpMessage[] additionalMessages) => new(response, additionalMessages);
}

/// <summary>Stores registered virtual reader message handlers.</summary>
public sealed class VirtualReaderHandlerRegistry
{
    private readonly List<IVirtualReaderMessageHandler> _handlers = [];

    /// <summary>Registers one handler.</summary>
    public void Add(IVirtualReaderMessageHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _handlers.Add(handler);
    }

    internal IReadOnlyList<IVirtualReaderMessageHandler> Handlers => _handlers;
}

/// <summary>Describes one currently connected virtual-reader client.</summary>
public sealed record VirtualReaderClientInfo(
    string ConnectionId,
    EndPoint? RemoteEndPoint,
    DateTimeOffset ConnectedAt,
    LlrpProtocolVersion? NegotiatedVersion,
    bool IsConnected);

/// <summary>Reports one host lifecycle transition.</summary>
public sealed class VirtualReaderLifecycleChangedEventArgs : EventArgs
{
    public VirtualReaderLifecycleChangedEventArgs(
        VirtualReaderLifecycleState previousState,
        VirtualReaderLifecycleState currentState,
        Exception? error = null)
    {
        PreviousState = previousState;
        CurrentState = currentState;
        Error = error;
    }

    public VirtualReaderLifecycleState PreviousState { get; }
    public VirtualReaderLifecycleState CurrentState { get; }
    public Exception? Error { get; }
}

/// <summary>Reports one client connection transition.</summary>
public sealed class VirtualReaderClientChangedEventArgs : EventArgs
{
    public VirtualReaderClientChangedEventArgs(VirtualReaderClientInfo client, bool connected)
    {
        Client = client;
        Connected = connected;
    }

    public VirtualReaderClientInfo Client { get; }
    public bool Connected { get; }
}

/// <summary>Reports one decoded message-level diagnostic event.</summary>
public sealed class VirtualReaderMessageEventArgs : EventArgs
{
    public VirtualReaderMessageEventArgs(
        string connectionId,
        LlrpProtocolVersion version,
        ushort messageType,
        uint messageId,
        bool incoming,
        string? detail = null)
    {
        ConnectionId = connectionId;
        Version = version;
        MessageType = messageType;
        MessageId = messageId;
        Incoming = incoming;
        Detail = detail;
    }

    public string ConnectionId { get; }
    public LlrpProtocolVersion Version { get; }
    public ushort MessageType { get; }
    public uint MessageId { get; }
    public bool Incoming { get; }
    public string? Detail { get; }
}
