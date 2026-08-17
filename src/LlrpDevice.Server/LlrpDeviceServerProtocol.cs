using System.Net;
using LlrpDevice.Abstractions;
using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Messages;
using LlrpNet.Protocol.Registry;

namespace LlrpDevice.Server;

public interface ILlrpDeviceProtocolModule
{
    public string Id { get; }
    public IReadOnlySet<LlrpProtocolVersion> SupportedVersions { get; }
    public void RegisterCodecs(LlrpCodecRegistry registry);
    public void RegisterHandlers(LlrpDeviceHandlerRegistry registry);
}

public interface ILlrpDeviceMessageHandler
{
    public string Name { get; }
    public bool CanHandle(LlrpProtocolVersion version, ILlrpMessage message);
    public ValueTask<LlrpDeviceDispatchResult> HandleAsync(
        LlrpDeviceRequestContext context,
        ILlrpMessage message,
        CancellationToken cancellationToken);
}

public sealed class LlrpDeviceRequestContext
{
    internal LlrpDeviceRequestContext(
        LlrpDeviceServer server,
        ILlrpDevice device,
        string connectionId,
        LlrpProtocolVersion version,
        uint messageId)
    {
        Server = server;
        Device = device;
        ConnectionId = connectionId;
        Version = version;
        MessageId = messageId;
    }

    public LlrpDeviceServer Server { get; }
    public ILlrpDevice Device { get; }
    public string ConnectionId { get; }
    public LlrpProtocolVersion Version { get; }
    public uint MessageId { get; }
}

public sealed record LlrpDeviceDispatchResult(
    ILlrpMessage? Response,
    IReadOnlyList<ILlrpMessage> AdditionalMessages,
    bool CloseConnection = false,
    LlrpProtocolVersion? ResponseVersion = null,
    LlrpProtocolVersion? NextProtocolVersion = null)
{
    public static LlrpDeviceDispatchResult FromResponse(ILlrpMessage response) => new(response, []);

    public static LlrpDeviceDispatchResult WithMessages(
        ILlrpMessage response,
        params ILlrpMessage[] additionalMessages) => new(response, additionalMessages);
}

public sealed class LlrpDeviceHandlerRegistry
{
    private readonly List<ILlrpDeviceMessageHandler> _handlers = [];

    public void Add(ILlrpDeviceMessageHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _handlers.Add(handler);
    }

    internal IReadOnlyList<ILlrpDeviceMessageHandler> Handlers => _handlers;
}

public sealed record LlrpDeviceClientInfo(
    string ConnectionId,
    EndPoint? RemoteEndPoint,
    DateTimeOffset ConnectedAt,
    LlrpProtocolVersion? NegotiatedVersion,
    bool IsConnected);

public sealed class LlrpDeviceServerLifecycleChangedEventArgs : EventArgs
{
    public LlrpDeviceServerLifecycleChangedEventArgs(
        LlrpDeviceServerLifecycleState previousState,
        LlrpDeviceServerLifecycleState currentState,
        Exception? error = null)
    {
        PreviousState = previousState;
        CurrentState = currentState;
        Error = error;
    }

    public LlrpDeviceServerLifecycleState PreviousState { get; }
    public LlrpDeviceServerLifecycleState CurrentState { get; }
    public Exception? Error { get; }
}

public sealed class LlrpDeviceClientChangedEventArgs : EventArgs
{
    public LlrpDeviceClientChangedEventArgs(LlrpDeviceClientInfo client, bool connected)
    {
        Client = client;
        Connected = connected;
    }

    public LlrpDeviceClientInfo Client { get; }
    public bool Connected { get; }
}

public sealed class LlrpDeviceMessageEventArgs : EventArgs
{
    public LlrpDeviceMessageEventArgs(
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
