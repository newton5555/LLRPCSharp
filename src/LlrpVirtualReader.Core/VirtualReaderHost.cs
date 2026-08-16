using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using LlrpNet.Core.Diagnostics;
using LlrpNet.Core.Protocol;
using LlrpNet.Core.Session;
using LlrpNet.Core.Transport;
using LlrpNet.Protocol.Messages;
using LlrpNet.Protocol.Parameters;
using LlrpNet.Protocol.Registry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LlrpVirtualReader;

/// <summary>
/// Runs one message-level LLRP virtual reader over a real TCP listener.
/// </summary>
/// <remarks>
/// A host is one device, not a platform instance directory. It owns its listener, device resources, tag source,
/// protocol registry, client sessions, and report scheduler. The independent Manager may create several hosts by
/// using these public lifecycle and options APIs without adding a Manager dependency to this assembly.
/// </remarks>
public sealed class VirtualReaderHost : IAsyncDisposable
{
    private readonly VirtualReaderHostOptions _hostOptions;
    private readonly VirtualReaderOptions _options;
    private readonly ILogger<VirtualReaderHost> _logger;
    private readonly LlrpCodecRegistry _registry;
    private readonly VirtualReaderDeviceState _deviceState;
    private readonly VirtualReaderProtocolDispatcher _dispatcher;
    private readonly HashSet<ushort> _dropResponseMessageTypes;
    private readonly Dictionary<ushort, VirtualReaderErrorResponse> _errorResponseMessageTypes;
    private readonly HashSet<ushort> _closeConnectionMessageTypes;
    private readonly HashSet<ushort> _truncateResponseMessageTypes;
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private readonly object _connectionGate = new();
    private readonly object _reportGate = new();
    private readonly Dictionary<string, VirtualReaderConnection> _connections = [];
    private readonly Dictionary<string, Task> _connectionTasks = [];
    private readonly Dictionary<uint, ReportLoop> _reportLoops = [];
    private TcpListener? _listener;
    private CancellationTokenSource? _lifetime;
    private Task? _acceptLoop;
    private int _lifecycleState = (int)VirtualReaderLifecycleState.Created;
    private int _disposeStarted;

    /// <summary>Creates a loopback host using the compatibility constructor.</summary>
    public VirtualReaderHost(int port = 0, VirtualReaderOptions? options = null)
        : this(new VirtualReaderHostOptions
        {
            Port = port,
            ReaderOptions = options ?? new VirtualReaderOptions(),
        })
    {
    }

    /// <summary>Creates a host bound to the exact endpoint in <paramref name="hostOptions"/>.</summary>
    public VirtualReaderHost(VirtualReaderHostOptions hostOptions)
    {
        ArgumentNullException.ThrowIfNull(hostOptions);
        hostOptions.Validate();
        _hostOptions = hostOptions;
        _options = hostOptions.ReaderOptions with
        {
            TagSource = hostOptions.ReaderOptions.NormalizeLegacyTagSource(),
        };
        _logger = (hostOptions.LoggerFactory ?? NullLoggerFactory.Instance).CreateLogger<VirtualReaderHost>();
        _registry = VirtualReaderProtocolDispatcher.CreateRegistry(hostOptions.ProtocolModules);
        _deviceState = new VirtualReaderDeviceState(_options);
        _dispatcher = new VirtualReaderProtocolDispatcher(
            _deviceState,
            _registry,
            hostOptions.ProtocolModules);
        _dropResponseMessageTypes = hostOptions.ReaderOptions.DropResponseForMessageTypes.ToHashSet();
        _errorResponseMessageTypes = hostOptions.ReaderOptions.ErrorResponseForMessageTypes.ToDictionary();
        _closeConnectionMessageTypes = hostOptions.ReaderOptions.CloseConnectionAfterRequestMessageTypes.ToHashSet();
        _truncateResponseMessageTypes = hostOptions.ReaderOptions.TruncateResponseForMessageTypes.ToHashSet();
    }

    /// <summary>Gets the configured device options.</summary>
    public VirtualReaderOptions Options => _options;

    /// <summary>Gets the current host lifecycle state.</summary>
    public VirtualReaderLifecycleState State => (VirtualReaderLifecycleState)Volatile.Read(ref _lifecycleState);

    /// <summary>Gets the exact configured listen address.</summary>
    public IPAddress ListenAddress => _hostOptions.ListenAddress;

    /// <summary>Gets the bound port, or the configured port before the host starts.</summary>
    public int Port
    {
        get
        {
            TcpListener? listener = Volatile.Read(ref _listener);
            return listener is null ? _hostOptions.Port : ((IPEndPoint)listener.LocalEndpoint).Port;
        }
    }

    /// <summary>Gets whether the host has a live listener.</summary>
    public bool IsRunning => State == VirtualReaderLifecycleState.Running;

    /// <summary>Gets a point-in-time view of active client connections.</summary>
    public IReadOnlyList<VirtualReaderClientInfo> ConnectedClients
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
    public event EventHandler<VirtualReaderLifecycleChangedEventArgs>? LifecycleChanged;

    /// <summary>Raised when a client connection is accepted or removed.</summary>
    public event EventHandler<VirtualReaderClientChangedEventArgs>? ClientChanged;

    /// <summary>Raised for decoded incoming and outgoing message metadata.</summary>
    public event EventHandler<VirtualReaderMessageEventArgs>? MessageObserved;

    /// <summary>Starts the listener and accepts clients asynchronously.</summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (State == VirtualReaderLifecycleState.Running || State == VirtualReaderLifecycleState.Starting)
            {
                throw new InvalidOperationException("The virtual reader is already running or starting.");
            }

            SetState(VirtualReaderLifecycleState.Starting);
            cancellationToken.ThrowIfCancellationRequested();
            var lifetime = new CancellationTokenSource();
            TcpListener? listener = null;
            try
            {
                listener = new TcpListener(_hostOptions.ListenAddress, _hostOptions.Port);
                listener.Start();
                _listener = listener;
                _lifetime = lifetime;
                _acceptLoop = AcceptLoopAsync(listener, lifetime.Token);
                SetState(VirtualReaderLifecycleState.Running);
                _logger.LogInformation(
                    "Virtual reader {ReaderName} started on {Address}:{Port} using LLRP {Version}",
                    _options.ReaderName,
                    ListenAddress,
                    Port,
                    _options.ProtocolVersion);
            }
            catch (Exception exception)
            {
                lifetime.Cancel();
                listener?.Stop();
                lifetime.Dispose();
                SetState(VirtualReaderLifecycleState.Faulted, exception);
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
            if (State is VirtualReaderLifecycleState.Created or VirtualReaderLifecycleState.Stopped)
            {
                if (State == VirtualReaderLifecycleState.Created)
                {
                    SetState(VirtualReaderLifecycleState.Stopped);
                }

                return;
            }

            if (State == VirtualReaderLifecycleState.Stopping)
            {
                return;
            }

            SetState(VirtualReaderLifecycleState.Stopping);
            CancellationTokenSource? lifetime = _lifetime;
            lifetime?.Cancel();
            _listener?.Stop();

            Task? acceptLoop = _acceptLoop;
            VirtualReaderConnection[] connections;
            Task[] connectionTasks;
            lock (_connectionGate)
            {
                connections = _connections.Values.ToArray();
                connectionTasks = _connectionTasks.Values.ToArray();
            }

            CancelReportLoops();
            foreach (VirtualReaderConnection connection in connections)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }

            if (acceptLoop is not null)
            {
                await IgnoreExpectedShutdownAsync(acceptLoop).ConfigureAwait(false);
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
            _lifetime = null;
            lifetime?.Dispose();
            SetState(VirtualReaderLifecycleState.Stopped);
            _logger.LogInformation("Virtual reader {ReaderName} stopped.", _options.ReaderName);
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
        }
        finally
        {
            Volatile.Write(ref _disposeStarted, 2);
            Volatile.Write(ref _lifecycleState, (int)VirtualReaderLifecycleState.Stopped);
            _lifecycleLock.Dispose();
        }
    }

    internal async ValueTask SendToAllClientsAsync(
        LlrpProtocolVersion version,
        ILlrpMessage message,
        CancellationToken cancellationToken)
    {
        VirtualReaderConnection[] connections;
        lock (_connectionGate)
        {
            connections = _connections.Values.ToArray();
        }

        foreach (VirtualReaderConnection connection in connections)
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

                VirtualReaderConnection connection;
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
                        _hostOptions.LoggerFactory,
                        _hostOptions.FrameObserver);
                    connection = new VirtualReaderConnection(transport);
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
                    "Virtual reader accepted client {ConnectionId} from {RemoteEndPoint}.",
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
            _logger.LogError(exception, "Virtual reader accept loop failed.");
            SetState(VirtualReaderLifecycleState.Faulted, exception);
        }
    }

    private bool TryReserveClientSlot(TcpClient client)
    {
        VirtualReaderConnection[] existing;
        lock (_connectionGate)
        {
            if (_connections.Count < _options.MaximumClientConnections)
            {
                return true;
            }

            if (_options.ConnectionLimitPolicy == VirtualReaderConnectionLimitPolicy.RejectAdditional)
            {
                _logger.LogWarning(
                    "Rejected a virtual-reader client from {RemoteEndPoint} because the connection limit {Limit} was reached.",
                    client.Client.RemoteEndPoint,
                    _options.MaximumClientConnections);
                return false;
            }

            existing = _connections.Values.Take(1).ToArray();
            foreach (VirtualReaderConnection connection in existing)
            {
                _connections.Remove(connection.ConnectionId);
            }
        }

        foreach (VirtualReaderConnection connection in existing)
        {
            _ = connection.DisposeAsync();
        }

        return true;
    }

    private async Task RunConnectionAsync(VirtualReaderConnection connection, CancellationToken hostCancellationToken)
    {
        using var connectionCancellation = CancellationTokenSource.CreateLinkedTokenSource(hostCancellationToken);
        CancellationToken cancellationToken = connectionCancellation.Token;
        Task? terminationWatcher = null;
        Task? keepAliveTask = null;
        try
        {
            await connection.Session.ConnectAsync(cancellationToken).ConfigureAwait(false);
            connection.SetProtocolVersion(_options.ProtocolVersion);
            await SendMessageAsync(
                connection,
                _dispatcher.CreateReaderEventNotification(
                    _options.ProtocolVersion,
                    connection.NextAsyncMessageId()),
                _options.ProtocolVersion,
                cancellationToken).ConfigureAwait(false);

            terminationWatcher = WatchSessionTerminationAsync(connection.Session, connectionCancellation);
            if (_options.KeepAliveInterval is TimeSpan keepAliveInterval)
            {
                keepAliveTask = KeepAliveAsync(connection, keepAliveInterval, cancellationToken);
            }

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
            _logger.LogDebug(exception, "Virtual-reader client {ConnectionId} disconnected.", connection.ConnectionId);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Virtual-reader client {ConnectionId} session failed.", connection.ConnectionId);
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
            ReconcileReportLoops();
        }
    }

    private async Task HandleFrameAsync(
        VirtualReaderConnection connection,
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

        VirtualReaderRequestContext context = new(
            this,
            _deviceState,
            connection.ConnectionId,
            header.Version,
            header.MessageId);
        VirtualReaderDispatchResult result;
        if (_errorResponseMessageTypes.TryGetValue(header.MessageType, out VirtualReaderErrorResponse? injectedError))
        {
            result = new VirtualReaderDispatchResult(
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
                result = new VirtualReaderDispatchResult(
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
        VirtualReaderConnection connection,
        TimeSpan interval,
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
                LlrpProtocolVersion version = connection.ProtocolVersion;
                ILlrpMessage keepalive = _dispatcher.CreateKeepalive(version, connection.NextAsyncMessageId());
                await SendMessageAsync(connection, keepalive, version, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void ReconcileReportLoops()
    {
        if (!IsRunning)
        {
            return;
        }

        IReadOnlyList<uint> activeRoSpecIds = _deviceState.GetActiveRoSpecIds();
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
                if (!_deviceState.GetActiveRoSpecIds().Contains(roSpecId))
                {
                    return;
                }

                VirtualReaderConnection[] connections;
                lock (_connectionGate)
                {
                    connections = _connections.Values.ToArray();
                }

                foreach (VirtualReaderConnection connection in connections)
                {
                    if (!connection.IsConnected)
                    {
                        continue;
                    }

                    LlrpProtocolVersion version = connection.ProtocolVersion;
                    foreach (ILlrpMessage report in _dispatcher.BuildInventoryReports(version, roSpecId))
                    {
                        try
                        {
                            await SendMessageAsync(connection, report, version, cancellationToken).ConfigureAwait(false);
                        }
                        catch (Exception exception) when (exception is IOException or SocketException or LlrpSessionDisconnectedException or ObjectDisposedException)
                        {
                            _logger.LogDebug(exception, "Inventory report delivery failed for {ConnectionId}.", connection.ConnectionId);
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
        VirtualReaderConnection connection,
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

    private void SetState(VirtualReaderLifecycleState state, Exception? error = null)
    {
        VirtualReaderLifecycleState previous = (VirtualReaderLifecycleState)Interlocked.Exchange(
            ref _lifecycleState,
            (int)state);
        if (previous == state && error is null)
        {
            return;
        }

        try
        {
            LifecycleChanged?.Invoke(this, new VirtualReaderLifecycleChangedEventArgs(previous, state, error));
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "A virtual-reader lifecycle observer threw.");
        }
    }

    private void RaiseClientChanged(VirtualReaderConnection connection, bool connected)
    {
        try
        {
            ClientChanged?.Invoke(this, new VirtualReaderClientChangedEventArgs(connection.ToInfo(), connected));
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "A virtual-reader client observer threw.");
        }
    }

    private void RaiseMessageObserved(
        VirtualReaderConnection connection,
        LlrpMessageHeader header,
        bool incoming,
        string? detail)
    {
        try
        {
            MessageObserved?.Invoke(
                this,
                new VirtualReaderMessageEventArgs(
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
            throw new ObjectDisposedException(nameof(VirtualReaderHost));
        }
    }

    private sealed record ReportLoop(CancellationTokenSource Cancellation, Task Task);

    private sealed class VirtualReaderConnection : IAsyncDisposable
    {
        private readonly LlrpAcceptedTcpTransport _transport;
        private int _disposed;
        private int _nextAsyncMessageId;
        private int _protocolVersion = (int)LlrpProtocolVersion.Version101;

        public VirtualReaderConnection(LlrpAcceptedTcpTransport transport)
        {
            _transport = transport;
            Session = new LlrpSession(transport, new LlrpSessionOptions
            {
                UnsolicitedFrameCapacity = 4096,
                UnsolicitedFrameOverflowPolicy = LlrpUnsolicitedFrameOverflowPolicy.FaultConnection,
            });
            ConnectedAt = DateTimeOffset.UtcNow;
        }

        public string ConnectionId => _transport.ConnectionId;
        public EndPoint? RemoteEndPoint => _transport.RemoteEndPoint;
        public DateTimeOffset ConnectedAt { get; }
        public LlrpSession Session { get; }
        public bool IsConnected => Session.IsConnected && Volatile.Read(ref _disposed) == 0;
        public LlrpProtocolVersion ProtocolVersion => (LlrpProtocolVersion)Volatile.Read(ref _protocolVersion);

        public void SetProtocolVersion(LlrpProtocolVersion version) => Volatile.Write(ref _protocolVersion, (int)version);

        public uint NextAsyncMessageId() => unchecked((uint)Interlocked.Increment(ref _nextAsyncMessageId));

        public VirtualReaderClientInfo ToInfo() => new(
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
