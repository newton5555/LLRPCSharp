using System.Runtime.ExceptionServices;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using LlrpNet.Core.Protocol;
using LlrpNet.Core.Session;
using LlrpNet.Core.Transactions;
using LlrpNet.Core.Transport;
using LlrpNet.Protocol.Messages;
using LlrpNet.Protocol.Messages.V1_0_1;
using LlrpNet.Protocol.Parameters;
using LlrpNet.Protocol.Parameters.V1_0_1;
using LlrpNet.Protocol.Registry;
using LlrpSdk.Extensions;
using Microsoft.Extensions.Logging;
using V101Messages = LlrpNet.Protocol.Messages.V1_0_1;
using V11Messages = LlrpNet.Protocol.Messages.V1_1;
using V11Enumerations = LlrpNet.Protocol.Enumerations.V1_1;
using V11Parameters = LlrpNet.Protocol.Parameters.V1_1;

namespace LlrpSdk;

/// <summary>
/// Represents one reader connection and owns its transport, LLRP session, protocol registry, and unsolicited-message pump.
/// </summary>
public sealed class LlrpReader : IAsyncDisposable
{
    internal const uint ManagedInventoryRoSpecId = 14150;
    internal const uint ManagedInventoryAttachedDataAccessSpecId = 14151;
    private readonly Channel<ILlrpMessage> _messages;
    private readonly Channel<TagReport> _tagReports;
    private readonly object _automaticReconnectGate = new();
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private readonly AsyncLocal<int> _internalResourceOperationDepth = new();
    private readonly ILogger<LlrpReader> _logger;
    private readonly LlrpMessageIdGenerator _messageIds = new();
    private readonly ReaderExtensionCollection _extensions = new();
    private readonly LlrpCodecRegistry _registry;
    private readonly IReadOnlyDictionary<LlrpProtocolVersion, ILlrpProtocolAdapter> _protocolAdapters;
    private ILlrpProtocolAdapter _protocolAdapter;
    private readonly LlrpSession _session;
    private CancellationTokenSource? _pumpCancellation;
    private Task? _pumpTask;
    private CancellationTokenSource? _keepaliveMonitorCancellation;
    private Task? _keepaliveMonitorTask;
    private CancellationTokenSource? _automaticReconnectCancellation;
    private Task? _automaticReconnectTask;
    private InventorySettings? _currentInventorySettings;
    private ReaderMetadataSnapshot? _metadata;
    private uint? _managedInventoryRoSpecId;
    private uint? _managedInventoryAttachedDataAccessSpecId;
    private InventorySession? _inventorySession;
    private int _nextManagedAccessSpecId = 24000;
    private int _connectionState = (int)ReaderConnectionState.Disconnected;
    private int _managedStateIsSynchronized = 1;
    private int _operationState = (int)ReaderOperationState.Idle;
    private int _resourceMode = (int)ReaderResourceMode.Idle;
    private int _disposed;
    private long _lastKeepaliveUtcTicks;
    private int _keepaliveTimeoutSignaled;

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
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = false,
            SingleWriter = true,
        });
        _tagReports = Channel.CreateBounded<TagReport>(new BoundedChannelOptions(
            options.IncomingMessageCapacity)
        {
            AllowSynchronousContinuations = false,
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = false,
            SingleWriter = true,
        });
        _logger = options.LoggerFactory.CreateLogger<LlrpReader>();
        _registry = new LlrpCodecRegistry();
        var protocolAdapters = new ILlrpProtocolAdapter[]
        {
            new Llrp101ProtocolAdapter(),
            new Llrp11ProtocolAdapter(),
        };
        _protocolAdapters = protocolAdapters.ToDictionary(adapter => adapter.Version);
        _protocolAdapter = _protocolAdapters[LlrpProtocolVersion.Version101];
        foreach (ILlrpProtocolAdapter protocolAdapter in protocolAdapters)
        {
            protocolAdapter.RegisterStandardCodecs(_registry);
        }
        foreach (ILlrpProtocolModule protocolModule in options.ProtocolModules)
        {
            protocolModule.Register(_registry);
        }

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
        RoSpecs = new RoSpecService(this, GetProtocolAdapter, _messageIds);
        AccessSpecs = new AccessSpecService(this, GetProtocolAdapter, _messageIds);
        Protocol = new ReaderProtocolAccess(this);
        Extensions = _extensions;
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

    /// <summary>Gets whether SDK high-level operations, an explicit manual session, or neither owns reader resources.</summary>
    public ReaderResourceMode ResourceMode =>
        (ReaderResourceMode)Volatile.Read(ref _resourceMode);

    /// <summary>
    /// Gets a value indicating whether the reader is Ready and its underlying session remains connected.
    /// </summary>
    public bool IsConnected =>
        ConnectionState == ReaderConnectionState.Ready && _session.IsConnected;

    /// <summary>
    /// Gets the protocol version selected for the current connection.
    /// </summary>
    public LlrpProtocolVersion NegotiatedVersion => GetProtocolAdapter().Version;

    /// <summary>
    /// Gets the identity from the current initialized connection, or <see langword="null"/> while disconnected or faulted.
    /// </summary>
    public ReaderIdentity? Identity => Volatile.Read(ref _metadata)?.Identity;

    /// <summary>
    /// Gets capabilities from the current initialized connection, or <see langword="null"/> while disconnected or faulted.
    /// </summary>
    public ReaderCapabilities? Capabilities => Volatile.Read(ref _metadata)?.Capabilities;

    /// <summary>
    /// Gets the settings for the SDK-managed inventory resource, or <see langword="null"/> when none is configured.
    /// </summary>
    public InventorySettings? CurrentInventorySettings => Volatile.Read(ref _currentInventorySettings);

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

    /// <summary>Gets extensions whose match rules selected this initialized reader.</summary>
    public IReaderExtensionCollection Extensions { get; }

    /// <summary>
    /// Gets the underlying transport correlation identifier used in diagnostics.
    /// </summary>
    public string ConnectionId => _session.ConnectionId;

    /// <summary>
    /// Gets the codec registry configured for this reader.
    /// </summary>
    public LlrpCodecRegistry Registry => _registry;

    /// <summary>
    /// Translates an incoming LLRP message into SDK tag reports using the active protocol adapter.
    /// </summary>
    public IReadOnlyList<TagReport> TranslateTagReports(ILlrpMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return GetProtocolAdapter().TranslateTagReports(message)
            .Select(ApplyTagReportContributors)
            .ToArray();
    }

    /// <summary>
    /// Occurs after a connection-state transition has been recorded.
    /// </summary>
    public event EventHandler<ReaderConnectionChangedEventArgs>? ConnectionChanged;

    /// <summary>
    /// Occurs when a connection lifecycle or background protocol-pump failure is recorded.
    /// </summary>
    public event EventHandler<ReaderErrorEventArgs>? ErrorOccurred;

    /// <summary>
    /// Occurs when an unsolicited access report produces a version-independent tag observation.
    /// </summary>
    /// <remarks>
    /// This event and <see cref="ReadTagReportsAsync(CancellationToken)"/> share the same translated report.
    /// An event subscriber failure is isolated and does not interrupt the reader message pump.
    /// </remarks>
    public event EventHandler<TagReportEventArgs>? TagsReported;

    /// <summary>
    /// Occurs when a GPI pin state change is reported by the reader.
    /// </summary>
    public event EventHandler<GpiChangedEventArgs>? GpiChanged;

    /// <summary>
    /// Occurs when the reader reports that an antenna was connected or disconnected.
    /// </summary>
    public event EventHandler<AntennaChangedEventArgs>? AntennaChanged;

    /// <summary>
    /// Occurs when a Keepalive message is received from the reader.
    /// </summary>
    public event EventHandler<EventArgs>? KeepaliveReceived;

    /// <summary>
    /// Occurs once for each uninterrupted KEEPALIVE silence period when opt-in liveness monitoring is enabled.
    /// The event is observational and does not disconnect the reader.
    /// </summary>
    public event EventHandler<KeepaliveTimeoutEventArgs>? KeepaliveTimedOut;

    /// <summary>
    /// Occurs when the reader reports a tag report buffer overflow event.
    /// </summary>
    public event EventHandler<EventArgs>? ReportBufferOverflow;

    /// <summary>
    /// Occurs when the reader reports that its tag-report buffer has reached a warning level.
    /// </summary>
    public event EventHandler<ReportBufferWarningEventArgs>? ReportBufferWarning;

    /// <summary>
    /// Connects the session and starts the sole unsolicited-frame consumer.
    /// </summary>
    /// <param name="cancellationToken">Cancels waiting for or establishing the connection.</param>
    /// <returns>A task representing the lifecycle operation.</returns>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        CancelAutomaticReconnect();
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
            SelectProtocolAdapter(LlrpProtocolVersion.Version101);

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
                await NegotiateProtocolVersionAsync(cancellationToken).ConfigureAwait(false);
                AddTransition(transitions, ReaderConnectionState.Initializing);
                await InitializeReaderAsync(cancellationToken).ConfigureAwait(false);
                AddTransition(transitions, ReaderConnectionState.Ready);
                StartKeepaliveMonitor();
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
    /// Deploys and starts exclusive SDK-managed inventory and returns its isolated report session.
    /// </summary>
    /// <remarks>
    /// Deployment takes full control of reader resources: before adding the SDK ROSpec (and optional attached-data
    /// AccessSpec) it deletes <b>all</b> ROSpecs and AccessSpecs on the device (LLRP id=0 delete semantics), including
    /// resources deployed by other applications. Do not use this on a reader shared with other managed ROSpecs; use
    /// the parameterless <see cref="StartInventoryAsync(CancellationToken)"/> overload after a prior deployment.
    /// </remarks>
    public async Task<InventorySession> StartInventoryAsync(InventorySettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_inventorySession is not null)
            {
                throw new InvalidOperationException("A managed inventory session already exists for this reader.");
            }
            ValidateSettingsCore(new ReaderSettings { Inventory = settings }).ThrowIfInvalid();
            InventorySettings active = settings;
            InventoryRuntimeState initialState = active.StartTrigger.Type == InventoryStartTriggerType.None
                ? InventoryRuntimeState.Running
                : InventoryRuntimeState.Enabled;
            var session = new InventorySession(this, active, ManagedInventoryRoSpecId,
                active.AttachedData.Enabled ? ManagedInventoryAttachedDataAccessSpecId : null, initialState);
            _inventorySession = session;
            try
            {
                await StartManagedInventoryCoreAsync(active, resourcesAlreadyCleared: false, cancellationToken).ConfigureAwait(false);
                return session;
            }
            catch
            {
                _inventorySession = null;
                session.Complete(InventoryRuntimeState.Disabled);
                throw;
            }
        }
        finally { _operationLock.Release(); }
    }

    /// <summary>Starts the inventory resource previously applied to this reader.</summary>
    /// <remarks>
    /// This overload requires a persisted SDK-managed inventory resource. Use the settings overload or
    /// <see cref="ApplySettingsAsync(ReaderSettings, CancellationToken)"/> first. Restoring inventory from a
    /// saved/default configuration is an application-layer concern: pass the recovered
    /// <see cref="InventorySettings"/> to
    /// <see cref="StartInventoryAsync(InventorySettings, CancellationToken)"/> explicitly.
    /// </remarks>
    public async Task<InventorySession> StartInventoryAsync(CancellationToken cancellationToken = default)
    {
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureProtocolAvailable();
            EnsureManagedStateSynchronized();
            if (_inventorySession is not null)
            {
                throw new InvalidOperationException("A managed inventory session already exists for this reader.");
            }
            if (ResourceMode != ReaderResourceMode.HighLevelConfigured ||
                _managedInventoryRoSpecId is not uint roSpecId ||
                CurrentInventorySettings is not { } settings)
            {
                throw new InvalidOperationException("No stopped SDK-managed inventory configuration is available to start.");
            }

            var session = new InventorySession(this, settings, roSpecId,
                _managedInventoryAttachedDataAccessSpecId, InventoryRuntimeState.Enabled);
            _inventorySession = session;
            try
            {
                await StartConfiguredManagedInventoryCoreAsync(cancellationToken).ConfigureAwait(false);
                return session;
            }
            catch
            {
                _inventorySession = null;
                session.Complete(InventoryRuntimeState.Disabled);
                throw;
            }
        }
        finally { _operationLock.Release(); }
    }

    internal async Task StopInventorySessionAsync(InventorySession session, CancellationToken cancellationToken)
    {
        if (ReferenceEquals(_inventorySession, session))
        {
            await StopAsync(cancellationToken).ConfigureAwait(false);
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
        CancelAutomaticReconnect();
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
    public Task ReconnectAsync(CancellationToken cancellationToken = default)
    {
        return ReconnectAsync(cancellationToken, cancelAutomaticReconnect: true);
    }

    private async Task ReconnectAsync(
        CancellationToken cancellationToken,
        bool cancelAutomaticReconnect)
    {
        ThrowIfDisposed();
        if (cancelAutomaticReconnect)
        {
            CancelAutomaticReconnect();
        }
        var transitions = new List<StateTransition>();
        Exception? reportedError = null;

        await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            InvalidateMetadata();
            SelectProtocolAdapter(LlrpProtocolVersion.Version101);
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
                await NegotiateProtocolVersionAsync(cancellationToken).ConfigureAwait(false);
                AddTransition(transitions, ReaderConnectionState.Initializing);
                await InitializeReaderAsync(cancellationToken).ConfigureAwait(false);
                AddTransition(transitions, ReaderConnectionState.Ready);
                StartKeepaliveMonitor();
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
            IReadOnlyList<ILlrpParameter> roSpecs = await RoSpecs.GetAllAsync(cancellationToken).ConfigureAwait(false);
            IReadOnlyList<ILlrpParameter> accessSpecs = await AccessSpecs.GetAllAsync(cancellationToken).ConfigureAwait(false);
            AdoptManagedInventorySnapshot(roSpecs.SingleOrDefault(IsManagedRoSpec), accessSpecs);
            Volatile.Write(ref _managedStateIsSynchronized, 1);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    /// <summary>
    /// Compiles and starts one SDK-managed inventory operation without an isolated report session.
    /// </summary>
    /// <param name="settings">The version-independent inventory intent to apply.</param>
    /// <param name="cancellationToken">Cancels the resource operations before inventory becomes active.</param>
    /// <returns>A task that completes after the reader accepts the managed ROSpec.</returns>
    /// <remarks>
    /// Internal: public inventory entry points are the <c>StartInventoryAsync</c> overloads, which return an
    /// isolated <see cref="InventorySession"/>. This session-less variant is used by tag access and internal
    /// flows where the connection-level report stream is sufficient.
    /// </remarks>
    internal async Task StartAsync(
        InventorySettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        EnsureProtocolAvailable();
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ValidateSettingsCore(new ReaderSettings { Inventory = settings }).ThrowIfInvalid();
            await StartManagedInventoryCoreAsync(settings, resourcesAlreadyCleared: false, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    /// <summary>
    /// Stops and disables the current SDK-managed inventory ROSpec while retaining its configuration.
    /// </summary>
    /// <param name="cancellationToken">Cancels the resource operations.</param>
    /// <returns>A task that completes after the managed ROSpec is stopped and disabled.</returns>
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

            using IDisposable scope = BeginInternalResourceOperationScope();
            Volatile.Write(ref _operationState, (int)ReaderOperationState.Stopping);
            bool completed = false;
            try
            {
                await StopManagedInventoryAsync(roSpecId, cancellationToken).ConfigureAwait(false);
                completed = true;
            }
            finally
            {
                InventorySession? session = _inventorySession;
                _inventorySession = null;
                session?.Complete(InventoryRuntimeState.Disabled);
                if (completed)
                {
                    Volatile.Write(ref _operationState, (int)ReaderOperationState.Idle);
                    Volatile.Write(ref _resourceMode, (int)ReaderResourceMode.HighLevelConfigured);
                }
                else
                {
                    InvalidateManagedStateAfterRawProtocolAccess();
                }
            }
        }
        finally
        {
            _operationLock.Release();
        }
    }

    /// <summary>
    /// Compiles the SDK-default inventory ROSpec without changing managed operation state.
    /// </summary>
    /// <remarks>
    /// This is used by <see cref="IRoSpecService.AddDefaultAsync"/> for callers that need to create a
    /// disabled default resource and control its lifecycle explicitly.
    /// </remarks>
    internal ILlrpParameter CompileDefaultInventoryRoSpec(InventorySettings settings, uint roSpecId = ManagedInventoryRoSpecId)
    {
        ArgumentNullException.ThrowIfNull(settings);
        EnsureProtocolAvailable();
        if (settings.StartTrigger.StartAtUtc is not null && Capabilities?.HasUtcClockCapability != true)
        {
            throw new InvalidOperationException(
                "A periodic inventory UTC start requires a reader that advertises UTC clock capability.");
        }
        bool supportsStateAwareSingulation = Capabilities?.CanDoTagInventoryStateAwareSingulation == true;
        return GetProtocolAdapter().CompileInventory(
            settings,
            roSpecId,
            BuildInventoryCustomItems(settings),
            supportsStateAwareSingulation);
    }

    /// <summary>
    /// Deletes the SDK-managed inventory ROSpec and AttachedData AccessSpec, releasing the high-level resource domain.
    /// </summary>
    public async Task ClearManagedSettingsAsync(CancellationToken cancellationToken = default)
    {
        EnsureProtocolAvailable();
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureManagedStateSynchronized();
            if (_managedInventoryRoSpecId is not uint roSpecId)
            {
                return;
            }

            using IDisposable scope = BeginInternalResourceOperationScope();
            try
            {
                if (OperationState == ReaderOperationState.Inventorying)
                {
                    await StopManagedInventoryAsync(roSpecId, cancellationToken).ConfigureAwait(false);
                }
                await DeleteManagedInventoryResourcesAsync(roSpecId, cancellationToken).ConfigureAwait(false);
                InventorySession? session = _inventorySession;
                _inventorySession = null;
                session?.Complete(InventoryRuntimeState.Disabled);
                ResetManagedInventoryState();
                Volatile.Write(ref _resourceMode, (int)ReaderResourceMode.Idle);
            }
            catch
            {
                InvalidateManagedStateAfterRawProtocolAccess();
                throw;
            }
        }
        finally { _operationLock.Release(); }
    }

    /// <summary>Reads the high-level configuration and, when currently managed, the SDK inventory snapshot.</summary>
    public async Task<ReaderSettingsSnapshot> QuerySettingsAsync(CancellationToken cancellationToken = default)
    {
        EnsureProtocolAvailable();
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ReaderConfiguration configuration = await QueryConfigurationCoreAsync(cancellationToken).ConfigureAwait(false);
            IReadOnlyList<ILlrpParameter> roSpecs = await RoSpecs.GetAllAsync(cancellationToken).ConfigureAwait(false);
            IReadOnlyList<ILlrpParameter> accessSpecs = await AccessSpecs.GetAllAsync(cancellationToken).ConfigureAwait(false);
            ILlrpParameter? managed = roSpecs.SingleOrDefault(IsManagedRoSpec);
            InventorySnapshot? snapshot = managed is null ? null : ParseManagedInventory(managed, accessSpecs);
            AdoptManagedInventorySnapshot(managed, accessSpecs, snapshot);
            InventorySettings? inventory = snapshot?.Settings;
            return new ReaderSettingsSnapshot(new ReaderSettings { Configuration = configuration, Inventory = inventory }, snapshot);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    /// <summary>
    /// Generates an editable SDK-recommended settings baseline for the initialized connected reader.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="QuerySettingsAsync(CancellationToken)"/>, this method does not read current reader
    /// configuration or resources. It resolves portable defaults and, when available, a single active vendor/model
    /// profile from the negotiated identity and capabilities.
    /// </remarks>
    public async Task<ReaderSettingsDefaults> GetDefaultSettingsAsync(CancellationToken cancellationToken = default)
    {
        EnsureProtocolAvailable();
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ReaderMetadataSnapshot metadata = Volatile.Read(ref _metadata) ?? throw new InvalidOperationException(
                "SDK defaults require initialized reader metadata. Connect the reader first.");
            var context = new ReaderSettingsDefaultContext(
                metadata.Identity,
                metadata.Capabilities,
                NegotiatedVersion);
            ReaderSettingsDefaults? result = null;
            var contributorIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (IReaderSettingsDefaultsContributor contributor in Extensions.OfType<IReaderSettingsDefaultsContributor>())
            {
                if (string.IsNullOrWhiteSpace(contributor.Id))
                {
                    throw new InvalidOperationException("A settings-default contributor must declare a non-empty Id.");
                }
                if (!contributorIds.Add(contributor.Id))
                {
                    throw new InvalidOperationException($"More than one active settings-default contributor uses Id '{contributor.Id}'.");
                }

                ReaderSettingsDefaults? candidate = contributor.GetDefaultSettings(context);
                if (candidate is not null && result is not null)
                {
                    throw new InvalidOperationException("More than one active reader profile supplied settings defaults.");
                }
                result ??= candidate;
            }

            return result ?? ReaderSettingsDefaults.CreateGeneric();
        }
        finally
        {
            _operationLock.Release();
        }
    }

    /// <summary>Validates high-level settings against the negotiated protocol, reader capabilities, and active extensions.</summary>
    /// <remarks>This method compiles the intent without sending messages or changing reader resources.</remarks>
    public async Task<SettingsValidationResult> ValidateSettingsAsync(
        ReaderSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        EnsureProtocolAvailable();
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return ValidateSettingsCore(settings);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    /// <summary>Starts the previously applied SDK-managed inventory configuration without an isolated report session.</summary>
    /// <remarks>Internal: public entry points are the <c>StartInventoryAsync</c> overloads.</remarks>
    internal async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureProtocolAvailable();
            EnsureManagedStateSynchronized();
            if (ResourceMode != ReaderResourceMode.HighLevelConfigured)
            {
                throw new InvalidOperationException("No stopped SDK-managed inventory configuration is available to start.");
            }
            await StartConfiguredManagedInventoryCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { _operationLock.Release(); }
    }

    /// <summary>Applies high-level configuration and deploys optional exclusive inventory intent without starting it.</summary>
    /// <remarks>
    /// When <paramref name="settings"/> carries Inventory intent, deployment takes full control of reader resources
    /// and first deletes <b>all</b> ROSpecs and AccessSpecs on the device (LLRP id=0 delete semantics), including
    /// resources deployed by other applications.
    /// </remarks>
    public async Task ApplySettingsAsync(ReaderSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        EnsureProtocolAvailable();
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureManagedStateSynchronized();
            ValidateSettingsCore(settings).ThrowIfInvalid();
            using IDisposable scope = BeginInternalResourceOperationScope();
            if (settings.Inventory is null)
            {
                await ApplyConfigurationCoreAsync(settings.Configuration, cancellationToken).ConfigureAwait(false);
                Volatile.Write(ref _managedStateIsSynchronized, 1);
                return;
            }

            try
            {
                await DeleteAllResourcesAsync(cancellationToken).ConfigureAwait(false);
                await ApplyConfigurationCoreAsync(settings.Configuration, cancellationToken).ConfigureAwait(false);
                await StartManagedInventoryCoreAsync(
                    settings.Inventory,
                    resourcesAlreadyCleared: true,
                    cancellationToken: cancellationToken,
                    startAfterDeployment: false).ConfigureAwait(false);
            }
            catch
            {
                InvalidateManagedStateAfterRawProtocolAccess();
                throw;
            }
        }
        finally
        {
            _operationLock.Release();
        }
    }

    private SettingsValidationResult ValidateSettingsCore(ReaderSettings settings)
    {
        var diagnostics = ReaderSettingsValidator.Validate(settings, Capabilities, NegotiatedVersion);
        if (settings.Configuration is not null)
        {
            TryCompileSettingsPart(
                () => _ = BuildSettingsApplyParameters(settings.Configuration),
                "SET-EXT-001",
                "configuration.extensions",
                diagnostics);
        }

        if (settings.Inventory is { } inventory &&
            !diagnostics.Any(static item => item.Severity == SettingsDiagnosticSeverity.Error))
        {
            TryCompileSettingsPart(
                () => _ = CompileDefaultInventoryRoSpec(inventory),
                "SET-INV-031",
                "inventory",
                diagnostics);
            if (inventory.AttachedData?.Enabled == true)
            {
                TryCompileSettingsPart(
                    () => _ = CompileAttachedDataAccessSpec(
                        ManagedInventoryAttachedDataAccessSpecId,
                        ManagedInventoryRoSpecId,
                        inventory.AttachedData),
                    "SET-INV-032",
                    "inventory.attachedData",
                    diagnostics);
            }
        }

        return new SettingsValidationResult { Diagnostics = diagnostics.AsReadOnly() };
    }

    private static void TryCompileSettingsPart(
        Action compile,
        string code,
        string path,
        List<SettingsDiagnostic> diagnostics)
    {
        try
        {
            compile();
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NotSupportedException or FormatException or OverflowException)
        {
            diagnostics.Add(new SettingsDiagnostic(code, SettingsDiagnosticSeverity.Error, path, exception.Message));
        }
    }

    private async Task StartManagedInventoryCoreAsync(
        InventorySettings settings,
        bool resourcesAlreadyCleared,
        CancellationToken cancellationToken,
        bool startAfterDeployment = true)
    {
        EnsureProtocolAvailable();
        EnsureManagedStateSynchronized();
        if (OperationState != ReaderOperationState.Idle)
        {
            throw new InvalidOperationException(
                $"Cannot start managed inventory while the reader operation state is {OperationState}.");
        }

        using IDisposable scope = BeginInternalResourceOperationScope();
        Volatile.Write(ref _operationState, (int)ReaderOperationState.Starting);
        bool added = false;
        bool enabled = false;
        uint? attachedDataAccessSpecId = null;
        bool attachedDataAdded = false;
        bool attachedDataEnabled = false;
        try
        {
            if (!resourcesAlreadyCleared)
            {
                await DeleteAllResourcesAsync(cancellationToken).ConfigureAwait(false);
            }

            ILlrpParameter roSpec = CompileDefaultInventoryRoSpec(settings);
            await RoSpecs.AddAsync(roSpec, cancellationToken).ConfigureAwait(false);
            added = true;

            if (settings.AttachedData.Enabled)
            {
                uint accessSpecId = ManagedInventoryAttachedDataAccessSpecId;
                ILlrpParameter accessSpec = CompileAttachedDataAccessSpec(accessSpecId, ManagedInventoryRoSpecId, settings.AttachedData);
                await AccessSpecs.AddAsync(accessSpec, cancellationToken).ConfigureAwait(false);
                attachedDataAccessSpecId = accessSpecId;
                attachedDataAdded = true;
            }

            if (startAfterDeployment)
            {
                await RoSpecs.EnableAsync(ManagedInventoryRoSpecId, cancellationToken).ConfigureAwait(false);
                enabled = true;
                if (attachedDataAccessSpecId is uint attachedDataId)
                {
                    await AccessSpecs.EnableAsync(attachedDataId, cancellationToken).ConfigureAwait(false);
                    attachedDataEnabled = true;
                }
                if (settings.StartTrigger.Type == InventoryStartTriggerType.None)
                {
                    await RoSpecs.StartAsync(ManagedInventoryRoSpecId, cancellationToken).ConfigureAwait(false);
                }
            }

            _managedInventoryRoSpecId = ManagedInventoryRoSpecId;
            _managedInventoryAttachedDataAccessSpecId = attachedDataAccessSpecId;
            Volatile.Write(ref _currentInventorySettings, settings);
            Volatile.Write(ref _operationState, (int)(startAfterDeployment
                ? ReaderOperationState.Inventorying
                : ReaderOperationState.Idle));
            Volatile.Write(ref _resourceMode, (int)(startAfterDeployment
                ? ReaderResourceMode.HighLevelRunning
                : ReaderResourceMode.HighLevelConfigured));
        }
        catch
        {
            await TryAttachedDataAccessSpecCleanupAsync(attachedDataAccessSpecId, attachedDataEnabled, attachedDataAdded, CancellationToken.None).ConfigureAwait(false);
            if (enabled)
            {
                await TryManagedInventoryCleanupAsync(ManagedInventoryRoSpecId, stop: true, CancellationToken.None).ConfigureAwait(false);
            }
            else if (added)
            {
                await TryDeleteManagedInventoryAsync(ManagedInventoryRoSpecId, CancellationToken.None).ConfigureAwait(false);
            }

            ResetManagedInventoryState();
            InvalidateManagedStateAfterRawProtocolAccess();
            throw;
        }
    }

    private async Task StartConfiguredManagedInventoryCoreAsync(CancellationToken cancellationToken)
    {
        if (_managedInventoryRoSpecId is not uint roSpecId || CurrentInventorySettings is not { } settings)
        {
            throw new InvalidOperationException("No stopped SDK-managed inventory configuration is available to start.");
        }

        using IDisposable scope = BeginInternalResourceOperationScope();
        Volatile.Write(ref _operationState, (int)ReaderOperationState.Starting);
        try
        {
            await RoSpecs.EnableAsync(roSpecId, cancellationToken).ConfigureAwait(false);
            if (_managedInventoryAttachedDataAccessSpecId is uint accessSpecId)
            {
                await AccessSpecs.EnableAsync(accessSpecId, cancellationToken).ConfigureAwait(false);
            }
            if (settings.StartTrigger.Type == InventoryStartTriggerType.None)
            {
                await RoSpecs.StartAsync(roSpecId, cancellationToken).ConfigureAwait(false);
            }

            Volatile.Write(ref _operationState, (int)ReaderOperationState.Inventorying);
            Volatile.Write(ref _resourceMode, (int)ReaderResourceMode.HighLevelRunning);
        }
        catch
        {
            InvalidateManagedStateAfterRawProtocolAccess();
            throw;
        }
    }

    private async Task<ReaderConfiguration> QueryConfigurationCoreAsync(CancellationToken cancellationToken)
    {
        EnsureProtocolAvailable();
        EnsureManagedStateSynchronized();
        ILlrpProtocolAdapter adapter = GetProtocolAdapter();
        uint messageId = _messageIds.Next();
        IReadOnlyList<ILlrpParameter> customItems = BuildSettingsQueryParameters();
        TranslatedReaderConfiguration translated = await adapter
            .QueryConfigurationAsync(this, messageId, customItems, cancellationToken)
            .ConfigureAwait(false);
        return ApplySettingsContributors(translated);
    }

    private async Task ApplyConfigurationCoreAsync(ReaderConfiguration configuration, CancellationToken cancellationToken)
    {
        ILlrpProtocolAdapter adapter = GetProtocolAdapter();
        uint messageId = _messageIds.Next();
        IReadOnlyList<ILlrpParameter> customItems = BuildSettingsApplyParameters(configuration);
        await adapter
            .ApplyConfigurationAsync(this, messageId, configuration, customItems, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Enters explicit application-owned ROSpec and AccessSpec control after high-level work has stopped.</summary>
    public async Task EnterManualResourceModeAsync(CancellationToken cancellationToken = default)
    {
        EnsureProtocolAvailable();
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureProtocolAvailable();
            EnsureManagedStateSynchronized();
            if (ResourceMode is ReaderResourceMode.HighLevelConfigured or ReaderResourceMode.HighLevelRunning)
            {
                throw new InvalidOperationException("Clear the SDK-managed inventory configuration before entering manual resource mode.");
            }

            Volatile.Write(ref _resourceMode, (int)ReaderResourceMode.ManualResources);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    /// <summary>Deletes all manual ROSpecs and AccessSpecs and returns the reader to idle resource mode.</summary>
    public async Task ExitManualResourceModeAsync(CancellationToken cancellationToken = default)
    {
        EnsureProtocolAvailable();
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureProtocolAvailable();
            EnsureManagedStateSynchronized();
            if (ResourceMode != ReaderResourceMode.ManualResources)
            {
                throw new InvalidOperationException("The reader is not in manual resource mode.");
            }

            using IDisposable scope = BeginInternalResourceOperationScope();
            try
            {
                await DeleteAllResourcesAsync(cancellationToken).ConfigureAwait(false);
                Volatile.Write(ref _resourceMode, (int)ReaderResourceMode.Idle);
            }
            catch
            {
                InvalidateManagedStateAfterRawProtocolAccess();
                throw;
            }
        }
        finally
        {
            _operationLock.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        CancelAutomaticReconnect();
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

    /// <summary>
    /// Actively requests tag reports from the physical reader's buffer via GET_REPORT.
    /// </summary>
    public async Task<IReadOnlyList<TagReport>> GetTagReportsAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureProtocolAvailable();
        EnsureManagedStateSynchronized();

        ILlrpProtocolAdapter adapter = GetProtocolAdapter();
        uint messageId = _messageIds.Next();
        IReadOnlyList<TranslatedTagReport> translated = await adapter
            .FetchReportsAsync(this, messageId, cancellationToken)
            .ConfigureAwait(false);

        var reports = new List<TagReport>();
        foreach (TranslatedTagReport report in translated)
        {
            TagReport result = ApplyTagReportContributors(report);
            reports.Add(result);
            PublishTagReport(result);
        }

        return reports;
    }

    /// <summary>
    /// Sets the output state of a specified GPO port on the reader.
    /// </summary>
    public async Task SetGpoAsync(
        ushort portNumber,
        bool state,
        CancellationToken cancellationToken = default)
    {
        ReaderSettingsSnapshot snapshot = await QuerySettingsAsync(cancellationToken).ConfigureAwait(false);
        GpoConfiguration[] gpos = snapshot.Settings.Configuration.Gpos
            .Where(gpo => gpo.GpoPortNumber != portNumber)
            .Append(new GpoConfiguration { GpoPortNumber = portNumber, GpoData = state })
            .OrderBy(static gpo => gpo.GpoPortNumber)
            .ToArray();
        await ApplySettingsAsync(snapshot.Settings with
        {
            Configuration = snapshot.Settings.Configuration with { Gpos = gpos }
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Executes one standard C1G2 tag access operation (Read, Write, Lock, Kill, BlockErase).
    /// </summary>
    public async Task<TagAccessResult> ExecuteTagAccessAsync(
        TagAccessRequest request,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureProtocolAvailable();
        if (timeout.HasValue && timeout.Value <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "Tag access timeout must be positive when specified.");
        }

        TagAccessInventoryLease inventoryLease = await AcquireTagAccessInventoryAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                using IDisposable scope = BeginInternalResourceOperationScope();
                EnsureProtocolAvailable();
                uint roSpecId = _managedInventoryRoSpecId ?? 14150;
                uint? attachedDataAccessSpecId = _managedInventoryAttachedDataAccessSpecId;
                bool attachedDataSuspended = false;
                if (attachedDataAccessSpecId is uint attachedDataId)
                {
                    await AccessSpecs.DisableAsync(attachedDataId, cancellationToken).ConfigureAwait(false);
                    attachedDataSuspended = true;
                }

                uint accessSpecId = NextManagedAccessSpecId();
                bool useBlockWrite = request is WriteTagRequest { WriteData.Count: > 1 } &&
                    Capabilities?.IsMultiwordBlockWriteAvailable == true;
                ILlrpParameter accessSpec = GetProtocolAdapter().CompileTagAccess(accessSpecId, roSpecId, request, useBlockWrite);
                var completion = new TaskCompletionSource<TagAccessResult>(TaskCreationOptions.RunContinuationsAsynchronously);
                EventHandler<TagReportEventArgs>? handler = null;
                handler = (_, args) =>
                {
                    TagReport report = args.Report;
                    if (report.AccessSpecId != accessSpecId)
                    {
                        return;
                    }

                    TagAccessOperationResult? operation = report.AccessOperationResults?
                        .FirstOrDefault(static result => result.OpSpecID == 1);
                    if (operation is not null)
                    {
                        completion.TrySetResult(new TagAccessResult(report, operation));
                    }
                };

                TagsReported += handler;
                bool added = false;
                bool enabled = false;
                try
                {
                    await AccessSpecs.AddAsync(accessSpec, cancellationToken).ConfigureAwait(false);
                    added = true;
                    await AccessSpecs.EnableAsync(accessSpecId, cancellationToken).ConfigureAwait(false);
                    enabled = true;
                    TimeSpan effectiveTimeout = timeout ?? Options.RequestTimeout;
                    return await completion.Task.WaitAsync(effectiveTimeout, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    TagsReported -= handler;
                    if (enabled)
                    {
                        try { await AccessSpecs.DisableAsync(accessSpecId, CancellationToken.None).ConfigureAwait(false); } catch { }
                    }
                    if (added)
                    {
                        try { await AccessSpecs.DeleteAsync(accessSpecId, CancellationToken.None).ConfigureAwait(false); } catch { }
                    }
                    if (attachedDataSuspended && attachedDataAccessSpecId is uint suspendedAttachedDataId && IsConnected && OperationState == ReaderOperationState.Inventorying)
                    {
                        try { await AccessSpecs.EnableAsync(suspendedAttachedDataId, CancellationToken.None).ConfigureAwait(false); } catch { }
                    }
                }
            }
            finally
            {
                _operationLock.Release();
            }
        }
        finally
        {
            if (inventoryLease.StopAfterAccess)
            {
                try { await StopAsync(cancellationToken).ConfigureAwait(false); } catch { }
            }
            if (inventoryLease.ClearAfterAccess)
            {
                try { await ClearManagedSettingsAsync(cancellationToken).ConfigureAwait(false); } catch { }
            }
        }
    }

    /// <summary>
    /// Executes multiple standard C1G2 operations in one AccessSpec against a shared tag selection.
    /// </summary>
    /// <remarks>
    /// The SDK owns the temporary AccessSpec and, when necessary, a temporary ROSpec. All operations must have
    /// the same target selection and antenna; use separate calls for different targets.
    /// </remarks>
    public async Task<TagAccessSequenceResult> ExecuteTagAccessSequenceAsync(
        TagAccessSequenceRequest request,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Operations);
        if (request.Operations.Count == 0)
        {
            throw new ArgumentException("A tag access sequence requires at least one operation.", nameof(request));
        }
        EnsureProtocolAvailable();
        if (timeout.HasValue && timeout.Value <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "Tag access timeout must be positive when specified.");
        }

        TagAccessRequest[] operations = request.Operations.ToArray();
        TagAccessInventoryLease inventoryLease = await AcquireTagAccessInventoryAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                using IDisposable scope = BeginInternalResourceOperationScope();
                EnsureProtocolAvailable();
                uint roSpecId = _managedInventoryRoSpecId ?? 14150;
                uint? attachedDataAccessSpecId = _managedInventoryAttachedDataAccessSpecId;
                bool attachedDataSuspended = false;
                if (attachedDataAccessSpecId is uint attachedDataId)
                {
                    await AccessSpecs.DisableAsync(attachedDataId, cancellationToken).ConfigureAwait(false);
                    attachedDataSuspended = true;
                }

                uint accessSpecId = NextManagedAccessSpecId();
                bool useBlockWrite = operations.Any(static operation => operation is WriteTagRequest { WriteData.Count: > 1 }) &&
                    Capabilities?.IsMultiwordBlockWriteAvailable == true;
                ILlrpParameter accessSpec = GetProtocolAdapter().CompileTagAccessSequence(
                    accessSpecId,
                    roSpecId,
                    operations,
                    useBlockWrite);
                var completion = new TaskCompletionSource<TagAccessSequenceResult>(TaskCreationOptions.RunContinuationsAsynchronously);
                EventHandler<TagReportEventArgs>? handler = null;
                handler = (_, args) =>
                {
                    TagReport report = args.Report;
                    IReadOnlyList<TagAccessOperationResult>? results = report.AccessOperationResults;
                    if (report.AccessSpecId != accessSpecId || results is null ||
                        !Enumerable.Range(1, operations.Length).All(id => results.Any(result => result.OpSpecID == id)))
                    {
                        return;
                    }

                    completion.TrySetResult(new TagAccessSequenceResult(
                        report,
                        results.OrderBy(static result => result.OpSpecID).ToArray()));
                };

                TagsReported += handler;
                bool added = false;
                bool enabled = false;
                try
                {
                    await AccessSpecs.AddAsync(accessSpec, cancellationToken).ConfigureAwait(false);
                    added = true;
                    await AccessSpecs.EnableAsync(accessSpecId, cancellationToken).ConfigureAwait(false);
                    enabled = true;
                    TimeSpan effectiveTimeout = timeout ?? Options.RequestTimeout;
                    return await completion.Task.WaitAsync(effectiveTimeout, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    TagsReported -= handler;
                    if (enabled)
                    {
                        try { await AccessSpecs.DisableAsync(accessSpecId, CancellationToken.None).ConfigureAwait(false); } catch { }
                    }
                    if (added)
                    {
                        try { await AccessSpecs.DeleteAsync(accessSpecId, CancellationToken.None).ConfigureAwait(false); } catch { }
                    }
                    if (attachedDataSuspended && attachedDataAccessSpecId is uint suspendedAttachedDataId && IsConnected && OperationState == ReaderOperationState.Inventorying)
                    {
                        try { await AccessSpecs.EnableAsync(suspendedAttachedDataId, CancellationToken.None).ConfigureAwait(false); } catch { }
                    }
                }
            }
            finally
            {
                _operationLock.Release();
            }
        }
        finally
        {
            if (inventoryLease.StopAfterAccess)
            {
                try { await StopAsync(cancellationToken).ConfigureAwait(false); } catch { }
            }
            if (inventoryLease.ClearAfterAccess)
            {
                try { await ClearManagedSettingsAsync(cancellationToken).ConfigureAwait(false); } catch { }
            }
        }
    }

    private async Task<TagAccessInventoryLease> AcquireTagAccessInventoryAsync(CancellationToken cancellationToken)
    {
        if (OperationState == ReaderOperationState.Inventorying)
        {
            return default;
        }

        if (ResourceMode == ReaderResourceMode.HighLevelConfigured)
        {
            await StartAsync(cancellationToken).ConfigureAwait(false);
            return new TagAccessInventoryLease(StopAfterAccess: true, ClearAfterAccess: false);
        }

        await StartAsync(new InventorySettings(), cancellationToken).ConfigureAwait(false);
        return new TagAccessInventoryLease(StopAfterAccess: true, ClearAfterAccess: true);
    }

    private readonly record struct TagAccessInventoryLease(bool StopAfterAccess, bool ClearAfterAccess);

    /// <summary>Reads memory from matching Gen2 RFID tags.</summary>
    public Task<TagAccessResult> ReadTagMemoryAsync(
        ReadTagRequest request,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) =>
        ExecuteTagAccessAsync(request, timeout, cancellationToken);

    /// <summary>Writes memory to matching Gen2 RFID tags.</summary>
    public Task<TagAccessResult> WriteTagMemoryAsync(
        WriteTagRequest request,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) =>
        ExecuteTagAccessAsync(request, timeout, cancellationToken);

    /// <summary>Locks memory or passwords on matching Gen2 RFID tags.</summary>
    public Task<TagAccessResult> LockTagMemoryAsync(
        LockTagRequest request,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) =>
        ExecuteTagAccessAsync(request, timeout, cancellationToken);

    /// <summary>Kills matching Gen2 RFID tags.</summary>
    public Task<TagAccessResult> KillTagAsync(
        KillTagRequest request,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) =>
        ExecuteTagAccessAsync(request, timeout, cancellationToken);

    /// <summary>Erases memory blocks on matching Gen2 RFID tags.</summary>
    public Task<TagAccessResult> BlockEraseTagMemoryAsync(
        BlockEraseTagRequest request,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) =>
        ExecuteTagAccessAsync(request, timeout, cancellationToken);

    private uint NextManagedAccessSpecId()
    {
        int value = Interlocked.Increment(ref _nextManagedAccessSpecId);
        return value > 0 ? (uint)value : (uint)Interlocked.Exchange(ref _nextManagedAccessSpecId, 24001);
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
        LlrpResponseMatcher? responseMatcher = null,
        LlrpProtocolVersion? protocolVersion = null)
        where TResponse : class, ILlrpMessage
    {
        ArgumentNullException.ThrowIfNull(request);

        byte[] requestFrame = _registry.EncodeMessage(protocolVersion ?? NegotiatedVersion, request);
        ReadOnlyMemory<byte> responseFrame = await _session
            .TransactAsync(
                requestFrame,
                responseMatcher ?? MatchesTypedResponse<TResponse>,
                timeout,
                cancellationToken)
            .ConfigureAwait(false);
        ILlrpMessage response = _registry.DecodeMessage(responseFrame.Span);
        if (TryCreateOperationException(request.GetType().Name, response, out LlrpReaderOperationException? error))
        {
            throw error!;
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

    internal Task<TResponse> TransactDuringInitializationAsync<TResponse>(
        ILlrpMessage request,
        CancellationToken cancellationToken,
        LlrpResponseMatcher? responseMatcher = null)
        where TResponse : class, ILlrpMessage
    {
        return TransactSessionAsync<TResponse>(
            request,
            Options.RequestTimeout,
            cancellationToken,
            responseMatcher);
    }

    internal async Task<TResponse> TransactDuringExtensionInitializationAsync<TResponse>(
        ILlrpMessage request,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
        where TResponse : class, ILlrpMessage
    {
        ArgumentNullException.ThrowIfNull(request);
        ThrowIfDisposed();
        if (ConnectionState != ReaderConnectionState.Initializing)
        {
            throw new InvalidOperationException("Extension initialization can only occur during reader connection initialization.");
        }
        if (!_session.IsConnected)
        {
            throw new InvalidOperationException("The LLRP session is disconnected.");
        }

        return await TransactSessionAsync<TResponse>(request, timeout, cancellationToken).ConfigureAwait(false);
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
        await StopKeepaliveMonitorAsync().ConfigureAwait(false);
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

    private async Task DeleteAllResourcesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await AccessSpecs.DeleteAsync(0, cancellationToken).ConfigureAwait(false);
        }
        catch (LlrpReaderOperationException exception) when (IsNoResourceError(exception))
        {
        }

        try
        {
            await RoSpecs.DeleteAsync(0, cancellationToken).ConfigureAwait(false);
        }
        catch (LlrpReaderOperationException exception) when (IsNoResourceError(exception))
        {
        }
    }

    private static bool IsNoResourceError(LlrpReaderOperationException exception) =>
        exception.StatusCode == 100 && exception.ErrorDescription.Contains("does not exist", StringComparison.OrdinalIgnoreCase);

    private void StartKeepaliveMonitor()
    {
        if (Options.KeepaliveTimeout is not { } timeout)
        {
            return;
        }

        if (_keepaliveMonitorTask is not null)
        {
            throw new InvalidOperationException("The LLRP keepalive monitor is already running.");
        }

        Volatile.Write(ref _lastKeepaliveUtcTicks, DateTimeOffset.UtcNow.UtcTicks);
        Volatile.Write(ref _keepaliveTimeoutSignaled, 0);
        var cancellation = new CancellationTokenSource();
        _keepaliveMonitorCancellation = cancellation;
        _keepaliveMonitorTask = MonitorKeepaliveAsync(cancellation, timeout);
    }

    private async Task StopKeepaliveMonitorAsync()
    {
        CancellationTokenSource? cancellation = _keepaliveMonitorCancellation;
        Task? monitorTask = _keepaliveMonitorTask;
        _keepaliveMonitorCancellation = null;
        _keepaliveMonitorTask = null;
        if (cancellation is null)
        {
            return;
        }

        cancellation.Cancel();
        if (monitorTask is not null)
        {
            try
            {
                await monitorTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                // Expected during explicit lifecycle shutdown.
            }
        }
        cancellation.Dispose();
    }

    private async Task MonitorKeepaliveAsync(CancellationTokenSource cancellation, TimeSpan timeout)
    {
        TimeSpan interval = TimeSpan.FromMilliseconds(Math.Clamp(timeout.TotalMilliseconds / 4d, 50d, 1000d));
        using var timer = new PeriodicTimer(interval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellation.Token).ConfigureAwait(false))
            {
                long ticks = Volatile.Read(ref _lastKeepaliveUtcTicks);
                var lastReceivedAt = new DateTimeOffset(ticks, TimeSpan.Zero);
                if (DateTimeOffset.UtcNow - lastReceivedAt < timeout ||
                    Interlocked.CompareExchange(ref _keepaliveTimeoutSignaled, 1, 0) != 0)
                {
                    continue;
                }

                PublishKeepaliveTimedOut(timeout, lastReceivedAt);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // Explicit lifecycle shutdown.
        }
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
                    if (message is V101Messages.KEEPALIVE or V11Messages.KEEPALIVE)
                    {
                        PublishKeepaliveReceived();
                        ILlrpMessage acknowledgementMessage = NegotiatedVersion switch
                        {
                            LlrpProtocolVersion.Version101 => new V101Messages.KEEPALIVE_ACK(message.MessageId),
                            LlrpProtocolVersion.Version11 => new V11Messages.KEEPALIVE_ACK(message.MessageId),
                            _ => throw new NotSupportedException(
                                $"No KEEPALIVE_ACK encoder is available for LLRP {NegotiatedVersion}."),
                        };
                        byte[] acknowledgement = _registry.EncodeMessage(
                            NegotiatedVersion,
                            acknowledgementMessage);
                        await _session
                            .SendFrameAsync(acknowledgement, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    else if (message is V101Messages.READER_EVENT_NOTIFICATION v101Notification)
                    {
                        ProcessReaderEventNotification(v101Notification);
                    }
                    else if (message is V11Messages.READER_EVENT_NOTIFICATION v11Notification)
                    {
                        ProcessReaderEventNotification(v11Notification);
                    }

                    foreach (TranslatedTagReport translatedReport in GetProtocolAdapter().TranslateTagReports(message))
                    {
                        TagReport tagReport = ApplyTagReportContributors(translatedReport);
                        _tagReports.Writer.TryWrite(tagReport);
                        PublishTagReport(tagReport);
                    }

                    _messages.Writer.TryWrite(message);
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
        bool scheduleAutomaticReconnect = false;
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
            scheduleAutomaticReconnect = Options.AutomaticReconnect is not null;
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

        if (scheduleAutomaticReconnect)
        {
            StartAutomaticReconnect();
        }
    }

    private void StartAutomaticReconnect()
    {
        LlrpAutomaticReconnectOptions? options = Options.AutomaticReconnect;
        if (options is null || Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        lock (_automaticReconnectGate)
        {
            if (_automaticReconnectTask is not null)
            {
                return;
            }

            var cancellation = new CancellationTokenSource();
            _automaticReconnectCancellation = cancellation;
            _automaticReconnectTask = RunAutomaticReconnectAsync(cancellation, options);
        }
    }

    private async Task RunAutomaticReconnectAsync(
        CancellationTokenSource cancellation,
        LlrpAutomaticReconnectOptions options)
    {
        try
        {
            for (int attempt = 1; attempt <= options.MaximumAttempts; attempt++)
            {
                await Task.Delay(options.GetDelay(attempt), cancellation.Token).ConfigureAwait(false);
                if (ConnectionState != ReaderConnectionState.Faulted)
                {
                    return;
                }

                try
                {
                    await ReconnectAsync(cancellation.Token, cancelAutomaticReconnect: false).ConfigureAwait(false);
                    _logger.LogInformation(
                        "LLRP reader session {ConnectionId} reconnected automatically on attempt {Attempt}",
                        ConnectionId,
                        attempt);
                    return;
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    _logger.LogWarning(
                        exception,
                        "Automatic reconnect attempt {Attempt} of {MaximumAttempts} failed for LLRP reader {ConnectionId}",
                        attempt,
                        options.MaximumAttempts,
                        ConnectionId);
                }
            }

            _logger.LogError(
                "Automatic reconnect exhausted {MaximumAttempts} attempts for LLRP reader {ConnectionId}",
                options.MaximumAttempts,
                ConnectionId);
        }
        finally
        {
            lock (_automaticReconnectGate)
            {
                if (ReferenceEquals(_automaticReconnectCancellation, cancellation))
                {
                    _automaticReconnectCancellation = null;
                    _automaticReconnectTask = null;
                }
            }

            cancellation.Dispose();
        }
    }

    private void CancelAutomaticReconnect()
    {
        lock (_automaticReconnectGate)
        {
            _automaticReconnectCancellation?.Cancel();
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

    private async Task NegotiateProtocolVersionAsync(CancellationToken cancellationToken)
    {
        if (Options.ProtocolVersionPolicy == LlrpProtocolVersionPolicy.Force101)
        {
            _logger.LogDebug(
                "Reader {ConnectionId} is configured to use LLRP 1.0.1 without version negotiation.",
                ConnectionId);
            return;
        }

        bool requireVersion11 = Options.ProtocolVersionPolicy == LlrpProtocolVersionPolicy.Force11;
        var getSupportedVersion = new V11Messages.GET_SUPPORTED_VERSION(_messageIds.Next());
        V11Messages.GET_SUPPORTED_VERSION_RESPONSE supported;
        try
        {
            supported = await TransactSessionAsync<V11Messages.GET_SUPPORTED_VERSION_RESPONSE>(
                getSupportedVersion,
                Options.RequestTimeout,
                cancellationToken,
                MatchesGetSupportedVersionResponse,
                LlrpProtocolVersion.Version11).ConfigureAwait(false);
        }
        catch (LlrpReaderOperationException exception) when (exception.StatusCode == 110 && !requireVersion11)
        {
            _logger.LogDebug(
                "Reader {ConnectionId} rejected LLRP 1.1 negotiation; retaining LLRP 1.0.1.",
                ConnectionId);
            return;
        }

        if (supported.LLRPStatus.StatusCode != LlrpNet.Protocol.Enumerations.V1_1.StatusCode.M_Success)
        {
            throw new LlrpReaderOperationException(
                "GET_SUPPORTED_VERSION",
                checked((ushort)supported.LLRPStatus.StatusCode),
                supported.LLRPStatus.ErrorDescription,
                supported.LLRPStatus);
        }

        if (supported.SupportedVersion < (byte)LlrpProtocolVersion.Version11)
        {
            if (requireVersion11)
            {
                throw new NotSupportedException(
                    $"Reader {ConnectionId} supports LLRP through {supported.SupportedVersion}, but LLRP 1.1 was required.");
            }

            _logger.LogDebug(
                "Reader {ConnectionId} supports LLRP through {SupportedVersion}; retaining LLRP 1.0.1.",
                ConnectionId,
                supported.SupportedVersion);
            return;
        }

        var setProtocolVersion = new V11Messages.SET_PROTOCOL_VERSION(
            _messageIds.Next(),
            (byte)LlrpProtocolVersion.Version11);
        V11Messages.SET_PROTOCOL_VERSION_RESPONSE setResponse =
            await TransactSessionAsync<V11Messages.SET_PROTOCOL_VERSION_RESPONSE>(
                setProtocolVersion,
                Options.RequestTimeout,
                cancellationToken,
                MatchesSetProtocolVersionResponse,
                LlrpProtocolVersion.Version11).ConfigureAwait(false);
        if (setResponse.LLRPStatus.StatusCode != LlrpNet.Protocol.Enumerations.V1_1.StatusCode.M_Success)
        {
            throw new LlrpReaderOperationException(
                "SET_PROTOCOL_VERSION",
                checked((ushort)setResponse.LLRPStatus.StatusCode),
                setResponse.LLRPStatus.ErrorDescription,
                setResponse.LLRPStatus);
        }

        SelectProtocolAdapter(LlrpProtocolVersion.Version11);
        _logger.LogDebug("Reader {ConnectionId} negotiated LLRP 1.1.", ConnectionId);
    }

    private ILlrpProtocolAdapter GetProtocolAdapter() => Volatile.Read(ref _protocolAdapter);

    private void SelectProtocolAdapter(LlrpProtocolVersion version)
    {
        if (!_protocolAdapters.TryGetValue(version, out ILlrpProtocolAdapter? adapter))
        {
            throw new NotSupportedException($"No SDK protocol adapter is available for LLRP {version}.");
        }

        Volatile.Write(ref _protocolAdapter, adapter);
    }

    internal uint NextMessageId() => _messageIds.Next();

    private async Task InitializeReaderAsync(CancellationToken cancellationToken)
    {
        try
        {
            ILlrpProtocolAdapter adapter = GetProtocolAdapter();

            // Phase 1: Fetch lightweight identity
            ReaderIdentity identity = await adapter
                .FetchIdentityAsync(this, _messageIds.Next(), cancellationToken)
                .ConfigureAwait(false);

            // Match and activate extensions based on the identity
            ActivateReaderExtensions(identity);

            // Phase 2: Execute active extension initializers
            var extensionConnection = new ExtensionConnection(this);
            foreach (IReaderExtension extension in Extensions)
            {
                await extension.InitializeConnectionAsync(extensionConnection, cancellationToken)
                    .ConfigureAwait(false);
            }

            // Phase 3: Fetch all capabilities (which will now contain custom extension parameters if enabled)
            ReaderCapabilities capabilities = await adapter
                .FetchCapabilitiesAsync(this, _messageIds.Next(), cancellationToken)
                .ConfigureAwait(false);

            Volatile.Write(ref _metadata, new ReaderMetadataSnapshot(identity, capabilities));
        }
        catch (LlrpProtocolException exception)
        {
            throw new LlrpReaderInitializationException(
                "The GET_READER_CAPABILITIES response could not be decoded into a valid " +
                $"LLRP {NegotiatedVersion} capability model.",
                exception);
        }
    }

    private void InvalidateMetadata()
    {
        Volatile.Write(ref _metadata, null);
        _extensions.Replace([]);
    }

    private void ActivateReaderExtensions(ReaderIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        var context = new ReaderExtensionMatchContext(
            identity.ManufacturerId,
            identity.ModelId,
            identity.FirmwareVersion,
            NegotiatedVersion);
        IReaderExtension[] activated = Options.ReaderExtensions
            .Where(extension => extension.Matches(context))
            .ToArray();

        foreach (IGrouping<string, IReaderExtension> group in activated
            .Where(static extension => !string.IsNullOrWhiteSpace(extension.MutualExclusionGroup))
            .GroupBy(static extension => extension.MutualExclusionGroup!, StringComparer.Ordinal))
        {
            if (group.Count() > 1)
            {
                throw new InvalidOperationException(
                    $"Reader extensions '{string.Join("', '", group.Select(static extension => extension.Id))}' " +
                    $"all match mutual-exclusion group '{group.Key}'.");
            }
        }

        _extensions.Replace(activated);
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

    private sealed class ExtensionConnection : IReaderConnection
    {
        private readonly LlrpReader _reader;

        public ExtensionConnection(LlrpReader reader)
        {
            _reader = reader;
        }

        public Task<TResponse> TransactAsync<TResponse>(
            ILlrpMessage request,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
            where TResponse : class, ILlrpMessage
        {
            return _reader.TransactDuringExtensionInitializationAsync<TResponse>(request, timeout, cancellationToken);
        }

        public uint NextMessageId() => _reader.NextMessageId();
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
        Volatile.Write(ref _resourceMode, (int)ReaderResourceMode.StateUnknown);
    }

    internal async Task ExecuteManualResourceOperationAsync(Func<Task> operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (_internalResourceOperationDepth.Value > 0)
        {
            await operation().ConfigureAwait(false);
            return;
        }

        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureProtocolAvailable();
            EnsureManagedStateSynchronized();
            if (ResourceMode != ReaderResourceMode.ManualResources)
            {
                throw new InvalidOperationException(
                    $"Resource write operations require {nameof(EnterManualResourceModeAsync)}. Current mode is {ResourceMode}.");
            }

            await operation().ConfigureAwait(false);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    private IDisposable BeginInternalResourceOperationScope()
    {
        _internalResourceOperationDepth.Value++;
        return new ResourceOperationScope(_internalResourceOperationDepth);
    }

    private sealed class ResourceOperationScope : IDisposable
    {
        private readonly AsyncLocal<int> _depth;
        private int _disposed;

        public ResourceOperationScope(AsyncLocal<int> depth) => _depth = depth;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _depth.Value--;
            }
        }
    }

    private void ResetManagedInventoryState()
    {
        _managedInventoryRoSpecId = null;
        _managedInventoryAttachedDataAccessSpecId = null;
        Volatile.Write(ref _currentInventorySettings, null);
        Volatile.Write(ref _operationState, (int)ReaderOperationState.Idle);
    }

    private void AdoptManagedInventorySnapshot(
        ILlrpParameter? managedRoSpec,
        IReadOnlyList<ILlrpParameter> accessSpecs,
        InventorySnapshot? snapshot = null)
    {
        if (managedRoSpec is null)
        {
            ResetManagedInventoryState();
            Volatile.Write(ref _resourceMode, (int)ReaderResourceMode.Idle);
            return;
        }

        InventorySnapshot actual = snapshot ?? ParseManagedInventory(managedRoSpec, accessSpecs);
        _managedInventoryRoSpecId = ManagedInventoryRoSpecId;
        _managedInventoryAttachedDataAccessSpecId = accessSpecs.Any(item => item switch
        {
            AccessSpec v101 => v101.AccessSpecID == ManagedInventoryAttachedDataAccessSpecId,
            LlrpNet.Protocol.Parameters.V1_1.AccessSpec v11 => v11.AccessSpecID == ManagedInventoryAttachedDataAccessSpecId,
            _ => false,
        }) ? ManagedInventoryAttachedDataAccessSpecId : null;
        Volatile.Write(ref _currentInventorySettings, actual.Settings);
        bool running = actual.State == InventoryRuntimeState.Running;
        Volatile.Write(ref _operationState, (int)(running ? ReaderOperationState.Inventorying : ReaderOperationState.Idle));
        Volatile.Write(ref _resourceMode, (int)(running ? ReaderResourceMode.HighLevelRunning : ReaderResourceMode.HighLevelConfigured));
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

        uint? attachedDataAccessSpecId = _managedInventoryAttachedDataAccessSpecId;
        if (attachedDataAccessSpecId is uint attachedDataId)
        {
            try
            {
                await AccessSpecs.DisableAsync(attachedDataId, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failure ??= exception;
            }

        }

        try
        {
            await RoSpecs.DisableAsync(roSpecId, cancellationToken).ConfigureAwait(false);
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

    private async Task RemoveManagedInventoryAsync(uint roSpecId, CancellationToken cancellationToken)
    {
        Exception? failure = null;
        try
        {
            await StopManagedInventoryAsync(roSpecId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        try { await DeleteManagedInventoryResourcesAsync(roSpecId, cancellationToken).ConfigureAwait(false); }
        catch (Exception exception) { failure ??= exception; }

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private async Task DeleteManagedInventoryResourcesAsync(uint roSpecId, CancellationToken cancellationToken)
    {
        Exception? failure = null;
        if (_managedInventoryAttachedDataAccessSpecId is uint attachedDataId)
        {
            try { await AccessSpecs.DeleteAsync(attachedDataId, cancellationToken).ConfigureAwait(false); }
            catch (Exception exception) { failure ??= exception; }
        }
        try { await RoSpecs.DeleteAsync(roSpecId, cancellationToken).ConfigureAwait(false); }
        catch (Exception exception) { failure ??= exception; }
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
                await RemoveManagedInventoryAsync(roSpecId, cancellationToken).ConfigureAwait(false);
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

    private static bool IsManagedRoSpec(ILlrpParameter item) => item switch
    {
        ROSpec v101 => v101.ROSpecID == ManagedInventoryRoSpecId,
        LlrpNet.Protocol.Parameters.V1_1.ROSpec v11 => v11.ROSpecID == ManagedInventoryRoSpecId,
        _ => false,
    };

    private InventorySnapshot ParseManagedInventory(ILlrpParameter item, IReadOnlyList<ILlrpParameter> accessSpecs)
    {
        return item switch
        {
            ROSpec roSpec => ParseManagedInventory(roSpec, accessSpecs),
            V11Parameters.ROSpec roSpec => ParseManagedInventory(roSpec, accessSpecs),
            _ => throw new NotSupportedException("Reading a persisted SDK ROSpec is not available for this negotiated protocol version."),
        };
    }

    private InventorySnapshot ParseManagedInventory(ROSpec roSpec, IReadOnlyList<ILlrpParameter> accessSpecs)
    {
        ArgumentNullException.ThrowIfNull(roSpec);
        ArgumentNullException.ThrowIfNull(accessSpecs);
        if (roSpec.ROSpecID != ManagedInventoryRoSpecId)
        {
            throw new InvalidOperationException("The supplied ROSpec is not the SDK-managed inventory ROSpec.");
        }
        AISpec aiSpec = roSpec.SpecParameterItems.OfType<AISpec>().SingleOrDefault()
            ?? throw new InvalidOperationException("The reserved SDK ROSpec must contain exactly one AISpec.");
        InventoryParameterSpec inventorySpec = aiSpec.InventoryParameterSpecItems.Single();
        C1G2InventoryCommand? command = inventorySpec.AntennaConfigurationItems
            .SelectMany(configuration => configuration.AirProtocolInventoryCommandSettingsItems)
            .OfType<C1G2InventoryCommand>().FirstOrDefault();
        AccessSpec? attachedDataSpec = accessSpecs.OfType<AccessSpec>()
            .SingleOrDefault(spec => spec.AccessSpecID == ManagedInventoryAttachedDataAccessSpecId);
        if (attachedDataSpec is not null && attachedDataSpec.ROSpecID != ManagedInventoryRoSpecId)
        {
            throw new InvalidOperationException("The reserved SDK AttachedData AccessSpec is not associated with the reserved SDK ROSpec.");
        }
        C1G2Read? read = attachedDataSpec is null
            ? null
            : attachedDataSpec.AccessCommand.AccessCommandOpSpecItems.OfType<C1G2Read>().FirstOrDefault();
        if (attachedDataSpec is not null && read is null)
        {
            throw new InvalidOperationException("The reserved SDK AttachedData AccessSpec must contain a C1G2Read operation.");
        }
        InventoryStateAwareSingulation? stateAwareSingulation = ParseStateAwareSingulation(command?.C1G2SingulationControl?.C1G2TagInventoryStateAwareSingulationAction);
        var settings = new InventorySettings
        {
            Priority = roSpec.Priority,
            AntennaIds = aiSpec.AntennaIDs,
            InventoryParameterSpecId = inventorySpec.InventoryParameterSpecID,
            ReportEveryNTags = roSpec.ROReportSpec?.N ?? 1,
            Report = ParseReportSettings(roSpec.ROReportSpec),
            Session = command?.C1G2SingulationControl?.Session ?? 0,
            TagPopulationEstimate = command?.C1G2SingulationControl?.TagPopulation ?? 32,
            ModeIndex = command?.C1G2RFControl?.ModeIndex ?? 0,
            Tari = command?.C1G2RFControl?.Tari ?? 0,
            AntennaConfigurations = inventorySpec.AntennaConfigurationItems
                .Where(configuration => configuration.RFReceiver is not null || configuration.RFTransmitter is not null)
                .Select(configuration => new InventoryAntennaConfiguration
                {
                    AntennaId = configuration.AntennaID,
                    ReceiverSensitivityIndex = configuration.RFReceiver?.ReceiverSensitivity,
                    TransmitPowerIndex = configuration.RFTransmitter?.TransmitPower,
                    HopTableId = configuration.RFTransmitter?.HopTableID,
                    ChannelIndex = configuration.RFTransmitter?.ChannelIndex,
                }).ToArray(),
            Filters = command?.C1G2FilterItems.Select(ParseFilter).ToArray() ?? [],
            StartTrigger = ParseStartTrigger(roSpec.ROBoundarySpec.ROSpecStartTrigger),
            StopTrigger = ParseStopTrigger(roSpec.ROBoundarySpec.ROSpecStopTrigger),
            StateAwareSingulation = stateAwareSingulation,
            AttachedData = read is null ? new AttachedDataOptions() : new AttachedDataOptions
            {
                Enabled = true, MemoryBank = read.MB, WordPointer = read.WordPointer, WordCount = read.WordCount,
                AccessPassword = read.AccessPassword.ToString("X8")
            }
        };
        ReaderMetadataSnapshot metadata = Volatile.Read(ref _metadata) ?? throw new InvalidOperationException(
            "Inventory settings query requires initialized reader metadata.");
        var extensionBuilder = new InventorySettingsExtensionBuilder();
        var contributionContext = new InventorySettingsContributionContext(
            metadata.Identity,
            metadata.Capabilities,
            NegotiatedVersion,
            roSpec.ROReportSpec?.CustomItems ?? [],
            command?.CustomItems ?? []);
        foreach (IInventorySettingsContributor contributor in Extensions.OfType<IInventorySettingsContributor>())
        {
            contributor.ContributeQuery(contributionContext, extensionBuilder);
        }
        settings = settings with { Extensions = extensionBuilder.Build() };
        InventoryRuntimeState state = roSpec.CurrentState switch
        {
            LlrpNet.Protocol.Enumerations.V1_0_1.ROSpecState.Active => InventoryRuntimeState.Running,
            LlrpNet.Protocol.Enumerations.V1_0_1.ROSpecState.Inactive => InventoryRuntimeState.Enabled,
            _ => InventoryRuntimeState.Disabled
        };
        return new InventorySnapshot(settings, state);
    }

    private InventorySnapshot ParseManagedInventory(V11Parameters.ROSpec roSpec, IReadOnlyList<ILlrpParameter> accessSpecs)
    {
        ArgumentNullException.ThrowIfNull(roSpec);
        ArgumentNullException.ThrowIfNull(accessSpecs);
        if (roSpec.ROSpecID != ManagedInventoryRoSpecId)
        {
            throw new InvalidOperationException("The supplied ROSpec is not the SDK-managed inventory ROSpec.");
        }

        V11Parameters.AISpec aiSpec = roSpec.SpecParameterItems.OfType<V11Parameters.AISpec>().SingleOrDefault()
            ?? throw new InvalidOperationException("The reserved SDK ROSpec must contain exactly one AISpec.");
        V11Parameters.InventoryParameterSpec inventorySpec = aiSpec.InventoryParameterSpecItems.Single();
        V11Parameters.C1G2InventoryCommand? command = inventorySpec.AntennaConfigurationItems
            .SelectMany(configuration => configuration.AirProtocolInventoryCommandSettingsItems)
            .OfType<V11Parameters.C1G2InventoryCommand>().FirstOrDefault();
        V11Parameters.AccessSpec? attachedDataSpec = accessSpecs.OfType<V11Parameters.AccessSpec>()
            .SingleOrDefault(spec => spec.AccessSpecID == ManagedInventoryAttachedDataAccessSpecId);
        if (attachedDataSpec is not null && attachedDataSpec.ROSpecID != ManagedInventoryRoSpecId)
        {
            throw new InvalidOperationException("The reserved SDK AttachedData AccessSpec is not associated with the reserved SDK ROSpec.");
        }
        V11Parameters.C1G2Read? read = attachedDataSpec is null
            ? null
            : attachedDataSpec.AccessCommand.AccessCommandOpSpecItems.OfType<V11Parameters.C1G2Read>().FirstOrDefault();
        if (attachedDataSpec is not null && read is null)
        {
            throw new InvalidOperationException("The reserved SDK AttachedData AccessSpec must contain a C1G2Read operation.");
        }

        var settings = new InventorySettings
        {
            Priority = roSpec.Priority,
            AntennaIds = aiSpec.AntennaIDs,
            InventoryParameterSpecId = inventorySpec.InventoryParameterSpecID,
            ReportEveryNTags = roSpec.ROReportSpec?.N ?? 1,
            Report = ParseReportSettings(roSpec.ROReportSpec),
            Session = command?.C1G2SingulationControl?.Session ?? 0,
            TagPopulationEstimate = command?.C1G2SingulationControl?.TagPopulation ?? 32,
            ModeIndex = command?.C1G2RFControl?.ModeIndex ?? 0,
            Tari = command?.C1G2RFControl?.Tari ?? 0,
            AntennaConfigurations = inventorySpec.AntennaConfigurationItems
                .Where(configuration => configuration.RFReceiver is not null || configuration.RFTransmitter is not null)
                .Select(configuration => new InventoryAntennaConfiguration
                {
                    AntennaId = configuration.AntennaID,
                    ReceiverSensitivityIndex = configuration.RFReceiver?.ReceiverSensitivity,
                    TransmitPowerIndex = configuration.RFTransmitter?.TransmitPower,
                    HopTableId = configuration.RFTransmitter?.HopTableID,
                    ChannelIndex = configuration.RFTransmitter?.ChannelIndex,
                }).ToArray(),
            Filters = command?.C1G2FilterItems.Select(ParseFilter).ToArray() ?? [],
            StartTrigger = ParseStartTrigger(roSpec.ROBoundarySpec.ROSpecStartTrigger),
            StopTrigger = ParseStopTrigger(roSpec.ROBoundarySpec.ROSpecStopTrigger),
            StateAwareSingulation = ParseStateAwareSingulation(command?.C1G2SingulationControl?.C1G2TagInventoryStateAwareSingulationAction),
            AttachedData = read is null ? new AttachedDataOptions() : new AttachedDataOptions
            {
                Enabled = true, MemoryBank = read.MB, WordPointer = read.WordPointer, WordCount = read.WordCount,
                AccessPassword = read.AccessPassword.ToString("X8")
            }
        };
        ReaderMetadataSnapshot metadata = Volatile.Read(ref _metadata) ?? throw new InvalidOperationException(
            "Inventory settings query requires initialized reader metadata.");
        var extensionBuilder = new InventorySettingsExtensionBuilder();
        var contributionContext = new InventorySettingsContributionContext(
            metadata.Identity,
            metadata.Capabilities,
            NegotiatedVersion,
            roSpec.ROReportSpec?.CustomItems ?? [],
            command?.CustomItems ?? []);
        foreach (IInventorySettingsContributor contributor in Extensions.OfType<IInventorySettingsContributor>())
        {
            contributor.ContributeQuery(contributionContext, extensionBuilder);
        }
        settings = settings with { Extensions = extensionBuilder.Build() };
        InventoryRuntimeState state = roSpec.CurrentState switch
        {
            V11Enumerations.ROSpecState.Active => InventoryRuntimeState.Running,
            V11Enumerations.ROSpecState.Inactive => InventoryRuntimeState.Enabled,
            _ => InventoryRuntimeState.Disabled
        };
        return new InventorySnapshot(settings, state);
    }

    private static InventorySelectFilter ParseFilter(C1G2Filter filter)
    {
        if (filter.C1G2TagInventoryStateAwareFilterAction is { } stateAware)
        {
            bool[] stateAwareBits = filter.C1G2TagInventoryMask.TagMask.ToArray();
            return new InventorySelectFilter
            {
                MemoryBank = filter.C1G2TagInventoryMask.MB,
                BitPointer = filter.C1G2TagInventoryMask.Pointer,
                Mask = BitsToBytes(stateAwareBits),
                BitLength = checked((ushort)stateAwareBits.Length),
                StateAwareAction = new InventoryStateAwareFilterAction
                {
                    Target = stateAware.Target switch
                    {
                        LlrpNet.Protocol.Enumerations.V1_0_1.C1G2StateAwareTarget.SL => InventoryFilterTarget.SelectedFlag,
                        LlrpNet.Protocol.Enumerations.V1_0_1.C1G2StateAwareTarget.Inventoried_State_For_Session_S0 => InventoryFilterTarget.Session0,
                        LlrpNet.Protocol.Enumerations.V1_0_1.C1G2StateAwareTarget.Inventoried_State_For_Session_S1 => InventoryFilterTarget.Session1,
                        LlrpNet.Protocol.Enumerations.V1_0_1.C1G2StateAwareTarget.Inventoried_State_For_Session_S2 => InventoryFilterTarget.Session2,
                        LlrpNet.Protocol.Enumerations.V1_0_1.C1G2StateAwareTarget.Inventoried_State_For_Session_S3 => InventoryFilterTarget.Session3,
                        _ => throw new InvalidOperationException("The reserved SDK ROSpec contains an unsupported state-aware filter target."),
                    },
                    Action = (InventoryFilterAction)(long)stateAware.Action,
                }
            };
        }
        C1G2TagInventoryStateUnawareFilterAction action = filter.C1G2TagInventoryStateUnawareFilterAction
            ?? throw new InvalidOperationException("A C1G2 filter must define exactly one Select action.");
        (InventorySelectAction match, InventorySelectAction nonMatch) = action.Action switch
        {
            LlrpNet.Protocol.Enumerations.V1_0_1.C1G2StateUnawareAction.Select_Unselect => (InventorySelectAction.Select, InventorySelectAction.Unselect),
            LlrpNet.Protocol.Enumerations.V1_0_1.C1G2StateUnawareAction.Select_DoNothing => (InventorySelectAction.Select, InventorySelectAction.DoNothing),
            LlrpNet.Protocol.Enumerations.V1_0_1.C1G2StateUnawareAction.DoNothing_Unselect => (InventorySelectAction.DoNothing, InventorySelectAction.Unselect),
            LlrpNet.Protocol.Enumerations.V1_0_1.C1G2StateUnawareAction.Unselect_DoNothing => (InventorySelectAction.Unselect, InventorySelectAction.DoNothing),
            LlrpNet.Protocol.Enumerations.V1_0_1.C1G2StateUnawareAction.Unselect_Select => (InventorySelectAction.Unselect, InventorySelectAction.Select),
            _ => (InventorySelectAction.DoNothing, InventorySelectAction.Select)
        };
        bool[] bits = filter.C1G2TagInventoryMask.TagMask.ToArray();
        return new InventorySelectFilter { MemoryBank = filter.C1G2TagInventoryMask.MB, BitPointer = filter.C1G2TagInventoryMask.Pointer, Mask = BitsToBytes(bits), BitLength = checked((ushort)bits.Length), MatchAction = match, NonMatchAction = nonMatch };
    }

    private static InventorySelectFilter ParseFilter(V11Parameters.C1G2Filter filter)
    {
        if (filter.C1G2TagInventoryStateAwareFilterAction is { } stateAware)
        {
            bool[] stateAwareBits = filter.C1G2TagInventoryMask.TagMask.ToArray();
            return new InventorySelectFilter
            {
                MemoryBank = filter.C1G2TagInventoryMask.MB,
                BitPointer = filter.C1G2TagInventoryMask.Pointer,
                Mask = BitsToBytes(stateAwareBits),
                BitLength = checked((ushort)stateAwareBits.Length),
                StateAwareAction = new InventoryStateAwareFilterAction
                {
                    Target = stateAware.Target switch
                    {
                        V11Enumerations.C1G2StateAwareTarget.SL => InventoryFilterTarget.SelectedFlag,
                        V11Enumerations.C1G2StateAwareTarget.Inventoried_State_For_Session_S0 => InventoryFilterTarget.Session0,
                        V11Enumerations.C1G2StateAwareTarget.Inventoried_State_For_Session_S1 => InventoryFilterTarget.Session1,
                        V11Enumerations.C1G2StateAwareTarget.Inventoried_State_For_Session_S2 => InventoryFilterTarget.Session2,
                        V11Enumerations.C1G2StateAwareTarget.Inventoried_State_For_Session_S3 => InventoryFilterTarget.Session3,
                        _ => throw new InvalidOperationException("The reserved SDK ROSpec contains an unsupported state-aware filter target."),
                    },
                    Action = (InventoryFilterAction)(long)stateAware.Action,
                }
            };
        }
        V11Parameters.C1G2TagInventoryStateUnawareFilterAction action = filter.C1G2TagInventoryStateUnawareFilterAction
            ?? throw new InvalidOperationException("A C1G2 filter must define exactly one Select action.");
        (InventorySelectAction match, InventorySelectAction nonMatch) = action.Action switch
        {
            V11Enumerations.C1G2StateUnawareAction.Select_Unselect => (InventorySelectAction.Select, InventorySelectAction.Unselect),
            V11Enumerations.C1G2StateUnawareAction.Select_DoNothing => (InventorySelectAction.Select, InventorySelectAction.DoNothing),
            V11Enumerations.C1G2StateUnawareAction.DoNothing_Unselect => (InventorySelectAction.DoNothing, InventorySelectAction.Unselect),
            V11Enumerations.C1G2StateUnawareAction.Unselect_DoNothing => (InventorySelectAction.Unselect, InventorySelectAction.DoNothing),
            V11Enumerations.C1G2StateUnawareAction.Unselect_Select => (InventorySelectAction.Unselect, InventorySelectAction.Select),
            _ => (InventorySelectAction.DoNothing, InventorySelectAction.Select)
        };
        bool[] bits = filter.C1G2TagInventoryMask.TagMask.ToArray();
        return new InventorySelectFilter { MemoryBank = filter.C1G2TagInventoryMask.MB, BitPointer = filter.C1G2TagInventoryMask.Pointer, Mask = BitsToBytes(bits), BitLength = checked((ushort)bits.Length), MatchAction = match, NonMatchAction = nonMatch };
    }

    private static byte[] BitsToBytes(IReadOnlyList<bool> bits) => bits.Chunk(8)
        .Select(group => Convert.ToByte(group.Select((bit, index) => bit ? 1 << (7 - index) : 0).Sum())).ToArray();

    private static InventoryStartTrigger ParseStartTrigger(ROSpecStartTrigger trigger) => trigger.ROSpecStartTriggerType switch
    {
        LlrpNet.Protocol.Enumerations.V1_0_1.ROSpecStartTriggerType.Null => new() { Type = InventoryStartTriggerType.None },
        LlrpNet.Protocol.Enumerations.V1_0_1.ROSpecStartTriggerType.Immediate => new() { Type = InventoryStartTriggerType.Immediate },
        LlrpNet.Protocol.Enumerations.V1_0_1.ROSpecStartTriggerType.Periodic when trigger.PeriodicTriggerValue is { } periodic => new()
        {
            Type = InventoryStartTriggerType.Periodic,
            OffsetMilliseconds = periodic.Offset,
            PeriodMilliseconds = periodic.Period,
            StartAtUtc = periodic.UTCTimestamp is { } utc ? FromUtcMicroseconds(utc.Microseconds) : null,
        },
        LlrpNet.Protocol.Enumerations.V1_0_1.ROSpecStartTriggerType.GPI when trigger.GPITriggerValue is { } gpi => new() { Type = InventoryStartTriggerType.Gpi, GpiPortNumber = gpi.GPIPortNum, GpiState = gpi.GPIEvent, TimeoutMilliseconds = gpi.Timeout },
        _ => throw new InvalidOperationException("The reserved SDK ROSpec has an unsupported or malformed start trigger."),
    };

    private static InventoryStartTrigger ParseStartTrigger(V11Parameters.ROSpecStartTrigger trigger) => trigger.ROSpecStartTriggerType switch
    {
        V11Enumerations.ROSpecStartTriggerType.Null => new() { Type = InventoryStartTriggerType.None },
        V11Enumerations.ROSpecStartTriggerType.Immediate => new() { Type = InventoryStartTriggerType.Immediate },
        V11Enumerations.ROSpecStartTriggerType.Periodic when trigger.PeriodicTriggerValue is { } periodic => new()
        {
            Type = InventoryStartTriggerType.Periodic,
            OffsetMilliseconds = periodic.Offset,
            PeriodMilliseconds = periodic.Period,
            StartAtUtc = periodic.UTCTimestamp is { } utc ? FromUtcMicroseconds(utc.Microseconds) : null,
        },
        V11Enumerations.ROSpecStartTriggerType.GPI when trigger.GPITriggerValue is { } gpi => new() { Type = InventoryStartTriggerType.Gpi, GpiPortNumber = gpi.GPIPortNum, GpiState = gpi.GPIEvent, TimeoutMilliseconds = gpi.Timeout },
        _ => throw new InvalidOperationException("The reserved SDK ROSpec has an unsupported or malformed start trigger."),
    };

    private static DateTimeOffset FromUtcMicroseconds(ulong microseconds)
    {
        try
        {
            return DateTimeOffset.UnixEpoch.AddTicks(checked((long)microseconds * TimeSpan.TicksPerMicrosecond));
        }
        catch (OverflowException exception)
        {
            throw new InvalidOperationException("The reserved SDK ROSpec contains an out-of-range UTC start timestamp.", exception);
        }
    }

    private static InventoryReportSettings ParseReportSettings(ROReportSpec? reportSpec)
    {
        if (reportSpec is null)
        {
            throw new InvalidOperationException("The reserved SDK ROSpec must contain an ROReportSpec.");
        }
        TagReportContentSelector selector = reportSpec.TagReportContentSelector;
        C1G2EPCMemorySelector? epc = selector.AirProtocolEPCMemorySelectorItems.OfType<C1G2EPCMemorySelector>().SingleOrDefault();
        if (selector.AirProtocolEPCMemorySelectorItems.Count != 0 && epc is null)
        {
            throw new InvalidOperationException("The reserved SDK ROSpec has an unsupported EPC report selector.");
        }
        return new InventoryReportSettings
        {
            Trigger = reportSpec.ROReportTrigger switch
            {
                LlrpNet.Protocol.Enumerations.V1_0_1.ROReportTriggerType.None => InventoryReportTrigger.None,
                LlrpNet.Protocol.Enumerations.V1_0_1.ROReportTriggerType.Upon_N_Tags_Or_End_Of_AISpec => InventoryReportTrigger.UponNTagsOrEndOfAiSpec,
                LlrpNet.Protocol.Enumerations.V1_0_1.ROReportTriggerType.Upon_N_Tags_Or_End_Of_ROSpec => InventoryReportTrigger.UponNTagsOrEndOfRoSpec,
                _ => throw new InvalidOperationException("The reserved SDK ROSpec has an unsupported report trigger."),
            },
            IncludeRoSpecId = selector.EnableROSpecID,
            IncludeSpecIndex = selector.EnableSpecIndex,
            IncludeInventoryParameterSpecId = selector.EnableInventoryParameterSpecID,
            IncludeAntennaId = selector.EnableAntennaID,
            IncludeChannelIndex = selector.EnableChannelIndex,
            IncludePeakRssi = selector.EnablePeakRSSI,
            IncludeFirstSeenTimestamp = selector.EnableFirstSeenTimestamp,
            IncludeLastSeenTimestamp = selector.EnableLastSeenTimestamp,
            IncludeTagSeenCount = selector.EnableTagSeenCount,
            IncludeAccessSpecId = selector.EnableAccessSpecID,
            IncludeCrc = epc?.EnableCRC ?? false,
            IncludePcBits = epc?.EnablePCBits ?? false,
        };
    }

    private static InventoryReportSettings ParseReportSettings(V11Parameters.ROReportSpec? reportSpec)
    {
        if (reportSpec is null)
        {
            throw new InvalidOperationException("The reserved SDK ROSpec must contain an ROReportSpec.");
        }
        V11Parameters.TagReportContentSelector selector = reportSpec.TagReportContentSelector;
        V11Parameters.C1G2EPCMemorySelector? epc = selector.AirProtocolEPCMemorySelectorItems.OfType<V11Parameters.C1G2EPCMemorySelector>().SingleOrDefault();
        if (selector.AirProtocolEPCMemorySelectorItems.Count != 0 && epc is null)
        {
            throw new InvalidOperationException("The reserved SDK ROSpec has an unsupported EPC report selector.");
        }
        return new InventoryReportSettings
        {
            Trigger = reportSpec.ROReportTrigger switch
            {
                V11Enumerations.ROReportTriggerType.None => InventoryReportTrigger.None,
                V11Enumerations.ROReportTriggerType.Upon_N_Tags_Or_End_Of_AISpec => InventoryReportTrigger.UponNTagsOrEndOfAiSpec,
                V11Enumerations.ROReportTriggerType.Upon_N_Tags_Or_End_Of_ROSpec => InventoryReportTrigger.UponNTagsOrEndOfRoSpec,
                _ => throw new InvalidOperationException("The reserved SDK ROSpec has an unsupported report trigger."),
            },
            IncludeRoSpecId = selector.EnableROSpecID,
            IncludeSpecIndex = selector.EnableSpecIndex,
            IncludeInventoryParameterSpecId = selector.EnableInventoryParameterSpecID,
            IncludeAntennaId = selector.EnableAntennaID,
            IncludeChannelIndex = selector.EnableChannelIndex,
            IncludePeakRssi = selector.EnablePeakRSSI,
            IncludeFirstSeenTimestamp = selector.EnableFirstSeenTimestamp,
            IncludeLastSeenTimestamp = selector.EnableLastSeenTimestamp,
            IncludeTagSeenCount = selector.EnableTagSeenCount,
            IncludeAccessSpecId = selector.EnableAccessSpecID,
            IncludeCrc = epc?.EnableCRC ?? false,
            IncludePcBits = epc?.EnablePCBits ?? false,
        };
    }

    private static InventoryStopTrigger ParseStopTrigger(ROSpecStopTrigger trigger) => trigger.ROSpecStopTriggerType switch
    {
        LlrpNet.Protocol.Enumerations.V1_0_1.ROSpecStopTriggerType.Null => new() { Type = InventoryStopTriggerType.None },
        LlrpNet.Protocol.Enumerations.V1_0_1.ROSpecStopTriggerType.Duration => new() { Type = InventoryStopTriggerType.Duration, DurationMilliseconds = trigger.DurationTriggerValue },
        LlrpNet.Protocol.Enumerations.V1_0_1.ROSpecStopTriggerType.GPI_With_Timeout when trigger.GPITriggerValue is { } gpi => new() { Type = InventoryStopTriggerType.GpiWithTimeout, GpiPortNumber = gpi.GPIPortNum, GpiState = gpi.GPIEvent, TimeoutMilliseconds = gpi.Timeout },
        _ => throw new InvalidOperationException("The reserved SDK ROSpec has an unsupported or malformed stop trigger."),
    };

    private static InventoryStopTrigger ParseStopTrigger(V11Parameters.ROSpecStopTrigger trigger) => trigger.ROSpecStopTriggerType switch
    {
        V11Enumerations.ROSpecStopTriggerType.Null => new() { Type = InventoryStopTriggerType.None },
        V11Enumerations.ROSpecStopTriggerType.Duration => new() { Type = InventoryStopTriggerType.Duration, DurationMilliseconds = trigger.DurationTriggerValue },
        V11Enumerations.ROSpecStopTriggerType.GPI_With_Timeout when trigger.GPITriggerValue is { } gpi => new() { Type = InventoryStopTriggerType.GpiWithTimeout, GpiPortNumber = gpi.GPIPortNum, GpiState = gpi.GPIEvent, TimeoutMilliseconds = gpi.Timeout },
        _ => throw new InvalidOperationException("The reserved SDK ROSpec has an unsupported or malformed stop trigger."),
    };

    private static InventoryStateAwareSingulation? ParseStateAwareSingulation(C1G2TagInventoryStateAwareSingulationAction? action) => action is null ? null : new InventoryStateAwareSingulation
    {
        Target = action.I switch
        {
            LlrpNet.Protocol.Enumerations.V1_0_1.C1G2TagInventoryStateAwareI.State_A => InventoryTarget.StateA,
            LlrpNet.Protocol.Enumerations.V1_0_1.C1G2TagInventoryStateAwareI.State_B => InventoryTarget.StateB,
            _ => throw new InvalidOperationException("The reserved SDK ROSpec has an unsupported state-aware singulation target."),
        },
        SelectedFlag = action.S switch
        {
            LlrpNet.Protocol.Enumerations.V1_0_1.C1G2TagInventoryStateAwareS.SL => InventorySelectedFlag.Set,
            LlrpNet.Protocol.Enumerations.V1_0_1.C1G2TagInventoryStateAwareS.Not_SL => InventorySelectedFlag.Clear,
            _ => throw new InvalidOperationException("The reserved SDK ROSpec has an unsupported state-aware singulation flag."),
        },
    };

    private static InventoryStateAwareSingulation? ParseStateAwareSingulation(V11Parameters.C1G2TagInventoryStateAwareSingulationAction? action) => action is null ? null : new InventoryStateAwareSingulation
    {
        Target = action.I switch
        {
            V11Enumerations.C1G2TagInventoryStateAwareI.State_A => InventoryTarget.StateA,
            V11Enumerations.C1G2TagInventoryStateAwareI.State_B => InventoryTarget.StateB,
            _ => throw new InvalidOperationException("The reserved SDK ROSpec has an unsupported state-aware singulation target."),
        },
        SelectedFlag = action.SAll ? InventorySelectedFlag.All : action.S switch
        {
            V11Enumerations.C1G2TagInventoryStateAwareS.SL => InventorySelectedFlag.Set,
            V11Enumerations.C1G2TagInventoryStateAwareS.Not_SL => InventorySelectedFlag.Clear,
            _ => throw new InvalidOperationException("The reserved SDK ROSpec has an unsupported state-aware singulation flag."),
        },
    };

    private ILlrpParameter CompileAttachedDataAccessSpec(uint accessSpecId, uint roSpecId, AttachedDataOptions options)
    {
        if (options.MemoryBank > (ushort)TagMemoryBank.User)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Attached-data memory bank must be between 0 and 3.");
        }
        if (options.WordCount == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Attached-data word count must be positive.");
        }
        if (options.AccessPassword.Length != 8 ||
            !uint.TryParse(options.AccessPassword, System.Globalization.NumberStyles.AllowHexSpecifier, System.Globalization.CultureInfo.InvariantCulture, out uint accessPassword))
        {
            throw new ArgumentException("Attached-data access password must be an eight-digit hexadecimal value.", nameof(options));
        }

        var request = new ReadTagRequest
        {
            Selection = new TagSelection
            {
                MemoryBank = TagMemoryBank.ElectronicProductCode,
                BitPointer = 32,
                BitLength = 1,
                Mask = new byte[] { 0 },
                Data = new byte[] { 0 },
            },
            AccessPassword = accessPassword,
            MemoryBank = (TagMemoryBank)options.MemoryBank,
            WordPointer = options.WordPointer,
            WordCount = options.WordCount,
        };
        return GetProtocolAdapter().CompileTagAccess(accessSpecId, roSpecId, request);
    }

    private async Task TryAttachedDataAccessSpecCleanupAsync(
        uint? accessSpecId,
        bool enabled,
        bool added,
        CancellationToken cancellationToken)
    {
        if (accessSpecId is not uint id)
        {
            return;
        }

        if (enabled)
        {
            try { await AccessSpecs.DisableAsync(id, cancellationToken).ConfigureAwait(false); } catch { }
        }
        if (added)
        {
            try { await AccessSpecs.DeleteAsync(id, cancellationToken).ConfigureAwait(false); } catch { }
        }
    }

    private bool MatchesTypedResponse<TResponse>(
        LlrpMessageHeader header,
        ReadOnlyMemory<byte> frame)
        where TResponse : class, ILlrpMessage
    {
        if (header.MessageType == 100)
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

    private static bool MatchesGetSupportedVersionResponse(
        LlrpMessageHeader header,
        ReadOnlyMemory<byte> frame)
    {
        return header.MessageType is V11Messages.GET_SUPPORTED_VERSION_RESPONSE.MessageType or 100;
    }

    private static bool MatchesSetProtocolVersionResponse(
        LlrpMessageHeader header,
        ReadOnlyMemory<byte> frame)
    {
        return header.MessageType is V11Messages.SET_PROTOCOL_VERSION_RESPONSE.MessageType or 100;
    }

    private static bool TryCreateOperationException(
        string operation,
        ILlrpMessage response,
        out LlrpReaderOperationException? exception)
    {
        if (response is V101Messages.ERROR_MESSAGE v101Error)
        {
            exception = new LlrpReaderOperationException(
                operation,
                checked((ushort)v101Error.LLRPStatus.StatusCode),
                v101Error.LLRPStatus.ErrorDescription,
                v101Error.LLRPStatus);
            return true;
        }

        if (response is V11Messages.ERROR_MESSAGE v11Error)
        {
            exception = new LlrpReaderOperationException(
                operation,
                checked((ushort)v11Error.LLRPStatus.StatusCode),
                v11Error.LLRPStatus.ErrorDescription,
                v11Error.LLRPStatus);
            return true;
        }

        exception = null;
        return false;
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

    private void PublishTagReport(TagReport report)
    {
        InventorySession? session = _inventorySession;
        if (session is not null && report.RoSpecId == session.RoSpecId &&
            (report.AccessSpecId is null || report.AccessSpecId == session.AttachedDataAccessSpecId))
        {
            session.Publish(report);
        }
        try
        {
            TagsReported?.Invoke(this, new TagReportEventArgs(report));
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "A reader tag-report event subscriber failed for connection {ConnectionId}",
                ConnectionId);
        }
    }

    private void ProcessManagedRoSpecEvent(uint? roSpecId, InventoryRuntimeState? state)
    {
        if (roSpecId != ManagedInventoryRoSpecId || state is not { } nextState)
        {
            return;
        }

        InventorySession? session = _inventorySession;
        if (nextState == InventoryRuntimeState.Disabled)
        {
            session?.Complete(InventoryRuntimeState.Disabled);
            _inventorySession = null;
            Volatile.Write(ref _operationState, (int)ReaderOperationState.Idle);
            Volatile.Write(ref _resourceMode, (int)ReaderResourceMode.HighLevelConfigured);
        }
        else
        {
            session?.SetState(nextState);
            Volatile.Write(ref _operationState, (int)ReaderOperationState.Inventorying);
            Volatile.Write(ref _resourceMode, (int)ReaderResourceMode.HighLevelRunning);
        }
    }

    private void ProcessReaderEventNotification(V101Messages.READER_EVENT_NOTIFICATION msg)
    {
        var data = msg.ReaderEventNotificationData;
        ProcessManagedRoSpecEvent(data.ROSpecEvent?.ROSpecID, data.ROSpecEvent?.EventType switch
        {
            LlrpNet.Protocol.Enumerations.V1_0_1.ROSpecEventType.Start_Of_ROSpec => InventoryRuntimeState.Running,
            LlrpNet.Protocol.Enumerations.V1_0_1.ROSpecEventType.End_Of_ROSpec or LlrpNet.Protocol.Enumerations.V1_0_1.ROSpecEventType.Preemption_Of_ROSpec => InventoryRuntimeState.Disabled,
            _ => null,
        });
        if (data.GPIEvent is { } gpi)
        {
            PublishGpiChanged(gpi.GPIPortNumber, gpi.GPIEvent_2);
        }
        if (data.AntennaEvent is { } antenna)
        {
            PublishAntennaChanged(antenna.AntennaID,
                antenna.EventType == LlrpNet.Protocol.Enumerations.V1_0_1.AntennaEventType.Antenna_Connected);
        }
        if (data.ReportBufferOverflowErrorEvent is not null)
        {
            PublishReportBufferOverflow();
        }
        if (data.ReportBufferLevelWarningEvent is { } warning)
        {
            PublishReportBufferWarning(warning.ReportBufferPercentageFull);
        }
    }

    private void ProcessReaderEventNotification(V11Messages.READER_EVENT_NOTIFICATION msg)
    {
        var data = msg.ReaderEventNotificationData;
        ProcessManagedRoSpecEvent(data.ROSpecEvent?.ROSpecID, data.ROSpecEvent?.EventType switch
        {
            LlrpNet.Protocol.Enumerations.V1_1.ROSpecEventType.Start_Of_ROSpec => InventoryRuntimeState.Running,
            LlrpNet.Protocol.Enumerations.V1_1.ROSpecEventType.End_Of_ROSpec or LlrpNet.Protocol.Enumerations.V1_1.ROSpecEventType.Preemption_Of_ROSpec => InventoryRuntimeState.Disabled,
            _ => null,
        });
        if (data.GPIEvent is { } gpi)
        {
            PublishGpiChanged(gpi.GPIPortNumber, gpi.GPIEvent_2);
        }
        if (data.AntennaEvent is { } antenna)
        {
            PublishAntennaChanged(antenna.AntennaID,
                antenna.EventType == LlrpNet.Protocol.Enumerations.V1_1.AntennaEventType.Antenna_Connected);
        }
        if (data.ReportBufferOverflowErrorEvent is not null)
        {
            PublishReportBufferOverflow();
        }
        if (data.ReportBufferLevelWarningEvent is { } warning)
        {
            PublishReportBufferWarning(warning.ReportBufferPercentageFull);
        }
    }

    private void PublishGpiChanged(ushort portNumber, bool state)
    {
        try
        {
            GpiChanged?.Invoke(this, new GpiChangedEventArgs(portNumber, state));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "A reader GPI event subscriber failed for connection {ConnectionId}", ConnectionId);
        }
    }

    private void PublishKeepaliveReceived()
    {
        Volatile.Write(ref _lastKeepaliveUtcTicks, DateTimeOffset.UtcNow.UtcTicks);
        Volatile.Write(ref _keepaliveTimeoutSignaled, 0);
        try
        {
            KeepaliveReceived?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "A reader Keepalive event subscriber failed for connection {ConnectionId}", ConnectionId);
        }
    }

    private void PublishReportBufferOverflow()
    {
        try
        {
            ReportBufferOverflow?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "A reader ReportBufferOverflow event subscriber failed for connection {ConnectionId}", ConnectionId);
        }
    }

    private void PublishAntennaChanged(ushort antennaId, bool isConnected)
    {
        try
        {
            AntennaChanged?.Invoke(this, new AntennaChangedEventArgs(antennaId, isConnected));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "A reader antenna event subscriber failed for connection {ConnectionId}", ConnectionId);
        }
    }

    private void PublishKeepaliveTimedOut(TimeSpan timeout, DateTimeOffset lastReceivedAt)
    {
        try
        {
            KeepaliveTimedOut?.Invoke(this, new KeepaliveTimeoutEventArgs(timeout, lastReceivedAt));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "A reader KeepaliveTimeout event subscriber failed for connection {ConnectionId}", ConnectionId);
        }
    }

    private void PublishReportBufferWarning(byte percentageFull)
    {
        try
        {
            ReportBufferWarning?.Invoke(this, new ReportBufferWarningEventArgs(percentageFull));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "A reader ReportBufferWarning event subscriber failed for connection {ConnectionId}", ConnectionId);
        }
    }

    private TagReport ApplyTagReportContributors(TranslatedTagReport translated)
    {
        var values = new TagReportExtensionBuilder();
        var context = new TagReportContributionContext(translated.Report, translated.CustomItems);
        foreach (ITagReportContributor contributor in Extensions.OfType<ITagReportContributor>())
        {
            contributor.Contribute(context, values);
        }

        return translated.Report with { Extensions = values.Build() };
    }

    private ReaderConfiguration ApplySettingsContributors(TranslatedReaderConfiguration translated)
    {
        var values = new ReaderConfigurationExtensionBuilder();
        var context = new ReaderSettingsContributionContext(translated.Configuration, translated.CustomItems);
        foreach (IReaderSettingsContributor contributor in GetSettingsContributors())
        {
            contributor.ContributeQuery(context, values);
        }

        return translated.Configuration with { Extensions = values.Build() };
    }

    private IReadOnlyList<ILlrpParameter> BuildSettingsApplyParameters(ReaderConfiguration configuration)
    {
        var customItems = new List<ILlrpParameter>();
        foreach (IReaderSettingsContributor contributor in GetSettingsContributors())
        {
            customItems.AddRange(contributor.BuildApplyParameters(configuration));
        }

        return customItems.Count == 0 ? [] : customItems.AsReadOnly();
    }

    private IReadOnlyList<ILlrpParameter> BuildSettingsQueryParameters()
    {
        var customItems = new List<ILlrpParameter>();
        foreach (IReaderSettingsContributor contributor in GetSettingsContributors())
        {
            customItems.AddRange(contributor.BuildQueryParameters());
        }

        return customItems.Count == 0 ? [] : customItems.AsReadOnly();
    }

    private IReadOnlyList<IReaderSettingsContributor> GetSettingsContributors()
    {
        IReaderSettingsContributor[] contributors = Extensions.OfType<IReaderSettingsContributor>().ToArray();
        var contributorIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (IReaderSettingsContributor contributor in contributors)
        {
            if (string.IsNullOrWhiteSpace(contributor.Id))
            {
                throw new InvalidOperationException("A reader settings contributor must declare a non-empty Id.");
            }

            if (!contributorIds.Add(contributor.Id))
            {
                throw new InvalidOperationException($"More than one reader settings contributor uses Id '{contributor.Id}'.");
            }
        }

        return contributors;
    }

    private InventoryCustomItems BuildInventoryCustomItems(InventorySettings settings)
    {
        ReaderMetadataSnapshot metadata = Volatile.Read(ref _metadata) ?? throw new InvalidOperationException(
            "Inventory contributors require initialized reader metadata.");
        var values = new InventoryExtensionBuilder();
        var context = new InventoryContributionContext(
            settings,
            metadata.Identity,
            metadata.Capabilities,
            NegotiatedVersion);
        var contributorIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (IInventoryContributor contributor in Extensions.OfType<IInventoryContributor>())
        {
            if (string.IsNullOrWhiteSpace(contributor.Id))
            {
                throw new InvalidOperationException("An inventory contributor must declare a non-empty Id.");
            }

            if (!contributorIds.Add(contributor.Id))
            {
                throw new InvalidOperationException($"More than one inventory contributor uses Id '{contributor.Id}'.");
            }

            contributor.Contribute(context, values);
        }

        return new InventoryCustomItems(values.RoReportSpecCustomItems, values.C1G2InventoryCommandCustomItems);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
    }

    private readonly record struct StateTransition(
        ReaderConnectionState PreviousState,
        ReaderConnectionState CurrentState,
        Exception? Error);

}
