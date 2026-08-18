using System.Net;
using LlrpDevice.Virtual;
using LlrpDevice.Virtual.Impinj;

namespace LlrpDevice.Virtual.Hosting;

/// <summary>Protocol version selected for one hosted virtual reader.</summary>
public enum VirtualDeviceProtocolVersion
{
    Llrp101,
    Llrp11,
    Llrp20,
}

/// <summary>RF simulation scenario exposed by the Hosting facade.</summary>
public enum VirtualDeviceRfScenario
{
    Static,
    MovingTags,
    Noisy,
}

/// <summary>One tag injected into a virtual reader before it starts.</summary>
public sealed record VirtualInventoryTag
{
    public required ReadOnlyMemory<byte> ElectronicProductCode { get; init; }
    public ReadOnlyMemory<byte> Tid { get; init; }
    public short PeakRssi { get; init; } = -42;
    public ushort AntennaId { get; init; } = 1;
    public ushort ChannelIndex { get; init; } = 1;
    public IReadOnlyList<ushort> UserMemory { get; init; } = [0, 0, 0, 0];
    public uint AccessPassword { get; init; }
    public uint KillPassword { get; init; }
}

/// <summary>Initial tag population for one virtual reader.</summary>
public sealed record VirtualInventoryOptions
{
    public const string DefaultSourceId = "default";

    public string SourceId { get; init; } = DefaultSourceId;
    public IReadOnlyList<VirtualInventoryTag> Tags { get; init; } = [];
}

/// <summary>Deterministic RF simulation settings for one virtual reader.</summary>
public sealed record VirtualDeviceSimulationOptions
{
    public VirtualDeviceRfScenario Scenario { get; init; } = VirtualDeviceRfScenario.Static;
    public int RandomSeed { get; init; } = 2026;
    public double DetectionProbability { get; init; } = 1.0;
    public double SingleTagProbability { get; init; } = 0.85;
    public int PresenceCycleRounds { get; init; } = 3;
    public int RssiJitterDb { get; init; }
    public int MaxTagsPerRound { get; init; } = 2;
}

/// <summary>Stable Hosting-level description of one built-in virtual reader profile.</summary>
public sealed record VirtualDeviceProfileInfo(
    string Id,
    string ProtocolVersion,
    string Name,
    uint ManufacturerId,
    uint ModelId,
    string FirmwareVersion,
    ushort MaxNumberOfAntennas);

/// <summary>High-level configuration for one virtual reader Host.</summary>
public sealed record VirtualDeviceHostOptions
{
    public string ProfileId { get; init; } = "llrp1.0.1_standard";
    public string? Name { get; init; }
    public IPAddress ListenAddress { get; init; } = IPAddress.Loopback;
    public int Port { get; init; } = 5084;
    public VirtualDeviceProtocolVersion ProtocolVersion { get; init; } = VirtualDeviceProtocolVersion.Llrp101;
    public int MaximumClientConnections { get; init; } = 1;
    public bool StrictStandardInventoryProfile { get; init; }
    public bool RelaxedRoSpecStateChecks { get; init; } = true;
    public TimeSpan? KeepAliveInterval { get; init; }
    public TimeSpan ReportInterval { get; init; } = TimeSpan.FromMilliseconds(100);
    public int ReportCount { get; init; }
    public bool RepeatReports { get; init; } = true;
    public VirtualInventoryOptions Inventory { get; init; } = new();
    public VirtualDeviceSimulationOptions Simulation { get; init; } = new();

    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(ListenAddress);
        ArgumentException.ThrowIfNullOrWhiteSpace(ProfileId);
        ArgumentNullException.ThrowIfNull(Inventory);
        ArgumentNullException.ThrowIfNull(Simulation);
        if (Port is < 0 or > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(Port));
        }

        if (MaximumClientConnections <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumClientConnections));
        }

        if (ReportInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(ReportInterval));
        }

        if (ReportCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ReportCount));
        }

        if (Simulation.DetectionProbability is < 0 or > 1 ||
            Simulation.SingleTagProbability is < 0 or > 1 ||
            Simulation.PresenceCycleRounds <= 0 ||
            Simulation.RssiJitterDb < 0 ||
            Simulation.MaxTagsPerRound < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Simulation));
        }
    }
}

/// <summary>Built-in profiles known by the Hosting facade.</summary>
public static class VirtualDeviceProfiles
{
    public const string Standard101Id = VirtualDeviceCapabilityProfiles.Standard101Id;
    public const string ImpinjR420Id = "impinj.r420.llrp-1.0.1";

    public static IReadOnlyList<VirtualDeviceProfileInfo> All { get; } =
        VirtualDeviceCapabilityProfiles.All
            .Select(static profile => new VirtualDeviceProfileInfo(
                profile.Id,
                profile.ProtocolVersion,
                profile.Identity.Name,
                profile.Identity.ManufacturerId,
                profile.Identity.ModelId,
                profile.Identity.FirmwareVersion,
                profile.Capabilities.MaxNumberOfAntennas))
            .ToArray();

    public static VirtualDeviceProfileInfo Get(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return All.FirstOrDefault(profile =>
                   string.Equals(profile.Id, id, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException($"Unknown virtual-device capability profile '{id}'.");
    }
}

/// <summary>Application-facing facade for one virtual reader endpoint.</summary>
public interface IVirtualDeviceHost : IAsyncDisposable
{
    public VirtualLlrpDeviceHostState State { get; }
    public VirtualDeviceHostOptions Definition { get; }
    public IPAddress ListenAddress { get; }
    public int ConfiguredPort { get; }
    public int BoundPort { get; }
    public int ConnectedClientCount { get; }

    public event EventHandler<VirtualDeviceHostLifecycleChangedEventArgs>? LifecycleChanged;
    public event EventHandler<VirtualDeviceClientChangedEventArgs>? ClientChanged;
    public event EventHandler<VirtualDeviceMessageObservedEventArgs>? MessageObserved;

    public Task StartAsync(CancellationToken cancellationToken = default);
    public Task StopAsync(CancellationToken cancellationToken = default);
    public Task RestartAsync(CancellationToken cancellationToken = default);
}

/// <summary>Hosting-level client information that does not expose Server types.</summary>
public sealed record VirtualDeviceClientInfo(
    string ConnectionId,
    EndPoint? RemoteEndPoint,
    DateTimeOffset ConnectedAt,
    VirtualDeviceProtocolVersion? NegotiatedVersion,
    bool IsConnected);

public sealed class VirtualDeviceHostLifecycleChangedEventArgs : EventArgs
{
    public VirtualDeviceHostLifecycleChangedEventArgs(
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

public sealed class VirtualDeviceClientChangedEventArgs : EventArgs
{
    public VirtualDeviceClientChangedEventArgs(VirtualDeviceClientInfo client, bool connected)
    {
        Client = client;
        Connected = connected;
    }

    public VirtualDeviceClientInfo Client { get; }
    public bool Connected { get; }
}

public sealed class VirtualDeviceMessageObservedEventArgs : EventArgs
{
    public VirtualDeviceMessageObservedEventArgs(
        string connectionId,
        VirtualDeviceProtocolVersion version,
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
    public VirtualDeviceProtocolVersion Version { get; }
    public ushort MessageType { get; }
    public uint MessageId { get; }
    public bool Incoming { get; }
    public string? Detail { get; }
}

internal static class VirtualDeviceHostOptionsMapper
{
    public static VirtualLlrpDeviceHostOptions Build(VirtualDeviceHostOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        VirtualDeviceCapabilityProfile profile = VirtualDeviceCapabilityProfiles.Get(options.ProfileId);
        LlrpNet.Core.Protocol.LlrpProtocolVersion protocolVersion = options.ProtocolVersion switch
        {
            VirtualDeviceProtocolVersion.Llrp101 => LlrpNet.Core.Protocol.LlrpProtocolVersion.Version101,
            VirtualDeviceProtocolVersion.Llrp11 => LlrpNet.Core.Protocol.LlrpProtocolVersion.Version11,
            VirtualDeviceProtocolVersion.Llrp20 => LlrpNet.Core.Protocol.LlrpProtocolVersion.Version20,
            _ => throw new ArgumentOutOfRangeException(nameof(options.ProtocolVersion)),
        };

        if (string.Equals(profile.Id, VirtualDeviceProfiles.ImpinjR420Id, StringComparison.OrdinalIgnoreCase) &&
            protocolVersion != LlrpNet.Core.Protocol.LlrpProtocolVersion.Version101)
        {
            throw new InvalidDataException($"Capability profile '{profile.Id}' only supports LLRP 1.0.1.");
        }

        IVirtualInventoryDataSource inventory = CreateInventory(options.Inventory);
        VirtualDeviceOptions device = profile.CreateDeviceOptions(inventory) with
        {
            Identity = string.IsNullOrWhiteSpace(options.Name)
                ? profile.Identity
                : profile.Identity with { Name = options.Name },
            RfSimulation = new VirtualRfSimulationOptions
            {
                Scenario = options.Simulation.Scenario switch
                {
                    VirtualDeviceRfScenario.Static => VirtualRfScenario.Static,
                    VirtualDeviceRfScenario.MovingTags => VirtualRfScenario.MovingTags,
                    VirtualDeviceRfScenario.Noisy => VirtualRfScenario.Noisy,
                    _ => throw new ArgumentOutOfRangeException(nameof(options.Simulation)),
                },
                RandomSeed = options.Simulation.RandomSeed,
                DetectionProbability = options.Simulation.DetectionProbability,
                SingleTagProbability = options.Simulation.SingleTagProbability,
                PresenceCycleRounds = options.Simulation.PresenceCycleRounds,
                RssiJitterDb = options.Simulation.RssiJitterDb,
                MaxTagsPerRound = options.Simulation.MaxTagsPerRound,
            },
        };

        var server = new LlrpDevice.Server.LlrpDeviceServerOptions
        {
            ListenAddress = options.ListenAddress,
            Port = options.Port,
            ProtocolVersion = protocolVersion,
            MaximumClientConnections = options.MaximumClientConnections,
            ConnectionLimitPolicy = options.MaximumClientConnections == 1
                ? LlrpDevice.Server.LlrpDeviceConnectionLimitPolicy.ReplaceExisting
                : LlrpDevice.Server.LlrpDeviceConnectionLimitPolicy.RejectAdditional,
            KeepAliveInterval = options.KeepAliveInterval,
            Reports = new LlrpDevice.Server.LlrpDeviceReportOptions
            {
                ReportInterval = options.ReportInterval,
                ReportCount = options.ReportCount,
                Repeat = options.RepeatReports,
            },
            UseStrictStandardInventoryProfile = options.StrictStandardInventoryProfile,
            AllowImplicitStopOnDisable = options.RelaxedRoSpecStateChecks,
        };

        if (string.Equals(profile.Id, VirtualDeviceProfiles.ImpinjR420Id, StringComparison.OrdinalIgnoreCase))
        {
            server = server with
            {
                ProtocolModules = [ImpinjVirtualDeviceProtocolModule.Instance],
                InitialReaderCapabilitiesCustomItems = ImpinjVirtualDeviceDefaults.CreateCapabilities(),
                InitialReaderConfigurationCustomItems = ImpinjVirtualDeviceDefaults.CreateReaderConfiguration(),
            };
        }

        return new VirtualLlrpDeviceHostOptions
        {
            Server = server,
            Device = device,
            InventoryDataSource = inventory,
        };
    }

    private static IVirtualInventoryDataSource CreateInventory(VirtualInventoryOptions options)
    {
        if (options.Tags.Count > 0)
        {
            return new InMemoryVirtualInventoryDataSource(
                options.SourceId,
                options.Tags.Select(static tag => new VirtualTagDefinition
                {
                    ElectronicProductCode = tag.ElectronicProductCode,
                    Tid = tag.Tid,
                    PeakRssi = tag.PeakRssi,
                    AntennaId = tag.AntennaId,
                    ChannelIndex = tag.ChannelIndex,
                    UserMemory = tag.UserMemory,
                    AccessPassword = tag.AccessPassword,
                    KillPassword = tag.KillPassword,
                }));
        }

        return VirtualInventoryDataSources.Default;
    }
}
