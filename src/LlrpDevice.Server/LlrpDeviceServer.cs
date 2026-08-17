using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using LlrpDevice.Abstractions;
using LlrpNet.Core.Diagnostics;
using LlrpNet.Core.Protocol;
using LlrpNet.Core.Session;
using LlrpNet.Core.Transport;
using LlrpNet.Protocol.Messages;
using LlrpNet.Protocol.Parameters;
using LlrpNet.Protocol.Registry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using V101Enumerations = LlrpNet.Protocol.Enumerations.V1_0_1;
using V101Messages = LlrpNet.Protocol.Messages.V1_0_1;
using V101Parameters = LlrpNet.Protocol.Parameters.V1_0_1;

namespace LlrpDevice.Server;

/// <summary>
/// Runs one message-level LLRP device over a real TCP listener.
/// </summary>
/// <remarks>
/// A server is one device endpoint, not a platform instance directory. It owns its listener, device resources,
/// protocol registry, client sessions, and report scheduler. An independent Manager may create several servers by
/// using these public lifecycle and options APIs without adding a Manager dependency to this assembly.
/// </remarks>
public sealed class LlrpDeviceServer : IAsyncDisposable
{
    private readonly LlrpDeviceServerOptions _options;
    private readonly ILogger<LlrpDeviceServer> _logger;
    private readonly LlrpCodecRegistry _registry;
    private readonly LlrpDeviceServerState _state;
    private readonly LlrpDeviceProtocolDispatcher _dispatcher;
    private readonly HashSet<ushort> _dropResponseMessageTypes;
    private readonly Dictionary<ushort, LlrpDeviceServerErrorResponse> _errorResponseMessageTypes;
    private readonly HashSet<ushort> _closeConnectionMessageTypes;
    private readonly HashSet<ushort> _truncateResponseMessageTypes;
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private readonly object _connectionGate = new();
    private readonly object _reportGate = new();
    private readonly Dictionary<string, LlrpDeviceConnection> _connections = [];
    private readonly Dictionary<string, Task> _connectionTasks = [];
    private readonly Dictionary<uint, ReportLoop> _reportLoops = [];
    private TcpListener? _listener;
    private CancellationTokenSource? _lifetime;
    private Task? _acceptLoop;
    private Task? _triggerLoop;
    private int _lifecycleState = (int)LlrpDeviceServerLifecycleState.Created;
    private int _disposeStarted;

    /// <summary>Creates a generic LLRP device server for one device implementation.</summary>
    public LlrpDeviceServer(LlrpDeviceServerOptions serverOptions, LlrpDevice.Abstractions.ILlrpDevice device)
    {
        ArgumentNullException.ThrowIfNull(serverOptions);
        ArgumentNullException.ThrowIfNull(device);
        serverOptions.Validate();
        _options = serverOptions;
        _state = new LlrpDeviceServerState(device, serverOptions);
        _logger = (_options.LoggerFactory ?? NullLoggerFactory.Instance).CreateLogger<LlrpDeviceServer>();
        _registry = LlrpDeviceProtocolDispatcher.CreateRegistry(_options.ProtocolModules);
        _dispatcher = new LlrpDeviceProtocolDispatcher(_state, _registry, _options.ProtocolModules);
        _dropResponseMessageTypes = serverOptions.DropResponseForMessageTypes.ToHashSet();
        _errorResponseMessageTypes = serverOptions.ErrorResponseForMessageTypes.ToDictionary();
        _closeConnectionMessageTypes = serverOptions.CloseConnectionAfterRequestMessageTypes.ToHashSet();
        _truncateResponseMessageTypes = serverOptions.TruncateResponseForMessageTypes.ToHashSet();
        _state.Device.EventRaised += OnDeviceEvent;
    }

    /// <summary>Gets the configured device options.</summary>
    public LlrpDeviceServerOptions Options => _options;

    /// <summary>Gets the current host lifecycle state.</summary>
    public LlrpDeviceServerLifecycleState State => (LlrpDeviceServerLifecycleState)Volatile.Read(ref _lifecycleState);

    /// <summary>Gets the exact configured listen address.</summary>
    public IPAddress ListenAddress => _options.ListenAddress;

    /// <summary>Gets the bound port, or the configured port before the host starts.</summary>
    public int Port
    {
        get
        {
            TcpListener? listener = Volatile.Read(ref _listener);
            return listener is null ? _options.Port : ((IPEndPoint)listener.LocalEndpoint).Port;
        }
    }

    /// <summary>Gets whether the host has a live listener.</summary>
    public bool IsRunning => State == LlrpDeviceServerLifecycleState.Running;

    /// <summary>Gets a point-in-time view of active client connections.</summary>
    public IReadOnlyList<LlrpDeviceClientInfo> ConnectedClients
    {
        get
        {
            lock (_connectionGate)
            {
                return _connections.Values.Select(static connection => connection.ToInfo()).ToArray();
            }
        }
    }

    /// <summary>Raised after the host lifecycle state changes.</summary>
    public event EventHandler<LlrpDeviceServerLifecycleChangedEventArgs>? LifecycleChanged;

    /// <summary>Raised when a client connection is accepted or removed.</summary>
    public event EventHandler<LlrpDeviceClientChangedEventArgs>? ClientChanged;

    /// <summary>Raised for decoded incoming and outgoing message metadata.</summary>
    public event EventHandler<LlrpDeviceMessageEventArgs>? MessageObserved;

    /// <summary>Starts the listener and accepts clients asynchronously.</summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (State == LlrpDeviceServerLifecycleState.Running || State == LlrpDeviceServerLifecycleState.Starting)
            {
                throw new InvalidOperationException("The LLRP device server is already running or starting.");
            }

            SetState(LlrpDeviceServerLifecycleState.Starting);
            cancellationToken.ThrowIfCancellationRequested();
            var lifetime = new CancellationTokenSource();
            TcpListener? listener = null;
            try
            {
                listener = new TcpListener(_options.ListenAddress, _options.Port);
                listener.Start();
                _listener = listener;
                _lifetime = lifetime;
                _acceptLoop = AcceptLoopAsync(listener, lifetime.Token);
                _triggerLoop = TriggerLoopAsync(lifetime.Token);
                SetState(LlrpDeviceServerLifecycleState.Running);
                _logger.LogInformation(
                    "LLRP device {ReaderName} started on {Address}:{Port} using LLRP {Version}",
                    _state.Device.Identity.Name,
                    ListenAddress,
                    Port,
                    _options.ProtocolVersion);
            }
            catch (Exception exception)
            {
                lifetime.Cancel();
                listener?.Stop();
                lifetime.Dispose();
                SetState(LlrpDeviceServerLifecycleState.Faulted, exception);
                throw;
            }
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    /// <summary>Compatibility synchronous start for the original single-host launcher.</summary>
    public void Start() => StartAsync().GetAwaiter().GetResult();

    /// <summary>Stops the listener, report schedulers, and all accepted client sessions.</summary>
    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return StopCoreAsync(cancellationToken);
    }

    private async Task StopCoreAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (State is LlrpDeviceServerLifecycleState.Created or LlrpDeviceServerLifecycleState.Stopped)
            {
                if (State == LlrpDeviceServerLifecycleState.Created)
                {
                    SetState(LlrpDeviceServerLifecycleState.Stopped);
                }

                return;
            }

            if (State == LlrpDeviceServerLifecycleState.Stopping)
            {
                return;
            }

            SetState(LlrpDeviceServerLifecycleState.Stopping);
            CancellationTokenSource? lifetime = _lifetime;
            lifetime?.Cancel();
            _listener?.Stop();

            Task? acceptLoop = _acceptLoop;
            Task? triggerLoop = _triggerLoop;
            LlrpDeviceConnection[] connections;
            Task[] connectionTasks;
            lock (_connectionGate)
            {
                connections = _connections.Values.ToArray();
                connectionTasks = _connectionTasks.Values.ToArray();
            }

            CancelReportLoops();
            foreach (LlrpDeviceConnection connection in connections)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }

            if (acceptLoop is not null)
            {
                await IgnoreExpectedShutdownAsync(acceptLoop).ConfigureAwait(false);
            }

            if (triggerLoop is not null)
            {
                await IgnoreExpectedShutdownAsync(triggerLoop).ConfigureAwait(false);
            }

            if (connectionTasks.Length > 0)
            {
                await Task.WhenAll(connectionTasks).ConfigureAwait(false);
            }

            lock (_connectionGate)
            {
                _connections.Clear();
                _connectionTasks.Clear();
            }

            _listener = null;
            _acceptLoop = null;
            _triggerLoop = null;
            _lifetime = null;
            lifetime?.Dispose();
            SetState(LlrpDeviceServerLifecycleState.Stopped);
            _logger.LogInformation("LLRP device {ReaderName} stopped.", _state.Device.Identity.Name);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    /// <summary>Stops and starts the same single-host runtime on the same configured endpoint.</summary>
    public async Task RestartAsync(CancellationToken cancellationToken = default)
    {
        await StopAsync(cancellationToken).ConfigureAwait(false);
        await StartAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
        {
            return;
        }

        try
        {
            await StopCoreAsync().ConfigureAwait(false);
            _state.Device.EventRaised -= OnDeviceEvent;
            await _state.Device.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            Volatile.Write(ref _disposeStarted, 2);
            Volatile.Write(ref _lifecycleState, (int)LlrpDeviceServerLifecycleState.Stopped);
            _lifecycleLock.Dispose();
        }
    }

    /// <summary>Sends a standard device-initiated CLOSE_CONNECTION and then ends the selected session.</summary>
    public async Task RequestCloseConnectionAsync(
        string? connectionId = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        LlrpDeviceConnection[] connections;
        lock (_connectionGate)
        {
            connections = _connections.Values
                .Where(connection => connectionId is null || connection.ConnectionId == connectionId)
                .ToArray();
        }

        foreach (LlrpDeviceConnection connection in connections)
        {
            if (!connection.IsConnected)
            {
                continue;
            }

            LlrpProtocolVersion version = connection.ProtocolVersion;
            await SendMessageAsync(
                connection,
                _dispatcher.CreateCloseConnection(version, connection.NextAsyncMessageId()),
                version,
                cancellationToken).ConfigureAwait(false);
            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken).ConfigureAwait(false);
            await connection.DisposeAsync().ConfigureAwait(false);
        }
    }

    internal async ValueTask SendToAllClientsAsync(
        LlrpProtocolVersion version,
        ILlrpMessage message,
        CancellationToken cancellationToken)
    {
        LlrpDeviceConnection[] connections;
        lock (_connectionGate)
        {
            connections = _connections.Values.ToArray();
        }

        foreach (LlrpDeviceConnection connection in connections)
        {
            if (connection.ProtocolVersion != version)
            {
                continue;
            }

            try
            {
                await SendMessageAsync(connection, message, version, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or SocketException or LlrpSessionDisconnectedException or ObjectDisposedException)
            {
                _logger.LogDebug(exception, "Failed to send a scheduled virtual-reader message to {ConnectionId}.", connection.ConnectionId);
            }
        }
    }

    internal void PublishDeviceEvent(LlrpDeviceEvent deviceEvent) => OnDeviceEvent(this, deviceEvent);

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                if (!TryReserveClientSlot(client))
                {
                    client.Dispose();
                    continue;
                }

                LlrpDeviceConnection connection;
                try
                {
                    ConfigureSocket(client);
                    var transport = new LlrpAcceptedTcpTransport(
                        client,
                        new LlrpAcceptedTcpTransportOptions
                        {
                            FrameAssemblyTimeout = _options.FrameAssemblyTimeout,
                            IdleTimeout = _options.IdleTimeout,
                            MaximumFrameLength = _options.MaximumFrameLength,
                            LogFrameHex = false,
                        },
                        _options.LoggerFactory,
                        _options.FrameObserver);
                    connection = new LlrpDeviceConnection(transport);
                }
                catch
                {
                    client.Dispose();
                    continue;
                }

                lock (_connectionGate)
                {
                    _connections[connection.ConnectionId] = connection;
                }

                RaiseClientChanged(connection, connected: true);
                _logger.LogInformation(
                    "LLRP device accepted client {ConnectionId} from {RemoteEndPoint}.",
                    connection.ConnectionId,
                    connection.RemoteEndPoint);

                Task connectionTask = RunConnectionAsync(connection, cancellationToken);
                lock (_connectionGate)
                {
                    if (_connections.ContainsKey(connection.ConnectionId))
                    {
                        _connectionTasks[connection.ConnectionId] = connectionTask;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (SocketException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "LLRP device accept loop failed.");
            SetState(LlrpDeviceServerLifecycleState.Faulted, exception);
        }
    }

    private bool TryReserveClientSlot(TcpClient client)
    {
        LlrpDeviceConnection[] existing;
        lock (_connectionGate)
        {
            // A client can close its socket before RunConnectionAsync reaches its
            // final dictionary cleanup.  Count only live sessions so a single-client
            // virtual Reader does not reject the legitimate reconnect that follows a
            // completed disconnect.  The final cleanup remains idempotent.
            int activeConnectionCount = _connections.Values.Count(static connection => connection.IsConnected);
            if (activeConnectionCount < _options.MaximumClientConnections)
            {
                return true;
            }

            if (_options.ConnectionLimitPolicy == LlrpDeviceConnectionLimitPolicy.RejectAdditional)
            {
                _logger.LogWarning(
                    "Rejected a virtual-reader client from {RemoteEndPoint} because the connection limit {Limit} was reached.",
                    client.Client.RemoteEndPoint,
                    _options.MaximumClientConnections);
                return false;
            }

            existing = _connections.Values.Take(1).ToArray();
            foreach (LlrpDeviceConnection connection in existing)
            {
                _connections.Remove(connection.ConnectionId);
            }
        }

        foreach (LlrpDeviceConnection connection in existing)
        {
            _ = connection.DisposeAsync();
        }

        return true;
    }

    private async Task RunConnectionAsync(LlrpDeviceConnection connection, CancellationToken hostCancellationToken)
    {
        using var connectionCancellation = CancellationTokenSource.CreateLinkedTokenSource(hostCancellationToken);
        CancellationToken cancellationToken = connectionCancellation.Token;
        Task? terminationWatcher = null;
        Task? keepAliveTask = null;
        try
        {
            await connection.Session.ConnectAsync(cancellationToken).ConfigureAwait(false);
            _state.ClientConnected();
            connection.SetProtocolVersion(_options.ProtocolVersion);
            await SendMessageAsync(
                connection,
                _dispatcher.CreateReaderEventNotification(
                    _options.ProtocolVersion,
                    connection.NextAsyncMessageId()),
                _options.ProtocolVersion,
                cancellationToken).ConfigureAwait(false);

            terminationWatcher = WatchSessionTerminationAsync(connection.Session, connectionCancellation);
            keepAliveTask = KeepAliveAsync(connection, cancellationToken);

            await foreach (ReadOnlyMemory<byte> frame in connection.Session.ReadUnsolicitedFramesAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                await HandleFrameAsync(connection, frame, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is IOException or SocketException or EndOfStreamException or LlrpSessionDisconnectedException or ObjectDisposedException)
        {
            _logger.LogDebug(exception, "LLRP device client {ConnectionId} disconnected.", connection.ConnectionId);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "LLRP device client {ConnectionId} session failed.", connection.ConnectionId);
        }
        finally
        {
            connectionCancellation.Cancel();
            if (terminationWatcher is not null)
            {
                await IgnoreExpectedShutdownAsync(terminationWatcher).ConfigureAwait(false);
            }

            if (keepAliveTask is not null)
            {
                await IgnoreExpectedShutdownAsync(keepAliveTask).ConfigureAwait(false);
            }

            await connection.DisposeAsync().ConfigureAwait(false);
            lock (_connectionGate)
            {
                _connections.Remove(connection.ConnectionId);
                _connectionTasks.Remove(connection.ConnectionId);
            }

            RaiseClientChanged(connection, connected: false);
            _state.ClientDisconnected();
            ReconcileReportLoops();
        }
    }

    private async Task HandleFrameAsync(
        LlrpDeviceConnection connection,
        ReadOnlyMemory<byte> frame,
        CancellationToken cancellationToken)
    {
        LlrpMessageHeader header = LlrpMessageHeader.Decode(frame.Span);
        connection.SetProtocolVersion(header.Version);
        ILlrpMessage message;
        try
        {
            message = _registry.DecodeMessage(frame.Span);
        }
        catch (Exception exception) when (
            exception is LlrpProtocolException or UnknownTvParameterException or ArgumentException)
        {
            _logger.LogWarning(
                exception,
                "Rejected malformed or undecodable message type {MessageType} from {ConnectionId}.",
                header.MessageType,
                connection.ConnectionId);
            ILlrpMessage error = _dispatcher.CreateError(
                header.Version,
                header.MessageId,
                (ushort)GetParameterErrorCode(header.Version),
                exception.Message);
            await SendMessageAsync(connection, error, header.Version, cancellationToken).ConfigureAwait(false);
            return;
        }

        RaiseMessageObserved(connection, header, incoming: true, detail: message.GetType().Name);
        if (ShouldCloseConnection(header.MessageType))
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            return;
        }

        LlrpDeviceRequestContext context = new(
            this,
            _state.Device,
            connection.ConnectionId,
            header.Version,
            header.MessageId);
        LlrpDeviceDispatchResult result;
        if (_errorResponseMessageTypes.TryGetValue(header.MessageType, out LlrpDeviceServerErrorResponse? injectedError))
        {
            result = new LlrpDeviceDispatchResult(
                _dispatcher.CreateError(
                    header.Version,
                    header.MessageId,
                    injectedError.StatusCode,
                    injectedError.Description),
                []);
        }
        else
        {
            try
            {
                result = await _dispatcher.DispatchAsync(context, message, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (
                exception is ArgumentException or InvalidOperationException or NotSupportedException or
                    OverflowException or LlrpProtocolException)
            {
                _logger.LogWarning(
                    exception,
                    "Rejected virtual-reader request {MessageType} from {ConnectionId}.",
                    header.MessageType,
                    connection.ConnectionId);
                result = new LlrpDeviceDispatchResult(
                    _dispatcher.CreateError(
                        header.Version,
                        header.MessageId,
                        GetParameterErrorCode(header.Version),
                        exception.Message),
                    []);
            }
        }

        if (_dropResponseMessageTypes.Contains(header.MessageType))
        {
            return;
        }

        LlrpProtocolVersion responseVersion = result.ResponseVersion ?? header.Version;
        if (result.Response is not null)
        {
            byte[] responseFrame = _registry.EncodeMessage(responseVersion, result.Response);
            RaiseMessageObserved(connection, LlrpMessageHeader.Decode(responseFrame), incoming: false, detail: result.Response.GetType().Name);
            if (_truncateResponseMessageTypes.Contains(header.MessageType))
            {
                await connection.SendRawFrameAsync(
                    responseFrame.AsMemory(0, Math.Max(1, responseFrame.Length - 1)),
                    cancellationToken).ConfigureAwait(false);
                await connection.DisposeAsync().ConfigureAwait(false);
                return;
            }

            await connection.Session.SendFrameAsync(responseFrame, cancellationToken).ConfigureAwait(false);
        }

        foreach (ILlrpMessage additionalMessage in result.AdditionalMessages)
        {
            LlrpProtocolVersion additionalVersion = result.ResponseVersion ?? header.Version;
            byte[] additionalFrame = _registry.EncodeMessage(additionalVersion, additionalMessage);
            RaiseMessageObserved(connection, LlrpMessageHeader.Decode(additionalFrame), incoming: false, detail: additionalMessage.GetType().Name);
            await connection.Session.SendFrameAsync(additionalFrame, cancellationToken).ConfigureAwait(false);
        }

        if (result.NextProtocolVersion is LlrpProtocolVersion nextVersion)
        {
            connection.SetProtocolVersion(nextVersion);
        }

        ReconcileReportLoops();
        if (result.CloseConnection)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            return;
        }
    }

    private async Task KeepAliveAsync(
        LlrpDeviceConnection connection,
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                V101Parameters.KeepaliveSpec keepaliveSpec = _state.GetKeepaliveSpec();
                TimeSpan? interval = _state.IsKeepaliveSpecConfigured
                    ? keepaliveSpec.KeepaliveTriggerType == V101Enumerations.KeepaliveTriggerType.Periodic &&
                      keepaliveSpec.PeriodicTriggerValue > 0
                        ? TimeSpan.FromMilliseconds(keepaliveSpec.PeriodicTriggerValue)
                        : null
                    : _options.KeepAliveInterval;
                await Task.Delay(interval ?? TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
                keepaliveSpec = _state.GetKeepaliveSpec();
                bool configured = _state.IsKeepaliveSpecConfigured
                    ? keepaliveSpec.KeepaliveTriggerType == V101Enumerations.KeepaliveTriggerType.Periodic &&
                      keepaliveSpec.PeriodicTriggerValue > 0
                    : _options.KeepAliveInterval is not null;
                if (!configured)
                {
                    continue;
                }

                LlrpProtocolVersion version = connection.ProtocolVersion;
                ILlrpMessage keepalive = _dispatcher.CreateKeepalive(version, connection.NextAsyncMessageId());
                await SendMessageAsync(connection, keepalive, version, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task TriggerLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                IReadOnlyList<RoSpecTriggerTransition> transitions = _state.ProcessRoSpecTriggers(DateTimeOffset.UtcNow);
                if (transitions.Count > 0)
                {
                    foreach (RoSpecTriggerTransition transition in transitions)
                    {
                        OnDeviceEvent(
                            this,
                            new LlrpDeviceEvent
                            {
                                Name = transition.Started ? "rospec.started" : "rospec.stopped",
                                RoSpecId = transition.RoSpecId,
                            });
                        if (!transition.Started)
                        {
                            await FlushRoSpecReportAsync(transition.RoSpecId, cancellationToken).ConfigureAwait(false);
                        }
                    }

                    ReconcileReportLoops();
                }

                await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task FlushRoSpecReportAsync(uint roSpecId, CancellationToken cancellationToken)
    {
        V101Messages.RO_ACCESS_REPORT? report = _state.TakeAccumulatedRoSpecReport(
            roSpecId,
            0);
        if (report is null)
        {
            return;
        }

        LlrpDeviceConnection[] connections;
        lock (_connectionGate)
        {
            connections = _connections.Values.Where(static connection => connection.IsConnected).ToArray();
        }

        if (connections.Length == 0 || _state.IsHoldingEventsAndReports ||
            !_state.IsAutomaticReportDeliveryEnabled(roSpecId))
        {
            BufferReport(report);
            return;
        }

        foreach (LlrpDeviceConnection connection in connections)
        {
            try
            {
                LlrpProtocolVersion version = connection.ProtocolVersion;
                ILlrpMessage translated = _dispatcher.TranslateFromCanonical(
                    version,
                    report with { MessageId = connection.NextAsyncMessageId() });
                await SendMessageAsync(connection, translated, version, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or SocketException or LlrpSessionDisconnectedException or ObjectDisposedException)
            {
                _logger.LogDebug(exception, "Final ROSpec report delivery failed for {ConnectionId}.", connection.ConnectionId);
            }
        }
    }

    private void ReconcileReportLoops()
    {
        if (!IsRunning)
        {
            return;
        }

        IReadOnlyList<uint> activeRoSpecIds = _state.GetActiveRoSpecIds();
        lock (_reportGate)
        {
            foreach (uint roSpecId in activeRoSpecIds)
            {
                if (_reportLoops.ContainsKey(roSpecId))
                {
                    continue;
                }

                CancellationToken hostToken = _lifetime?.Token ?? CancellationToken.None;
                var cancellation = CancellationTokenSource.CreateLinkedTokenSource(hostToken);
                _reportLoops[roSpecId] = new ReportLoop(cancellation, ReportLoopAsync(roSpecId, cancellation.Token));
            }

            foreach ((uint roSpecId, ReportLoop loop) in _reportLoops.ToArray())
            {
                if (activeRoSpecIds.Contains(roSpecId))
                {
                    continue;
                }

                loop.Cancellation.Cancel();
                loop.Cancellation.Dispose();
                _reportLoops.Remove(roSpecId);
            }
        }
    }

    private async Task ReportLoopAsync(uint roSpecId, CancellationToken cancellationToken)
    {
        int reportCount = 0;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (!_state.GetActiveRoSpecIds().Contains(roSpecId))
                {
                    return;
                }

                LlrpDeviceConnection[] connections;
                lock (_connectionGate)
                {
                    connections = _connections.Values.Where(static connection => connection.IsConnected).ToArray();
                }

                IReadOnlyList<V101Messages.RO_ACCESS_REPORT> canonicalReports = _dispatcher
                    .BuildInventoryReports(LlrpProtocolVersion.Version101, roSpecId, reportCount)
                    .OfType<V101Messages.RO_ACCESS_REPORT>()
                    .ToArray();
                bool bufferReports = connections.Length == 0 || _state.IsHoldingEventsAndReports ||
                    !_state.IsAutomaticReportDeliveryEnabled(roSpecId);
                if (bufferReports)
                {
                    foreach (V101Messages.RO_ACCESS_REPORT report in canonicalReports)
                    {
                        BufferReport(report);
                    }
                }
                else
                {
                    foreach (LlrpDeviceConnection connection in connections)
                    {
                        foreach (V101Messages.RO_ACCESS_REPORT report in canonicalReports)
                        {
                            try
                            {
                                LlrpProtocolVersion version = connection.ProtocolVersion;
                                ILlrpMessage translated = _dispatcher.TranslateFromCanonical(
                                    version,
                                    report with { MessageId = connection.NextAsyncMessageId() });
                                await SendMessageAsync(connection, translated, version, cancellationToken).ConfigureAwait(false);
                            }
                            catch (Exception exception) when (exception is IOException or SocketException or LlrpSessionDisconnectedException or ObjectDisposedException)
                            {
                                _logger.LogDebug(exception, "Inventory report delivery failed for {ConnectionId}.", connection.ConnectionId);
                            }
                        }
                    }
                }

                reportCount++;
                if (!_options.Reports.Repeat ||
                    (_options.Reports.ReportCount > 0 && reportCount >= _options.Reports.ReportCount))
                {
                    return;
                }

                await Task.Delay(_options.Reports.ReportInterval, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void BufferReport(V101Messages.RO_ACCESS_REPORT report)
    {
        LlrpReportBufferResult result = _state.BufferReport(report);
        if (result.Overflowed)
        {
            OnDeviceEvent(
                this,
                new LlrpDeviceEvent
                {
                    Name = "report.buffer.overflow",
                    ReportBufferPercentage = result.Percentage,
                });
        }
        else if (result.Warning)
        {
            OnDeviceEvent(
                this,
                new LlrpDeviceEvent
                {
                    Name = "report.buffer.warning",
                    ReportBufferPercentage = result.Percentage,
                });
        }
    }

    private void CancelReportLoops()
    {
        lock (_reportGate)
        {
            foreach (ReportLoop loop in _reportLoops.Values)
            {
                loop.Cancellation.Cancel();
                loop.Cancellation.Dispose();
            }

            _reportLoops.Clear();
        }
    }

    private async Task SendMessageAsync(
        LlrpDeviceConnection connection,
        ILlrpMessage message,
        LlrpProtocolVersion version,
        CancellationToken cancellationToken)
    {
        byte[] frame = _registry.EncodeMessage(version, message);
        RaiseMessageObserved(connection, LlrpMessageHeader.Decode(frame), incoming: false, detail: message.GetType().Name);
        await connection.Session.SendFrameAsync(frame, cancellationToken).ConfigureAwait(false);
    }

    private bool ShouldCloseConnection(ushort messageType)
    {
        lock (_closeConnectionMessageTypes)
        {
            return _closeConnectionMessageTypes.Remove(messageType);
        }
    }

    private void ConfigureSocket(TcpClient client)
    {
        if (!_options.UseTcpKeepAlive)
        {
            return;
        }

        try
        {
            client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
        }
        catch (SocketException exception)
        {
            _logger.LogDebug(exception, "The virtual-reader socket did not accept TCP keepalive configuration.");
        }
    }

    private void SetState(LlrpDeviceServerLifecycleState state, Exception? error = null)
    {
        LlrpDeviceServerLifecycleState previous = (LlrpDeviceServerLifecycleState)Interlocked.Exchange(
            ref _lifecycleState,
            (int)state);
        if (previous == state && error is null)
        {
            return;
        }

        try
        {
            LifecycleChanged?.Invoke(this, new LlrpDeviceServerLifecycleChangedEventArgs(previous, state, error));
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "A virtual-reader lifecycle observer threw.");
        }
    }

    private void RaiseClientChanged(LlrpDeviceConnection connection, bool connected)
    {
        try
        {
            ClientChanged?.Invoke(this, new LlrpDeviceClientChangedEventArgs(connection.ToInfo(), connected));
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "A virtual-reader client observer threw.");
        }
    }

    private void RaiseMessageObserved(
        LlrpDeviceConnection connection,
        LlrpMessageHeader header,
        bool incoming,
        string? detail)
    {
        try
        {
            MessageObserved?.Invoke(
                this,
                new LlrpDeviceMessageEventArgs(
                    connection.ConnectionId,
                    header.Version,
                    header.MessageType,
                    header.MessageId,
                    incoming,
                    detail));
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "A virtual-reader message observer threw.");
        }
    }

    private void OnDeviceEvent(object? sender, LlrpDeviceEvent deviceEvent)
    {
        ArgumentNullException.ThrowIfNull(deviceEvent);
        if (deviceEvent.GpiPortNumber is ushort gpiPort && deviceEvent.GpiState is bool gpiState)
        {
            _state.SetGpiState(gpiPort, gpiState);
        }
        if (deviceEvent.AntennaId is ushort antennaId && deviceEvent.AntennaConnected is bool antennaConnected)
        {
            _state.SetAntennaConnection(antennaId, antennaConnected);
        }

        if (deviceEvent.Name == "connection.close")
        {
            _ = RequestCloseConnectionAsync();
            return;
        }

        V101Enumerations.NotificationEventType? eventType = deviceEvent.Name switch
        {
            "gpi.changed" => V101Enumerations.NotificationEventType.GPI_Event,
            "rospec.started" or "rospec.stopped" => V101Enumerations.NotificationEventType.ROSpec_Event,
            "report.buffer.warning" => V101Enumerations.NotificationEventType.Report_Buffer_Fill_Warning,
            "report.buffer.overflow" => V101Enumerations.NotificationEventType.Report_Buffer_Fill_Warning,
            "reader.exception" => V101Enumerations.NotificationEventType.Reader_Exception_Event,
            "antenna.changed" => V101Enumerations.NotificationEventType.Antenna_Event,
            _ => null,
        };
        if (eventType is not { } enabledEvent || !_state.IsEventEnabled(enabledEvent))
        {
            return;
        }

        if (_state.IsHoldingEventsAndReports)
        {
            _state.BufferHeldEvent(deviceEvent);
            return;
        }

        _ = SendDeviceEventAsync(deviceEvent);
    }

    private async Task SendDeviceEventAsync(LlrpDeviceEvent deviceEvent)
    {
        LlrpDeviceConnection[] connections;
        lock (_connectionGate)
        {
            connections = _connections.Values.ToArray();
        }

        foreach (LlrpDeviceConnection connection in connections)
        {
            if (!connection.IsConnected)
            {
                continue;
            }

            try
            {
                LlrpProtocolVersion version = connection.ProtocolVersion;
                await SendMessageAsync(
                    connection,
                    _dispatcher.CreateReaderEventNotification(version, connection.NextAsyncMessageId(), deviceEvent),
                    version,
                    _lifetime?.Token ?? CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or SocketException or LlrpSessionDisconnectedException or ObjectDisposedException)
            {
                _logger.LogDebug(exception, "Failed to send reader event notification to {ConnectionId}.", connection.ConnectionId);
            }
        }
    }

    private static async Task WatchSessionTerminationAsync(
        LlrpSession session,
        CancellationTokenSource cancellation)
    {
        await session.ConnectionCompletion.ConfigureAwait(false);
        cancellation.Cancel();
    }

    private static async Task IgnoreExpectedShutdownAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException or SocketException or IOException or EndOfStreamException)
        {
        }
    }

    private static ushort GetParameterErrorCode(LlrpProtocolVersion version) => version switch
    {
        LlrpProtocolVersion.Version101 => 100,
        LlrpProtocolVersion.Version11 => 100,
        _ => 100,
    };

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposeStarted) != 0)
        {
            throw new ObjectDisposedException(nameof(LlrpDeviceServer));
        }
    }

    private sealed record ReportLoop(CancellationTokenSource Cancellation, Task Task);

    private sealed class LlrpDeviceConnection : IAsyncDisposable
    {
        private readonly LlrpAcceptedTcpTransport _transport;
        private readonly EndPoint? _remoteEndPoint;
        private int _disposed;
        private int _nextAsyncMessageId;
        private int _protocolVersion = (int)LlrpProtocolVersion.Version101;

        public LlrpDeviceConnection(LlrpAcceptedTcpTransport transport)
        {
            _transport = transport;
            _remoteEndPoint = transport.RemoteEndPoint;
            Session = new LlrpSession(transport, new LlrpSessionOptions
            {
                UnsolicitedFrameCapacity = 4096,
                UnsolicitedFrameOverflowPolicy = LlrpUnsolicitedFrameOverflowPolicy.FaultConnection,
            });
            ConnectedAt = DateTimeOffset.UtcNow;
        }

        public string ConnectionId => _transport.ConnectionId;
        public EndPoint? RemoteEndPoint => _remoteEndPoint;
        public DateTimeOffset ConnectedAt { get; }
        public LlrpSession Session { get; }
        public bool IsConnected => Session.IsConnected && Volatile.Read(ref _disposed) == 0;
        public LlrpProtocolVersion ProtocolVersion => (LlrpProtocolVersion)Volatile.Read(ref _protocolVersion);

        public void SetProtocolVersion(LlrpProtocolVersion version) => Volatile.Write(ref _protocolVersion, (int)version);

        public uint NextAsyncMessageId() => unchecked((uint)Interlocked.Increment(ref _nextAsyncMessageId));

        public LlrpDeviceClientInfo ToInfo() => new(
            ConnectionId,
            RemoteEndPoint,
            ConnectedAt,
            ProtocolVersion,
            IsConnected);

        public ValueTask SendRawFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken cancellationToken) =>
            _transport.SendRawFrameAsync(frame, cancellationToken);

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            await Session.DisposeAsync().ConfigureAwait(false);
        }
    }
}
