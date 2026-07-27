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

namespace LlrpSdk;

/// <summary>
/// Represents one reader connection and owns its transport, LLRP session, protocol registry, and unsolicited-message pump.
/// </summary>
public sealed class LlrpReader : IAsyncDisposable
{
    private readonly Channel<ILlrpMessage> _messages;
    private readonly Channel<TagReport> _tagReports;
    private readonly object _automaticReconnectGate = new();
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private readonly ILogger<LlrpReader> _logger;
    private readonly LlrpMessageIdGenerator _messageIds = new();
    private readonly ReaderExtensionCollection _extensions = new();
    private readonly LlrpCodecRegistry _registry;
    private readonly IReadOnlyDictionary<LlrpProtocolVersion, ILlrpProtocolAdapter> _protocolAdapters;
    private ILlrpProtocolAdapter _protocolAdapter;
    private readonly LlrpSession _session;
    private CancellationTokenSource? _pumpCancellation;
    private Task? _pumpTask;
    private CancellationTokenSource? _automaticReconnectCancellation;
    private Task? _automaticReconnectTask;
    private ReaderSettings? _currentSettings;
    private ReaderMetadataSnapshot? _metadata;
    private uint? _managedInventoryRoSpecId;
    private int _nextManagedAccessSpecId = 24000;
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
    /// Returns the SDK-recommended configuration baseline for this initialized reader without sending an LLRP request.
    /// </summary>
    /// <remarks>
    /// The result is neither the reader's current configuration nor its persistent configuration. It never applies
    /// settings; callers must explicitly call <see cref="ApplySettingsAsync(ReaderConfiguration, CancellationToken)"/>
    /// to write a configuration.
    /// </remarks>
    public ReaderConfiguration GetDefaultConfiguration()
    {
        return GetDefaultConfigurationResult().Configuration;
    }

    /// <summary>
    /// Resolves the SDK-recommended configuration baseline and exposes the provider/profile that produced it.
    /// </summary>
    /// <returns>The non-writing resolved configuration and source information.</returns>
    public ReaderConfigurationDefaultsResult GetDefaultConfigurationResult()
    {
        ThrowIfDisposed();
        ReaderMetadataSnapshot metadata = Volatile.Read(ref _metadata) ?? throw new InvalidOperationException(
            "Reader configuration defaults require an initialized connection.");
        if (!IsConnected)
        {
            throw new InvalidOperationException("Reader configuration defaults require a ready connection.");
        }

        var context = new ReaderConfigurationProfileContext(
            metadata.Identity,
            metadata.Capabilities,
            NegotiatedVersion,
            Extensions.Select(static extension => extension.Id).ToArray());
        IReaderConfigurationDefaultsProvider[] providers = Options.ConfigurationDefaultsProviders
            .Concat(Extensions.OfType<IReaderConfigurationDefaultsProvider>())
            .GroupBy(static provider => provider.Id, StringComparer.Ordinal)
            .Select(static group => group.Count() == 1
                ? group.Single()
                : throw new InvalidOperationException(
                    $"Configuration defaults provider '{group.Key}' is registered more than once for this reader."))
            .ToArray();
        (string ProviderId, ReaderConfigurationProfile Profile)[] profiles = providers
            .Select(provider => (ProviderId: provider.Id, Profile: provider.GetProfile(context)))
            .Where(static candidate => candidate.Profile is not null)
            .Select(static candidate => (candidate.ProviderId, candidate.Profile!))
            .ToArray();

        ReaderConfiguration baseline = CreateSafeDefaultConfiguration();
        if (profiles.Length == 0)
        {
            return new ReaderConfigurationDefaultsResult(baseline, null, null);
        }

        int highestPriority = profiles.Max(static candidate => candidate.Profile.Priority);
        (string ProviderId, ReaderConfigurationProfile Profile)[] selected = profiles
            .Where(candidate => candidate.Profile.Priority == highestPriority)
            .ToArray();
        if (selected.Length != 1)
        {
            throw new InvalidOperationException(
                $"Configuration default profiles '{string.Join("', '", selected.Select(static candidate => candidate.Profile.Id))}' " +
                $"all match at priority {highestPriority}.");
        }

        return new ReaderConfigurationDefaultsResult(
            selected[0].Profile.Patch.ApplyTo(baseline),
            selected[0].ProviderId,
            selected[0].Profile.Id);
    }

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
    /// Queries the current physical reader settings and configurations.
    /// </summary>
    /// <param name="cancellationToken">Cancels the query operation.</param>
    /// <returns>The high-level reader configuration snapshot.</returns>
    public async Task<ReaderConfiguration> QuerySettingsAsync(
        CancellationToken cancellationToken = default)
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

    /// <summary>
    /// Queries the current configuration and applies a partial change in memory without writing the reader.
    /// </summary>
    /// <param name="patch">The explicit fields to replace.</param>
    /// <param name="cancellationToken">Cancels the query operation.</param>
    /// <returns>The complete configuration that would be written by <see cref="ApplyConfigurationPatchAsync"/>.</returns>
    public async Task<ReaderConfiguration> ResolveConfigurationPatchAsync(
        ReaderConfigurationPatch patch,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(patch);
        ReaderConfiguration current = await QuerySettingsAsync(cancellationToken).ConfigureAwait(false);
        return patch.ApplyTo(current);
    }

    /// <summary>
    /// Applies an explicit partial configuration change while preserving all fields returned by the current reader query.
    /// </summary>
    /// <param name="patch">The explicit fields to replace.</param>
    /// <param name="cancellationToken">Cancels either the query or apply operation.</param>
    /// <returns>A task representing the query, merge, and apply operations.</returns>
    /// <remarks>
    /// This method writes the reader. Use <see cref="ResolveConfigurationPatchAsync"/> first to display or inspect
    /// the complete resolved configuration when an application requires approval before the write.
    /// </remarks>
    public async Task ApplyConfigurationPatchAsync(
        ReaderConfigurationPatch patch,
        CancellationToken cancellationToken = default)
    {
        ReaderConfiguration resolved = await ResolveConfigurationPatchAsync(patch, cancellationToken)
            .ConfigureAwait(false);
        await ApplySettingsAsync(resolved, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Applies specific configuration settings to the physical reader.
    /// </summary>
    /// <param name="configuration">The settings to apply.</param>
    /// <param name="cancellationToken">Cancels the application operation.</param>
    /// <returns>A task representing the apply operation.</returns>
    public async Task ApplySettingsAsync(
        ReaderConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        EnsureProtocolAvailable();

        ILlrpProtocolAdapter adapter = GetProtocolAdapter();
        uint messageId = _messageIds.Next();
        IReadOnlyList<ILlrpParameter> customItems = BuildSettingsApplyParameters(configuration);
        await adapter
            .ApplyConfigurationAsync(this, messageId, configuration, customItems, cancellationToken)
            .ConfigureAwait(false);
        InvalidateManagedStateAfterRawProtocolAccess();
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
                try
                {
                    await RoSpecs.StopAsync(settings.RoSpecId, cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    // Best-effort pre-cleanup if ROSpec is already Active
                }

                try
                {
                    await RoSpecs.DisableAsync(settings.RoSpecId, cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    // Best-effort pre-cleanup if ROSpec is already Enabled
                }

                try
                {
                    await RoSpecs.DeleteAsync(settings.RoSpecId, cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    // Best-effort pre-cleanup if ROSpec already exists
                }

                ILlrpParameter roSpec = CompileDefaultInventoryRoSpec(settings);
                await RoSpecs.AddAsync(roSpec, cancellationToken).ConfigureAwait(false);
                added = true;
                await RoSpecs.EnableAsync(settings.RoSpecId, cancellationToken).ConfigureAwait(false);
                enabled = true;

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

    /// <summary>
    /// Compiles the SDK-default inventory ROSpec without changing managed operation state.
    /// </summary>
    /// <remarks>
    /// This is used by <see cref="IRoSpecService.AddDefaultAsync"/> for callers that need to create a
    /// disabled default resource and control its lifecycle explicitly.
    /// </remarks>
    internal ILlrpParameter CompileDefaultInventoryRoSpec(ReaderSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        EnsureProtocolAvailable();
        return GetProtocolAdapter().CompileInventory(settings, BuildInventoryCustomItems(settings));
    }

    /// <summary>Reads one selected tag's standard C1G2 memory through the active SDK-managed inventory operation.</summary>
    public Task<TagAccessResult> ReadTagMemoryAsync(
        ReadTagRequest request,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) =>
        ExecuteTagAccessAsync(request, timeout, cancellationToken);

    /// <summary>Writes one selected tag's standard C1G2 memory through the active SDK-managed inventory operation.</summary>
    public Task<TagAccessResult> WriteTagMemoryAsync(
        WriteTagRequest request,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) =>
        ExecuteTagAccessAsync(request, timeout, cancellationToken);

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

    private async Task<TagAccessResult> ExecuteTagAccessAsync(
        TagAccessRequest request,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureProtocolAvailable();
        if (timeout.HasValue && timeout.Value <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "Tag access timeout must be positive when specified.");
        }

        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureProtocolAvailable();
            if (OperationState != ReaderOperationState.Inventorying || _managedInventoryRoSpecId is not uint roSpecId)
            {
                throw new InvalidOperationException(
                    "Tag access requires an active SDK-managed inventory operation. Call StartAsync before issuing tag access.");
            }

            uint accessSpecId = NextManagedAccessSpecId();
            ILlrpParameter accessSpec = GetProtocolAdapter().CompileTagAccess(accessSpecId, roSpecId, request);
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
                    .FirstOrDefault(static result => result.OpSpecId == 1);
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
            }
        }
        finally
        {
            _operationLock.Release();
        }
    }

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
                    if (message is V101Messages.KEEPALIVE or V11Messages.KEEPALIVE)
                    {
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

    private static ReaderConfiguration CreateSafeDefaultConfiguration() => new()
    {
        Keepalive = new KeepaliveConfiguration
        {
            TriggerType = KeepaliveTriggerType.None,
            IntervalMs = 0
        },
        Antennas = Array.Empty<AntennaConfigurationSettings>(),
        Gpos = Array.Empty<GpoConfiguration>(),
        Gpis = Array.Empty<GpiStatus>(),
        Events = new EventNotificationConfiguration()
    };

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

    private IReadOnlyList<ILlrpParameter> BuildInventoryCustomItems(ReaderSettings settings)
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

        return values.RoReportSpecCustomItems;
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
