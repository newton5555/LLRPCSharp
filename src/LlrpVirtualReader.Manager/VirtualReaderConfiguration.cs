using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using LlrpDevice.Server;
using LlrpDevice.Virtual;
using LlrpNet.Core.Protocol;
using LlrpVirtualReader;

namespace LlrpVirtualReader.Manager;

/// <summary>Versioned local configuration document for virtual-reader instances and inventory presets.</summary>
public sealed record VirtualReaderConfigurationDocument
{
    /// <summary>Gets the configuration schema version.</summary>
    public int SchemaVersion { get; init; } = 1;

    /// <summary>Gets local declarative presets.</summary>
    public IReadOnlyList<VirtualReaderLocalPreset> Presets { get; init; } = [];

    /// <summary>Gets explicitly addressable reader instances.</summary>
    public IReadOnlyList<VirtualReaderLocalInstance> Instances { get; init; } = [];
}

/// <summary>Describes one local virtual-reader instance without causing it to start.</summary>
public sealed record VirtualReaderLocalInstance
{
    public required string InstanceId { get; init; }
    public string Name { get; init; } = "Virtual Reader";
    public required string PresetId { get; init; }
    public string ListenAddress { get; init; } = "127.0.0.1";
    public int Port { get; init; } = 5084;
}

/// <summary>Describes a local, declarative inventory/device preset.</summary>
public sealed record VirtualReaderLocalPreset
{
    public required string Id { get; init; }
    public string Description { get; init; } = string.Empty;
    public string ProtocolVersion { get; init; } = "1.0.1";
    public bool Strict { get; init; }
    public int ReportIntervalMilliseconds { get; init; } = 100;
    public int ReportCount { get; init; }
    public bool Repeat { get; init; } = true;
    public string RfScenario { get; init; } = "static";
    public int RandomSeed { get; init; } = 2026;
    public double DetectionProbability { get; init; } = 1.0;
    public int PresenceCycleRounds { get; init; } = 3;
    public int RssiJitterDb { get; init; }
    public int MaxTagsPerRound { get; init; }
    public int MaximumClientConnections { get; init; } = 1;
    public int? KeepAliveIntervalMilliseconds { get; init; }
    public ushort MaxNumberOfAntennas { get; init; } = 4;
    public string FirmwareVersion { get; init; } = "virtual-reader";
    public IReadOnlyList<VirtualReaderLocalTag> Tags { get; init; } = [];
}

/// <summary>Describes one tag in a local inventory preset.</summary>
public sealed record VirtualReaderLocalTag
{
    public required string Epc { get; init; }
    public string? Tid { get; init; }
    public short PeakRssi { get; init; } = -42;
    public ushort AntennaId { get; init; } = 1;
    public ushort ChannelIndex { get; init; } = 1;
    public IReadOnlyList<ushort> UserMemory { get; init; } = [0, 0, 0, 0];
}

/// <summary>Loads, validates, and resolves local virtual-reader configuration documents.</summary>
public sealed class VirtualReaderConfiguration
{
    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

    private readonly Dictionary<string, VirtualReaderLocalPreset> _presets;
    private readonly Dictionary<string, VirtualReaderLocalInstance> _instances;

    private VirtualReaderConfiguration(VirtualReaderConfigurationDocument document)
    {
        Document = document;
        ValidateDocument(document);
        _presets = document.Presets.ToDictionary(static preset => preset.Id, StringComparer.OrdinalIgnoreCase);
        _instances = document.Instances.ToDictionary(static instance => instance.InstanceId, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Gets the validated source document.</summary>
    public VirtualReaderConfigurationDocument Document { get; }

    /// <summary>Loads and validates one local JSON document.</summary>
    public static VirtualReaderConfiguration Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A virtual-reader configuration path is required.", nameof(path));
        }

        string json = File.ReadAllText(path);
        VirtualReaderConfigurationDocument document = JsonSerializer.Deserialize<VirtualReaderConfigurationDocument>(
                json,
                SerializerOptions)
            ?? throw new InvalidDataException("The virtual-reader configuration document is empty.");
        return new VirtualReaderConfiguration(document);
    }

    /// <summary>Saves a configuration document using the stable local JSON schema.</summary>
    public static void Save(string path, VirtualReaderConfigurationDocument document)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A virtual-reader configuration path is required.", nameof(path));
        }

        ArgumentNullException.ThrowIfNull(document);
        ValidateDocument(document);
        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(document, SerializerOptions));
    }

    /// <summary>Gets local presets in stable identifier order.</summary>
    public IReadOnlyList<VirtualReaderLocalPreset> Presets =>
        _presets.Values.OrderBy(static preset => preset.Id, StringComparer.OrdinalIgnoreCase).ToArray();

    /// <summary>Gets local instances in stable identifier order.</summary>
    public IReadOnlyList<VirtualReaderLocalInstance> Instances =>
        _instances.Values.OrderBy(static instance => instance.InstanceId, StringComparer.OrdinalIgnoreCase).ToArray();

    /// <summary>Creates a Manager catalog containing built-ins and the local declarative presets.</summary>
    public VirtualReaderPresetCatalog CreateCatalog()
    {
        var catalog = new VirtualReaderPresetCatalog();
        foreach (VirtualReaderLocalPreset preset in Presets)
        {
            catalog.Register(new DeclarativeVirtualReaderPresetContributor(preset));
        }

        return catalog;
    }

    /// <summary>Gets one local instance, or throws when its identifier is not present.</summary>
    public VirtualReaderLocalInstance GetInstance(string? instanceId = null)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            return _instances.Count == 1
                ? _instances.Values.Single()
                : throw new ArgumentException(
                    "An instance identifier is required when the configuration contains zero or multiple instances.",
                    nameof(instanceId));
        }

        return _instances.TryGetValue(instanceId, out VirtualReaderLocalInstance? instance)
            ? instance
            : throw new KeyNotFoundException($"Virtual-reader instance '{instanceId}' is not defined in the configuration.");
    }

    /// <summary>Builds Manager options for an explicitly selected local instance.</summary>
    public VirtualReaderInstanceOptions BuildInstanceOptions(string? instanceId = null)
    {
        VirtualReaderLocalInstance instance = GetInstance(instanceId);
        if (!IPAddress.TryParse(instance.ListenAddress, out IPAddress? address))
        {
            throw new InvalidDataException($"The listen address '{instance.ListenAddress}' is invalid.");
        }

        return new VirtualReaderInstanceOptions
        {
            InstanceId = instance.InstanceId,
            Name = instance.Name,
            PresetId = instance.PresetId,
            ListenAddress = address,
            Port = instance.Port,
        };
    }

    internal static LlrpProtocolVersion ParseProtocolVersion(string value) => value.Trim() switch
    {
        "1.0.1" => LlrpProtocolVersion.Version101,
        "1.1" => LlrpProtocolVersion.Version11,
        "2.0" => LlrpProtocolVersion.Version20,
        _ => throw new InvalidDataException($"Unsupported local virtual-reader protocol version '{value}'."),
    };

    public static VirtualReaderRfScenario ParseRfScenario(string value) => value.Trim().ToLowerInvariant() switch
    {
        "static" => VirtualReaderRfScenario.Static,
        "moving-tags" or "moving" => VirtualReaderRfScenario.MovingTags,
        "noisy" => VirtualReaderRfScenario.Noisy,
        _ => throw new InvalidDataException($"Unsupported local RF scenario '{value}'."),
    };

    internal static VirtualReaderOptions BuildReaderOptions(VirtualReaderLocalPreset preset, string readerName)
    {
        ArgumentNullException.ThrowIfNull(preset);
        VirtualTag[] tags = preset.Tags.Count > 0
            ? preset.Tags.Select(ToVirtualTag).ToArray()
            : FixedVirtualTagSource.CreateDefault().GetTags().ToArray();

        return new VirtualReaderOptions
        {
            ReaderName = readerName,
            ProtocolVersion = ParseProtocolVersion(preset.ProtocolVersion),
            UseStrictStandardInventoryProfile = preset.Strict,
            MaximumClientConnections = preset.MaximumClientConnections,
            KeepAliveInterval = preset.KeepAliveIntervalMilliseconds is int interval
                ? TimeSpan.FromMilliseconds(interval)
                : null,
            Capabilities = new VirtualReaderCapabilities
            {
                MaxNumberOfAntennas = preset.MaxNumberOfAntennas,
                FirmwareVersion = preset.FirmwareVersion,
            },
            TagSource = new FixedVirtualTagSource(tags),
            Reports = new VirtualReaderReportOptions
            {
                ReportInterval = TimeSpan.FromMilliseconds(preset.ReportIntervalMilliseconds),
                ReportCount = preset.ReportCount,
                Repeat = preset.Repeat,
            },
            RfSimulation = new VirtualReaderRfSimulationOptions
            {
                Scenario = ParseRfScenario(preset.RfScenario),
                RandomSeed = preset.RandomSeed,
                DetectionProbability = preset.DetectionProbability,
                PresenceCycleRounds = preset.PresenceCycleRounds,
                RssiJitterDb = preset.RssiJitterDb,
                MaxTagsPerRound = preset.MaxTagsPerRound,
            },
        };
    }

    private static VirtualTag ToVirtualTag(VirtualReaderLocalTag tag)
    {
        ArgumentNullException.ThrowIfNull(tag);
        byte[] epc = Convert.FromHexString(tag.Epc.Trim());
        ReadOnlyMemory<byte> tid = string.IsNullOrWhiteSpace(tag.Tid)
            ? ReadOnlyMemory<byte>.Empty
            : Convert.FromHexString(tag.Tid.Trim());
        return new VirtualTag
        {
            ElectronicProductCode = epc,
            Tid = tid,
            PeakRssi = tag.PeakRssi,
            AntennaId = tag.AntennaId,
            ChannelIndex = tag.ChannelIndex,
            UserMemory = tag.UserMemory.ToArray(),
        };
    }

    private static void ValidateDocument(VirtualReaderConfigurationDocument document)
    {
        if (document.SchemaVersion != 1)
        {
            throw new InvalidDataException($"Unsupported virtual-reader configuration schema {document.SchemaVersion}; expected 1.");
        }

        ArgumentNullException.ThrowIfNull(document.Presets);
        ArgumentNullException.ThrowIfNull(document.Instances);
        if (document.Presets.Any(static preset => preset is null))
        {
            throw new InvalidDataException("The local preset list cannot contain null entries.");
        }

        if (document.Instances.Any(static instance => instance is null))
        {
            throw new InvalidDataException("The local instance list cannot contain null entries.");
        }

        EnsureUnique(document.Presets.Select(static preset => preset.Id), "preset");
        EnsureUnique(document.Instances.Select(static instance => instance.InstanceId), "instance");
        var presetIds = document.Presets.Select(static preset => preset.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var builtInPresetIds = new VirtualReaderPresetCatalog()
            .Presets
            .Select(static preset => preset.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (VirtualReaderLocalPreset preset in document.Presets)
        {
            if (string.IsNullOrWhiteSpace(preset.Id))
            {
                throw new InvalidDataException("A local preset identifier is required.");
            }

            if (builtInPresetIds.Contains(preset.Id))
            {
                throw new InvalidDataException(
                    $"Local preset '{preset.Id}' conflicts with a built-in virtual-reader preset.");
            }

            if (preset.Tags is null)
            {
                throw new InvalidDataException($"Local preset '{preset.Id}' has a null tag list.");
            }

            if (preset.Tags.Any(static tag => tag is null))
            {
                throw new InvalidDataException($"Local preset '{preset.Id}' has a null tag entry.");
            }

            _ = ParseProtocolVersion(preset.ProtocolVersion);
            _ = ParseRfScenario(preset.RfScenario);
            _ = BuildReaderOptions(preset, "configuration-validation");
        }

        var availablePresetIds = presetIds
            .Concat(builtInPresetIds)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (VirtualReaderLocalInstance instance in document.Instances)
        {
            if (string.IsNullOrWhiteSpace(instance.InstanceId) || string.IsNullOrWhiteSpace(instance.Name))
            {
                throw new InvalidDataException("Local instance identifiers and names are required.");
            }

            if (!availablePresetIds.Contains(instance.PresetId))
            {
                throw new InvalidDataException(
                    $"Local instance '{instance.InstanceId}' references unknown preset '{instance.PresetId}'.");
            }

            if (!IPAddress.TryParse(instance.ListenAddress, out _))
            {
                throw new InvalidDataException(
                    $"Local instance '{instance.InstanceId}' has invalid listen address '{instance.ListenAddress}'.");
            }

            if (instance.Port is <= 0 or > ushort.MaxValue)
            {
                throw new InvalidDataException(
                    $"Local instance '{instance.InstanceId}' port must be between 1 and 65535.");
            }
        }
    }

    private static void EnsureUnique(IEnumerable<string> values, string kind)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string value in values)
        {
            if (string.IsNullOrWhiteSpace(value) || !seen.Add(value))
            {
                throw new InvalidDataException($"Local {kind} identifiers must be non-empty and unique.");
            }
        }
    }

    private static JsonSerializerOptions CreateSerializerOptions() => new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };
}

/// <summary>Adapts one local declarative preset to the Manager preset contributor contract.</summary>
public sealed class DeclarativeVirtualReaderPresetContributor : IVirtualReaderPresetContributor, ILlrpDevicePresetContributor
{
    private readonly VirtualReaderLocalPreset _preset;

    public DeclarativeVirtualReaderPresetContributor(VirtualReaderLocalPreset preset)
    {
        _preset = preset ?? throw new ArgumentNullException(nameof(preset));
        if (string.IsNullOrWhiteSpace(preset.Id))
        {
            throw new ArgumentException("A local preset identifier is required.", nameof(preset));
        }
    }

    public string Id => _preset.Id;
    public string Description => _preset.Description;

    public VirtualReaderHostOptions Build(VirtualReaderInstanceOptions options) => new()
    {
        ListenAddress = options.ListenAddress,
        Port = options.Port,
        ProtocolModules = options.ProtocolModules,
        ReaderOptions = VirtualReaderConfiguration.BuildReaderOptions(_preset, options.Name),
    };

    public LlrpDeviceServerOptions BuildServerOptions(VirtualReaderInstanceOptions options)
    {
        VirtualReaderOptions readerOptions = VirtualReaderConfiguration.BuildReaderOptions(_preset, options.Name);
        return VirtualReaderDeviceOptionMapper.BuildServerOptions(
            new VirtualReaderInstanceOptions
            {
                Name = options.Name,
                ListenAddress = options.ListenAddress,
                Port = options.Port,
                ReaderOptions = readerOptions,
            },
            readerOptions.ProtocolVersion,
            _preset.Strict);
    }

    public VirtualDeviceOptions BuildDeviceOptions(VirtualReaderInstanceOptions options)
    {
        VirtualReaderOptions readerOptions = VirtualReaderConfiguration.BuildReaderOptions(_preset, options.Name);
        return VirtualReaderDeviceOptionMapper.BuildDeviceOptions(
            new VirtualReaderInstanceOptions
            {
                Name = options.Name,
                ListenAddress = options.ListenAddress,
                Port = options.Port,
                ReaderOptions = readerOptions,
            });
    }
}
