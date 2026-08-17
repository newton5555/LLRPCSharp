using System.Reflection;
using System.Text.Json;
using LlrpDevice.Abstractions;
using LlrpDevice.Virtual;

namespace LlrpVirtualDevice.Cli;

/// <summary>Small manifest stored beside the virtual-device configuration files.</summary>
internal sealed record VirtualDeviceCapabilityProfileManifest
{
    public int SchemaVersion { get; init; } = 1;
    public required string Id { get; init; }
    public required string BaseProfileId { get; init; }
    public required string ProtocolVersion { get; init; }
    public string? Name { get; init; }
    public ulong? ReaderId { get; init; }
    public uint? ManufacturerId { get; init; }
    public uint? ModelId { get; init; }
    public string? FirmwareVersion { get; init; }
    public ushort? MaxNumberOfAntennas { get; init; }
    public bool? CanSetAntennaProperties { get; init; }
}

/// <summary>Loads the capability profile manifests shipped with the device CLI.</summary>
internal static class VirtualDeviceCapabilityProfileCatalog
{
    private const string ResourcePrefix = "LlrpVirtualDevice.Cli.Config.llrp.caps.";
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static IReadOnlyList<VirtualDeviceCapabilityProfile> All { get; } =
    [
        Get(VirtualDeviceCapabilityProfiles.Standard101Id),
    ];

    public static VirtualDeviceCapabilityProfile Get(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        string resourceName = $"{ResourcePrefix}{id}.json";
        using Stream stream = typeof(VirtualDeviceCapabilityProfileCatalog).Assembly
            .GetManifestResourceStream(resourceName)
            ?? throw new InvalidDataException(
                $"Capability profile resource '{id}' is not available in the virtual-device CLI.");

        VirtualDeviceCapabilityProfileManifest manifest = JsonSerializer.Deserialize<VirtualDeviceCapabilityProfileManifest>(
                stream,
                SerializerOptions)
            ?? throw new InvalidDataException($"Capability profile '{id}' is empty.");

        if (manifest.SchemaVersion != 1 ||
            !string.Equals(manifest.Id, id, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Capability profile '{id}' has an unsupported manifest.");
        }

        VirtualDeviceCapabilityProfile profile = VirtualDeviceCapabilityProfiles.Get(manifest.BaseProfileId);
        if (!string.Equals(profile.ProtocolVersion, manifest.ProtocolVersion, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Capability profile '{id}' protocol version does not match its SDK base profile.");
        }

        ushort maxAntennas = manifest.MaxNumberOfAntennas ?? profile.Capabilities.MaxNumberOfAntennas;
        if (maxAntennas == 0)
        {
            throw new InvalidDataException($"Capability profile '{id}' must expose at least one antenna.");
        }

        LlrpDeviceIdentity identity = profile.Identity with
        {
            ReaderId = manifest.ReaderId ?? profile.Identity.ReaderId,
            Name = manifest.Name ?? profile.Identity.Name,
            ManufacturerId = manifest.ManufacturerId ?? profile.Identity.ManufacturerId,
            ModelId = manifest.ModelId ?? profile.Identity.ModelId,
            FirmwareVersion = manifest.FirmwareVersion ?? profile.Identity.FirmwareVersion,
        };
        LlrpDeviceCapabilities capabilities = profile.Capabilities with
        {
            MaxNumberOfAntennas = maxAntennas,
            CanSetAntennaProperties = manifest.CanSetAntennaProperties
                ?? profile.Capabilities.CanSetAntennaProperties,
        };
        LlrpDeviceConfiguration configuration = profile.InitialConfiguration with
        {
            Antennas = VirtualDeviceOptions.CreateDefaultAntennaConfigurations(maxAntennas),
        };

        return profile with
        {
            Id = manifest.Id,
            ProtocolVersion = manifest.ProtocolVersion,
            Identity = identity,
            Capabilities = capabilities,
            InitialConfiguration = configuration,
        };
    }
}
