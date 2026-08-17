using System.Net;
using LlrpDevice.Abstractions;
using LlrpDevice.Server;
using LlrpDevice.Virtual;
using LlrpNet.Core.Protocol;
using LlrpVirtualReader;

namespace LlrpVirtualReader.Manager;

/// <summary>Names of the built-in virtual-reader presets.</summary>
public static class VirtualReaderPresetIds
{
    public const string Standard101Basic = "llrp.standard101.basic";
    public const string Standard101Strict = "llrp.standard101.strict";
    public const string Standard101TagAccess = "llrp.standard101.tag-access";
    public const string Standard11Basic = "llrp.standard11.basic";
    public const string RequestTimeoutFault = "llrp.fault.request-timeout";
    public const string StatusErrorFault = "llrp.fault.status-error";
    public const string DeviceDisconnectFault = "llrp.fault.device-disconnect";
}

/// <summary>Describes a manager-owned virtual-reader instance before it is created.</summary>
public sealed record VirtualReaderInstanceOptions
{
    /// <summary>Gets the stable instance identifier; when omitted the manager generates one.</summary>
    public string? InstanceId { get; init; }

    /// <summary>Gets the display name exposed by the reader.</summary>
    public string Name { get; init; } = "Virtual Reader";

    /// <summary>Gets the registered preset contributor used to build the host.</summary>
    public string PresetId { get; init; } = VirtualReaderPresetIds.Standard101Basic;

    /// <summary>Gets the exact local address to bind.</summary>
    public IPAddress ListenAddress { get; init; } = IPAddress.Loopback;

    /// <summary>Gets the exact port to bind. Zero is supported for in-process allocation.</summary>
    public int Port { get; init; }

    /// <summary>Gets device options layered by the selected preset.</summary>
    public VirtualReaderOptions ReaderOptions { get; init; } = new();

    /// <summary>Gets protocol modules contributed by the caller.</summary>
    public IReadOnlyList<IVirtualReaderProtocolModule> ProtocolModules { get; init; } = [];

    /// <summary>Gets protocol modules for the generic device Server.</summary>
    public IReadOnlyList<ILlrpDeviceProtocolModule> DeviceProtocolModules { get; init; } = [];
}

/// <summary>Builds one host configuration for a registered Manager preset.</summary>
public interface IVirtualReaderPresetContributor
{
    /// <summary>Gets the stable preset identifier.</summary>
    public string Id { get; }

    /// <summary>Gets a human-readable description.</summary>
    public string Description { get; }

    /// <summary>Builds the exact single-host options for an instance.</summary>
    public VirtualReaderHostOptions Build(VirtualReaderInstanceOptions options);
}

/// <summary>
/// Optional next-generation preset contract used by the Manager's device/server split.
/// Existing contributors that only implement <see cref="IVirtualReaderPresetContributor"/>
/// remain supported through the compatibility Host path.
/// </summary>
public interface ILlrpDevicePresetContributor
{
    public LlrpDeviceServerOptions BuildServerOptions(VirtualReaderInstanceOptions options);

    public VirtualDeviceOptions BuildDeviceOptions(VirtualReaderInstanceOptions options);
}

/// <summary>Stores the available virtual-reader preset contributors.</summary>
public sealed class VirtualReaderPresetCatalog
{
    private readonly Dictionary<string, IVirtualReaderPresetContributor> _contributors =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates a catalog containing the standard and fault-injection presets.</summary>
    public VirtualReaderPresetCatalog(IEnumerable<IVirtualReaderPresetContributor>? contributors = null)
    {
        RegisterBuiltIns();
        if (contributors is not null)
        {
            foreach (IVirtualReaderPresetContributor contributor in contributors)
            {
                Register(contributor);
            }
        }
    }

    /// <summary>Gets the registered preset descriptions.</summary>
    public IReadOnlyList<IVirtualReaderPresetContributor> Presets =>
        _contributors.Values.OrderBy(static contributor => contributor.Id, StringComparer.OrdinalIgnoreCase).ToArray();

    /// <summary>Registers a contributor; duplicate identifiers are rejected deterministically.</summary>
    public void Register(IVirtualReaderPresetContributor contributor)
    {
        ArgumentNullException.ThrowIfNull(contributor);
        if (string.IsNullOrWhiteSpace(contributor.Id))
        {
            throw new ArgumentException("A virtual-reader preset identifier is required.", nameof(contributor));
        }

        if (!_contributors.TryAdd(contributor.Id, contributor))
        {
            throw new InvalidOperationException($"Virtual-reader preset '{contributor.Id}' is already registered.");
        }
    }

    /// <summary>Gets one contributor by stable identifier.</summary>
    public IVirtualReaderPresetContributor Get(string presetId)
    {
        if (string.IsNullOrWhiteSpace(presetId))
        {
            throw new ArgumentException("A virtual-reader preset identifier is required.", nameof(presetId));
        }

        return _contributors.TryGetValue(presetId, out IVirtualReaderPresetContributor? contributor)
            ? contributor
            : throw new KeyNotFoundException($"Virtual-reader preset '{presetId}' is not registered.");
    }

    private void RegisterBuiltIns()
    {
        Register(new StandardVirtualReaderPresetContributor(
            VirtualReaderPresetIds.Standard101Basic,
            "LLRP 1.0.1 standard reader with deterministic tag reports.",
            LlrpProtocolVersion.Version101));
        Register(new StandardVirtualReaderPresetContributor(
            VirtualReaderPresetIds.Standard101Strict,
            "LLRP 1.0.1 reader with strict AISpec and antenna validation.",
            LlrpProtocolVersion.Version101,
            strict: true));
        Register(new StandardVirtualReaderPresetContributor(
            VirtualReaderPresetIds.Standard101TagAccess,
            "LLRP 1.0.1 reader with deterministic tag-access memory support.",
            LlrpProtocolVersion.Version101));
        Register(new StandardVirtualReaderPresetContributor(
            VirtualReaderPresetIds.Standard11Basic,
            "LLRP 1.1 standard reader with explicit version negotiation.",
            LlrpProtocolVersion.Version11));
        Register(new FaultVirtualReaderPresetContributor(
            VirtualReaderPresetIds.RequestTimeoutFault,
            "Drops ADD_ROSPEC responses to exercise request timeout handling.",
            closeConnection: false));
        Register(new FaultVirtualReaderPresetContributor(
            VirtualReaderPresetIds.StatusErrorFault,
            "Returns M_ParameterError for ADD_ROSPEC requests.",
            closeConnection: false,
            statusError: true));
        Register(new FaultVirtualReaderPresetContributor(
            VirtualReaderPresetIds.DeviceDisconnectFault,
            "Closes the connection after the first GET_READER_CONFIG request.",
            closeConnection: true));
    }
}

/// <summary>Describes a manager instance lifecycle state.</summary>
public enum VirtualReaderInstanceState
{
    Created,
    Starting,
    Running,
    Stopping,
    Stopped,
    Faulted,
    Deleted,
}

/// <summary>Describes why the Manager published an instance change.</summary>
public enum VirtualReaderInstanceChangeKind
{
    Created,
    Started,
    Stopped,
    Restarted,
    Deleted,
    Faulted,
    ClientChanged,
}

/// <summary>Immutable status returned by Manager lifecycle and inspection APIs.</summary>
public sealed record VirtualReaderInstanceInfo(
    string InstanceId,
    string Name,
    string PresetId,
    IPAddress ListenAddress,
    int ConfiguredPort,
    int BoundPort,
    VirtualReaderInstanceState State,
    LlrpProtocolVersion ProtocolVersion,
    int ConnectedClientCount,
    string? LastError);

/// <summary>Event data for Manager instance lifecycle and client changes.</summary>
public sealed class VirtualReaderInstanceChangedEventArgs : EventArgs
{
    public VirtualReaderInstanceChangedEventArgs(
        VirtualReaderInstanceChangeKind kind,
        VirtualReaderInstanceInfo instance)
    {
        Kind = kind;
        Instance = instance;
    }

    /// <summary>Gets the change category.</summary>
    public VirtualReaderInstanceChangeKind Kind { get; }

    /// <summary>Gets the current instance snapshot.</summary>
    public VirtualReaderInstanceInfo Instance { get; }
}

/// <summary>
/// Manages multiple independently configured virtual-reader Hosts in one process.
/// The Manager owns instance identity and lifecycle; protocol behavior remains in Core.
/// </summary>
public sealed class VirtualReaderManager : IAsyncDisposable
{
    private readonly VirtualReaderPresetCatalog _catalog;
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private readonly object _gate = new();
    private readonly Dictionary<string, ManagedInstance> _instances =
        new(StringComparer.OrdinalIgnoreCase);
    private int _disposed;

    /// <summary>Creates a Manager with the built-in catalog and optional custom contributors.</summary>
    public VirtualReaderManager(VirtualReaderPresetCatalog? catalog = null)
    {
        _catalog = catalog ?? new VirtualReaderPresetCatalog();
    }

    /// <summary>Gets the preset catalog used by this Manager.</summary>
    public VirtualReaderPresetCatalog Presets => _catalog;

    /// <summary>Raised after a managed instance or its clients change.</summary>
    public event EventHandler<VirtualReaderInstanceChangedEventArgs>? InstanceChanged;

    /// <summary>Gets a point-in-time list of all non-deleted instances.</summary>
    public IReadOnlyList<VirtualReaderInstanceInfo> Instances
    {
        get
        {
            lock (_gate)
            {
                return _instances.Values
                    .Select(static instance => instance.ToInfo())
                    .OrderBy(static info => info.InstanceId, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
        }
    }

    /// <summary>Creates an inactive instance without binding its endpoint.</summary>
    public async Task<VirtualReaderInstanceInfo> CreateAsync(
        VirtualReaderInstanceOptions options,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(options);
        ValidateInstanceOptions(options);
        await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            string instanceId = options.InstanceId ?? $"vr-{Guid.NewGuid():N}";
            if (string.IsNullOrWhiteSpace(instanceId))
            {
                throw new ArgumentException("A virtual-reader instance identifier is required.", nameof(options));
            }

            lock (_gate)
            {
                if (_instances.ContainsKey(instanceId))
                {
                    throw new InvalidOperationException($"Virtual-reader instance '{instanceId}' already exists.");
                }
            }

            IVirtualReaderPresetContributor contributor = _catalog.Get(options.PresetId);
            ManagedInstance managed = contributor is ILlrpDevicePresetContributor deviceContributor
                ? new ManagedInstance(
                    instanceId,
                    options,
                    contributor,
                    new LlrpDeviceServer(
                        deviceContributor.BuildServerOptions(options),
                        new VirtualLlrpDevice(deviceContributor.BuildDeviceOptions(options))))
                : new ManagedInstance(instanceId, options, contributor, new VirtualReaderHost(contributor.Build(options)));
            AttachHostEvents(managed);
            lock (_gate)
            {
                _instances.Add(instanceId, managed);
            }

            Publish(VirtualReaderInstanceChangeKind.Created, managed);
            return managed.ToInfo();
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    /// <summary>Creates an instance and starts it on its exact configured endpoint.</summary>
    public async Task<VirtualReaderInstanceInfo> CreateAndStartAsync(
        VirtualReaderInstanceOptions options,
        CancellationToken cancellationToken = default)
    {
        VirtualReaderInstanceInfo created = await CreateAsync(options, cancellationToken).ConfigureAwait(false);
        return await StartAsync(created.InstanceId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Starts one created or stopped instance.</summary>
    public async Task<VirtualReaderInstanceInfo> StartAsync(
        string instanceId,
        CancellationToken cancellationToken = default)
    {
        return await RunLifecycleAsync(
            instanceId,
            static instance => instance.StartAsync,
            VirtualReaderInstanceChangeKind.Started,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Stops one running instance without deleting its configuration.</summary>
    public async Task<VirtualReaderInstanceInfo> StopAsync(
        string instanceId,
        CancellationToken cancellationToken = default)
    {
        return await RunLifecycleAsync(
            instanceId,
            static instance => instance.StopAsync,
            VirtualReaderInstanceChangeKind.Stopped,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Restarts one instance while retaining its identity and configuration.</summary>
    public async Task<VirtualReaderInstanceInfo> RestartAsync(
        string instanceId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ManagedInstance instance = GetInstance(instanceId);
            await instance.RestartAsync(cancellationToken).ConfigureAwait(false);
            VirtualReaderInstanceInfo info = instance.ToInfo();
            Publish(VirtualReaderInstanceChangeKind.Restarted, info);
            return info;
        }
        catch (Exception exception)
        {
            PublishFault(instanceId, exception);
            throw;
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    /// <summary>Stops and removes one instance. Its endpoint is released and identity is no longer listed.</summary>
    public async Task DeleteAsync(
        string instanceId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ManagedInstance instance = GetInstance(instanceId);
            await instance.DisposeAsync().ConfigureAwait(false);
            VirtualReaderInstanceInfo deleted = instance.ToInfo() with { State = VirtualReaderInstanceState.Deleted };
            lock (_gate)
            {
                _instances.Remove(instance.InstanceId);
            }

            Publish(VirtualReaderInstanceChangeKind.Deleted, deleted);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    /// <summary>Gets one current instance snapshot or throws when the identity is unknown.</summary>
    public VirtualReaderInstanceInfo Get(string instanceId) => GetInstance(instanceId).ToInfo();

    /// <summary>Tries to get one current instance snapshot.</summary>
    public bool TryGet(string instanceId, out VirtualReaderInstanceInfo instance)
    {
        lock (_gate)
        {
            if (_instances.TryGetValue(instanceId, out ManagedInstance? found))
            {
                instance = found.ToInfo();
                return true;
            }
        }

        instance = default!;
        return false;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        ManagedInstance[] instances;
        lock (_gate)
        {
            instances = _instances.Values.ToArray();
            _instances.Clear();
        }

        foreach (ManagedInstance instance in instances)
        {
            await instance.DisposeAsync().ConfigureAwait(false);
        }

        _lifecycleLock.Dispose();
    }

    private async Task<VirtualReaderInstanceInfo> RunLifecycleAsync(
        string instanceId,
        Func<ManagedInstance, Func<CancellationToken, Task>> operationSelector,
        VirtualReaderInstanceChangeKind changeKind,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        ManagedInstance? instance = null;
        try
        {
            instance = GetInstance(instanceId);
            Func<CancellationToken, Task> operation = operationSelector(instance);
            await operation(cancellationToken).ConfigureAwait(false);
            VirtualReaderInstanceInfo info = instance.ToInfo();
            Publish(changeKind, info);
            return info;
        }
        catch (Exception exception)
        {
            if (instance is not null)
            {
                PublishFault(instance.InstanceId, exception);
            }

            throw;
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    private void AttachHostEvents(ManagedInstance instance)
    {
        if (instance.DeviceServer is not null)
        {
            instance.DeviceServer.LifecycleChanged += (_, args) =>
            {
                VirtualReaderInstanceChangeKind kind = args.CurrentState == LlrpDeviceServerLifecycleState.Faulted
                    ? VirtualReaderInstanceChangeKind.Faulted
                    : args.CurrentState == LlrpDeviceServerLifecycleState.Running
                        ? VirtualReaderInstanceChangeKind.Started
                        : args.CurrentState == LlrpDeviceServerLifecycleState.Stopped
                            ? VirtualReaderInstanceChangeKind.Stopped
                            : VirtualReaderInstanceChangeKind.Created;
                Publish(kind, instance);
            };
            instance.DeviceServer.ClientChanged += (_, _) => Publish(VirtualReaderInstanceChangeKind.ClientChanged, instance);
        }
        else if (instance.LegacyHost is not null)
        {
            instance.LegacyHost.LifecycleChanged += (_, args) =>
            {
                VirtualReaderInstanceChangeKind kind = args.CurrentState == VirtualReaderLifecycleState.Faulted
                    ? VirtualReaderInstanceChangeKind.Faulted
                    : args.CurrentState == VirtualReaderLifecycleState.Running
                        ? VirtualReaderInstanceChangeKind.Started
                        : args.CurrentState == VirtualReaderLifecycleState.Stopped
                            ? VirtualReaderInstanceChangeKind.Stopped
                            : VirtualReaderInstanceChangeKind.Created;
                Publish(kind, instance);
            };
            instance.LegacyHost.ClientChanged += (_, _) => Publish(VirtualReaderInstanceChangeKind.ClientChanged, instance);
        }
    }

    private ManagedInstance GetInstance(string instanceId)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            throw new ArgumentException("A virtual-reader instance identifier is required.", nameof(instanceId));
        }

        lock (_gate)
        {
            return _instances.TryGetValue(instanceId, out ManagedInstance? instance)
                ? instance
                : throw new KeyNotFoundException($"Virtual-reader instance '{instanceId}' was not found.");
        }
    }

    private void Publish(VirtualReaderInstanceChangeKind kind, ManagedInstance instance) => Publish(kind, instance.ToInfo());

    private void Publish(VirtualReaderInstanceChangeKind kind, VirtualReaderInstanceInfo instance)
    {
        try
        {
            InstanceChanged?.Invoke(this, new VirtualReaderInstanceChangedEventArgs(kind, instance));
        }
        catch
        {
            // Manager observers must not be able to break a host lifecycle operation.
        }
    }

    private void PublishFault(string instanceId, Exception exception)
    {
        if (TryGet(instanceId, out VirtualReaderInstanceInfo info))
        {
            Publish(VirtualReaderInstanceChangeKind.Faulted, info with { LastError = exception.Message });
        }
    }

    private static void ValidateInstanceOptions(VirtualReaderInstanceOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Name))
        {
            throw new ArgumentException("A virtual-reader instance name is required.", nameof(options));
        }

        if (options.ListenAddress is null)
        {
            throw new ArgumentNullException(nameof(options.ListenAddress));
        }

        if (options.Port is < 0 or > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(options.Port));
        }

        ArgumentNullException.ThrowIfNull(options.ReaderOptions);
        ArgumentNullException.ThrowIfNull(options.ProtocolModules);
        ArgumentNullException.ThrowIfNull(options.DeviceProtocolModules);
        foreach (IVirtualReaderProtocolModule module in options.ProtocolModules)
        {
            ArgumentNullException.ThrowIfNull(module);
        }

        foreach (ILlrpDeviceProtocolModule module in options.DeviceProtocolModules)
        {
            ArgumentNullException.ThrowIfNull(module);
        }
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(VirtualReaderManager));
        }
    }

    private sealed class ManagedInstance
    {
        public ManagedInstance(
            string instanceId,
            VirtualReaderInstanceOptions options,
            IVirtualReaderPresetContributor contributor,
            VirtualReaderHost host)
        {
            InstanceId = instanceId;
            Options = options;
            Contributor = contributor;
            LegacyHost = host;
        }

        public ManagedInstance(
            string instanceId,
            VirtualReaderInstanceOptions options,
            IVirtualReaderPresetContributor contributor,
            LlrpDeviceServer server)
        {
            InstanceId = instanceId;
            Options = options;
            Contributor = contributor;
            DeviceServer = server;
        }

        public string InstanceId { get; }
        public VirtualReaderInstanceOptions Options { get; }
        public IVirtualReaderPresetContributor Contributor { get; }
        public VirtualReaderHost? LegacyHost { get; }
        public LlrpDeviceServer? DeviceServer { get; }

        public Task StartAsync(CancellationToken cancellationToken) => DeviceServer is not null
            ? DeviceServer.StartAsync(cancellationToken)
            : LegacyHost!.StartAsync(cancellationToken);

        public Task StopAsync(CancellationToken cancellationToken) => DeviceServer is not null
            ? DeviceServer.StopAsync(cancellationToken)
            : LegacyHost!.StopAsync(cancellationToken);

        public Task RestartAsync(CancellationToken cancellationToken) => DeviceServer is not null
            ? DeviceServer.RestartAsync(cancellationToken)
            : LegacyHost!.RestartAsync(cancellationToken);

        public ValueTask DisposeAsync() => DeviceServer is not null
            ? DeviceServer.DisposeAsync()
            : LegacyHost!.DisposeAsync();

        public VirtualReaderInstanceInfo ToInfo()
        {
            if (DeviceServer is not null)
            {
                return new VirtualReaderInstanceInfo(
                    InstanceId,
                    Options.Name,
                    Contributor.Id,
                    Options.ListenAddress,
                    Options.Port,
                    DeviceServer.Port,
                    MapState(DeviceServer.State),
                    DeviceServer.Options.ProtocolVersion,
                    DeviceServer.ConnectedClients.Count,
                    DeviceServer.State == LlrpDeviceServerLifecycleState.Faulted ? "The device server is faulted." : null);
            }

            return new VirtualReaderInstanceInfo(
                InstanceId,
                Options.Name,
                Contributor.Id,
                Options.ListenAddress,
                Options.Port,
                LegacyHost!.Port,
                MapState(LegacyHost.State),
                LegacyHost.Options.ProtocolVersion,
                LegacyHost.ConnectedClients.Count,
                LegacyHost.State == VirtualReaderLifecycleState.Faulted ? "The compatibility virtual-reader host is faulted." : null);
        }

        private static VirtualReaderInstanceState MapState(LlrpDeviceServerLifecycleState state) => state switch
        {
            LlrpDeviceServerLifecycleState.Created => VirtualReaderInstanceState.Created,
            LlrpDeviceServerLifecycleState.Starting => VirtualReaderInstanceState.Starting,
            LlrpDeviceServerLifecycleState.Running => VirtualReaderInstanceState.Running,
            LlrpDeviceServerLifecycleState.Stopping => VirtualReaderInstanceState.Stopping,
            LlrpDeviceServerLifecycleState.Faulted => VirtualReaderInstanceState.Faulted,
            _ => VirtualReaderInstanceState.Stopped,
        };

        private static VirtualReaderInstanceState MapState(VirtualReaderLifecycleState state) => state switch
        {
            VirtualReaderLifecycleState.Created => VirtualReaderInstanceState.Created,
            VirtualReaderLifecycleState.Starting => VirtualReaderInstanceState.Starting,
            VirtualReaderLifecycleState.Running => VirtualReaderInstanceState.Running,
            VirtualReaderLifecycleState.Stopping => VirtualReaderInstanceState.Stopping,
            VirtualReaderLifecycleState.Faulted => VirtualReaderInstanceState.Faulted,
            _ => VirtualReaderInstanceState.Stopped,
        };
    }
}

internal sealed class StandardVirtualReaderPresetContributor : IVirtualReaderPresetContributor, ILlrpDevicePresetContributor
{
    private readonly bool _strict;
    private readonly LlrpProtocolVersion _version;

    public StandardVirtualReaderPresetContributor(
        string id,
        string description,
        LlrpProtocolVersion version,
        bool strict = false)
    {
        Id = id;
        Description = description;
        _version = version;
        _strict = strict;
    }

    public string Id { get; }
    public string Description { get; }

    public VirtualReaderHostOptions Build(VirtualReaderInstanceOptions options) => new()
    {
        ListenAddress = options.ListenAddress,
        Port = options.Port,
        ProtocolModules = options.ProtocolModules,
        ReaderOptions = options.ReaderOptions with
        {
            ReaderName = options.Name,
            ProtocolVersion = _version,
            UseStrictStandardInventoryProfile = options.ReaderOptions.UseStrictStandardInventoryProfile || _strict,
        },
    };

    public LlrpDeviceServerOptions BuildServerOptions(VirtualReaderInstanceOptions options) =>
        VirtualReaderDeviceOptionMapper.BuildServerOptions(options, _version, _strict);

    public VirtualDeviceOptions BuildDeviceOptions(VirtualReaderInstanceOptions options) =>
        VirtualReaderDeviceOptionMapper.BuildDeviceOptions(options);
}

internal sealed class FaultVirtualReaderPresetContributor : IVirtualReaderPresetContributor, ILlrpDevicePresetContributor
{
    private readonly bool _closeConnection;
    private readonly bool _statusError;

    public FaultVirtualReaderPresetContributor(
        string id,
        string description,
        bool closeConnection,
        bool statusError = false)
    {
        Id = id;
        Description = description;
        _closeConnection = closeConnection;
        _statusError = statusError;
    }

    public string Id { get; }
    public string Description { get; }

    public VirtualReaderHostOptions Build(VirtualReaderInstanceOptions options)
    {
        VirtualReaderOptions readerOptions = options.ReaderOptions with
        {
            ReaderName = options.Name,
            DropResponseForMessageTypes = _closeConnection
                ? options.ReaderOptions.DropResponseForMessageTypes
                : AddMessageType(options.ReaderOptions.DropResponseForMessageTypes, LlrpNet.Protocol.Messages.V1_0_1.ADD_ROSPEC.MessageType),
            CloseConnectionAfterRequestMessageTypes = _closeConnection
                ? AddMessageType(options.ReaderOptions.CloseConnectionAfterRequestMessageTypes, LlrpNet.Protocol.Messages.V1_0_1.GET_READER_CONFIG.MessageType)
                : options.ReaderOptions.CloseConnectionAfterRequestMessageTypes,
            ErrorResponseForMessageTypes = _statusError
                ? AddError(options.ReaderOptions.ErrorResponseForMessageTypes, LlrpNet.Protocol.Messages.V1_0_1.ADD_ROSPEC.MessageType)
                : options.ReaderOptions.ErrorResponseForMessageTypes,
        };
        return new VirtualReaderHostOptions
        {
            ListenAddress = options.ListenAddress,
            Port = options.Port,
            ProtocolModules = options.ProtocolModules,
            ReaderOptions = readerOptions,
        };
    }

    public LlrpDeviceServerOptions BuildServerOptions(VirtualReaderInstanceOptions options)
    {
        LlrpDeviceServerOptions serverOptions = VirtualReaderDeviceOptionMapper.BuildServerOptions(
            options,
            LlrpProtocolVersion.Version101,
            strict: false);
        if (_closeConnection)
        {
            return serverOptions with
            {
                CloseConnectionAfterRequestMessageTypes = AddMessageType(
                    serverOptions.CloseConnectionAfterRequestMessageTypes,
                    LlrpNet.Protocol.Messages.V1_0_1.GET_READER_CONFIG.MessageType),
            };
        }

        if (_statusError)
        {
            var errors = serverOptions.ErrorResponseForMessageTypes.ToDictionary();
            errors[LlrpNet.Protocol.Messages.V1_0_1.ADD_ROSPEC.MessageType] =
                new LlrpDeviceServerErrorResponse(100, "Injected device-server status fault.");
            return serverOptions with { ErrorResponseForMessageTypes = errors };
        }

        return serverOptions with
        {
            DropResponseForMessageTypes = AddMessageType(
                serverOptions.DropResponseForMessageTypes,
                LlrpNet.Protocol.Messages.V1_0_1.ADD_ROSPEC.MessageType),
        };
    }

    public VirtualDeviceOptions BuildDeviceOptions(VirtualReaderInstanceOptions options) =>
        VirtualReaderDeviceOptionMapper.BuildDeviceOptions(options);

    private static IReadOnlySet<ushort> AddMessageType(IReadOnlySet<ushort> current, ushort messageType) =>
        current.Append(messageType).ToHashSet();

    private static IReadOnlyDictionary<ushort, VirtualReaderErrorResponse> AddError(
        IReadOnlyDictionary<ushort, VirtualReaderErrorResponse> current,
        ushort messageType)
    {
        var result = current.ToDictionary();
        result[messageType] = new VirtualReaderErrorResponse(100, "Injected virtual-reader status fault.");
        return result;
    }
}

internal static class VirtualReaderDeviceOptionMapper
{
    public static LlrpDeviceServerOptions BuildServerOptions(
        VirtualReaderInstanceOptions options,
        LlrpProtocolVersion version,
        bool strict)
    {
        VirtualReaderOptions reader = options.ReaderOptions;
        return new LlrpDeviceServerOptions
        {
            ListenAddress = options.ListenAddress,
            Port = options.Port,
            ProtocolVersion = version,
            MaximumClientConnections = reader.MaximumClientConnections,
            ConnectionLimitPolicy = reader.ConnectionLimitPolicy switch
            {
                VirtualReaderConnectionLimitPolicy.ReplaceExisting => LlrpDeviceConnectionLimitPolicy.ReplaceExisting,
                _ => LlrpDeviceConnectionLimitPolicy.RejectAdditional,
            },
            IdleTimeout = reader.IdleTimeout,
            FrameAssemblyTimeout = reader.FrameAssemblyTimeout,
            MaximumFrameLength = reader.MaximumFrameLength,
            UseTcpKeepAlive = reader.UseTcpKeepAlive,
            KeepAliveInterval = reader.KeepAliveInterval,
            Reports = new LlrpDeviceReportOptions
            {
                ReportInterval = reader.Reports.ReportInterval,
                ReportCount = reader.Reports.ReportCount,
                Repeat = reader.Reports.Repeat,
            },
            UnknownVendorParameterBehavior = reader.UnknownVendorParameterBehavior switch
            {
                VirtualReaderUnknownVendorParameterBehavior.Reject => LlrpUnknownVendorParameterBehavior.Reject,
                _ => LlrpUnknownVendorParameterBehavior.PreserveAndIgnore,
            },
            UseStrictStandardInventoryProfile = reader.UseStrictStandardInventoryProfile || strict,
            DropResponseForMessageTypes = reader.DropResponseForMessageTypes,
            ErrorResponseForMessageTypes = reader.ErrorResponseForMessageTypes.ToDictionary(
                static pair => pair.Key,
                static pair => new LlrpDeviceServerErrorResponse(pair.Value.StatusCode, pair.Value.Description)),
            CloseConnectionAfterRequestMessageTypes = reader.CloseConnectionAfterRequestMessageTypes,
            TruncateResponseForMessageTypes = reader.TruncateResponseForMessageTypes,
            ProtocolModules = options.DeviceProtocolModules,
        };
    }

    public static VirtualDeviceOptions BuildDeviceOptions(VirtualReaderInstanceOptions options)
    {
        VirtualReaderOptions reader = options.ReaderOptions;
        IReadOnlyList<VirtualTag> oldTags = reader.TagSource.GetTags();
        if (!reader.ElectronicProductCode.IsEmpty && oldTags.Count > 0)
        {
            oldTags = [oldTags[0] with
            {
                ElectronicProductCode = reader.ElectronicProductCode,
                UserMemory = reader.UserMemory.Count > 0 ? reader.UserMemory : oldTags[0].UserMemory,
            }];
        }

        return new VirtualDeviceOptions
        {
            Identity = new LlrpDeviceIdentity
            {
                ReaderId = reader.ReaderId,
                Name = options.Name,
                ManufacturerId = reader.Capabilities.ManufacturerId,
                ModelId = reader.Capabilities.ModelId,
                FirmwareVersion = reader.Capabilities.FirmwareVersion,
            },
            Capabilities = new LlrpDeviceCapabilities
            {
                MaxNumberOfAntennas = reader.Capabilities.MaxNumberOfAntennas,
                CanSetAntennaProperties = reader.Capabilities.CanSetAntennaProperties,
                HasUtcClockCapability = reader.Capabilities.HasUtcClockCapability,
            },
            Configuration = new LlrpDeviceConfiguration
            {
                Antennas = reader.AntennaConfigurations.Select(static antenna => new LlrpDeviceAntennaConfiguration
                {
                    AntennaId = antenna.AntennaId,
                    ReceiverSensitivityIndex = antenna.ReceiverSensitivityIndex,
                    TransmitPowerIndex = antenna.TransmitPowerIndex,
                    HopTableId = antenna.HopTableId,
                    ChannelIndex = antenna.ChannelIndex,
                }).ToArray(),
                Gpos = reader.GpoStates.Select(static gpo => new LlrpDeviceGpoState
                {
                    PortNumber = gpo.PortNumber,
                    State = gpo.State,
                }).ToArray(),
            },
            Tags = oldTags.Select(static tag => new VirtualTagDefinition
            {
                ElectronicProductCode = tag.ElectronicProductCode,
                Tid = tag.Tid,
                PeakRssi = tag.PeakRssi,
                AntennaId = tag.AntennaId,
                ChannelIndex = tag.ChannelIndex,
                UserMemory = tag.UserMemory,
            }).ToArray(),
            RfSimulation = new VirtualRfSimulationOptions
            {
                Scenario = reader.RfSimulation.Scenario switch
                {
                    VirtualReaderRfScenario.MovingTags => VirtualRfScenario.MovingTags,
                    VirtualReaderRfScenario.Noisy => VirtualRfScenario.Noisy,
                    _ => VirtualRfScenario.Static,
                },
                RandomSeed = reader.RfSimulation.RandomSeed,
                DetectionProbability = reader.RfSimulation.DetectionProbability,
                PresenceCycleRounds = reader.RfSimulation.PresenceCycleRounds,
                RssiJitterDb = reader.RfSimulation.RssiJitterDb,
                MaxTagsPerRound = reader.RfSimulation.MaxTagsPerRound,
            },
        };
    }

    private static IReadOnlySet<ushort> AddMessageType(IReadOnlySet<ushort> current, ushort messageType) =>
        current.Append(messageType).ToHashSet();
}
