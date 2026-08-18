using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using LlrpDevice.Virtual.Hosting;

namespace LlrpVirtualDevice.Cli;

public static class VirtualDevicePresetIds
{
    public const string Standard101Basic = "llrp.standard101.basic";
    public const string Standard101Strict = "llrp.standard101.strict";
    public const string Standard11Basic = "llrp.standard11.basic";
    public const string Standard20Basic = "llrp.standard20.basic";
    public const string ImpinjR420Basic = "impinj.r420.basic";
}

public sealed record VirtualDevicePreset(
    string Id,
    string Description,
    string ProtocolVersion,
    bool Strict)
{
    public string CapabilityProfileId { get; init; } = VirtualDeviceProfiles.Standard101Id;
}

public static class VirtualDevicePresets
{
    public static IReadOnlyList<VirtualDevicePreset> All { get; } =
    [
        new(VirtualDevicePresetIds.Standard101Basic, "LLRP 1.0.1 reader with deterministic tag reports.", "1.0.1", false),
        new(VirtualDevicePresetIds.Standard101Strict, "LLRP 1.0.1 reader with strict standard inventory validation.", "1.0.1", true),
        new(VirtualDevicePresetIds.Standard11Basic, "LLRP 1.1 reader with explicit version negotiation.", "1.1", false),
        new(VirtualDevicePresetIds.Standard20Basic, "LLRP 2.0 reader using the current translated device profile.", "2.0", false),
        new(VirtualDevicePresetIds.ImpinjR420Basic,
            "Impinj R420-shaped LLRP 1.0.1 reader with extension activation and captured defaults.",
            "1.0.1",
            false)
        {
            CapabilityProfileId = VirtualDeviceProfiles.ImpinjR420Id,
        },
    ];

    public static VirtualDevicePreset Get(string presetId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(presetId);
        return All.FirstOrDefault(preset =>
                   string.Equals(preset.Id, presetId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException($"Unknown virtual-device preset '{presetId}'.");
    }
}

public sealed record VirtualDeviceConfigurationDocument
{
    public int SchemaVersion { get; init; } = 1;
    public string PresetId { get; init; } = VirtualDevicePresetIds.Standard101Basic;
    public string CapabilityProfileId { get; init; } = VirtualDeviceProfiles.Standard101Id;
    public string? Name { get; init; }
    public string? ProtocolVersion { get; init; }
    public bool? Strict { get; init; }
    public bool? RelaxedRoSpecStateChecks { get; init; }
    public bool? AllowImplicitStopOnDisable { get; init; }
    public string? InventoryDataSource { get; init; } = VirtualInventoryOptions.DefaultSourceId;
    public int ReportIntervalMilliseconds { get; init; } = 100;
    public int ReportCount { get; init; }
    public bool Repeat { get; init; } = true;
    public string RfScenario { get; init; } = "static";
    public int RandomSeed { get; init; } = 2026;
    public double DetectionProbability { get; init; } = 1.0;
    public double SingleTagProbability { get; init; } = 0.85;
    public int PresenceCycleRounds { get; init; } = 3;
    public int RssiJitterDb { get; init; }
    public int MaxTagsPerRound { get; init; } = 2;
    public IReadOnlyList<VirtualDeviceTagConfiguration> Tags { get; init; } = [];
}

public sealed record VirtualDeviceTagConfiguration
{
    public required string Epc { get; init; }
    public string? Tid { get; init; }
    public short PeakRssi { get; init; } = -42;
    public ushort AntennaId { get; init; } = 1;
    public ushort ChannelIndex { get; init; } = 1;
    public IReadOnlyList<ushort> UserMemory { get; init; } = [0, 0, 0, 0];
    public uint AccessPassword { get; init; }
    public uint KillPassword { get; init; }
}

public static class VirtualDeviceConfiguration
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public static VirtualDeviceConfigurationDocument Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A virtual-device configuration path is required.", nameof(path));
        }

        VirtualDeviceConfigurationDocument document = JsonSerializer.Deserialize<VirtualDeviceConfigurationDocument>(
                File.ReadAllText(path), SerializerOptions)
            ?? throw new InvalidDataException("The virtual-device configuration document is empty.");
        Validate(document);
        return document;
    }

    public static void Validate(VirtualDeviceConfigurationDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.SchemaVersion != 1)
        {
            throw new InvalidDataException($"Unsupported virtual-device configuration schema {document.SchemaVersion}; expected 1.");
        }

        VirtualDevicePreset preset = VirtualDevicePresets.Get(document.PresetId);
        _ = VirtualDeviceProfiles.Get(string.IsNullOrWhiteSpace(document.CapabilityProfileId)
            ? preset.CapabilityProfileId
            : document.CapabilityProfileId);

        if (document.Name is not null && string.IsNullOrWhiteSpace(document.Name))
        {
            throw new InvalidDataException("A virtual-device name cannot be empty.");
        }

        if (document.ProtocolVersion is not null)
        {
            _ = ParseProtocolVersion(document.ProtocolVersion);
        }

        if (document.ReportIntervalMilliseconds <= 0 || document.ReportCount < 0 ||
            document.PresenceCycleRounds <= 0 || document.RssiJitterDb < 0 || document.MaxTagsPerRound < 0)
        {
            throw new InvalidDataException("Report and RF simulation values are invalid.");
        }

        if (document.DetectionProbability is < 0 or > 1 || document.SingleTagProbability is < 0 or > 1 ||
            double.IsNaN(document.DetectionProbability) || double.IsInfinity(document.DetectionProbability) ||
            double.IsNaN(document.SingleTagProbability) || double.IsInfinity(document.SingleTagProbability))
        {
            throw new InvalidDataException("Detection and single-tag probabilities must be between 0 and 1.");
        }

        ArgumentNullException.ThrowIfNull(document.Tags);
        var epcs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (VirtualDeviceTagConfiguration tag in document.Tags)
        {
            ArgumentNullException.ThrowIfNull(tag);
            byte[] epc = ParseHex(tag.Epc, "EPC");
            if (!epcs.Add(Convert.ToHexString(epc)))
            {
                throw new InvalidDataException($"Virtual tag EPC '{tag.Epc}' is duplicated.");
            }

            if (!string.IsNullOrWhiteSpace(tag.Tid))
            {
                _ = ParseHex(tag.Tid, "TID");
            }
        }
    }

    internal static VirtualDeviceProtocolVersion ParseProtocolVersion(string value) => value.Trim() switch
    {
        "1.0.1" => VirtualDeviceProtocolVersion.Llrp101,
        "1.1" => VirtualDeviceProtocolVersion.Llrp11,
        "2.0" => VirtualDeviceProtocolVersion.Llrp20,
        _ => throw new InvalidDataException($"Unsupported virtual-device protocol version '{value}'."),
    };

    internal static VirtualDeviceRfScenario ParseRfScenario(string value) => value.Trim().ToLowerInvariant() switch
    {
        "static" => VirtualDeviceRfScenario.Static,
        "moving-tags" or "moving" => VirtualDeviceRfScenario.MovingTags,
        "noisy" => VirtualDeviceRfScenario.Noisy,
        _ => throw new InvalidDataException($"Unsupported virtual-device RF scenario '{value}'."),
    };

    internal static byte[] ParseHex(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException($"A {name} value is required.");
        }

        try
        {
            byte[] bytes = Convert.FromHexString(value.Trim());
            if (bytes.Length == 0)
            {
                throw new InvalidDataException($"A {name} value cannot be empty.");
            }

            return bytes;
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException($"The {name} value '{value}' is not valid hexadecimal.", exception);
        }
    }

    internal static IReadOnlyList<VirtualInventoryTag> BuildTags(
        IReadOnlyList<VirtualDeviceTagConfiguration> configuredTags) =>
        configuredTags.Select(static tag => new VirtualInventoryTag
        {
            ElectronicProductCode = ParseHex(tag.Epc, "EPC"),
            Tid = string.IsNullOrWhiteSpace(tag.Tid) ? ReadOnlyMemory<byte>.Empty : ParseHex(tag.Tid, "TID"),
            PeakRssi = tag.PeakRssi,
            AntennaId = tag.AntennaId,
            ChannelIndex = tag.ChannelIndex,
            UserMemory = tag.UserMemory,
            AccessPassword = tag.AccessPassword,
            KillPassword = tag.KillPassword,
        }).ToArray();
}

internal sealed record VirtualDeviceLaunchOptions
{
    public string? ConfigPath { get; init; }
    public string? PresetId { get; init; }
    public string? CapabilityProfileId { get; init; }
    public string? InventoryDataSource { get; init; }
    public string? ListenAddress { get; init; }
    public int? Port { get; init; }
    public string? ProtocolVersion { get; init; }
    public string? Name { get; init; }
    public string? Tag { get; init; }
    public int? ReportIntervalMilliseconds { get; init; }
    public int? ReportCount { get; init; }
    public string? RfScenario { get; init; }
    public int? RandomSeed { get; init; }
    public double? DetectionProbability { get; init; }
    public double? SingleTagProbability { get; init; }
    public int? PresenceCycleRounds { get; init; }
    public int? RssiJitterDb { get; init; }
    public int? MaxTagsPerRound { get; init; }
    public int? MaximumClientConnections { get; init; }
    public int? KeepAliveIntervalMilliseconds { get; init; }
    public bool? Strict { get; init; }
    public bool? RelaxedRoSpecStateChecks { get; init; }
    public bool? AllowImplicitStopOnDisable { get; init; }
}

internal static class VirtualDeviceHostOptionsBuilder
{
    public static VirtualDeviceHostOptions Build(
        VirtualDeviceLaunchOptions launch,
        VirtualDeviceConfigurationDocument? document)
    {
        string presetId = launch.PresetId ?? document?.PresetId ?? VirtualDevicePresetIds.Standard101Basic;
        VirtualDevicePreset preset = VirtualDevicePresets.Get(presetId);
        string profileId = launch.CapabilityProfileId ?? document?.CapabilityProfileId ?? preset.CapabilityProfileId;
        VirtualDeviceProfileInfo profile = VirtualDeviceProfiles.Get(profileId);

        string listenText = launch.ListenAddress ?? "127.0.0.1";
        if (!IPAddress.TryParse(listenText, out IPAddress? listenAddress))
        {
            throw new InvalidDataException($"The listen address '{listenText}' is invalid.");
        }

        VirtualInventoryOptions inventory = document?.Tags is { Count: > 0 }
            ? new VirtualInventoryOptions
            {
                SourceId = "legacy-inline",
                Tags = VirtualDeviceConfiguration.BuildTags(document.Tags),
            }
            : VirtualInventoryDataSourceConfiguration.Resolve(launch.InventoryDataSource ?? document?.InventoryDataSource);

        if (!string.IsNullOrWhiteSpace(launch.Tag))
        {
            inventory = new VirtualInventoryOptions
            {
                SourceId = "cli-tag",
                Tags = [new VirtualInventoryTag { ElectronicProductCode = VirtualDeviceConfiguration.ParseHex(launch.Tag, "EPC") }],
            };
        }

        return new VirtualDeviceHostOptions
        {
            ProfileId = profile.Id,
            Name = launch.Name ?? document?.Name,
            ListenAddress = listenAddress,
            Port = launch.Port ?? 5084,
            ProtocolVersion = VirtualDeviceConfiguration.ParseProtocolVersion(
                launch.ProtocolVersion ?? document?.ProtocolVersion ?? preset.ProtocolVersion),
            MaximumClientConnections = launch.MaximumClientConnections ?? 1,
            StrictStandardInventoryProfile = launch.Strict ?? document?.Strict ?? preset.Strict,
            RelaxedRoSpecStateChecks = launch.RelaxedRoSpecStateChecks ??
                                        launch.AllowImplicitStopOnDisable ??
                                        document?.RelaxedRoSpecStateChecks ??
                                        document?.AllowImplicitStopOnDisable ?? true,
            KeepAliveInterval = launch.KeepAliveIntervalMilliseconds is int keepAlive
                ? TimeSpan.FromMilliseconds(keepAlive)
                : null,
            ReportInterval = TimeSpan.FromMilliseconds(
                launch.ReportIntervalMilliseconds ?? document?.ReportIntervalMilliseconds ?? 100),
            ReportCount = launch.ReportCount ?? document?.ReportCount ?? 0,
            RepeatReports = document?.Repeat ?? true,
            Inventory = inventory,
            Simulation = new VirtualDeviceSimulationOptions
            {
                Scenario = VirtualDeviceConfiguration.ParseRfScenario(launch.RfScenario ?? document?.RfScenario ?? "static"),
                RandomSeed = launch.RandomSeed ?? document?.RandomSeed ?? 2026,
                DetectionProbability = launch.DetectionProbability ?? document?.DetectionProbability ?? 1.0,
                SingleTagProbability = launch.SingleTagProbability ?? document?.SingleTagProbability ?? 0.85,
                PresenceCycleRounds = launch.PresenceCycleRounds ?? document?.PresenceCycleRounds ?? 3,
                RssiJitterDb = launch.RssiJitterDb ?? document?.RssiJitterDb ?? 0,
                MaxTagsPerRound = launch.MaxTagsPerRound ?? document?.MaxTagsPerRound ?? 2,
            },
        };
    }
}
