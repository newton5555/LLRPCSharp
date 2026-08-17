using System.Net;
using LlrpDevice.Abstractions;
using LlrpDevice.Server;
using LlrpDevice.Virtual;

namespace LlrpDevice.Virtual.Hosting;

/// <summary>Lifecycle state of one hosted virtual LLRP device.</summary>
public enum VirtualLlrpDeviceHostState
{
    Created,
    Starting,
    Running,
    Stopping,
    Stopped,
    Faulted,
}

/// <summary>Composes one virtual device implementation with one generic LLRP server.</summary>
public sealed record VirtualLlrpDeviceHostOptions
{
    /// <summary>Gets the generic LLRP server options for this one device endpoint.</summary>
    public LlrpDeviceServerOptions Server { get; init; } = new();

    /// <summary>Gets the virtual device behavior and tag-state options.</summary>
    public VirtualDeviceOptions Device { get; init; } = new();

    /// <summary>
    /// Gets the independent inventory data source. When omitted, the device
    /// options' direct tag list is used for SDK compatibility.
    /// </summary>
    public IVirtualInventoryDataSource? InventoryDataSource { get; init; }

    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Server);
        ArgumentNullException.ThrowIfNull(Device);
        if (InventoryDataSource is not null)
        {
            ArgumentNullException.ThrowIfNull(InventoryDataSource.Tags);
        }
    }
}

/// <summary>Application-facing lifecycle entry point for one virtual LLRP device.</summary>
public interface IVirtualLlrpDeviceHost : IAsyncDisposable
{
    /// <summary>Gets the current lifecycle state.</summary>
    public VirtualLlrpDeviceHostState State { get; }

    /// <summary>Gets the device-side behavior contract exposed by this host.</summary>
    public ILlrpDevice Device { get; }

    /// <summary>Gets the exact configured listen address.</summary>
    public IPAddress ListenAddress { get; }

    /// <summary>Gets the configured port. Zero means an ephemeral port was requested.</summary>
    public int ConfiguredPort { get; }

    /// <summary>Gets the actual bound port, or the configured port before startup.</summary>
    public int BoundPort { get; }

    /// <summary>Gets the current number of connected LLRP clients.</summary>
    public int ConnectedClientCount { get; }

    /// <summary>Raised after the host lifecycle state changes.</summary>
    public event EventHandler<VirtualLlrpDeviceHostLifecycleChangedEventArgs>? LifecycleChanged;

    /// <summary>Raised when one LLRP client is accepted or removed.</summary>
    public event EventHandler<VirtualLlrpDeviceHostClientChangedEventArgs>? ClientChanged;

    /// <summary>Raised for decoded incoming and outgoing LLRP message metadata.</summary>
    public event EventHandler<VirtualLlrpDeviceHostMessageObservedEventArgs>? MessageObserved;

    /// <summary>Starts the one-device LLRP listener.</summary>
    public Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>Stops the listener and all accepted client sessions.</summary>
    public Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>Stops and starts the same one-device runtime.</summary>
    public Task RestartAsync(CancellationToken cancellationToken = default);
}

/// <summary>Default one-device SDK facade for a virtual LLRP device.</summary>
public sealed class VirtualLlrpDeviceHost : IVirtualLlrpDeviceHost
{
    private readonly VirtualLlrpDeviceHostOptions _options;
    private readonly VirtualLlrpDevice _virtualDevice;
    private readonly LlrpDeviceServer _server;
    private int _disposeStarted;

    /// <summary>Creates one stopped virtual device host.</summary>
    public VirtualLlrpDeviceHost(VirtualLlrpDeviceHostOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        _options = options;
        _virtualDevice = new VirtualLlrpDevice(options.Device, options.InventoryDataSource);
        _server = new LlrpDeviceServer(options.Server, _virtualDevice);
        _server.LifecycleChanged += OnServerLifecycleChanged;
        _server.ClientChanged += OnServerClientChanged;
        _server.MessageObserved += OnServerMessageObserved;
    }

    /// <summary>Gets the immutable host composition options.</summary>
    public VirtualLlrpDeviceHostOptions Options => _options;

    /// <summary>Gets the current lifecycle state.</summary>
    public VirtualLlrpDeviceHostState State => MapState(_server.State);

    /// <summary>Gets the generic server for advanced device-side integrations.</summary>
    public LlrpDeviceServer Server => _server;

    /// <summary>Gets the concrete virtual behavior implementation.</summary>
    public VirtualLlrpDevice VirtualDevice => _virtualDevice;

    /// <inheritdoc />
    public ILlrpDevice Device => _virtualDevice;

    /// <inheritdoc />
    public IPAddress ListenAddress => _server.ListenAddress;

    /// <inheritdoc />
    public int ConfiguredPort => _options.Server.Port;

    /// <inheritdoc />
    public int BoundPort => _server.Port;

    /// <inheritdoc />
    public int ConnectedClientCount => _server.ConnectedClients.Count;

    /// <inheritdoc />
    public event EventHandler<VirtualLlrpDeviceHostLifecycleChangedEventArgs>? LifecycleChanged;

    /// <inheritdoc />
    public event EventHandler<VirtualLlrpDeviceHostClientChangedEventArgs>? ClientChanged;

    /// <inheritdoc />
    public event EventHandler<VirtualLlrpDeviceHostMessageObservedEventArgs>? MessageObserved;

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _server.StartAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _server.StopAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task RestartAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _server.RestartAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
        {
            return;
        }

        _server.LifecycleChanged -= OnServerLifecycleChanged;
        _server.ClientChanged -= OnServerClientChanged;
        _server.MessageObserved -= OnServerMessageObserved;
        await _server.DisposeAsync().ConfigureAwait(false);
    }

    private void OnServerLifecycleChanged(object? sender, LlrpDeviceServerLifecycleChangedEventArgs args)
    {
        LifecycleChanged?.Invoke(
            this,
            new VirtualLlrpDeviceHostLifecycleChangedEventArgs(
                MapState(args.PreviousState),
                MapState(args.CurrentState),
                args.Error));
    }

    private void OnServerClientChanged(object? sender, LlrpDeviceClientChangedEventArgs args)
    {
        ClientChanged?.Invoke(this, new VirtualLlrpDeviceHostClientChangedEventArgs(args.Client, args.Connected));
    }

    private void OnServerMessageObserved(object? sender, LlrpDeviceMessageEventArgs args)
    {
        MessageObserved?.Invoke(this, new VirtualLlrpDeviceHostMessageObservedEventArgs(
            args.ConnectionId,
            args.Version,
            args.MessageType,
            args.MessageId,
            args.Incoming,
            args.Detail));
    }

    private static VirtualLlrpDeviceHostState MapState(LlrpDeviceServerLifecycleState state) => state switch
    {
        LlrpDeviceServerLifecycleState.Created => VirtualLlrpDeviceHostState.Created,
        LlrpDeviceServerLifecycleState.Starting => VirtualLlrpDeviceHostState.Starting,
        LlrpDeviceServerLifecycleState.Running => VirtualLlrpDeviceHostState.Running,
        LlrpDeviceServerLifecycleState.Stopping => VirtualLlrpDeviceHostState.Stopping,
        LlrpDeviceServerLifecycleState.Faulted => VirtualLlrpDeviceHostState.Faulted,
        _ => VirtualLlrpDeviceHostState.Stopped,
    };

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposeStarted) != 0)
        {
            throw new ObjectDisposedException(nameof(VirtualLlrpDeviceHost));
        }
    }
}

/// <summary>Describes a one-device host lifecycle transition.</summary>
public sealed class VirtualLlrpDeviceHostLifecycleChangedEventArgs : EventArgs
{
    public VirtualLlrpDeviceHostLifecycleChangedEventArgs(
        VirtualLlrpDeviceHostState previousState,
        VirtualLlrpDeviceHostState currentState,
        Exception? error = null)
    {
        PreviousState = previousState;
        CurrentState = currentState;
        Error = error;
    }

    public VirtualLlrpDeviceHostState PreviousState { get; }
    public VirtualLlrpDeviceHostState CurrentState { get; }
    public Exception? Error { get; }
}

/// <summary>Describes one client change on a one-device host.</summary>
public sealed class VirtualLlrpDeviceHostClientChangedEventArgs : EventArgs
{
    public VirtualLlrpDeviceHostClientChangedEventArgs(LlrpDeviceClientInfo client, bool connected)
    {
        Client = client;
        Connected = connected;
    }

    public LlrpDeviceClientInfo Client { get; }
    public bool Connected { get; }
}

/// <summary>Describes one decoded incoming or outgoing LLRP message.</summary>
public sealed class VirtualLlrpDeviceHostMessageObservedEventArgs : EventArgs
{
    public VirtualLlrpDeviceHostMessageObservedEventArgs(
        string connectionId,
        LlrpNet.Core.Protocol.LlrpProtocolVersion version,
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
    public LlrpNet.Core.Protocol.LlrpProtocolVersion Version { get; }
    public ushort MessageType { get; }
    public uint MessageId { get; }
    public bool Incoming { get; }
    public string? Detail { get; }
}
