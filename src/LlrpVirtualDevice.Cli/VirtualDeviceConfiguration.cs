using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using LlrpDevice.Abstractions;
using LlrpDevice.Server;
using LlrpDevice.Virtual;
using LlrpDevice.Virtual.Hosting;
using LlrpNet.Core.Protocol;

namespace LlrpVirtualDevice.Cli;

/// <summary>Stable identifiers for the standalone single-device CLI presets.</summary>
public static class VirtualDevicePresetIds
{
    public const string Standard101Basic = "llrp.standard101.basic";
    public const string Standard101Strict = "llrp.standard101.strict";
    public const string Standard11Basic = "llrp.standard11.basic";
    public const string Standard20Basic = "llrp.standard20.basic";
}

/// <summary>Describes one built-in single-device CLI preset.</summary>
public sealed record VirtualDevicePreset(
    string Id,
    string Description,
    string ProtocolVersion,
    bool Strict);

/// <summary>Built-in presets for one standalone virtual LLRP device.</summary>
public static class VirtualDevicePresets
{
    public static IReadOnlyList<VirtualDevicePreset> All { get; } =
    [
        new(
            VirtualDevicePresetIds.Standard101Basic,
            "LLRP 1.0.1 reader with deterministic tag reports.",
            "1.0.1",
            false),
        new(
            VirtualDevicePresetIds.Standard101Strict,
            "LLRP 1.0.1 reader with strict standard inventory validation.",
            "1.0.1",
            true),
        new(
            VirtualDevicePresetIds.Standard11Basic,
            "LLRP 1.1 reader with explicit version negotiation.",
            "1.1",
            false),
        new(
            VirtualDevicePresetIds.Standard20Basic,
            "LLRP 2.0 reader using the current translated device profile.",
            "2.0",
            false),
    ];

    public static VirtualDevicePreset Get(string presetId)
    {
        if (string.IsNullOrWhiteSpace(presetId))
        {
            throw new ArgumentException("A virtual-device preset identifier is required.", nameof(presetId));
        }

        return All.FirstOrDefault(preset =>
                   string.Equals(preset.Id, presetId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException($"Unknown virtual-device preset '{presetId}'.");
    }
}

/// <summary>Versioned local JSON document for exactly one virtual device.</summary>
public sealed record VirtualDeviceConfigurationDocument
{
    public int SchemaVersion { get; init; } = 1;
    public string PresetId { get; init; } = VirtualDevicePresetIds.Standard101Basic;
    public string Name { get; init; } = "Virtual Reader";
    public string ListenAddress { get; init; } = "127.0.0.1";
    public int Port { get; init; } = 5084;
    public string? ProtocolVersion { get; init; }
    public bool? Strict { get; init; }
    public ulong ReaderId { get; init; } = 1;
    public uint ManufacturerId { get; init; } = 161;
    public uint ModelId { get; init; } = 96008;
    public string FirmwareVersion { get; init; } = "3.32.37.0";
    public ushort MaxNumberOfAntennas { get; init; } = 4;
    public int MaximumClientConnections { get; init; } = 1;
    public int? KeepAliveIntervalMilliseconds { get; init; }
    public int ReportIntervalMilliseconds { get; init; } = 100;
    public int ReportCount { get; init; }
    public bool Repeat { get; init; } = true;
    public string RfScenario { get; init; } = "static";
    public int RandomSeed { get; init; } = 2026;
    public double DetectionProbability { get; init; } = 1.0;
    public int PresenceCycleRounds { get; init; } = 3;
    public int RssiJitterDb { get; init; }
    public int MaxTagsPerRound { get; init; }
    public IReadOnlyList<VirtualDeviceTagConfiguration> Tags { get; init; } = [];
}

/// <summary>Describes one tag in a single-device JSON configuration.</summary>
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

/// <summary>Loads and validates the single-device JSON format used by the standalone CLI.</summary>
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

        string json = File.ReadAllText(path);
        VirtualDeviceConfigurationDocument document = JsonSerializer.Deserialize<VirtualDeviceConfigurationDocument>(
                json,
                SerializerOptions)
            ?? throw new InvalidDataException("The virtual-device configuration document is empty.");
        Validate(document);
        return document;
    }

    public static void Validate(VirtualDeviceConfigurationDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.SchemaVersion != 1)
        {
            throw new InvalidDataException(
                $"Unsupported virtual-device configuration schema {document.SchemaVersion}; expected 1.");
        }

        _ = VirtualDevicePresets.Get(document.PresetId);
        if (string.IsNullOrWhiteSpace(document.Name))
        {
            throw new InvalidDataException("A virtual-device name is required.");
        }

        if (!IPAddress.TryParse(document.ListenAddress, out _))
        {
            throw new InvalidDataException($"The listen address '{document.ListenAddress}' is invalid.");
        }

        if (document.Port is <= 0 or > ushort.MaxValue)
        {
            throw new InvalidDataException("The virtual-device port must be between 1 and 65535.");
        }

        if (document.ProtocolVersion is not null)
        {
            _ = ParseProtocolVersion(document.ProtocolVersion);
        }

        if (document.MaxNumberOfAntennas == 0 || document.MaximumClientConnections <= 0)
        {
            throw new InvalidDataException("A virtual device must expose at least one antenna and client slot.");
        }

        if (document.ReportIntervalMilliseconds <= 0 || document.ReportCount < 0)
        {
            throw new InvalidDataException("Report interval must be positive and report count cannot be negative.");
        }

        if (document.KeepAliveIntervalMilliseconds is <= 0)
        {
            throw new InvalidDataException("Keepalive interval must be positive when specified.");
        }

        if (document.PresenceCycleRounds <= 0 || document.RssiJitterDb < 0 || document.MaxTagsPerRound < 0)
        {
            throw new InvalidDataException("RF cycle, RSSI jitter, and tag limits are invalid.");
        }

        if (double.IsNaN(document.DetectionProbability) ||
            double.IsInfinity(document.DetectionProbability) ||
            document.DetectionProbability is < 0 or > 1)
        {
            throw new InvalidDataException("Detection probability must be between 0 and 1.");
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

            ArgumentNullException.ThrowIfNull(tag.UserMemory);
        }
    }

    internal static LlrpProtocolVersion ParseProtocolVersion(string value) => value.Trim() switch
    {
        "1.0.1" => LlrpProtocolVersion.Version101,
        "1.1" => LlrpProtocolVersion.Version11,
        "2.0" => LlrpProtocolVersion.Version20,
        _ => throw new InvalidDataException($"Unsupported virtual-device protocol version '{value}'."),
    };

    internal static VirtualRfScenario ParseRfScenario(string value) => value.Trim().ToLowerInvariant() switch
    {
        "static" => VirtualRfScenario.Static,
        "moving-tags" or "moving" => VirtualRfScenario.MovingTags,
        "noisy" => VirtualRfScenario.Noisy,
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
}

internal sealed record VirtualDeviceLaunchOptions
{
    public string? ConfigPath { get; init; }
    public string? PresetId { get; init; }
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
    public int? PresenceCycleRounds { get; init; }
    public int? RssiJitterDb { get; init; }
    public int? MaxTagsPerRound { get; init; }
    public int? MaximumClientConnections { get; init; }
    public int? KeepAliveIntervalMilliseconds { get; init; }
    public bool? Strict { get; init; }
}

internal static class VirtualDeviceHostOptionsBuilder
{
    public static VirtualLlrpDeviceHostOptions Build(
        VirtualDeviceLaunchOptions launch,
        VirtualDeviceConfigurationDocument? document)
    {
        string presetId = launch.PresetId ?? document?.PresetId ?? VirtualDevicePresetIds.Standard101Basic;
        VirtualDevicePreset preset = VirtualDevicePresets.Get(presetId);
        string name = launch.Name ?? document?.Name ?? "Virtual Reader";
        string listenText = launch.ListenAddress ?? document?.ListenAddress ?? "127.0.0.1";
        if (!IPAddress.TryParse(listenText, out IPAddress? listenAddress))
        {
            throw new InvalidDataException($"The listen address '{listenText}' is invalid.");
        }

        int port = launch.Port ?? document?.Port ?? 5084;
        if (port is <= 0 or > ushort.MaxValue)
        {
            throw new InvalidDataException("The virtual-device port must be between 1 and 65535.");
        }

        string protocolText = launch.ProtocolVersion ?? document?.ProtocolVersion ?? preset.ProtocolVersion;
        LlrpProtocolVersion protocolVersion = VirtualDeviceConfiguration.ParseProtocolVersion(protocolText);
        bool strict = launch.Strict ?? document?.Strict ?? preset.Strict;
        int reportInterval = launch.ReportIntervalMilliseconds ?? document?.ReportIntervalMilliseconds ?? 100;
        int reportCount = launch.ReportCount ?? document?.ReportCount ?? 0;
        bool repeat = document?.Repeat ?? true;
        string rfScenarioText = launch.RfScenario ?? document?.RfScenario ?? "static";
        VirtualRfScenario rfScenario = VirtualDeviceConfiguration.ParseRfScenario(rfScenarioText);
        int randomSeed = launch.RandomSeed ?? document?.RandomSeed ?? 2026;
        double detectionProbability = launch.DetectionProbability ?? document?.DetectionProbability ?? 1.0;
        int presenceCycleRounds = launch.PresenceCycleRounds ?? document?.PresenceCycleRounds ?? 3;
        int rssiJitterDb = launch.RssiJitterDb ?? document?.RssiJitterDb ?? 0;
        int maxTagsPerRound = launch.MaxTagsPerRound ?? document?.MaxTagsPerRound ?? 0;
        int maximumClientConnections =
            launch.MaximumClientConnections ?? document?.MaximumClientConnections ?? 1;
        int? keepAliveMilliseconds =
            launch.KeepAliveIntervalMilliseconds ?? document?.KeepAliveIntervalMilliseconds;

        IReadOnlyList<VirtualTagDefinition> tags = BuildTags(document?.Tags);
        if (!string.IsNullOrWhiteSpace(launch.Tag))
        {
            tags =
            [
                new VirtualTagDefinition
                {
                    ElectronicProductCode = VirtualDeviceConfiguration.ParseHex(launch.Tag, "EPC"),
                },
            ];
        }

        ushort maxAntennas = document?.MaxNumberOfAntennas ?? 4;
        var deviceOptions = new VirtualDeviceOptions
        {
            Identity = VirtualDeviceOptions.CreateDefaultIdentity() with
            {
                ReaderId = document?.ReaderId ?? 1,
                Name = name,
                ManufacturerId = document?.ManufacturerId ?? 161,
                ModelId = document?.ModelId ?? 96008,
                FirmwareVersion = document?.FirmwareVersion ?? "3.32.37.0",
            },
            Capabilities = VirtualDeviceOptions.CreateDefaultCapabilities(maxAntennas),
            Configuration = new LlrpDeviceConfiguration
            {
                Antennas = VirtualDeviceOptions.CreateDefaultAntennaConfigurations(maxAntennas),
                Gpos = [new LlrpDeviceGpoState { PortNumber = 1, State = false }],
            },
            Tags = tags,
            RfSimulation = new VirtualRfSimulationOptions
            {
                Scenario = rfScenario,
                RandomSeed = randomSeed,
                DetectionProbability = detectionProbability,
                PresenceCycleRounds = presenceCycleRounds,
                RssiJitterDb = rssiJitterDb,
                MaxTagsPerRound = maxTagsPerRound,
            },
        };

        var serverOptions = new LlrpDeviceServerOptions
        {
            ListenAddress = listenAddress,
            Port = port,
            ProtocolVersion = protocolVersion,
            MaximumClientConnections = maximumClientConnections,
            // WPF and other SDK consumers commonly finish a short probe/configuration
            // lease and reconnect immediately. With a single client slot the server-side
            // receive loop may still be cleaning up the old socket, so rejecting the new
            // TCP connection produces a spurious 10054. One-device CLI instances model a
            // physical single-owner reader: the newest control session takes ownership.
            ConnectionLimitPolicy = maximumClientConnections == 1
                ? LlrpDeviceConnectionLimitPolicy.ReplaceExisting
                : LlrpDeviceConnectionLimitPolicy.RejectAdditional,
            KeepAliveInterval = keepAliveMilliseconds is int keepAlive
                ? TimeSpan.FromMilliseconds(keepAlive)
                : null,
            Reports = new LlrpDeviceReportOptions
            {
                ReportInterval = TimeSpan.FromMilliseconds(reportInterval),
                ReportCount = reportCount,
                Repeat = repeat,
            },
            UseStrictStandardInventoryProfile = strict,
        };

        return new VirtualLlrpDeviceHostOptions
        {
            Server = serverOptions,
            Device = deviceOptions,
        };
    }

    private static IReadOnlyList<VirtualTagDefinition> BuildTags(
        IReadOnlyList<VirtualDeviceTagConfiguration>? configuredTags)
    {
        if (configuredTags is null || configuredTags.Count == 0)
        {
            return new VirtualDeviceOptions().Tags;
        }

        return configuredTags.Select(static tag => new VirtualTagDefinition
        {
            ElectronicProductCode = VirtualDeviceConfiguration.ParseHex(tag.Epc, "EPC"),
            Tid = string.IsNullOrWhiteSpace(tag.Tid)
                ? ReadOnlyMemory<byte>.Empty
                : VirtualDeviceConfiguration.ParseHex(tag.Tid, "TID"),
            PeakRssi = tag.PeakRssi,
            AntennaId = tag.AntennaId,
            ChannelIndex = tag.ChannelIndex,
            UserMemory = tag.UserMemory,
            AccessPassword = tag.AccessPassword,
            KillPassword = tag.KillPassword,
        }).ToArray();
    }
}
