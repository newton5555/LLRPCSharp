using System.Net;
using LlrpDevice.Abstractions;
using LlrpDevice.Server;
using LlrpDevice.Virtual;
using LlrpNet.Core.Protocol;

namespace LlrpVirtualReader;

/// <summary>
/// Compatibility façade for the original Virtual Reader API.
/// </summary>
/// <remarks>
/// The façade owns no listener, protocol dispatcher, resource registry, report loop, or RF state.
/// It maps the legacy options and events to <see cref="LlrpDeviceServer"/> and a device implementation.
/// </remarks>
public sealed class VirtualReaderHost : IAsyncDisposable
{
    private readonly VirtualReaderOptions _options;
    private readonly ILlrpReaderDeviceBackend _legacyBackend;
    private readonly LlrpDeviceServer _server;

    public VirtualReaderHost(int port = 0, VirtualReaderOptions? options = null)
        : this(new VirtualReaderHostOptions
        {
            Port = port,
            ReaderOptions = options ?? new VirtualReaderOptions(),
        })
    {
    }

    public VirtualReaderHost(VirtualReaderHostOptions hostOptions)
    {
        ArgumentNullException.ThrowIfNull(hostOptions);
        hostOptions.Validate();
        _options = hostOptions.ReaderOptions with
        {
            TagSource = hostOptions.ReaderOptions.NormalizeLegacyTagSource(),
        };
        _legacyBackend = hostOptions.DeviceBackendFactory?.Invoke(_options)
            ?? new VirtualReaderDeviceBackend(_options);
        ILlrpDevice device = hostOptions.DeviceBackendFactory is null
            ? new VirtualLlrpDevice(LegacyDeviceOptionMapper.BuildVirtualDeviceOptions(_options))
            : new LegacyLlrpDeviceAdapter(_legacyBackend, _options);
        var modules = hostOptions.ProtocolModules
            .Select(module => (ILlrpDeviceProtocolModule)new LegacyProtocolModuleAdapter(module, this, _legacyBackend))
            .ToArray();
        _server = new LlrpDeviceServer(
            LegacyDeviceOptionMapper.BuildServerOptions(
                hostOptions with { ReaderOptions = _options },
                modules),
            device);
        _server.LifecycleChanged += OnServerLifecycleChanged;
        _server.ClientChanged += OnServerClientChanged;
        _server.MessageObserved += OnServerMessageObserved;
    }

    public VirtualReaderOptions Options => _options;

    public VirtualReaderLifecycleState State => MapState(_server.State);

    public IPAddress ListenAddress => _server.ListenAddress;

    public int Port => _server.Port;

    public bool IsRunning => _server.IsRunning;

    public IReadOnlyList<VirtualReaderClientInfo> ConnectedClients => _server.ConnectedClients
        .Select(static client => new VirtualReaderClientInfo(
            client.ConnectionId,
            client.RemoteEndPoint,
            client.ConnectedAt,
            client.NegotiatedVersion,
            client.IsConnected))
        .ToArray();

    public event EventHandler<VirtualReaderLifecycleChangedEventArgs>? LifecycleChanged;

    public event EventHandler<VirtualReaderClientChangedEventArgs>? ClientChanged;

    public event EventHandler<VirtualReaderMessageEventArgs>? MessageObserved;

    public Task StartAsync(CancellationToken cancellationToken = default) => _server.StartAsync(cancellationToken);

    public void Start() => StartAsync().GetAwaiter().GetResult();

    public Task StopAsync(CancellationToken cancellationToken = default) => _server.StopAsync(cancellationToken);

    public Task RestartAsync(CancellationToken cancellationToken = default) => _server.RestartAsync(cancellationToken);

    public ValueTask DisposeAsync() => _server.DisposeAsync();

    private void OnServerLifecycleChanged(object? sender, LlrpDeviceServerLifecycleChangedEventArgs args)
    {
        try
        {
            LifecycleChanged?.Invoke(
                this,
                new VirtualReaderLifecycleChangedEventArgs(
                    MapState(args.PreviousState),
                    MapState(args.CurrentState),
                    args.Error));
        }
        catch
        {
            // Compatibility observers must not alter the server lifecycle.
        }
    }

    private void OnServerClientChanged(object? sender, LlrpDeviceClientChangedEventArgs args)
    {
        try
        {
            ClientChanged?.Invoke(
                this,
                new VirtualReaderClientChangedEventArgs(
                    new VirtualReaderClientInfo(
                        args.Client.ConnectionId,
                        args.Client.RemoteEndPoint,
                        args.Client.ConnectedAt,
                        args.Client.NegotiatedVersion,
                        args.Client.IsConnected),
                    args.Connected));
        }
        catch
        {
        }
    }

    private void OnServerMessageObserved(object? sender, LlrpDeviceMessageEventArgs args)
    {
        try
        {
            MessageObserved?.Invoke(
                this,
                new VirtualReaderMessageEventArgs(
                    args.ConnectionId,
                    args.Version,
                    args.MessageType,
                    args.MessageId,
                    args.Incoming,
                    args.Detail));
        }
        catch
        {
        }
    }

    private static VirtualReaderLifecycleState MapState(LlrpDeviceServerLifecycleState state) => state switch
    {
        LlrpDeviceServerLifecycleState.Created => VirtualReaderLifecycleState.Created,
        LlrpDeviceServerLifecycleState.Starting => VirtualReaderLifecycleState.Starting,
        LlrpDeviceServerLifecycleState.Running => VirtualReaderLifecycleState.Running,
        LlrpDeviceServerLifecycleState.Stopping => VirtualReaderLifecycleState.Stopping,
        LlrpDeviceServerLifecycleState.Faulted => VirtualReaderLifecycleState.Faulted,
        _ => VirtualReaderLifecycleState.Stopped,
    };
}
