using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Threading.Channels;
using LlrpNet.Core.Protocol;
using LlrpNet.Core.Session;
using LlrpNet.Core.Transactions;
using LlrpNet.Core.Transport;
using LlrpNet.Protocol.Messages;
using LlrpNet.Protocol.Parameters;
using LlrpNet.Protocol.Registry;
using LlrpSdk.Extensions;
using Microsoft.Extensions.Logging;

namespace LlrpSdk;

/// <summary>
/// Represents one reader connection and owns its transport, LLRP session, protocol registry, and unsolicited-message pump.
/// </summary>
public sealed class LlrpReader : IAsyncDisposable
{
    /// <summary>Gets the fixed ROSpec identifier recognized as the SDK-managed inventory resource.</summary>
    public const uint ManagedInventoryRoSpecId = 14150;

    /// <summary>Gets the fixed AccessSpec identifier reserved for SDK-managed AttachedData.</summary>
    public const uint ManagedInventoryAttachedDataAccessSpecId = 14151;
    private readonly Channel<ILlrpMessage> _messages;
    private readonly Channel<TagReport> _tagReports;
    private readonly object _tagReportDeliveryGate = new();
    private readonly List<TagReportWaiter> _tagReportWaiters = [];
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
    private ReaderSettings? _desiredReaderSettings;
    private ReaderMetadataSnapshot? _metadata;
    private uint? _managedInventoryRoSpecId;
    private uint? _managedInventoryAttachedDataAccessSpecId;
    private InventorySession? _inventorySession;
    private int _nextManagedAccessSpecId = 24000;
    private int _connectionState = (int)ReaderConnectionState.Disconnected;
    private int _deviceInitiatedClose;
    private int _managedStateIsSynchronized;
    private int _observedResourceState = (int)ReaderObservedState.Unknown;
    private int _observedManagedResourcePresent;
    private int _observedManagedInventoryState = (int)InventoryRuntimeState.Disabled;
    private int _operationState = (int)ReaderOperationState.Idle;
    private int _resourceMode = (int)ReaderResourceMode.Idle;
    private ReaderResourceSnapshot? _lastResourceSnapshot;
    private int _disposed;
    private long _lastKeepaliveUtcTicks;
    private int _keepaliveTimeoutSignaled;
    private long _tagReportsDropped;
    private EventHandler<TagReportEventArgs>? _tagsReported;
    private TagReportDeliveryOwner _tagReportDeliveryOwner;
    private bool _readerTagReportStreamActive;
    private bool _sessionTagReportReaderActive;

    private enum TagReportDeliveryOwner
    {
        None,
        Session,
        ReaderAsync,
        Event,
    }

    private sealed record TagReportWaiter(
        Func<TagReport, bool> Predicate,
        Action<TagReport> OnMatch);

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
            new Llrp20ProtocolAdapter(),
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

    /// <summary>Gets the reader logger for version-boundary components that run outside the facade.</summary>
    internal ILogger Logger => _logger;

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

    /// <summary>Gets the current managed resource observation mode; expert protocol resources are not a third mode.</summary>
    public ReaderResourceMode ResourceMode =>
        (ReaderResourceMode)Volatile.Read(ref _resourceMode);

    /// <summary>Gets the freshness of the last observed standard resource snapshot.</summary>
    public ReaderObservedState ObservedState =>
        (ReaderObservedState)Volatile.Read(ref _observedResourceState);

    /// <summary>Gets the most recent standard ROSpec/AccessSpec observation, if one was captured.</summary>
    public ReaderResourceSnapshot? LastResourceSnapshot => Volatile.Read(ref _lastResourceSnapshot);

    /// <summary>Gets whether the last resource observation found resources outside the SDK-managed IDs.</summary>
    public bool HasForeignResources => LastResourceSnapshot?.HasForeignResources == true;

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

    /// <summary>Gets the last settings intent successfully or intentionally submitted to the managed API.</summary>
    public ReaderSettings? DesiredSettings => Volatile.Read(ref _desiredReaderSettings);

    /// <summary>
    /// Gets a value indicating whether the most recent SDK resource observation is trustworthy.
    /// </summary>
    /// <remarks>
    /// This property describes observed-device freshness only. It does not indicate whether a desired managed
    /// inventory exists, and a managed operation may reconcile a stale observation without a preceding sync call.
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
    /// Gets a read-only view of the codec registry configured for this reader.
    /// </summary>
    /// <remarks>
    /// Codec registration remains a configuration-time concern (<see cref="LlrpReaderBuilder.ConfigureProtocol"/>,
    /// <c>UseProtocolModule</c>); the returned view only decodes and encodes against the configured codecs.
    /// </remarks>
    public ILlrpCodecRegistryReader Registry => _registry;

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
    /// Registering a handler selects the connection-level observer report outlet for the current inventory
    /// lifetime. It is mutually exclusive with <see cref="InventorySession.ReadReportsAsync"/> and
    /// <see cref="ReadTagReportsAsync(CancellationToken)"/> while an inventory is active.
    /// </remarks>
    public event EventHandler<TagReportEventArgs>? TagsReported
    {
        add => AddTagReportObserver(value);
        remove => RemoveTagReportObserver(value);
    }

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
    /// Occurs when the reader reports an internal exception (ReaderExceptionEvent), for example an RF or command
    /// execution failure. The event carries the reader-supplied message and optional ROSpec/AccessSpec context.
    /// </summary>
    public event EventHandler<ReaderExceptionEventArgs>? ReaderExceptionOccurred;

    /// <summary>
    /// Occurs when the SDK's connection-level tag-report stream dropped reports because the bounded buffer was full
    /// (DropOldest policy). Distinct from <see cref="ReportBufferOverflow"/>, which is the reader's own buffer event.
    /// </summary>
    /// <remarks>This event is raised only after <see cref="ReadTagReportsAsync(CancellationToken)"/> claims the outlet.</remarks>
    public event EventHandler<TagReportOverflowEventArgs>? TagReportsDropped;

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
            ResetConnectionResourceState();
            Volatile.Write(ref _deviceInitiatedClose, 0);
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
                await LlrpVersionNegotiator.NegotiateAsync(this, cancellationToken).ConfigureAwait(false);
                AddTransition(transitions, ReaderConnectionState.Initializing);
                await InitializeReaderAsync(cancellationToken).ConfigureAwait(false);
                AddTransition(transitions, ReaderConnectionState.Ready);
                StartKeepaliveMonitor();
            }
            catch (Exception exception)
            {
                InvalidateMetadata();
                ResetConnectionResourceState();
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

    /// <summary>Deploys and starts SDK-managed inventory while preserving foreign resources.</summary>
    public async Task<InventorySession> StartInventoryAsync(InventorySettings settings, CancellationToken cancellationToken = default)
        => await StartInventoryAsync(settings, ResourceTakeoverPolicy.PreserveForeign, cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Deploys and starts SDK-managed inventory with an explicit resource takeover policy.
    /// </summary>
    /// <remarks>
    /// <see cref="ResourceTakeoverPolicy.PreserveForeign"/> replaces only the SDK-reserved resources and is safe
    /// for a reader that also contains expert or foreign ROSpecs. <see cref="ResourceTakeoverPolicy.ReplaceAll"/>
    /// uses LLRP id zero and deletes every standard ROSpec and AccessSpec.
    /// </remarks>
    public async Task<InventorySession> StartInventoryAsync(
        InventorySettings settings,
        ResourceTakeoverPolicy takeoverPolicy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ValidateTakeoverPolicy(takeoverPolicy);
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_inventorySession is not null)
            {
                throw new InvalidOperationException("A managed inventory session already exists for this reader.");
            }
            ValidateSettingsCore(new ReaderSettings { Inventory = settings }).ThrowIfInvalid();
            InventorySettings active = settings;
            SetDesiredInventorySettings(active);
            InventoryRuntimeState initialState = active.StartTrigger.Type == InventoryStartTriggerType.None
                ? InventoryRuntimeState.Running
                : InventoryRuntimeState.Enabled;
            try
            {
                PrepareForManagedTakeover();
                // Managed identifiers are reserved and fixed, so install the session before START_ROSPEC. This
                // prevents a reader from emitting its first report before the report outlet is attached.
                var session = new InventorySession(
                    this,
                    active,
                    ManagedInventoryRoSpecId,
                    active.AttachedData.Enabled ? ManagedInventoryAttachedDataAccessSpecId : null,
                    initialState,
                    Options.IncomingMessageCapacity);
                _inventorySession = session;
                await StartManagedInventoryCoreAsync(
                    active,
                    resourcesAlreadyCleared: false,
                    cancellationToken,
                    forceTakeover: true,
                    takeoverPolicy: takeoverPolicy).ConfigureAwait(false);
                return session;
            }
            catch
            {
                CompleteActiveInventorySession();
                throw;
            }
        }
        finally { _operationLock.Release(); }
    }

    /// <summary>Starts or reconciles the inventory intent previously applied to this reader.</summary>
    /// <remarks>
    /// This overload uses retained <see cref="DesiredSettings"/> when the reserved resource was changed or deleted
    /// by the expert control plane, and redeploys it when necessary. After
    /// <see cref="ClearManagedSettingsAsync"/>, use the settings overload or apply settings again.
    /// </remarks>
    public Task<InventorySession> StartInventoryAsync(CancellationToken cancellationToken = default) =>
        StartInventoryAsync(ResourceTakeoverPolicy.PreserveForeign, cancellationToken);

    /// <summary>
    /// Starts an already deployed or desired SDK-managed inventory configuration, optionally taking over all
    /// standard resources before reconciling it.
    /// </summary>
    /// <param name="takeoverPolicy">Preserve the reader's foreign resources, or explicitly replace all of them.</param>
    /// <param name="cancellationToken">Cancels the resource operations.</param>
    public async Task<InventorySession> StartInventoryAsync(
        ResourceTakeoverPolicy takeoverPolicy,
        CancellationToken cancellationToken = default)
    {
        ValidateTakeoverPolicy(takeoverPolicy);
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureProtocolAvailable();
            if (_inventorySession is not null)
            {
                throw new InvalidOperationException("A managed inventory session already exists for this reader.");
            }
            InventorySettings? desiredInventory = Volatile.Read(ref _desiredReaderSettings)?.Inventory;
            InventorySettings? settings = desiredInventory ?? Volatile.Read(ref _currentInventorySettings);
            if (settings is null)
            {
                throw new InvalidOperationException("No stopped SDK-managed inventory configuration is available to start.");
            }
            uint roSpecId = _managedInventoryRoSpecId ?? ManagedInventoryRoSpecId;

            // The desired inventory is authoritative when an observed ROSpec was changed by Raw/expert code.
            // Keep the attached-data identifier aligned with that intent before constructing the session.
            _managedInventoryAttachedDataAccessSpecId = settings.AttachedData.Enabled
                ? ManagedInventoryAttachedDataAccessSpecId
                : null;

            bool desiredDiffersFromObserved = desiredInventory is not null &&
                Volatile.Read(ref _currentInventorySettings) is { } observedInventoryForAttach &&
                !InventorySettingsMatch(desiredInventory, observedInventoryForAttach);
            if (takeoverPolicy == ResourceTakeoverPolicy.PreserveForeign &&
                IsManagedStateSynchronized &&
                Volatile.Read(ref _observedManagedResourcePresent) != 0 &&
                (InventoryRuntimeState)Volatile.Read(ref _observedManagedInventoryState) == InventoryRuntimeState.Running &&
                !desiredDiffersFromObserved)
            {
                var attachedSession = new InventorySession(
                    this,
                    settings,
                    roSpecId,
                    _managedInventoryAttachedDataAccessSpecId,
                    InventoryRuntimeState.Running,
                    Options.IncomingMessageCapacity,
                    ownsResource: true);
                _inventorySession = attachedSession;
                Volatile.Write(ref _operationState, (int)ReaderOperationState.Inventorying);
                Volatile.Write(ref _resourceMode, (int)ReaderResourceMode.HighLevelRunning);
                return attachedSession;
            }

            bool redeploy = takeoverPolicy == ResourceTakeoverPolicy.ReplaceAll ||
                !IsManagedStateSynchronized || Volatile.Read(ref _observedManagedResourcePresent) == 0 ||
                ResourceMode is ReaderResourceMode.StateUnknown or ReaderResourceMode.Idle ||
                desiredDiffersFromObserved;
            if (!redeploy && ResourceMode != ReaderResourceMode.HighLevelConfigured)
            {
                throw new InvalidOperationException("No stopped SDK-managed inventory configuration is available to start.");
            }

            var session = new InventorySession(this, settings, roSpecId,
                _managedInventoryAttachedDataAccessSpecId, InventoryRuntimeState.Enabled,
                Options.IncomingMessageCapacity, ownsResource: true);
            _inventorySession = session;
            try
            {
                if (redeploy)
                {
                    PrepareForManagedTakeover();
                    await StartManagedInventoryCoreAsync(
                        settings,
                        resourcesAlreadyCleared: false,
                        cancellationToken,
                        forceTakeover: true,
                        takeoverPolicy: takeoverPolicy).ConfigureAwait(false);
                }
                else
                {
                    await StartConfiguredManagedInventoryCoreAsync(cancellationToken).ConfigureAwait(false);
                }
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

    /// <summary>
    /// Starts an existing ROSpec and exposes its reports through the managed inventory session API.
    /// </summary>
    /// <remarks>
    /// This is the bridge for an ROSpec created by <see cref="RoSpecs"/> or <see cref="Protocol"/>. The SDK does not
    /// compile, delete, or replace the definition. Stopping the returned session stops the ROSpec but leaves it on
    /// the reader for further expert or raw inspection.
    /// </remarks>
    public async Task<InventorySession> StartExistingRoSpecAsync(
        uint roSpecId,
        CancellationToken cancellationToken = default)
    {
        if (roSpecId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(roSpecId), "An existing ROSpec identifier must be non-zero.");
        }

        EnsureProtocolAvailable();
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureProtocolAvailable();
            if (_inventorySession is not null)
            {
                throw new InvalidOperationException("An inventory session already exists for this reader.");
            }

            using IDisposable scope = BeginInternalResourceOperationScope();
            IReadOnlyList<ILlrpParameter> roSpecs = await RoSpecs.GetAllAsync(cancellationToken).ConfigureAwait(false);
            IReadOnlyList<ILlrpParameter> accessSpecs = await AccessSpecs.GetAllAsync(cancellationToken).ConfigureAwait(false);
            StoreResourceSnapshot(roSpecs, accessSpecs);
            ILlrpParameter roSpec = roSpecs.SingleOrDefault(item => GetProtocolAdapter().GetRoSpecId(item) == roSpecId)
                ?? throw new InvalidOperationException($"ROSpec {roSpecId} was not found on the reader.");
            InventoryRuntimeState currentState = GetProtocolAdapter().GetRoSpecRuntimeState(roSpec);
            InventorySettings sessionSettings = CurrentInventorySettings ?? new InventorySettings();
            var session = new InventorySession(
                this,
                sessionSettings,
                roSpecId,
                attachedDataAccessSpecId: null,
                InventoryRuntimeState.Running,
                Options.IncomingMessageCapacity,
                ownsResource: false);
            _inventorySession = session;

            try
            {
                if (currentState == InventoryRuntimeState.Disabled)
                {
                    await RoSpecs.EnableAsync(roSpecId, cancellationToken).ConfigureAwait(false);
                    await RoSpecs.StartAsync(roSpecId, cancellationToken).ConfigureAwait(false);
                }
                else if (currentState == InventoryRuntimeState.Enabled)
                {
                    await RoSpecs.StartAsync(roSpecId, cancellationToken).ConfigureAwait(false);
                }

                // Refresh after START_ROSPEC so an attached session can safely decide whether a report that
                // omits ROSpecID is unambiguous. The pre-start snapshot may not reflect the running state yet.
                IReadOnlyList<ILlrpParameter> startedRoSpecs = await RoSpecs.GetAllAsync(cancellationToken).ConfigureAwait(false);
                IReadOnlyList<ILlrpParameter> startedAccessSpecs = await AccessSpecs.GetAllAsync(cancellationToken).ConfigureAwait(false);
                StoreResourceSnapshot(startedRoSpecs, startedAccessSpecs);

                Volatile.Write(ref _operationState, (int)ReaderOperationState.Inventorying);
                Volatile.Write(ref _resourceMode, (int)ReaderResourceMode.AttachedInventory);
                Volatile.Write(ref _observedResourceState, (int)ReaderObservedState.Synchronized);
                Volatile.Write(ref _managedStateIsSynchronized, 1);
                return session;
            }
            catch
            {
                CompleteActiveInventorySession();
                MarkResourceObservationStale();
                throw;
            }
        }
        finally
        {
            _operationLock.Release();
        }
    }

    internal async Task StopInventorySessionAsync(InventorySession session, CancellationToken cancellationToken)
    {
        if (ReferenceEquals(_inventorySession, session))
        {
            if (session.OwnsResource)
            {
                await StopAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await StopAttachedInventorySessionAsync(session, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task StopAttachedInventorySessionAsync(InventorySession session, CancellationToken cancellationToken)
    {
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!ReferenceEquals(_inventorySession, session))
            {
                return;
            }

            using IDisposable scope = BeginInternalResourceOperationScope();
            try
            {
                await RoSpecs.StopAsync(session.RoSpecId, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _inventorySession = null;
                session.Complete(InventoryRuntimeState.Disabled);
                Volatile.Write(ref _operationState, (int)ReaderOperationState.Idle);
                Volatile.Write(ref _resourceMode, (int)ReaderResourceMode.Idle);
                Volatile.Write(ref _managedStateIsSynchronized, 0);
                Volatile.Write(ref _observedResourceState, (int)ReaderObservedState.Stale);
            }
        }
        finally
        {
            _operationLock.Release();
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
                CompleteActiveInventorySession();
                ResetConnectionResourceState();
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
            ResetConnectionResourceState();
            Volatile.Write(ref _deviceInitiatedClose, 0);
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
                await LlrpVersionNegotiator.NegotiateAsync(this, cancellationToken).ConfigureAwait(false);
                AddTransition(transitions, ReaderConnectionState.Initializing);
                await InitializeReaderAsync(cancellationToken).ConfigureAwait(false);
                AddTransition(transitions, ReaderConnectionState.Ready);
                await SynchronizeManagedStateOnReconnectAsync(cancellationToken).ConfigureAwait(false);
                await EnsureEventsAndReportsEnabledOnReconnectAsync(cancellationToken).ConfigureAwait(false);
                StartKeepaliveMonitor();
            }
            catch (Exception exception)
            {
                InvalidateMetadata();
                ResetConnectionResourceState();
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
    /// The first consumer claims the connection-level observer outlet. It is mutually exclusive with
    /// <see cref="InventorySession.ReadReportsAsync"/> and <see cref="TagsReported"/> while an inventory is active.
    /// Multiple simultaneous enumerators are rejected; callers needing fan-out should distribute the sequence in
    /// their application. Raw LLRP messages remain independently available through
    /// <see cref="ReadMessagesAsync(CancellationToken)"/>.
    /// </remarks>
    public IAsyncEnumerable<TagReport> ReadTagReportsAsync(
        CancellationToken cancellationToken = default)
    {
        AcquireReaderTagReportStream();
        return ReadReaderTagReportsAsync(cancellationToken);
    }

    private async IAsyncEnumerable<TagReport> ReadReaderTagReportsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        try
        {
            await foreach (TagReport report in _tagReports.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return report;
            }
        }
        finally
        {
            ReleaseReaderTagReportStream();
        }
    }

    internal IAsyncEnumerable<TagReport> ReadInventorySessionReports(
        InventorySession session,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        AcquireSessionTagReportStream(session);
        return session.ReadReportsCore(cancellationToken);
    }

    private void AddTagReportObserver(EventHandler<TagReportEventArgs>? handler)
    {
        if (handler is null)
        {
            return;
        }
        lock (_tagReportDeliveryGate)
        {
            if (_tagReportDeliveryOwner is TagReportDeliveryOwner.Session or TagReportDeliveryOwner.ReaderAsync)
            {
                throw CreateTagReportDeliveryConflictException(_tagReportDeliveryOwner, "TagsReported");
            }

            if (_tagReportDeliveryOwner == TagReportDeliveryOwner.None)
            {
                _tagReportDeliveryOwner = TagReportDeliveryOwner.Event;
                _inventorySession?.DiscardPendingReports();
            }

            _tagsReported += handler;
        }
    }

    private void RemoveTagReportObserver(EventHandler<TagReportEventArgs>? handler)
    {
        if (handler is null)
        {
            return;
        }

        lock (_tagReportDeliveryGate)
        {
            _tagsReported -= handler;
            if (_tagsReported is null &&
                _tagReportDeliveryOwner == TagReportDeliveryOwner.Event &&
                _inventorySession is null)
            {
                _tagReportDeliveryOwner = TagReportDeliveryOwner.None;
            }
        }
    }

    private void AcquireReaderTagReportStream()
    {
        lock (_tagReportDeliveryGate)
        {
            if (_tagReportDeliveryOwner is TagReportDeliveryOwner.Session or TagReportDeliveryOwner.Event)
            {
                throw CreateTagReportDeliveryConflictException(_tagReportDeliveryOwner, "ReadTagReportsAsync");
            }

            if (_readerTagReportStreamActive)
            {
                throw new InvalidOperationException(
                    "ReadTagReportsAsync already has an active consumer for this reader connection.");
            }

            _tagReportDeliveryOwner = TagReportDeliveryOwner.ReaderAsync;
            _readerTagReportStreamActive = true;
            _inventorySession?.DiscardPendingReports();
        }
    }

    private void ReleaseReaderTagReportStream()
    {
        lock (_tagReportDeliveryGate)
        {
            _readerTagReportStreamActive = false;
            if (_tagReportDeliveryOwner == TagReportDeliveryOwner.ReaderAsync && _inventorySession is null)
            {
                _tagReportDeliveryOwner = TagReportDeliveryOwner.None;
            }
        }
    }

    private void AcquireSessionTagReportStream(InventorySession session)
    {
        lock (_tagReportDeliveryGate)
        {
            if (!ReferenceEquals(_inventorySession, session))
            {
                if (session.IsCompleted)
                {
                    return;
                }

                throw new InvalidOperationException(
                    "The inventory session is no longer the active session for this reader.");
            }

            if (_tagReportDeliveryOwner is TagReportDeliveryOwner.Event or TagReportDeliveryOwner.ReaderAsync)
            {
                throw CreateTagReportDeliveryConflictException(_tagReportDeliveryOwner, "InventorySession.ReadReportsAsync");
            }

            if (_sessionTagReportReaderActive)
            {
                throw new InvalidOperationException(
                    "InventorySession.ReadReportsAsync already has an active consumer for this session.");
            }

            _tagReportDeliveryOwner = TagReportDeliveryOwner.Session;
            _sessionTagReportReaderActive = true;
        }
    }

    internal void ReleaseSessionTagReportOwnership(InventorySession? session)
    {
        if (session is null)
        {
            return;
        }

        lock (_tagReportDeliveryGate)
        {
            if (_tagReportDeliveryOwner == TagReportDeliveryOwner.Session &&
                (ReferenceEquals(_inventorySession, session) || session.IsCompleted))
            {
                _sessionTagReportReaderActive = false;
                if (session.IsCompleted || _inventorySession is null)
                {
                    _tagReportDeliveryOwner = TagReportDeliveryOwner.None;
                }
            }
        }
    }

    private static InvalidOperationException CreateTagReportDeliveryConflictException(
        TagReportDeliveryOwner owner,
        string requestedOutlet) =>
        new(
            $"Tag reports are already owned by {DescribeTagReportDeliveryOwner(owner)}. " +
            $"The requested {requestedOutlet} outlet is mutually exclusive; " +
            "stop the current inventory before selecting another report consumer.");

    private static string DescribeTagReportDeliveryOwner(TagReportDeliveryOwner owner) => owner switch
    {
        TagReportDeliveryOwner.Session => "InventorySession.ReadReportsAsync",
        TagReportDeliveryOwner.ReaderAsync => "ReadTagReportsAsync",
        TagReportDeliveryOwner.Event => "TagsReported",
        _ => "another report consumer",
    };

    private IDisposable RegisterTagReportWaiter(Func<TagReport, bool> predicate, Action<TagReport> onMatch)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(onMatch);
        var waiter = new TagReportWaiter(predicate, onMatch);
        lock (_tagReportDeliveryGate)
        {
            _tagReportWaiters.Add(waiter);
        }

        return new TagReportWaiterLease(this, waiter);
    }

    private void RemoveTagReportWaiter(TagReportWaiter waiter)
    {
        lock (_tagReportDeliveryGate)
        {
            _tagReportWaiters.Remove(waiter);
        }
    }

    private sealed class TagReportWaiterLease : IDisposable
    {
        private readonly LlrpReader reader;
        private readonly TagReportWaiter waiter;
        private int disposed;

        public TagReportWaiterLease(LlrpReader reader, TagReportWaiter waiter)
        {
            this.reader = reader;
            this.waiter = waiter;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
            {
                reader.RemoveTagReportWaiter(waiter);
            }
        }
    }

    /// <summary>
    /// Refreshes the reader's observed ROSpec and AccessSpec snapshot.
    /// </summary>
    /// <param name="cancellationToken">Cancels the synchronization queries.</param>
    /// <returns>A task that completes after standard ROSpec and AccessSpec state has been queried.</returns>
    /// <remarks>
    /// Synchronization is observational. It refreshes <see cref="CurrentInventorySettings"/> from the device while
    /// preserving <see cref="DesiredSettings"/>. A stale snapshot does not
    /// block a later managed API call; managed APIs reconcile their own reserved resources as needed.
    /// </remarks>
    public async Task SynchronizeStateAsync(CancellationToken cancellationToken = default)
    {
        EnsureProtocolAvailable();

        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureProtocolAvailable();
            using IDisposable scope = BeginInternalResourceOperationScope();
            IReadOnlyList<ILlrpParameter> roSpecs = await RoSpecs.GetAllAsync(cancellationToken).ConfigureAwait(false);
            IReadOnlyList<ILlrpParameter> accessSpecs = await AccessSpecs.GetAllAsync(cancellationToken).ConfigureAwait(false);
            StoreResourceSnapshot(roSpecs, accessSpecs);
            AdoptManagedInventorySnapshot(roSpecs.SingleOrDefault(GetProtocolAdapter().IsManagedRoSpec), accessSpecs);
            Volatile.Write(ref _observedResourceState, (int)ReaderObservedState.Synchronized);
            Volatile.Write(ref _managedStateIsSynchronized, 1);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    /// <summary>
    /// Queries the reader's current ROSpec/AccessSpec state after a reconnection and aligns the SDK's locally
    /// cached managed-inventory assumptions to the device facts.
    /// </summary>
    /// <remarks>
    /// Called from <see cref="ReconnectAsync(CancellationToken)"/> while the lifecycle lock is already held. It
    /// deliberately does <b>not</b> recreate or redeploy the previous managed inventory operation: it only observes
    /// device reality. If the SDK-managed ROSpec is still present the prior session is retained so its isolated
    /// report stream can continue receiving reports (routing already matches on the managed RoSpec id); if the
    /// resource disappeared (e.g. the device rebooted and wiped its configuration) the stale session is completed
    /// and the reader returns to idle so the application can explicitly re-establish its next desired state.
    /// </remarks>
    private async Task SynchronizeManagedStateOnReconnectAsync(CancellationToken cancellationToken)
    {
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using IDisposable scope = BeginInternalResourceOperationScope();
        IReadOnlyList<ILlrpParameter> roSpecs = await RoSpecs.GetAllAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<ILlrpParameter> accessSpecs = await AccessSpecs.GetAllAsync(cancellationToken).ConfigureAwait(false);
        StoreResourceSnapshot(roSpecs, accessSpecs);
        ILlrpParameter? managed = roSpecs.SingleOrDefault(GetProtocolAdapter().IsManagedRoSpec);
        if (managed is null)
        {
            InventorySession? session = _inventorySession;
            _inventorySession = null;
            session?.Complete(InventoryRuntimeState.Disabled);
            AdoptManagedInventorySnapshot(null, accessSpecs);
        }
        else
        {
            AdoptManagedInventorySnapshot(managed, accessSpecs);
        }
        Volatile.Write(ref _observedResourceState, (int)ReaderObservedState.Synchronized);
        Volatile.Write(ref _managedStateIsSynchronized, 1);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    private async Task EnsureEventsAndReportsEnabledOnReconnectAsync(CancellationToken cancellationToken)
    {
        try
        {
            ReaderConfiguration configuration = await QueryConfigurationCoreAsync(cancellationToken).ConfigureAwait(false);
            if (!configuration.HoldEventsAndReportsUponReconnect)
            {
                return;
            }

            // The reader is holding events and reports after this reconnect (the application configured
            // HoldEventsAndReportsUponReconnect=true); release them now that managed state is synchronized.
            ILlrpMessage enableMessage = LlrpProtocolMessageFactory.CreateEnableEventsAndReports(
                NegotiatedVersion,
                _messageIds.Next());
            byte[] enableFrame = _registry.EncodeMessage(NegotiatedVersion, enableMessage);
            await _session.SendFrameAsync(enableFrame, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation(
                "Released held events and reports after reconnect on connection {ConnectionId}",
                ConnectionId);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(
                exception,
                "Failed to release held events and reports after reconnect on connection {ConnectionId}",
                ConnectionId);
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
            SetDesiredInventorySettings(settings);
            await StartManagedInventoryCoreAsync(
                settings,
                resourcesAlreadyCleared: false,
                cancellationToken,
                forceTakeover: true,
                takeoverPolicy: ResourceTakeoverPolicy.PreserveForeign).ConfigureAwait(false);
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

        if (_inventorySession is { OwnsResource: false } attachedSession)
        {
            await StopAttachedInventorySessionAsync(attachedSession, cancellationToken).ConfigureAwait(false);
            return;
        }

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
                    MarkResourceObservationStale();
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
        settings = NormalizeInventorySettings(settings);
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

    private InventorySettings NormalizeInventorySettings(InventorySettings settings) =>
        InventorySettingsNormalizer.ExpandAllAntennas(
            settings,
            Capabilities?.MaxNumberOfAntennas ?? 0);

    /// <summary>
    /// Deletes the SDK-managed inventory ROSpec and AttachedData AccessSpec, releasing the high-level resource domain.
    /// </summary>
    public async Task ClearManagedSettingsAsync(CancellationToken cancellationToken = default)
    {
        EnsureProtocolAvailable();
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ReaderSettings? desiredSettings = Volatile.Read(ref _desiredReaderSettings);
            if (_managedInventoryRoSpecId is not uint roSpecId &&
                CurrentInventorySettings is null &&
                desiredSettings?.Inventory is null)
            {
                return;
            }
            roSpecId = _managedInventoryRoSpecId ?? ManagedInventoryRoSpecId;

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
                ClearDesiredManagedState();
                Volatile.Write(ref _resourceMode, (int)ReaderResourceMode.Idle);
                Volatile.Write(ref _observedManagedResourcePresent, 0);
                Volatile.Write(ref _observedManagedInventoryState, (int)InventoryRuntimeState.Disabled);
                Volatile.Write(ref _observedResourceState, (int)ReaderObservedState.Synchronized);
                Volatile.Write(ref _managedStateIsSynchronized, 1);
            }
            catch
            {
                MarkResourceObservationStale();
                throw;
            }
        }
        finally { _operationLock.Release(); }
    }

    /// <summary>Explicitly deletes all standard ROSpec and AccessSpec resources.</summary>
    /// <remarks>
    /// This is the only reader-level operation whose contract is to remove foreign standard resources. The desired
    /// managed settings remain available for a later managed deployment unless <see cref="ClearManagedSettingsAsync"/>
    /// is also called.
    /// </remarks>
    public async Task DeleteAllResourcesAsync(
        ResourceTakeoverPolicy takeoverPolicy,
        CancellationToken cancellationToken = default)
    {
        if (takeoverPolicy != ResourceTakeoverPolicy.ReplaceAll)
        {
            throw new ArgumentException(
                "Deleting all resources requires ResourceTakeoverPolicy.ReplaceAll.",
                nameof(takeoverPolicy));
        }

        EnsureProtocolAvailable();
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using IDisposable scope = BeginInternalResourceOperationScope();
            await DeleteAllStandardResourcesAsync(cancellationToken).ConfigureAwait(false);
            MarkResourceObservationStale();
        }
        finally
        {
            _operationLock.Release();
        }
    }

    /// <summary>Reads the high-level configuration and, when currently managed, the SDK inventory snapshot.</summary>
    public async Task<ReaderSettingsSnapshot> QuerySettingsAsync(CancellationToken cancellationToken = default)
    {
        EnsureProtocolAvailable();
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using IDisposable scope = BeginInternalResourceOperationScope();
            ReaderConfiguration configuration = await QueryConfigurationCoreAsync(cancellationToken).ConfigureAwait(false);
            IReadOnlyList<ILlrpParameter> roSpecs = await RoSpecs.GetAllAsync(cancellationToken).ConfigureAwait(false);
            IReadOnlyList<ILlrpParameter> accessSpecs = await AccessSpecs.GetAllAsync(cancellationToken).ConfigureAwait(false);
            ILlrpParameter? managed = roSpecs.SingleOrDefault(GetProtocolAdapter().IsManagedRoSpec);
            ManagedRoSpecSnapshot? snapshot = managed is null
                ? null
                : GetProtocolAdapter().ParseManagedRoSpec(this, managed, accessSpecs);
            StoreResourceSnapshot(roSpecs, accessSpecs);
            AdoptManagedInventorySnapshot(managed, accessSpecs, snapshot);
            Volatile.Write(ref _observedResourceState, (int)ReaderObservedState.Synchronized);
            Volatile.Write(ref _managedStateIsSynchronized, 1);
            InventorySettings? inventory = snapshot?.Inventory;
            return new ReaderSettingsSnapshot(new ReaderSettings { Configuration = configuration, Inventory = inventory }, snapshot)
            {
                RoSpecs = roSpecs,
                AccessSpecs = accessSpecs,
            };
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

            ReaderSettingsDefaults resolved = result ?? ReaderSettingsDefaults.CreateForReader(context);
            if (metadata.Capabilities.ResourceLimits.MaxNumROSpecs == 0 && resolved.Settings.Inventory is not null)
            {
                resolved = resolved with
                {
                    Settings = resolved.Settings with { Inventory = null },
                    Notes = resolved.Notes
                        .Append("The reader explicitly advertises MaxNumROSpecs=0; Inventory was omitted from defaults.")
                        .ToArray(),
                };
            }

            return resolved;
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
            InventorySettings? desiredInventory = Volatile.Read(ref _desiredReaderSettings)?.Inventory;
            InventorySettings? settings = desiredInventory ?? Volatile.Read(ref _currentInventorySettings);
            if (settings is null || _managedInventoryRoSpecId is not uint)
            {
                throw new InvalidOperationException("No stopped SDK-managed inventory configuration is available to start.");
            }

            _managedInventoryAttachedDataAccessSpecId = settings.AttachedData.Enabled
                ? ManagedInventoryAttachedDataAccessSpecId
                : null;

            bool desiredDiffersFromObserved = desiredInventory is not null &&
                Volatile.Read(ref _currentInventorySettings) is { } observedInventory &&
                !InventorySettingsMatch(desiredInventory, observedInventory);
            bool redeploy = !IsManagedStateSynchronized || Volatile.Read(ref _observedManagedResourcePresent) == 0 ||
                ResourceMode is ReaderResourceMode.StateUnknown or ReaderResourceMode.Idle ||
                desiredDiffersFromObserved;
            if (redeploy)
            {
                PrepareForManagedTakeover();
                await StartManagedInventoryCoreAsync(
                    settings,
                    resourcesAlreadyCleared: false,
                    cancellationToken,
                    forceTakeover: true,
                    takeoverPolicy: ResourceTakeoverPolicy.PreserveForeign).ConfigureAwait(false);
            }
            else if (ResourceMode == ReaderResourceMode.HighLevelConfigured)
            {
                await StartConfiguredManagedInventoryCoreAsync(cancellationToken).ConfigureAwait(false);
            }
            else if (OperationState != ReaderOperationState.Inventorying)
            {
                throw new InvalidOperationException("No stopped SDK-managed inventory configuration is available to start.");
            }
        }
        finally { _operationLock.Release(); }
    }

    /// <summary>Applies high-level configuration and optionally deploys managed inventory without starting it.</summary>
    /// <remarks>
    /// The default policy replaces only SDK-reserved resources. Use the overload with
    /// <see cref="ResourceTakeoverPolicy.ReplaceAll"/> when deleting all standard resources is intentional.
    /// </remarks>
    public async Task ApplySettingsAsync(ReaderSettings settings, CancellationToken cancellationToken = default)
        => await ApplySettingsAsync(settings, ResourceTakeoverPolicy.PreserveForeign, cancellationToken).ConfigureAwait(false);

    /// <summary>Applies high-level settings with an explicit resource takeover policy.</summary>
    public async Task ApplySettingsAsync(
        ReaderSettings settings,
        ResourceTakeoverPolicy takeoverPolicy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ValidateTakeoverPolicy(takeoverPolicy);
        EnsureProtocolAvailable();
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ValidateSettingsCore(settings).ThrowIfInvalid();
            using IDisposable scope = BeginInternalResourceOperationScope();
            if (settings.Inventory is null)
            {
                await ApplyConfigurationCoreAsync(settings.Configuration, cancellationToken).ConfigureAwait(false);
                ReaderSettings? existingDesired = Volatile.Read(ref _desiredReaderSettings);
                Volatile.Write(ref _desiredReaderSettings, new ReaderSettings
                {
                    Configuration = settings.Configuration,
                    Inventory = existingDesired?.Inventory,
                    Extensions = settings.Extensions,
                });
                return;
            }

            try
            {
                Volatile.Write(ref _desiredReaderSettings, settings);
                SetDesiredInventorySettings(settings.Inventory);
                PrepareForManagedTakeover();
                await PreflightManagedDeploymentAsync(settings.Inventory, takeoverPolicy, cancellationToken).ConfigureAwait(false);
                if (takeoverPolicy == ResourceTakeoverPolicy.ReplaceAll)
                {
                    await DeleteAllStandardResourcesAsync(cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await DeleteSdkOwnedResourcesAsync(cancellationToken).ConfigureAwait(false);
                }
                await ApplyConfigurationCoreAsync(settings.Configuration, cancellationToken).ConfigureAwait(false);
                await StartManagedInventoryCoreAsync(
                    settings.Inventory,
                    resourcesAlreadyCleared: true,
                    cancellationToken: cancellationToken,
                    startAfterDeployment: false,
                    forceTakeover: true,
                    takeoverPolicy: takeoverPolicy).ConfigureAwait(false);
            }
            catch
            {
                MarkResourceObservationStale();
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

    /// <summary>
    /// Reads the current standard resource set and checks the final managed graph before any deployment mutation.
    /// The check is intentionally skipped when the reader did not advertise the corresponding resource limits;
    /// the device remains authoritative in that case.
    /// </summary>
    private async Task PreflightManagedDeploymentAsync(
        InventorySettings settings,
        ResourceTakeoverPolicy takeoverPolicy,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ReaderResourceLimits limits = Capabilities?.ResourceLimits ?? ReaderResourceLimits.Unknown;
        bool needsResourceSnapshot = limits.MaxNumROSpecs.HasValue || limits.MaxNumAccessSpecs.HasValue;
        if (!needsResourceSnapshot)
        {
            return;
        }

        IReadOnlyList<ILlrpParameter> roSpecs = await RoSpecs.GetAllAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<ILlrpParameter> accessSpecs = await AccessSpecs.GetAllAsync(cancellationToken).ConfigureAwait(false);
        StoreResourceSnapshot(roSpecs, accessSpecs);

        uint foreignRoSpecs = CountForeignRoSpecs(roSpecs);
        uint foreignAccessSpecs = CountForeignAccessSpecs(accessSpecs);
        uint requiredRoSpecs = takeoverPolicy == ResourceTakeoverPolicy.ReplaceAll
            ? 1u
            : checked(foreignRoSpecs + 1u);
        uint requiredAccessSpecs = takeoverPolicy == ResourceTakeoverPolicy.ReplaceAll
            ? (settings.AttachedData.Enabled ? 1u : 0u)
            : checked(foreignAccessSpecs + (settings.AttachedData.Enabled ? 1u : 0u));

        EnsureCapacity(
            "ROSpec",
            limits.MaxNumROSpecs,
            (uint)roSpecs.Count,
            requiredRoSpecs,
            takeoverPolicy,
            "The managed graph requires one ROSpec. Use ReplaceAll when foreign ROSpecs cannot fit.");
        EnsureCapacity(
            "AccessSpec",
            limits.MaxNumAccessSpecs,
            (uint)accessSpecs.Count,
            requiredAccessSpecs,
            takeoverPolicy,
            "AttachedData requires one AccessSpec; disabled resources still consume capacity. Use ReplaceAll when foreign AccessSpecs cannot fit.");
    }

    private uint CountForeignRoSpecs(IReadOnlyList<ILlrpParameter> roSpecs)
    {
        uint count = 0;
        foreach (ILlrpParameter item in roSpecs)
        {
            if (!IsManagedRoSpecId(item, ManagedInventoryRoSpecId))
            {
                count++;
            }
        }

        return count;
    }

    private uint CountForeignAccessSpecs(IReadOnlyList<ILlrpParameter> accessSpecs)
    {
        uint count = 0;
        foreach (ILlrpParameter item in accessSpecs)
        {
            if (!IsManagedAccessSpecId(item, ManagedInventoryAttachedDataAccessSpecId))
            {
                count++;
            }
        }

        return count;
    }

    private bool IsManagedRoSpecId(ILlrpParameter item, uint managedId)
    {
        try { return GetProtocolAdapter().GetRoSpecId(item) == managedId; }
        catch (ArgumentException) { return false; }
    }

    private bool IsManagedAccessSpecId(ILlrpParameter item, uint managedId)
    {
        try { return GetProtocolAdapter().GetAccessSpecId(item) == managedId; }
        catch (ArgumentException) { return false; }
    }

    private static void EnsureCapacity(
        string resourceType,
        uint? limit,
        uint current,
        uint required,
        ResourceTakeoverPolicy takeoverPolicy,
        string detail)
    {
        if (limit.HasValue && required > limit.Value)
        {
            throw new LlrpResourceCapacityException(resourceType, limit, current, required, takeoverPolicy, detail);
        }
    }

    private async Task StartManagedInventoryCoreAsync(
        InventorySettings settings,
        bool resourcesAlreadyCleared,
        CancellationToken cancellationToken,
        bool startAfterDeployment = true,
        bool forceTakeover = false,
        ResourceTakeoverPolicy takeoverPolicy = ResourceTakeoverPolicy.PreserveForeign)
    {
        EnsureProtocolAvailable();
        ValidateTakeoverPolicy(takeoverPolicy);
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
                await PreflightManagedDeploymentAsync(settings, takeoverPolicy, cancellationToken).ConfigureAwait(false);
                if (takeoverPolicy == ResourceTakeoverPolicy.ReplaceAll)
                {
                    await DeleteAllStandardResourcesAsync(cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await DeleteSdkOwnedResourcesAsync(cancellationToken).ConfigureAwait(false);
                }
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
                // LLRP's Null trigger has no autonomous start condition, so it must be started explicitly by the
                // client. Immediate/Periodic/GPI triggers start from the enabled state when their trigger fires.
                if (settings.StartTrigger.Type == InventoryStartTriggerType.None)
                {
                    await RoSpecs.StartAsync(ManagedInventoryRoSpecId, cancellationToken).ConfigureAwait(false);
                }
            }

            _managedInventoryRoSpecId = ManagedInventoryRoSpecId;
            _managedInventoryAttachedDataAccessSpecId = attachedDataAccessSpecId;
            Volatile.Write(ref _currentInventorySettings, settings);
            Volatile.Write(ref _observedManagedResourcePresent, 1);
            Volatile.Write(ref _observedManagedInventoryState, (int)(startAfterDeployment
                ? (settings.StartTrigger.Type == InventoryStartTriggerType.None ? InventoryRuntimeState.Running : InventoryRuntimeState.Enabled)
                : InventoryRuntimeState.Enabled));
            Volatile.Write(ref _operationState, (int)(startAfterDeployment
                ? ReaderOperationState.Inventorying
                : ReaderOperationState.Idle));
            Volatile.Write(ref _resourceMode, (int)(startAfterDeployment
                ? ReaderResourceMode.HighLevelRunning
                : ReaderResourceMode.HighLevelConfigured));
            Volatile.Write(ref _managedStateIsSynchronized, 1);
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

            MarkResourceObservationStale();
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
            Volatile.Write(ref _observedManagedResourcePresent, 1);
            Volatile.Write(ref _observedManagedInventoryState, (int)InventoryRuntimeState.Running);
            Volatile.Write(ref _observedResourceState, (int)ReaderObservedState.Synchronized);
            Volatile.Write(ref _managedStateIsSynchronized, 1);
        }
        catch
        {
            MarkResourceObservationStale();
            throw;
        }
    }

    private async Task<ReaderConfiguration> QueryConfigurationCoreAsync(CancellationToken cancellationToken)
    {
        EnsureProtocolAvailable();
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

            ClearDesiredManagedState();
            ResetConnectionResourceState();
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
        }

        return reports;
    }

    /// <summary>
    /// Sets the output state of a specified GPO port on the reader.
    /// </summary>
    /// <remarks>
    /// LLRP has no single-GPO write message: <c>SET_READER_CONFIG</c> carries the whole GPO list, so this method
    /// replays the full configuration (query current settings, replace one GPO, re-apply). On shared readers the
    /// re-apply can overwrite non-SDK configuration changes made since the last query. Vendors with an advanced GPO
    /// extension (e.g. Impinj) may offer lower-level control separately.
    /// </remarks>
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
        // GPO is part of SET_READER_CONFIG. It must not redeploy the managed inventory
        // resource merely because the output state changed; doing so can send DELETE for
        // an optional SDK-owned AccessSpec/ROSpec that is not present on the reader.
        await ApplySettingsAsync(
            snapshot.Settings with
            {
                Configuration = snapshot.Settings.Configuration with { Gpos = gpos },
                Inventory = null,
            },
            ResourceTakeoverPolicy.PreserveForeign,
            cancellationToken).ConfigureAwait(false);
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

        // Check the temporary AccessSpec before creating a temporary ROSpec for a reader that has no inventory yet.
        await PreflightTagAccessCapacityAsync(cancellationToken).ConfigureAwait(false);
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
                await PreflightTagAccessCapacityAsync(cancellationToken).ConfigureAwait(false);
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
                using IDisposable waiter = RegisterTagReportWaiter(report => report.AccessSpecId == accessSpecId, report =>
                {
                    TagAccessOperationResult? operation = report.AccessOperationResults?
                        .FirstOrDefault(static result => result.OpSpecID == 1);
                    if (operation is not null)
                    {
                        completion.TrySetResult(new TagAccessResult(report, operation));
                    }
                });
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
        EnsureTagAccessSequenceCapacity(operations.Length);
        await PreflightTagAccessCapacityAsync(cancellationToken).ConfigureAwait(false);
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
                await PreflightTagAccessCapacityAsync(cancellationToken).ConfigureAwait(false);
                EnsureTagAccessSequenceCapacity(operations.Length);
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
                using IDisposable waiter = RegisterTagReportWaiter(report => report.AccessSpecId == accessSpecId, report =>
                {
                    IReadOnlyList<TagAccessOperationResult>? results = report.AccessOperationResults;
                    if (report.AccessSpecId != accessSpecId || results is null ||
                        !Enumerable.Range(1, operations.Length).All(id => results.Any(result => result.OpSpecID == id)))
                    {
                        return;
                    }

                    completion.TrySetResult(new TagAccessSequenceResult(
                        report,
                        results.OrderBy(static result => result.OpSpecID).ToArray()));
                });
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

    private async Task PreflightTagAccessCapacityAsync(CancellationToken cancellationToken)
    {
        uint? limit = Capabilities?.ResourceLimits.MaxNumAccessSpecs;
        if (!limit.HasValue)
        {
            return;
        }

        IReadOnlyList<ILlrpParameter> accessSpecs = await AccessSpecs.GetAllAsync(cancellationToken).ConfigureAwait(false);
        StoreResourceSnapshot(
            LastResourceSnapshot?.RoSpecs ?? Array.Empty<ILlrpParameter>(),
            accessSpecs);
        EnsureCapacity(
            "AccessSpec",
            limit,
            (uint)accessSpecs.Count,
            checked((uint)accessSpecs.Count + 1u),
            ResourceTakeoverPolicy.PreserveForeign,
            "A temporary Tag Access AccessSpec is required; foreign resources and disabled 14151 remain counted.");
    }

    private void EnsureTagAccessSequenceCapacity(int operationCount)
    {
        uint? limit = Capabilities?.ResourceLimits.MaxNumOpSpecsPerAccessSpec;
        if (limit.HasValue && (uint)operationCount > limit.Value)
        {
            throw new LlrpResourceCapacityException(
                "OpSpec",
                limit,
                0,
                checked((uint)operationCount),
                ResourceTakeoverPolicy.PreserveForeign,
                "The requested Tag Access sequence exceeds MaxNumOpSpecsPerAccessSpec.");
        }
    }

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
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await TransactAsync<TResponse>(request, timeout, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (!IsNonResourceAffectingTypedMessage(request))
            {
                MarkResourceObservationStale();
            }
            _operationLock.Release();
        }
    }

    internal async Task<TResponse> TransactSessionAsync<TResponse>(
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
        if (LlrpProtocolMessageFactory.TryCreateOperationException(request.GetType().Name, response, out LlrpReaderOperationException? error))
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
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await SendAsync(message, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (!IsNonResourceAffectingTypedMessage(message))
            {
                MarkResourceObservationStale();
            }
            _operationLock.Release();
        }
    }

    internal async Task<ReadOnlyMemory<byte>> TransactRawAsync(
        ReadOnlyMemory<byte> requestFrame,
        LlrpResponseMatcher responseMatcher,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(responseMatcher);
        EnsureProtocolAvailable();
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await _session.TransactAsync(
                requestFrame,
                responseMatcher,
                timeout,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            MarkResourceObservationStale();
            _operationLock.Release();
        }
    }

    internal async Task SendRawAsync(
        ReadOnlyMemory<byte> frame,
        CancellationToken cancellationToken)
    {
        EnsureProtocolAvailable();
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _session.SendFrameAsync(frame, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            MarkResourceObservationStale();
            _operationLock.Release();
        }
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

    internal async Task DeleteAllRoSpecsAsync(
        ResourceTakeoverPolicy takeoverPolicy,
        CancellationToken cancellationToken)
    {
        if (takeoverPolicy != ResourceTakeoverPolicy.ReplaceAll)
        {
            throw new ArgumentException(
                "Deleting all ROSpecs requires ResourceTakeoverPolicy.ReplaceAll.",
                nameof(takeoverPolicy));
        }

        EnsureProtocolAvailable();
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using IDisposable scope = BeginInternalResourceOperationScope();
            await DeleteAllRoSpecsCoreAsync(cancellationToken).ConfigureAwait(false);
            MarkResourceObservationStale();
        }
        finally
        {
            _operationLock.Release();
        }
    }

    internal async Task DeleteAllAccessSpecsAsync(
        ResourceTakeoverPolicy takeoverPolicy,
        CancellationToken cancellationToken)
    {
        if (takeoverPolicy != ResourceTakeoverPolicy.ReplaceAll)
        {
            throw new ArgumentException(
                "Deleting all AccessSpecs requires ResourceTakeoverPolicy.ReplaceAll.",
                nameof(takeoverPolicy));
        }

        EnsureProtocolAvailable();
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using IDisposable scope = BeginInternalResourceOperationScope();
            await DeleteAllAccessSpecsCoreAsync(cancellationToken).ConfigureAwait(false);
            MarkResourceObservationStale();
        }
        finally
        {
            _operationLock.Release();
        }
    }

    private async Task DeleteAllStandardResourcesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await DeleteAllAccessSpecsCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (LlrpReaderOperationException exception) when (IsNoResourceError(exception))
        {
        }

        try
        {
            await DeleteAllRoSpecsCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (LlrpReaderOperationException exception) when (IsNoResourceError(exception))
        {
        }
    }

    private async Task DeleteAllRoSpecsCoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            await GetProtocolAdapter().DeleteRoSpecAsync(this, _messageIds.Next(), 0, cancellationToken).ConfigureAwait(false);
        }
        catch (LlrpReaderOperationException exception) when (IsNoResourceError(exception))
        {
        }
    }

    private async Task DeleteAllAccessSpecsCoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            await GetProtocolAdapter().DeleteAccessSpecAsync(this, _messageIds.Next(), 0, cancellationToken).ConfigureAwait(false);
        }
        catch (LlrpReaderOperationException exception) when (IsNoResourceError(exception))
        {
        }
    }

    private async Task DeleteSdkOwnedResourcesAsync(CancellationToken cancellationToken)
    {
        // The reserved IDs are the only resources the managed compiler is allowed to replace in the default mode.
        try
        {
            await GetProtocolAdapter().StopRoSpecAsync(this, _messageIds.Next(), ManagedInventoryRoSpecId, cancellationToken).ConfigureAwait(false);
        }
        catch (LlrpReaderOperationException exception) when (IsIgnorableOwnedCleanupError(exception, "STOP_ROSPEC"))
        {
        }

        try
        {
            await GetProtocolAdapter().DisableRoSpecAsync(this, _messageIds.Next(), ManagedInventoryRoSpecId, cancellationToken).ConfigureAwait(false);
        }
        catch (LlrpReaderOperationException exception) when (IsIgnorableOwnedCleanupError(exception, "DISABLE_ROSPEC"))
        {
        }

        try
        {
            await GetProtocolAdapter().DeleteAccessSpecAsync(this, _messageIds.Next(), ManagedInventoryAttachedDataAccessSpecId, cancellationToken).ConfigureAwait(false);
        }
        catch (LlrpReaderOperationException exception) when (IsIgnorableOwnedCleanupError(exception, "DELETE_ACCESSSPEC"))
        {
        }

        try
        {
            await GetProtocolAdapter().DeleteRoSpecAsync(this, _messageIds.Next(), ManagedInventoryRoSpecId, cancellationToken).ConfigureAwait(false);
        }
        catch (LlrpReaderOperationException exception) when (IsIgnorableOwnedCleanupError(exception, "DELETE_ROSPEC"))
        {
        }
    }

    private static bool IsNoResourceError(LlrpReaderOperationException exception)
    {
        bool descriptionIndicatesMissingResource =
            exception.ErrorDescription.Contains("does not exist", StringComparison.OrdinalIgnoreCase) ||
            exception.ErrorDescription.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
            exception.ErrorDescription.Contains("not exist", StringComparison.OrdinalIgnoreCase);

        // LLRP 1.0.1 devices commonly use M_ParameterError (100) for a missing
        // resource. Zebra returns P_UnknownParameter (207) with "*ID Not Found"
        // for the same idempotent delete operation.
        return descriptionIndicatesMissingResource && (exception.StatusCode == 100 || exception.StatusCode == 207);
    }

    private static bool IsIgnorableOwnedCleanupError(
        LlrpReaderOperationException exception,
        string expectedOperation)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedOperation);

        // This predicate is used only by lifecycle cleanup of SDK-reserved resources. Keep the operation check
        // explicit so a status from an unrelated request can never become an idempotent cleanup success.
        if (!string.Equals(exception.Operation, expectedOperation, StringComparison.Ordinal))
        {
            return false;
        }

        bool descriptionIndicatesMissingResource =
            exception.ErrorDescription.Contains("does not exist", StringComparison.OrdinalIgnoreCase) ||
            exception.ErrorDescription.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
            exception.ErrorDescription.Contains("not exist", StringComparison.OrdinalIgnoreCase) ||
            exception.ErrorDescription.Contains("unknown rospec", StringComparison.OrdinalIgnoreCase) ||
            exception.ErrorDescription.Contains("unknown accessspec", StringComparison.OrdinalIgnoreCase);

        // A few readers report an SDK-owned delete of an already absent resource as M_FieldError (101), while
        // other readers use M_ParameterError (100) or P_UnknownParameter (207). The operation/description gate
        // keeps this tolerance local to the expected cleanup request.
        if (IsNoResourceError(exception) ||
            (exception.StatusCode == 101 && descriptionIndicatesMissingResource))
        {
            return true;
        }

        // Readers use both M_ParameterError (100) and M_FieldError (101) for an already inactive/disabled
        // resource. Only STOP/DISABLE operations can safely treat those state descriptions as idempotent; a
        // DELETE response with the same wording may indicate a real lifecycle violation and must still fail.
        if (expectedOperation is not ("STOP_ROSPEC" or "DISABLE_ROSPEC" or "DISABLE_ACCESSSPEC"))
        {
            return false;
        }

        return (exception.StatusCode is 100 or 101) && (
            exception.ErrorDescription.Contains("not active", StringComparison.OrdinalIgnoreCase) ||
            exception.ErrorDescription.Contains("only an active", StringComparison.OrdinalIgnoreCase) ||
            exception.ErrorDescription.Contains("not enabled", StringComparison.OrdinalIgnoreCase) ||
            exception.ErrorDescription.Contains("disabled", StringComparison.OrdinalIgnoreCase));
    }

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
                    if (LlrpProtocolMessageFactory.IsKeepalive(message))
                    {
                        PublishKeepaliveReceived();
                        ILlrpMessage acknowledgementMessage = LlrpProtocolMessageFactory.CreateKeepaliveAck(
                            NegotiatedVersion,
                            message.MessageId);
                        byte[] acknowledgement = _registry.EncodeMessage(
                            NegotiatedVersion,
                            acknowledgementMessage);
                        await _session
                            .SendFrameAsync(acknowledgement, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    else if (LlrpProtocolMessageFactory.IsCloseConnection(message))
                    {
                        Volatile.Write(ref _deviceInitiatedClose, 1);
                        await SendCloseConnectionAcknowledgmentAsync(message.MessageId, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    foreach (ReaderEventProjection projection in ReaderEventProjector.Project(message))
                    {
                        HandleEventProjection(projection);
                    }

                    foreach (TranslatedTagReport translatedReport in GetProtocolAdapter().TranslateTagReports(message))
                    {
                        TagReport tagReport = ApplyTagReportContributors(translatedReport);
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
            CompleteActiveInventorySession();
            ResetConnectionResourceState();
            InvalidateMetadata();
            AddTransition(
                transitions,
                ReaderConnectionState.Faulted,
                failure,
                Volatile.Read(ref _deviceInitiatedClose) != 0);
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

    private async Task SendCloseConnectionAcknowledgmentAsync(
        uint messageId,
        CancellationToken cancellationToken)
    {
        ILlrpMessage responseMessage = LlrpProtocolMessageFactory.CreateCloseConnectionResponse(
            NegotiatedVersion,
            messageId);
        try
        {
            byte[] responseFrame = _registry.EncodeMessage(NegotiatedVersion, responseMessage);
            await _session.SendFrameAsync(responseFrame, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Best-effort acknowledgment: the reader may close the TCP connection immediately after sending
            // CLOSE_CONNECTION, so a failed reply must not surface as an additional pump failure.
            _logger.LogDebug(
                exception,
                "Failed to acknowledge a reader-initiated CLOSE_CONNECTION for connection {ConnectionId}",
                ConnectionId);
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

    private ILlrpProtocolAdapter GetProtocolAdapter() => Volatile.Read(ref _protocolAdapter);

    internal void SelectProtocolAdapter(LlrpProtocolVersion version)
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
                "The reader resource observation is not synchronized. Call " +
                $"{nameof(SynchronizeStateAsync)} when an up-to-date observation is required.");
        }
    }

    private static void ValidateTakeoverPolicy(ResourceTakeoverPolicy takeoverPolicy)
    {
        if (takeoverPolicy is not ResourceTakeoverPolicy.PreserveForeign and not ResourceTakeoverPolicy.ReplaceAll)
        {
            throw new ArgumentOutOfRangeException(nameof(takeoverPolicy), takeoverPolicy, "Unknown resource takeover policy.");
        }
    }

    private static bool IsNonResourceAffectingTypedMessage(ILlrpMessage message)
    {
        string name = message.GetType().Name;
        return name.StartsWith("GET_", StringComparison.Ordinal) || name switch
        {
            "KEEPALIVE" or
            "KEEPALIVE_ACK" or
            "CLOSE_CONNECTION" or
            "CLOSE_CONNECTION_RESPONSE" => true,
            _ => false,
        };
    }

    private static bool InventorySettingsMatch(InventorySettings desired, InventorySettings observed)
    {
        if (ReferenceEquals(desired, observed))
        {
            return true;
        }

        try
        {
            return string.Equals(
                JsonSerializer.Serialize(desired),
                JsonSerializer.Serialize(observed),
                StringComparison.Ordinal);
        }
        catch (JsonException)
        {
            // If an extension value cannot be compared safely, redeployment is the conservative recovery path.
            return false;
        }
        catch (NotSupportedException)
        {
            // If an extension value cannot be compared safely, redeployment is the conservative recovery path.
            return false;
        }
    }

    private void MarkResourceObservationStale()
    {
        CompleteActiveInventorySession();
        Volatile.Write(ref _observedManagedResourcePresent, 0);
        Volatile.Write(ref _observedManagedInventoryState, (int)InventoryRuntimeState.Disabled);
        Volatile.Write(ref _operationState, (int)ReaderOperationState.Idle);
        Volatile.Write(ref _managedStateIsSynchronized, 0);
        Volatile.Write(ref _observedResourceState, (int)ReaderObservedState.Stale);
        Volatile.Write(ref _resourceMode, (int)ReaderResourceMode.StateUnknown);
    }

    /// <summary>Re-queries all reader capabilities and replaces the initialized capability snapshot.</summary>
    public async Task<ReaderCapabilities> RefreshCapabilitiesAsync(CancellationToken cancellationToken = default)
    {
        EnsureProtocolAvailable();
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ReaderCapabilities capabilities = await GetProtocolAdapter()
                .FetchCapabilitiesAsync(this, _messageIds.Next(), cancellationToken)
                .ConfigureAwait(false);
            ReaderMetadataSnapshot metadata = Volatile.Read(ref _metadata) ?? throw new InvalidOperationException(
                "Reader metadata is unavailable. Connect the reader first.");
            Volatile.Write(ref _metadata, new ReaderMetadataSnapshot(metadata.Identity, capabilities));
            return capabilities;
        }
        finally
        {
            _operationLock.Release();
        }
    }

    private void PrepareForManagedTakeover()
    {
        CompleteActiveInventorySession();
        Volatile.Write(ref _observedManagedResourcePresent, 0);
        Volatile.Write(ref _observedManagedInventoryState, (int)InventoryRuntimeState.Disabled);
        Volatile.Write(ref _operationState, (int)ReaderOperationState.Idle);
        Volatile.Write(ref _managedStateIsSynchronized, 0);
        Volatile.Write(ref _observedResourceState, (int)ReaderObservedState.Stale);
        Volatile.Write(ref _resourceMode, (int)ReaderResourceMode.StateUnknown);
    }

    internal async Task ExecuteExpertResourceOperationAsync(Func<Task> operation, CancellationToken cancellationToken)
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
            bool operationInvoked = false;
            try
            {
                operationInvoked = true;
                await operation().ConfigureAwait(false);
            }
            finally
            {
                // A request may have reached the device even when its response is lost. Treat every
                // externally issued resource write as stale on both success and failure; internal
                // scopes are reconciled by their owning high-level operation instead.
                if (operationInvoked && _internalResourceOperationDepth.Value == 0)
                {
                    MarkResourceObservationStale();
                }
            }
        }
        finally
        {
            _operationLock.Release();
        }
    }

    internal async Task<IReadOnlyList<ILlrpParameter>> ExecuteExpertResourceQueryAsync(
        Func<Task<IReadOnlyList<ILlrpParameter>>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (_internalResourceOperationDepth.Value > 0)
        {
            EnsureProtocolAvailable();
            return await operation().ConfigureAwait(false);
        }

        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureProtocolAvailable();
            return await operation().ConfigureAwait(false);
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

    private void CompleteActiveInventorySession()
    {
        InventorySession? session = _inventorySession;
        _inventorySession = null;
        session?.Complete(InventoryRuntimeState.Disabled);
    }

    private void SetDesiredInventorySettings(InventorySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Volatile.Write(ref _currentInventorySettings, settings);
        _managedInventoryRoSpecId = ManagedInventoryRoSpecId;
        _managedInventoryAttachedDataAccessSpecId = settings.AttachedData.Enabled
            ? ManagedInventoryAttachedDataAccessSpecId
            : null;
        ReaderSettings? desired = Volatile.Read(ref _desiredReaderSettings);
        Volatile.Write(ref _desiredReaderSettings, new ReaderSettings
        {
            Configuration = desired?.Configuration ?? new ReaderConfiguration(),
            Inventory = settings,
            Extensions = desired?.Extensions ?? new Dictionary<string, object?>(),
        });
    }

    private void ClearDesiredManagedState()
    {
        _managedInventoryRoSpecId = null;
        _managedInventoryAttachedDataAccessSpecId = null;
        Volatile.Write(ref _currentInventorySettings, null);
        ReaderSettings? desired = Volatile.Read(ref _desiredReaderSettings);
        Volatile.Write(ref _desiredReaderSettings, desired is null ? null : desired with { Inventory = null });
        Volatile.Write(ref _observedManagedResourcePresent, 0);
        Volatile.Write(ref _observedManagedInventoryState, (int)InventoryRuntimeState.Disabled);
        Volatile.Write(ref _operationState, (int)ReaderOperationState.Idle);
    }

    private void ResetConnectionResourceState()
    {
        CompleteActiveInventorySession();
        Volatile.Write(ref _observedManagedResourcePresent, 0);
        Volatile.Write(ref _observedManagedInventoryState, (int)InventoryRuntimeState.Disabled);
        Volatile.Write(ref _managedStateIsSynchronized, 0);
        Volatile.Write(ref _observedResourceState, (int)ReaderObservedState.Unknown);
        Volatile.Write(ref _operationState, (int)ReaderOperationState.Idle);
        Volatile.Write(ref _resourceMode, (int)ReaderResourceMode.Idle);
        Volatile.Write(ref _lastResourceSnapshot, null);
        if (CurrentInventorySettings is null)
        {
            _managedInventoryRoSpecId = null;
            _managedInventoryAttachedDataAccessSpecId = null;
        }
    }

    private void StoreResourceSnapshot(
        IReadOnlyList<ILlrpParameter> roSpecs,
        IReadOnlyList<ILlrpParameter> accessSpecs)
    {
        bool HasForeignRoSpec(ILlrpParameter item)
        {
            try { return GetProtocolAdapter().GetRoSpecId(item) != ManagedInventoryRoSpecId; }
            catch (ArgumentException) { return true; }
        }

        bool HasForeignAccessSpec(ILlrpParameter item)
        {
            try { return GetProtocolAdapter().GetAccessSpecId(item) != ManagedInventoryAttachedDataAccessSpecId; }
            catch (ArgumentException) { return true; }
        }

        bool hasManaged = roSpecs.Any(GetProtocolAdapter().IsManagedRoSpec);
        bool hasForeign = roSpecs.Any(HasForeignRoSpec) || accessSpecs.Any(HasForeignAccessSpec);
        Volatile.Write(ref _lastResourceSnapshot, new ReaderResourceSnapshot(
            roSpecs.ToArray(),
            accessSpecs.ToArray(),
            hasManaged,
            hasForeign,
            DateTimeOffset.UtcNow));
    }

    private void AdoptManagedInventorySnapshot(
        ILlrpParameter? managedRoSpec,
        IReadOnlyList<ILlrpParameter> accessSpecs,
        ManagedRoSpecSnapshot? snapshot = null)
    {
        if (managedRoSpec is null)
        {
            if (_inventorySession is { OwnsResource: true })
            {
                CompleteActiveInventorySession();
            }
            Volatile.Write(ref _observedManagedResourcePresent, 0);
            Volatile.Write(ref _observedManagedInventoryState, (int)InventoryRuntimeState.Disabled);
            // A query is a device-fact observation. If the reserved ROSpec is absent, do not
            // retain an old CurrentInventorySettings snapshot as though it were still deployed.
            Volatile.Write(ref _currentInventorySettings, null);
            Volatile.Write(ref _operationState, (int)(ResourceMode == ReaderResourceMode.AttachedInventory
                ? ReaderOperationState.Inventorying
                : ReaderOperationState.Idle));
            if (ResourceMode != ReaderResourceMode.AttachedInventory)
            {
                Volatile.Write(ref _resourceMode, (int)ReaderResourceMode.Idle);
            }
            return;
        }

        ManagedRoSpecSnapshot actual = snapshot ?? GetProtocolAdapter().ParseManagedRoSpec(this, managedRoSpec, accessSpecs);
        _managedInventoryRoSpecId = ManagedInventoryRoSpecId;
        _managedInventoryAttachedDataAccessSpecId = GetProtocolAdapter().HasAttachedDataAccessSpec(accessSpecs)
            ? ManagedInventoryAttachedDataAccessSpecId
            : null;
        Volatile.Write(ref _currentInventorySettings, actual.Inventory);
        bool running = actual.State == InventoryRuntimeState.Running;
        Volatile.Write(ref _observedManagedResourcePresent, 1);
        Volatile.Write(ref _observedManagedInventoryState, (int)actual.State);
        Volatile.Write(ref _operationState, (int)(running ? ReaderOperationState.Inventorying : ReaderOperationState.Idle));
        if (ResourceMode != ReaderResourceMode.AttachedInventory)
        {
            Volatile.Write(ref _resourceMode, (int)(running ? ReaderResourceMode.HighLevelRunning : ReaderResourceMode.HighLevelConfigured));
        }
    }

    private async Task StopManagedInventoryAsync(uint roSpecId, CancellationToken cancellationToken)
    {
        Exception? failure = null;
        try
        {
            await RoSpecs.StopAsync(roSpecId, cancellationToken).ConfigureAwait(false);
        }
        catch (LlrpReaderOperationException exception) when (IsIgnorableOwnedCleanupError(exception, "STOP_ROSPEC"))
        {
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
            catch (LlrpReaderOperationException exception) when (IsIgnorableOwnedCleanupError(exception, "DISABLE_ACCESSSPEC"))
            {
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
        catch (LlrpReaderOperationException exception) when (IsIgnorableOwnedCleanupError(exception, "DISABLE_ROSPEC"))
        {
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
        // 14151 is reserved by the managed control plane even when the last observed
        // settings snapshot did not contain AttachedData (for example after an expert
        // write or reconnect). Clear must therefore always target the fixed identifier.
        try { await AccessSpecs.DeleteAsync(ManagedInventoryAttachedDataAccessSpecId, cancellationToken).ConfigureAwait(false); }
        catch (LlrpReaderOperationException exception) when (IsIgnorableOwnedCleanupError(exception, "DELETE_ACCESSSPEC")) { }
        catch (Exception exception) { failure ??= exception; }
        if (_managedInventoryAttachedDataAccessSpecId is uint attachedDataId && attachedDataId != ManagedInventoryAttachedDataAccessSpecId)
        {
            try { await AccessSpecs.DeleteAsync(attachedDataId, cancellationToken).ConfigureAwait(false); }
            catch (LlrpReaderOperationException exception) when (IsIgnorableOwnedCleanupError(exception, "DELETE_ACCESSSPEC")) { }
            catch (Exception exception) { failure ??= exception; }
        }
        try { await RoSpecs.DeleteAsync(roSpecId, cancellationToken).ConfigureAwait(false); }
        catch (LlrpReaderOperationException exception) when (IsIgnorableOwnedCleanupError(exception, "DELETE_ROSPEC")) { }
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
            !uint.TryParse(options.AccessPassword, System.Globalization.NumberStyles.AllowHexSpecifier, System.Globalization.CultureInfo.InvariantCulture, out _))
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
            AccessPassword = options.AccessPassword,
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

    private void AddTransition(
        ICollection<StateTransition> transitions,
        ReaderConnectionState newState,
        Exception? error = null,
        bool deviceInitiatedClose = false)
    {
        ReaderConnectionState previousState = ConnectionState;
        if (previousState == newState)
        {
            return;
        }

        Volatile.Write(ref _connectionState, (int)newState);
        transitions.Add(new StateTransition(previousState, newState, error, deviceInitiatedClose));
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
                        transition.Error,
                        transition.DeviceInitiatedClose));
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
        TagReportWaiter[] waiters;
        TagReportDeliveryOwner owner;
        EventHandler<TagReportEventArgs>? observer;
        InventorySession? session;
        lock (_tagReportDeliveryGate)
        {
            waiters = _tagReportWaiters.ToArray();
            owner = _tagReportDeliveryOwner;
            observer = _tagsReported;
            session = _inventorySession;
        }

        foreach (TagReportWaiter waiter in waiters)
        {
            if (!waiter.Predicate(report))
            {
                continue;
            }

            try { waiter.OnMatch(report); }
            catch (Exception exception)
            {
                _logger.LogError(exception, "A tag-report waiter failed for connection {ConnectionId}", ConnectionId);
            }
        }

        if (owner == TagReportDeliveryOwner.Event)
        {
            try
            {
                observer?.Invoke(this, new TagReportEventArgs(report));
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "A reader tag-report event subscriber failed for connection {ConnectionId}",
                    ConnectionId);
            }
            return;
        }

        if (owner == TagReportDeliveryOwner.ReaderAsync)
        {
            if (_tagReports.Reader.Count >= Options.IncomingMessageCapacity)
            {
                long dropped = Interlocked.Increment(ref _tagReportsDropped);
                try
                {
                    TagReportsDropped?.Invoke(
                        this,
                        new TagReportOverflowEventArgs(Options.IncomingMessageCapacity, dropped));
                }
                catch (Exception exception)
                {
                    _logger.LogError(
                        exception,
                        "A tag-report overflow event subscriber failed for connection {ConnectionId}",
                        ConnectionId);
                }
            }

            _tagReports.Writer.TryWrite(report);
            return;
        }

        // ROSpecID is optional in an RO_ACCESS_REPORT. A reader may legitimately omit it when
        // ROReportSpec.EnableROSpecID is false. An attached expert session cannot claim such a
        // report while another ROSpec may be running, so it is allowed only when the last complete
        // snapshot proves that the attached ROSpec is the sole running ROSpec. Managed sessions retain
        // the historical SDK-owned routing rule; an explicit ROSpecID is always matched strictly.
        bool matchesManagedRoSpec = report.RoSpecId == session?.RoSpecId ||
            (report.RoSpecId is null &&
                (session?.OwnsResource != false
                    ? ResourceMode == ReaderResourceMode.HighLevelRunning
                    : session is not null && CanRouteMissingRoSpecId(session)));
        if (session is not null &&
            (owner == TagReportDeliveryOwner.Session || owner == TagReportDeliveryOwner.None) &&
            matchesManagedRoSpec &&
            (report.AccessSpecId is null or 0 || report.AccessSpecId == session.AttachedDataAccessSpecId))
        {
            session.Publish(report);
        }
    }

    private bool CanRouteMissingRoSpecId(InventorySession session)
    {
        if (ObservedState != ReaderObservedState.Synchronized || LastResourceSnapshot is not { } snapshot)
        {
            return false;
        }

        int runningCount = 0;
        bool targetIsRunning = false;
        foreach (ILlrpParameter roSpec in snapshot.RoSpecs)
        {
            uint id;
            InventoryRuntimeState state;
            try
            {
                id = GetProtocolAdapter().GetRoSpecId(roSpec);
                state = GetProtocolAdapter().GetRoSpecRuntimeState(roSpec);
            }
            catch (ArgumentException)
            {
                // An unknown ROSpec cannot be proven inactive; fail closed for an ID-less report.
                return false;
            }

            if (state != InventoryRuntimeState.Running)
            {
                continue;
            }

            runningCount++;
            targetIsRunning |= id == session.RoSpecId;
        }

        return targetIsRunning && runningCount == 1;
    }

    private void ProcessManagedRoSpecEvent(uint? roSpecId, InventoryRuntimeState? state)
    {
        if (state is not { } nextState)
        {
            return;
        }

        InventorySession? session = _inventorySession;
        if (session is { OwnsResource: false } attached && attached.RoSpecId == roSpecId)
        {
            attached.SetState(nextState);
            if (nextState == InventoryRuntimeState.Disabled)
            {
                attached.Complete(InventoryRuntimeState.Disabled);
                _inventorySession = null;
                Volatile.Write(ref _operationState, (int)ReaderOperationState.Idle);
                Volatile.Write(ref _resourceMode, (int)ReaderResourceMode.Idle);
            }
            else
            {
                Volatile.Write(ref _operationState, (int)ReaderOperationState.Inventorying);
                Volatile.Write(ref _resourceMode, (int)ReaderResourceMode.AttachedInventory);
            }
            return;
        }

        if (roSpecId != ManagedInventoryRoSpecId)
        {
            return;
        }

        Volatile.Write(ref _observedManagedResourcePresent, 1);
        Volatile.Write(ref _observedManagedInventoryState, (int)nextState);
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

    private void HandleEventProjection(ReaderEventProjection projection)
    {
        switch (projection)
        {
            case ManagedRoSpecEventProjection roSpecEvent:
                ProcessManagedRoSpecEvent(roSpecEvent.RoSpecId, roSpecEvent.State);
                break;
            case GpiChangedEventProjection gpi:
                PublishGpiChanged(gpi.PortNumber, gpi.State);
                break;
            case AntennaChangedEventProjection antenna:
                PublishAntennaChanged(antenna.AntennaId, antenna.IsConnected);
                break;
            case ReportBufferOverflowEventProjection:
                PublishReportBufferOverflow();
                break;
            case ReportBufferWarningEventProjection warning:
                PublishReportBufferWarning(warning.PercentageFull);
                break;
            case ReaderExceptionEventProjection readerException:
                PublishReaderException(readerException);
                break;
            default:
                throw new InvalidOperationException($"Unsupported reader event projection '{projection.GetType().Name}'.");
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

    private void PublishReaderException(ReaderExceptionEventProjection exception)
    {
        try
        {
            ReaderExceptionOccurred?.Invoke(
                this,
                new ReaderExceptionEventArgs(
                    exception.Message,
                    exception.RoSpecId,
                    exception.SpecIndex,
                    exception.InventoryParameterSpecId,
                    exception.AntennaId,
                    exception.AccessSpecId,
                    exception.OpSpecId));
        }
        catch (Exception subscriberFailure)
        {
            _logger.LogError(
                subscriberFailure,
                "A reader exception event subscriber failed for connection {ConnectionId}",
                ConnectionId);
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
        Exception? Error,
        bool DeviceInitiatedClose);

}
