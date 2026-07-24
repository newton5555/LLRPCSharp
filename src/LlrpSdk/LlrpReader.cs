using System.Runtime.ExceptionServices;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using LlrpNet.Core.Protocol;
using LlrpNet.Core.Session;
using LlrpNet.Core.Transactions;
using LlrpNet.Core.Transport;
using LlrpNet.Protocol.Messages;
using LlrpNet.Protocol.Messages.V1_0_1;
using LlrpNet.Protocol.Parameters.V1_0_1;
using LlrpNet.Protocol.Registry;
using LlrpNet.Protocol.Registry.V1_0_1;
using Microsoft.Extensions.Logging;

namespace LlrpSdk;

/// <summary>
/// Represents one reader connection and owns its transport, LLRP session, protocol registry, and unsolicited-message pump.
/// </summary>
public sealed class LlrpReader : IAsyncDisposable
{
    private readonly Channel<ILlrpMessage> _messages;
    private readonly Channel<TagReport> _tagReports;
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private readonly ILogger<LlrpReader> _logger;
    private readonly LlrpMessageIdGenerator _messageIds = new();
    private readonly LlrpCodecRegistry _registry;
    private readonly LlrpSession _session;
    private CancellationTokenSource? _pumpCancellation;
    private Task? _pumpTask;
    private ReaderSettings? _currentSettings;
    private ReaderMetadataSnapshot? _metadata;
    private uint? _managedInventoryRoSpecId;
    private int _connectionState = (int)ReaderConnectionState.Disconnected;
    private int _managedStateIsSynchronized = 1;
    private int _operationState = (int)ReaderOperationState.Idle;
    private int _disposed;

    /// <summary>
    /// Creates the application-facing fluent builder for one reader.
    /// </summary>
    /// <param name="host">The reader hostname or IP address.</param>
    /// <returns>A builder that creates a disconnected <see cref="LlrpReader"/>.</returns>
    public static LlrpReaderBuilder CreateBuilder(string host)
    {
        return new LlrpReaderBuilder(host);
    }

    /// <summary>
    /// Initializes a reader from validated immutable options.
    /// </summary>
    /// <param name="options">Connection, timeout, logging, observation, and transport settings.</param>
    public LlrpReader(LlrpReaderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        Options = options;
        _messages = Channel.CreateBounded<ILlrpMessage>(new BoundedChannelOptions(
            options.IncomingMessageCapacity)
        {
            AllowSynchronousContinuations = false,
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = true,
        });
        _tagReports = Channel.CreateBounded<TagReport>(new BoundedChannelOptions(
            options.IncomingMessageCapacity)
        {
            AllowSynchronousContinuations = false,
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = true,
        });
        _logger = options.LoggerFactory.CreateLogger<LlrpReader>();
        _registry = new LlrpCodecRegistry();
        Llrp101StandardModule.Register(_registry);
        foreach (Action<LlrpCodecRegistry> configureProtocol in options.ProtocolConfigurations)
        {
            configureProtocol(_registry);
        }

        ILlrpTransport transport = options.TransportFactory(options) ??
            throw new InvalidOperationException("The configured LLRP transport factory returned null.");
        _session = new LlrpSession(
            transport,
            new LlrpSessionOptions
            {
                DefaultRequestTimeout = options.RequestTimeout,
                UnsolicitedFrameCapacity = Math.Max(options.IncomingMessageCapacity, 16),
                UnsolicitedFrameOverflowPolicy =
                    LlrpUnsolicitedFrameOverflowPolicy.FaultConnection,
            },
            options.LoggerFactory);
        RoSpecs = new RoSpecService(this, _messageIds, _registry);
        AccessSpecs = new AccessSpecService(this, _messageIds, _registry);
        Protocol = new ReaderProtocolAccess(this);
    }

    /// <summary>
    /// Gets the immutable options used by this reader.
    /// </summary>
    public LlrpReaderOptions Options { get; }

    /// <summary>
    /// Gets the current connection state.
    /// </summary>
    public ReaderConnectionState ConnectionState =>
        (ReaderConnectionState)Volatile.Read(ref _connectionState);

    /// <summary>
    /// Gets the current managed-operation state.
    /// </summary>
    /// <remarks>The M2 session baseline remains <see cref="ReaderOperationState.Idle"/>.</remarks>
    public ReaderOperationState OperationState =>
        (ReaderOperationState)Volatile.Read(ref _operationState);

    /// <summary>
    /// Gets a value indicating whether the reader is Ready and its underlying session remains connected.
    /// </summary>
    public bool IsConnected =>
        ConnectionState == ReaderConnectionState.Ready && _session.IsConnected;

    /// <summary>
    /// Gets the protocol version used by the initial session baseline.
    /// </summary>
    /// <remarks>
    /// Version negotiation is an M2 follow-up. Until then the standard registry and typed encoder are fixed to
    /// LLRP 1.0.1 and this property always returns <see cref="LlrpProtocolVersion.Version101"/>.
    /// </remarks>
    public LlrpProtocolVersion NegotiatedVersion => LlrpProtocolVersion.Version101;

    /// <summary>
    /// Gets the identity from the current initialized connection, or <see langword="null"/> while disconnected or faulted.
    /// </summary>
    public ReaderIdentity? Identity => Volatile.Read(ref _metadata)?.Identity;

    /// <summary>
    /// Gets capabilities from the current initialized connection, or <see langword="null"/> while disconnected or faulted.
    /// </summary>
    public ReaderCapabilities? Capabilities => Volatile.Read(ref _metadata)?.Capabilities;

    /// <summary>
    /// Gets the settings for the currently managed inventory operation, or <see langword="null"/> when idle.
    /// </summary>
    public ReaderSettings? CurrentSettings => Volatile.Read(ref _currentSettings);

    /// <summary>
    /// Gets a value indicating whether SDK-managed resource state is known after the most recent raw protocol call.
    /// </summary>
    /// <remarks>
    /// A successful call through <see cref="Protocol"/> may change reader state outside the SDK's managed services.
    /// Call <see cref="SynchronizeStateAsync(CancellationToken)"/> before resuming a managed operation when this
    /// property is <see langword="false"/>.
    /// </remarks>
    public bool IsManagedStateSynchronized => Volatile.Read(ref _managedStateIsSynchronized) != 0;

    /// <summary>
    /// Gets the ROSpec resource service for this reader.
    /// </summary>
    /// <remarks>
    /// Operations are available only while the reader is <see cref="ReaderConnectionState.Ready"/> and are sent
    /// directly to the reader without maintaining a local resource cache.
    /// </remarks>
    public IRoSpecService RoSpecs { get; }

    /// <summary>
    /// Gets the AccessSpec resource service for this reader.
    /// </summary>
    /// <remarks>
    /// Operations are available only while the reader is <see cref="ReaderConnectionState.Ready"/> and are sent
    /// directly to the reader without maintaining a local resource cache.
    /// </remarks>
    public IAccessSpecService AccessSpecs { get; }

    /// <summary>
    /// Gets typed and exact-frame protocol access for this reader.
    /// </summary>
    public IReaderProtocolAccess Protocol { get; }

    /// <summary>
    /// Gets the underlying transport correlation identifier used in diagnostics.
    /// </summary>
    public string ConnectionId => _session.ConnectionId;

    /// <summary>
    /// Occurs after a connection-state transition has been recorded.
    /// </summary>
    public event EventHandler<ReaderConnectionChangedEventArgs>? ConnectionChanged;

    /// <summary>
    /// Occurs when a connection lifecycle or background protocol-pump failure is recorded.
    /// </summary>
    public event EventHandler<ReaderErrorEventArgs>? ErrorOccurred;

    /// <summary>
    /// Connects the session and starts the sole unsolicited-frame consumer.
    /// </summary>
    /// <param name="cancellationToken">Cancels waiting for or establishing the connection.</param>
    /// <returns>A task representing the lifecycle operation.</returns>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var transitions = new List<StateTransition>();
        Exception? reportedError = null;

        await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (IsConnected)
            {
                return;
            }

            InvalidateMetadata();

            if (ConnectionState == ReaderConnectionState.Ready)
            {
                var interruption = new LlrpReaderConnectionException(
                    ConnectionId,
                    $"LLRP reader session {ConnectionId} stopped before its background pump observed the interruption.");
                AddTransition(transitions, ReaderConnectionState.Faulted, interruption);
                reportedError = interruption;
            }

            AddTransition(transitions, ReaderConnectionState.Connecting);
            try
            {
                await _session.ConnectAsync(cancellationToken).ConfigureAwait(false);
                ThrowIfDisposed();
                if (!_session.IsConnected)
                {
                    throw new LlrpReaderConnectionException(
                        ConnectionId,
                        $"LLRP reader session {ConnectionId} did not remain connected after ConnectAsync completed.");
                }

                StartPump();
                AddTransition(transitions, ReaderConnectionState.Negotiating);
                AddTransition(transitions, ReaderConnectionState.Initializing);
                await InitializeReaderAsync(cancellationToken).ConfigureAwait(false);
                AddTransition(transitions, ReaderConnectionState.Ready);
            }
            catch (Exception exception)
            {
                InvalidateMetadata();
                await StopPumpAsync().ConfigureAwait(false);
                await TryDisconnectAfterFailureAsync().ConfigureAwait(false);

                bool expectedCancellation =
                    exception is OperationCanceledException && cancellationToken.IsCancellationRequested ||
                    Volatile.Read(ref _disposed) != 0;
                AddTransition(
                    transitions,
                    expectedCancellation
                        ? ReaderConnectionState.Disconnected
                        : ReaderConnectionState.Faulted,
                    expectedCancellation ? null : exception);
                if (!expectedCancellation)
                {
                    reportedError = exception;
                }

                throw;
            }
        }
        finally
        {
            _lifecycleLock.Release();
            PublishTransitions(transitions);
            if (reportedError is not null)
            {
                PublishError(reportedError);
            }
        }
    }

    /// <summary>
    /// Stops the message pump and disconnects the owned session. Repeated calls while disconnected are safe.
    /// </summary>
    /// <param name="cancellationToken">Cancels waiting to begin the lifecycle operation.</param>
    /// <returns>A task representing the lifecycle operation.</returns>
    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var transitions = new List<StateTransition>();
        Exception? reportedError = null;

        await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            InvalidateMetadata();

            if (ConnectionState == ReaderConnectionState.Disconnected)
            {
                return;
            }

            AddTransition(transitions, ReaderConnectionState.Disconnecting);
            try
            {
                await StopPumpAsync().ConfigureAwait(false);
                await _session.DisconnectAsync().ConfigureAwait(false);
                ResetManagedInventoryState();
                AddTransition(transitions, ReaderConnectionState.Disconnected);
            }
            catch (Exception exception)
            {
                AddTransition(transitions, ReaderConnectionState.Faulted, exception);
                reportedError = exception;
                throw;
            }
        }
        finally
        {
            _lifecycleLock.Release();
            PublishTransitions(transitions);
            if (reportedError is not null)
            {
                PublishError(reportedError);
            }
        }
    }

    /// <summary>
    /// Explicitly disconnects and reconnects this reader.
    /// </summary>
    /// <param name="cancellationToken">Cancels either lifecycle operation.</param>
    /// <returns>A task representing both lifecycle operations.</returns>
    public async Task ReconnectAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var transitions = new List<StateTransition>();
        Exception? reportedError = null;

        await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            InvalidateMetadata();
            AddTransition(transitions, ReaderConnectionState.Reconnecting);
            try
            {
                await StopPumpAsync().ConfigureAwait(false);
                await _session.DisconnectAsync(cancellationToken).ConfigureAwait(false);
                await _session.ConnectAsync(cancellationToken).ConfigureAwait(false);
                ThrowIfDisposed();
                if (!_session.IsConnected)
                {
                    throw new LlrpReaderConnectionException(
                        ConnectionId,
                        $"LLRP reader session {ConnectionId} did not remain connected after reconnecting.");
                }

                StartPump();
                AddTransition(transitions, ReaderConnectionState.Negotiating);
                AddTransition(transitions, ReaderConnectionState.Initializing);
                await InitializeReaderAsync(cancellationToken).ConfigureAwait(false);
                AddTransition(transitions, ReaderConnectionState.Ready);
            }
            catch (Exception exception)
            {
                InvalidateMetadata();
                await StopPumpAsync().ConfigureAwait(false);
                await TryDisconnectAfterFailureAsync().ConfigureAwait(false);

                bool expectedCancellation =
                    exception is OperationCanceledException && cancellationToken.IsCancellationRequested ||
                    Volatile.Read(ref _disposed) != 0;
                AddTransition(
                    transitions,
                    expectedCancellation
                        ? ReaderConnectionState.Disconnected
                        : ReaderConnectionState.Faulted,
                    expectedCancellation ? null : exception);
                if (!expectedCancellation)
                {
                    reportedError = exception;
                }

                throw;
            }
        }
        finally
        {
            _lifecycleLock.Release();
            PublishTransitions(transitions);
            if (reportedError is not null)
            {
                PublishError(reportedError);
            }
        }
    }

    /// <summary>
    /// Asynchronously reads decoded messages that were not matched to pending transactions.
    /// </summary>
    /// <param name="cancellationToken">Cancels this enumeration without disconnecting the reader.</param>
    /// <returns>A channel-backed asynchronous sequence that remains open across explicit reconnects.</returns>
    /// <remarks>
    /// Multiple simultaneous enumerators compete for messages; callers needing fan-out should distribute the
    /// sequence in their application. KEEPALIVE messages are included after their automatic ACK is sent.
    /// </remarks>
    public IAsyncEnumerable<ILlrpMessage> ReadMessagesAsync(
        CancellationToken cancellationToken = default)
    {
        return _messages.Reader.ReadAllAsync(cancellationToken);
    }

    /// <summary>
    /// Asynchronously reads version-independent tag observations projected from reader access reports.
    /// </summary>
    /// <param name="cancellationToken">Cancels this enumeration without disconnecting the reader.</param>
    /// <returns>A channel-backed asynchronous sequence that remains open across explicit reconnects.</returns>
    /// <remarks>
    /// Multiple simultaneous enumerators compete for observations. Callers needing fan-out should distribute the
    /// sequence in their application. Raw LLRP messages remain independently available through
    /// <see cref="ReadMessagesAsync(CancellationToken)"/>.
    /// </remarks>
    public IAsyncEnumerable<TagReport> ReadTagReportsAsync(
        CancellationToken cancellationToken = default)
    {
        return _tagReports.Reader.ReadAllAsync(cancellationToken);
    }

    /// <summary>
    /// Queries reader-managed resources after raw protocol access invalidated the SDK's local state assumptions.
    /// </summary>
    /// <param name="cancellationToken">Cancels the synchronization queries.</param>
    /// <returns>A task that completes after standard ROSpec and AccessSpec state has been queried.</returns>
    /// <remarks>
    /// Synchronization deliberately does not recreate a previous high-level inventory operation. If raw access changed
    /// a resource, the application must explicitly establish the next desired managed state.
    /// </remarks>
    public async Task SynchronizeStateAsync(CancellationToken cancellationToken = default)
    {
        EnsureProtocolAvailable();

        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureProtocolAvailable();
            await RoSpecs.GetAllAsync(cancellationToken).ConfigureAwait(false);
            await AccessSpecs.GetAllAsync(cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref _managedStateIsSynchronized, 1);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    /// <summary>
    /// Compiles and starts one SDK-managed inventory operation.
    /// </summary>
    /// <param name="settings">The version-independent inventory intent to apply.</param>
    /// <param name="cancellationToken">Cancels the resource operations before inventory becomes active.</param>
    /// <returns>A task that completes after the reader accepts the managed ROSpec.</returns>
    /// <remarks>
    /// The current baseline compiles settings to LLRP 1.0.1. The public method does not expose generated protocol
    /// types, allowing later protocol adapters to compile the same intent for another negotiated version.
    /// </remarks>
    public async Task StartAsync(
        ReaderSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        EnsureProtocolAvailable();

        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureProtocolAvailable();
            EnsureManagedStateSynchronized();
            if (OperationState != ReaderOperationState.Idle)
            {
                throw new InvalidOperationException(
                    $"Cannot start managed inventory while the reader operation state is {OperationState}.");
            }

            Volatile.Write(ref _operationState, (int)ReaderOperationState.Starting);
            bool added = false;
            bool enabled = false;
            try
            {
                global::LlrpNet.Protocol.Parameters.V1_0_1.ROSpec roSpec =
                    Llrp101InventoryCompiler.Compile(settings);
                await RoSpecs.AddAsync(roSpec, cancellationToken).ConfigureAwait(false);
                added = true;
                await RoSpecs.EnableAsync(settings.RoSpecId, cancellationToken).ConfigureAwait(false);
                enabled = true;
                await RoSpecs.StartAsync(settings.RoSpecId, cancellationToken).ConfigureAwait(false);

                _managedInventoryRoSpecId = settings.RoSpecId;
                Volatile.Write(ref _currentSettings, settings);
                Volatile.Write(ref _operationState, (int)ReaderOperationState.Inventorying);
            }
            catch
            {
                if (enabled)
                {
                    await TryManagedInventoryCleanupAsync(
                        settings.RoSpecId,
                        stop: true,
                        CancellationToken.None).ConfigureAwait(false);
                }
                else if (added)
                {
                    await TryDeleteManagedInventoryAsync(settings.RoSpecId, CancellationToken.None).ConfigureAwait(false);
                }

                ResetManagedInventoryState();
                throw;
            }
        }
        finally
        {
            _operationLock.Release();
        }
    }

    /// <summary>
    /// Stops and removes the current SDK-managed inventory ROSpec.
    /// </summary>
    /// <param name="cancellationToken">Cancels the resource operations.</param>
    /// <returns>A task that completes after the managed ROSpec is removed.</returns>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        EnsureProtocolAvailable();

        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureProtocolAvailable();
            if (OperationState == ReaderOperationState.Idle)
            {
                return;
            }

            if (OperationState != ReaderOperationState.Inventorying || _managedInventoryRoSpecId is not uint roSpecId)
            {
                throw new InvalidOperationException(
                    $"Cannot stop managed inventory while the reader operation state is {OperationState}.");
            }

            Volatile.Write(ref _operationState, (int)ReaderOperationState.Stopping);
            try
            {
                await StopManagedInventoryAsync(roSpecId, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                ResetManagedInventoryState();
            }
        }
        finally
        {
            _operationLock.Release();
        }
    }

    /// <summary>
    /// Starts managed inventory and yields version-independent tag observations until the enumeration ends.
    /// </summary>
    /// <param name="settings">The inventory intent, or <see langword="null"/> to use the default settings.</param>
    /// <param name="cancellationToken">Cancels inventory enumeration and then requests managed inventory cleanup.</param>
    /// <returns>An asynchronous sequence of observed tags.</returns>
    public async IAsyncEnumerable<TagReport> InventoryAsync(
        ReaderSettings? settings = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await StartAsync(settings ?? new ReaderSettings(), cancellationToken).ConfigureAwait(false);
        try
        {
            await foreach (TagReport report in ReadTagReportsAsync(cancellationToken))
            {
                yield return report;
            }
        }
        finally
        {
            if (IsConnected && OperationState == ReaderOperationState.Inventorying)
            {
                try
                {
                    await StopAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    _logger.LogWarning(
                        exception,
                        "Failed to stop SDK-managed inventory while completing an inventory enumeration for reader {ConnectionId}",
                        ConnectionId);
                }
            }
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        var transitions = new List<StateTransition>();
        Exception? failure = null;
        await _lifecycleLock.WaitAsync().ConfigureAwait(false);
        try
        {
            InvalidateMetadata();
            if (ConnectionState != ReaderConnectionState.Disconnected)
            {
                AddTransition(transitions, ReaderConnectionState.Disconnecting);
            }

            await StopPumpAsync().ConfigureAwait(false);
            try
            {
                await _session.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            ResetManagedInventoryState();
            AddTransition(transitions, ReaderConnectionState.Disconnected, failure);
            _messages.Writer.TryComplete(failure);
            _tagReports.Writer.TryComplete(failure);
        }
        finally
        {
            _lifecycleLock.Release();
            PublishTransitions(transitions);
            if (failure is not null)
            {
                PublishError(failure);
            }
        }

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    internal async Task<TResponse> TransactAsync<TResponse>(
        ILlrpMessage request,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
        where TResponse : class, ILlrpMessage
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureProtocolAvailable();

        return await TransactSessionAsync<TResponse>(request, timeout, cancellationToken)
            .ConfigureAwait(false);
    }

    internal async Task<TResponse> TransactFromRawProtocolAsync<TResponse>(
        ILlrpMessage request,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
        where TResponse : class, ILlrpMessage
    {
        TResponse response = await TransactAsync<TResponse>(request, timeout, cancellationToken).ConfigureAwait(false);
        InvalidateManagedStateAfterRawProtocolAccess();
        return response;
    }

    private async Task<TResponse> TransactSessionAsync<TResponse>(
        ILlrpMessage request,
        TimeSpan? timeout,
        CancellationToken cancellationToken,
        LlrpResponseMatcher? responseMatcher = null)
        where TResponse : class, ILlrpMessage
    {
        ArgumentNullException.ThrowIfNull(request);

        byte[] requestFrame = _registry.EncodeMessage(NegotiatedVersion, request);
        ReadOnlyMemory<byte> responseFrame = await _session
            .TransactAsync(
                requestFrame,
                responseMatcher ?? MatchesTypedResponse<TResponse>,
                timeout,
                cancellationToken)
            .ConfigureAwait(false);
        ILlrpMessage response = _registry.DecodeMessage(responseFrame.Span);
        if (response is ErrorMessage errorMessage)
        {
            throw new LlrpReaderOperationException(
                request.GetType().Name,
                errorMessage.LLRPStatus);
        }

        if (response.GetType() != typeof(TResponse))
        {
            throw new LlrpUnexpectedResponseException(
                request.GetType(),
                typeof(TResponse),
                response);
        }

        return (TResponse)response;
    }

    internal async Task SendAsync<TMessage>(
        TMessage message,
        CancellationToken cancellationToken)
        where TMessage : ILlrpMessage
    {
        ArgumentNullException.ThrowIfNull(message);
        EnsureProtocolAvailable();

        byte[] frame = _registry.EncodeMessage(NegotiatedVersion, message);
        await _session.SendFrameAsync(frame, cancellationToken).ConfigureAwait(false);
    }

    internal async Task SendFromRawProtocolAsync<TMessage>(
        TMessage message,
        CancellationToken cancellationToken)
        where TMessage : ILlrpMessage
    {
        await SendAsync(message, cancellationToken).ConfigureAwait(false);
        InvalidateManagedStateAfterRawProtocolAccess();
    }

    internal async Task<ReadOnlyMemory<byte>> TransactRawAsync(
        ReadOnlyMemory<byte> requestFrame,
        LlrpResponseMatcher responseMatcher,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(responseMatcher);
        EnsureProtocolAvailable();
        ReadOnlyMemory<byte> response = await _session.TransactAsync(
            requestFrame,
            responseMatcher,
            timeout,
            cancellationToken).ConfigureAwait(false);
        InvalidateManagedStateAfterRawProtocolAccess();
        return response;
    }

    internal async Task SendRawAsync(
        ReadOnlyMemory<byte> frame,
        CancellationToken cancellationToken)
    {
        EnsureProtocolAvailable();
        await _session.SendFrameAsync(frame, cancellationToken).ConfigureAwait(false);
        InvalidateManagedStateAfterRawProtocolAccess();
    }

    private void StartPump()
    {
        if (_pumpTask is not null)
        {
            throw new InvalidOperationException("The LLRP unsolicited-message pump is already running.");
        }

        var cancellation = new CancellationTokenSource();
        _pumpCancellation = cancellation;
        _pumpTask = PumpAsync(cancellation, _session.ConnectionCompletion);
    }

    private async Task StopPumpAsync()
    {
        CancellationTokenSource? cancellation = _pumpCancellation;
        Task? pumpTask = _pumpTask;
        _pumpCancellation = null;
        _pumpTask = null;

        if (cancellation is null)
        {
            return;
        }

        cancellation.Cancel();
        if (pumpTask is not null)
        {
            await pumpTask.ConfigureAwait(false);
        }

        cancellation.Dispose();
    }

    private async Task PumpAsync(
        CancellationTokenSource cancellation,
        Task<LlrpSessionTermination> connectionCompletion)
    {
        CancellationToken cancellationToken = cancellation.Token;
        Exception? failure = null;
        try
        {
            Task<bool> pendingAvailability = _session.UnsolicitedFrames
                .WaitToReadAsync(cancellationToken)
                .AsTask();

            while (true)
            {
                Task completed = await Task
                    .WhenAny(connectionCompletion, pendingAvailability)
                    .ConfigureAwait(false);
                if (ReferenceEquals(completed, connectionCompletion))
                {
                    LlrpSessionTermination termination = await connectionCompletion.ConfigureAwait(false);
                    if (cancellationToken.IsCancellationRequested || termination.WasRequested)
                    {
                        return;
                    }

                    throw termination.Error ?? new LlrpReaderConnectionException(
                        ConnectionId,
                        $"LLRP reader session {ConnectionId} stopped without reporting a cause.");
                }

                if (!await pendingAvailability.ConfigureAwait(false))
                {
                    throw new LlrpReaderConnectionException(
                        ConnectionId,
                        $"LLRP reader session {ConnectionId} ended its unsolicited-frame stream unexpectedly.");
                }

                while (_session.UnsolicitedFrames.TryRead(out ReadOnlyMemory<byte> frame))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ILlrpMessage message = _registry.DecodeMessage(frame.Span);
                    if (message is Keepalive keepalive)
                    {
                        byte[] acknowledgement = _registry.EncodeMessage(
                            NegotiatedVersion,
                            new KeepaliveAck(keepalive.MessageId));
                        await _session
                            .SendFrameAsync(acknowledgement, cancellationToken)
                            .ConfigureAwait(false);
                    }

                    if (message is global::LlrpNet.Protocol.Messages.V1_0_1.RO_ACCESS_REPORT accessReport)
                    {
                        foreach (TagReport tagReport in Llrp101TagReportTranslator.Translate(accessReport))
                        {
                            if (!_tagReports.Writer.TryWrite(tagReport))
                            {
                                throw new LlrpReaderBackpressureException(
                                    ConnectionId,
                                    Options.IncomingMessageCapacity);
                            }
                        }
                    }

                    if (!_messages.Writer.TryWrite(message))
                    {
                        throw new LlrpReaderBackpressureException(
                            ConnectionId,
                            Options.IncomingMessageCapacity);
                    }
                }

                pendingAvailability = _session.UnsolicitedFrames
                    .WaitToReadAsync(cancellationToken)
                    .AsTask();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Explicit disconnect or disposal owns the lifecycle transition.
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            // A concurrent disconnect can close the session while an ACK or health check is in progress.
        }
        catch (Exception exception)
        {
            failure = exception;
            _logger.LogError(
                exception,
                "LLRP reader message pump failed for connection {ConnectionId}",
                ConnectionId);
        }

        if (failure is not null)
        {
            await HandlePumpFailureAsync(cancellation, failure).ConfigureAwait(false);
        }
    }

    private async Task HandlePumpFailureAsync(
        CancellationTokenSource cancellation,
        Exception failure)
    {
        var transitions = new List<StateTransition>();
        try
        {
            await _lifecycleLock.WaitAsync(cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return;
        }

        try
        {
            if (!ReferenceEquals(_pumpCancellation, cancellation) ||
                ConnectionState is ReaderConnectionState.Disconnecting or ReaderConnectionState.Disconnected)
            {
                return;
            }

            _pumpCancellation = null;
            _pumpTask = null;
            ResetManagedInventoryState();
            InvalidateMetadata();
            AddTransition(transitions, ReaderConnectionState.Faulted, failure);
            try
            {
                await _session.DisconnectAsync().ConfigureAwait(false);
            }
            catch (Exception disconnectFailure)
            {
                _logger.LogWarning(
                    disconnectFailure,
                    "Failed to close LLRP session {ConnectionId} after its reader message pump stopped",
                    ConnectionId);
            }
        }
        finally
        {
            _lifecycleLock.Release();
            cancellation.Dispose();
            PublishTransitions(transitions);
            if (transitions.Count != 0)
            {
                PublishError(failure);
            }
        }
    }

    private async Task TryDisconnectAfterFailureAsync()
    {
        try
        {
            await _session.DisconnectAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Failed to clean up LLRP session {ConnectionId} after a connection attempt failed",
                ConnectionId);
        }
    }

    private async Task InitializeReaderAsync(CancellationToken cancellationToken)
    {
        var request = new GetReaderCapabilities(
            _messageIds.Next(),
            GetReaderCapabilitiesRequestedData.All,
            CustomItems: []);
        GetReaderCapabilitiesResponse response;
        try
        {
            response = await TransactSessionAsync<GetReaderCapabilitiesResponse>(
                request,
                Options.RequestTimeout,
                cancellationToken,
                MatchesCapabilitiesResponse).ConfigureAwait(false);
        }
        catch (LlrpProtocolException exception)
        {
            throw new LlrpReaderInitializationException(
                "The GET_READER_CAPABILITIES(All) response could not be decoded into a valid " +
                "LLRP 1.0.1 capability model.",
                exception);
        }

        if (response.LLRPStatus.StatusCode != LlrpStatusCode.M_Success)
        {
            throw new LlrpReaderOperationException("GET_READER_CAPABILITIES", response.LLRPStatus);
        }

        GeneralDeviceCapabilities[] generalCapabilities = response.GeneralDeviceCapabilities is null
            ? Array.Empty<GeneralDeviceCapabilities>()
            : [response.GeneralDeviceCapabilities];
        if (generalCapabilities.Length != 1)
        {
            throw new LlrpReaderInitializationException(
                "A successful GET_READER_CAPABILITIES(All) response must contain exactly one " +
                $"GeneralDeviceCapabilities parameter, but received {generalCapabilities.Length}.");
        }

        GeneralDeviceCapabilities general = generalCapabilities[0];
        var identity = new ReaderIdentity(
            general.DeviceManufacturerName,
            general.ModelName,
            general.ReaderFirmwareVersion);
        var capabilities = new ReaderCapabilities(general, response);
        Volatile.Write(ref _metadata, new ReaderMetadataSnapshot(identity, capabilities));
    }

    private void InvalidateMetadata()
    {
        Volatile.Write(ref _metadata, null);
    }

    private void EnsureProtocolAvailable()
    {
        ThrowIfDisposed();
        if (!IsConnected)
        {
            throw new InvalidOperationException(
                $"The LLRP reader is not ready for protocol operations; current state is {ConnectionState}.");
        }
    }

    private void EnsureManagedStateSynchronized()
    {
        if (!IsManagedStateSynchronized)
        {
            throw new InvalidOperationException(
                "SDK-managed reader state is unknown after raw protocol access. " +
                $"Call {nameof(SynchronizeStateAsync)} before starting a managed operation.");
        }
    }

    private void InvalidateManagedStateAfterRawProtocolAccess()
    {
        ResetManagedInventoryState();
        Volatile.Write(ref _managedStateIsSynchronized, 0);
    }

    private void ResetManagedInventoryState()
    {
        _managedInventoryRoSpecId = null;
        Volatile.Write(ref _currentSettings, null);
        Volatile.Write(ref _operationState, (int)ReaderOperationState.Idle);
    }

    private async Task StopManagedInventoryAsync(uint roSpecId, CancellationToken cancellationToken)
    {
        Exception? failure = null;
        try
        {
            await RoSpecs.StopAsync(roSpecId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        try
        {
            await RoSpecs.DisableAsync(roSpecId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failure ??= exception;
        }

        try
        {
            await RoSpecs.DeleteAsync(roSpecId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failure ??= exception;
        }

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private async Task TryManagedInventoryCleanupAsync(uint roSpecId, bool stop, CancellationToken cancellationToken)
    {
        try
        {
            if (stop)
            {
                await StopManagedInventoryAsync(roSpecId, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await RoSpecs.DeleteAsync(roSpecId, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Failed to clean up SDK-managed inventory ROSpec {RoSpecId} on reader {ConnectionId}",
                roSpecId,
                ConnectionId);
        }
    }

    private Task TryDeleteManagedInventoryAsync(uint roSpecId, CancellationToken cancellationToken)
    {
        return TryManagedInventoryCleanupAsync(roSpecId, stop: false, cancellationToken);
    }

    private bool MatchesTypedResponse<TResponse>(
        LlrpMessageHeader header,
        ReadOnlyMemory<byte> frame)
        where TResponse : class, ILlrpMessage
    {
        if (header.MessageType == ErrorMessage.MessageType)
        {
            return true;
        }

        try
        {
            return _registry.DecodeMessage(frame.Span).GetType() == typeof(TResponse);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool MatchesCapabilitiesResponse(
        LlrpMessageHeader header,
        ReadOnlyMemory<byte> frame)
    {
        return header.MessageType is GetReaderCapabilitiesResponse.MessageType or ErrorMessage.MessageType;
    }

    private void AddTransition(
        ICollection<StateTransition> transitions,
        ReaderConnectionState newState,
        Exception? error = null)
    {
        ReaderConnectionState previousState = ConnectionState;
        if (previousState == newState)
        {
            return;
        }

        Volatile.Write(ref _connectionState, (int)newState);
        transitions.Add(new StateTransition(previousState, newState, error));
    }

    private void PublishTransitions(IEnumerable<StateTransition> transitions)
    {
        foreach (StateTransition transition in transitions)
        {
            try
            {
                ConnectionChanged?.Invoke(
                    this,
                    new ReaderConnectionChangedEventArgs(
                        transition.PreviousState,
                        transition.CurrentState,
                        transition.Error));
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "A reader connection-state event subscriber failed for connection {ConnectionId}",
                    ConnectionId);
            }
        }
    }

    private void PublishError(Exception error)
    {
        try
        {
            ErrorOccurred?.Invoke(this, new ReaderErrorEventArgs(error, ConnectionState));
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "A reader error event subscriber failed for connection {ConnectionId}",
                ConnectionId);
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
    }

    private readonly record struct StateTransition(
        ReaderConnectionState PreviousState,
        ReaderConnectionState CurrentState,
        Exception? Error);

    private sealed record ReaderMetadataSnapshot(
        ReaderIdentity Identity,
        ReaderCapabilities Capabilities);
}
