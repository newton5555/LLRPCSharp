using LlrpDevice.Abstractions;

namespace LlrpDevice.Virtual;

/// <summary>
/// Describes the fixed reader-side capability shape of a virtual device.
/// Endpoint binding and inventory data are intentionally not part of this
/// profile.
/// </summary>
public sealed record VirtualDeviceCapabilityProfile
{
    public required string Id { get; init; }
    public required string ProtocolVersion { get; init; }
    public required LlrpDeviceIdentity Identity { get; init; }
    public required LlrpDeviceCapabilities Capabilities { get; init; }
    public required LlrpDeviceConfiguration InitialConfiguration { get; init; }

    /// <summary>Creates device behavior options from this capability profile.</summary>
    public VirtualDeviceOptions CreateDeviceOptions(IVirtualInventoryDataSource? inventoryDataSource = null)
    {
        IVirtualInventoryDataSource source = inventoryDataSource ?? VirtualInventoryDataSources.Default;
        return new VirtualDeviceOptions
        {
            CapabilityProfileId = Id,
            Identity = Identity,
            Capabilities = Capabilities,
            Configuration = InitialConfiguration,
            Tags = source.Tags,
        };
    }
}

/// <summary>Built-in virtual-device capability profiles.</summary>
public static class VirtualDeviceCapabilityProfiles
{
    /// <summary>Standard LLRP 1.0.1 profile with generic identity and captured RF capability tables.</summary>
    public const string Standard101Id = "llrp1.0.1_standard";

    /// <summary>Impinj Speedway R420 profile captured from the acceptance reader.</summary>
    public const string ImpinjR420Id = "impinj.r420.llrp-1.0.1";

    /// <summary>Gets the capability profiles shipped by the SDK.</summary>
    public static IReadOnlyList<VirtualDeviceCapabilityProfile> All { get; } =
    [
        CreateStandard101(),
        CreateImpinjR420(),
    ];

    /// <summary>Finds one built-in capability profile by identifier.</summary>
    public static VirtualDeviceCapabilityProfile Get(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return All.FirstOrDefault(profile =>
                   string.Equals(profile.Id, id, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException($"Unknown virtual-device capability profile '{id}'.");
    }

    /// <summary>Creates the standard 1.0.1 capability profile.</summary>
    public static VirtualDeviceCapabilityProfile CreateStandard101() => new()
    {
        Id = Standard101Id,
        ProtocolVersion = "1.0.1",
        Identity = VirtualDeviceOptions.CreateDefaultIdentity(),
        Capabilities = VirtualDeviceOptions.CreateDefaultCapabilities(),
        InitialConfiguration = new LlrpDeviceConfiguration
        {
            Antennas = VirtualDeviceOptions.CreateDefaultAntennaConfigurations(),
            Gpos = [new LlrpDeviceGpoState { PortNumber = 1, State = false }],
        },
    };

    /// <summary>Creates the protocol-neutral R420 identity and capability profile.</summary>
    public static VirtualDeviceCapabilityProfile CreateImpinjR420() => new()
    {
        Id = ImpinjR420Id,
        ProtocolVersion = "1.0.1",
        Identity = new LlrpDeviceIdentity
        {
            ReaderId = 1,
            Name = "Impinj R420 (Virtual)",
            ManufacturerId = 25_882,
            ModelId = 2_001_002,
            FirmwareVersion = "6.4.1.240",
        },
        Capabilities = VirtualDeviceOptions.CreateImpinjR420Capabilities(4),
        InitialConfiguration = new LlrpDeviceConfiguration
        {
            Antennas = VirtualDeviceOptions.CreateImpinjR420AntennaConfigurations(4),
            Gpos = [new LlrpDeviceGpoState { PortNumber = 1, State = false }],
        },
    };
}
