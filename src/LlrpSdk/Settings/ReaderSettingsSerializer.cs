using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;

namespace LlrpSdk;

/// <summary>Serializes standard high-level reader settings. Vendor extensions require contributor serializers.</summary>
public static class ReaderSettingsSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static ReaderSettings DeserializeFromJson(
        string json,
        IEnumerable<IReaderSettingsSerializationContributor>? contributors = null)
    {
        JsonObject document = JsonNode.Parse(json)?.AsObject()
            ?? throw new JsonException("Settings document must be a JSON object.");
        if (string.Equals(document["documentType"]?.GetValue<string>(), "readerSettingsDefaults", StringComparison.Ordinal))
        {
            throw new JsonException("Settings defaults documents must be loaded into a draft before they can be applied as ReaderSettings.");
        }
        if (document["schemaVersion"]?.GetValue<int>() != 1)
        {
            throw new JsonException("Settings document must declare schemaVersion 1.");
        }
        JsonObject settingsNode = document["settings"]?.AsObject()
            ?? throw new JsonException("Settings document must contain an object named settings.");
        return DeserializeSettings(settingsNode, contributors ?? []);
    }

    public static string SerializeToJson(
        ReaderSettings settings,
        IEnumerable<IReaderSettingsSerializationContributor>? contributors = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        JsonObject document = new()
        {
            ["schemaVersion"] = 1,
            ["settings"] = SerializeSettings(settings, contributors ?? [])
        };
        return document.ToJsonString(Options);
    }

    /// <summary>Serializes a profile-generated settings baseline together with its profile provenance.</summary>
    public static string SerializeDefaultsToJson(
        ReaderSettingsDefaults defaults,
        IEnumerable<IReaderSettingsSerializationContributor>? contributors = null)
    {
        ArgumentNullException.ThrowIfNull(defaults);
        JsonObject document = new()
        {
            ["schemaVersion"] = 1,
            ["documentType"] = "readerSettingsDefaults",
            ["profileId"] = defaults.ProfileId,
            ["source"] = defaults.Source.ToString(),
            ["notes"] = JsonSerializer.SerializeToNode(defaults.Notes, Options),
            ["settings"] = SerializeSettings(defaults.Settings, contributors ?? [])
        };
        return document.ToJsonString(Options);
    }

    /// <summary>Deserializes a profile-generated settings baseline and its profile provenance.</summary>
    public static ReaderSettingsDefaults DeserializeDefaultsFromJson(
        string json,
        IEnumerable<IReaderSettingsSerializationContributor>? contributors = null)
    {
        JsonObject document = JsonNode.Parse(json)?.AsObject()
            ?? throw new JsonException("Settings defaults document must be a JSON object.");
        if (document["schemaVersion"]?.GetValue<int>() != 1 ||
            !string.Equals(document["documentType"]?.GetValue<string>(), "readerSettingsDefaults", StringComparison.Ordinal))
        {
            throw new JsonException("Settings defaults document must declare schemaVersion 1 and documentType readerSettingsDefaults.");
        }
        string profileId = document["profileId"]?.GetValue<string>()
            ?? throw new JsonException("Settings defaults document must declare profileId.");
        string sourceText = document["source"]?.GetValue<string>()
            ?? throw new JsonException("Settings defaults document must declare source.");
        if (!Enum.TryParse(sourceText, ignoreCase: true, out ReaderSettingsDefaultSource source))
        {
            throw new JsonException($"Unknown settings defaults source '{sourceText}'.");
        }
        IReadOnlyList<string> notes = document["notes"]?.Deserialize<string[]>(Options) ?? [];
        JsonObject settings = document["settings"]?.AsObject()
            ?? throw new JsonException("Settings defaults document must contain an object named settings.");
        return new ReaderSettingsDefaults
        {
            ProfileId = profileId,
            Source = source,
            Notes = notes,
            Settings = DeserializeSettings(settings, contributors ?? [])
        };
    }

    public static ReaderSettings LoadFromFile(string path, IEnumerable<IReaderSettingsSerializationContributor>? contributors = null)
        => DeserializeFromJson(File.ReadAllText(path), contributors);
    public static void SaveToFile(string path, ReaderSettings settings, IEnumerable<IReaderSettingsSerializationContributor>? contributors = null)
        => File.WriteAllText(path, SerializeToJson(settings, contributors));

    /// <summary>Loads a profile-generated defaults document, retaining its provenance.</summary>
    public static ReaderSettingsDefaults LoadDefaultsFromFile(string path, IEnumerable<IReaderSettingsSerializationContributor>? contributors = null)
        => DeserializeDefaultsFromJson(File.ReadAllText(path), contributors);

    /// <summary>Saves a profile-generated defaults document, including profile provenance.</summary>
    public static void SaveDefaultsToFile(string path, ReaderSettingsDefaults defaults, IEnumerable<IReaderSettingsSerializationContributor>? contributors = null)
        => File.WriteAllText(path, SerializeDefaultsToJson(defaults, contributors));

    private static JsonObject SerializeSettings(
        ReaderSettings settings,
        IEnumerable<IReaderSettingsSerializationContributor> contributors)
    {
        ReaderSettings standard = settings with
        {
            Extensions = EmptyExtensions(),
            Configuration = settings.Configuration with { Extensions = EmptyExtensions() },
            Inventory = settings.Inventory is null ? null : settings.Inventory with { Extensions = EmptyExtensions() }
        };
        JsonObject node = JsonSerializer.SerializeToNode(standard, Options)!.AsObject();
        WriteExtensions(node, "extensions", ReaderSettingsExtensionScope.Reader, settings.Extensions, contributors);
        WriteExtensions(node["configuration"]!.AsObject(), "extensions", ReaderSettingsExtensionScope.Configuration, settings.Configuration.Extensions, contributors);
        if (settings.Inventory is not null)
        {
            WriteExtensions(node["inventory"]!.AsObject(), "extensions", ReaderSettingsExtensionScope.Inventory, settings.Inventory.Extensions, contributors);
        }
        return node;
    }

    private static ReaderSettings DeserializeSettings(
        JsonObject settingsNode,
        IEnumerable<IReaderSettingsSerializationContributor> contributors)
    {
        JsonObject mutable = settingsNode.DeepClone().AsObject();
        IReadOnlyDictionary<string, object?> root = ReadExtensions(mutable, "extensions", ReaderSettingsExtensionScope.Reader, contributors);
        JsonObject config = mutable["configuration"]?.AsObject() ?? throw new JsonException("Settings configuration is required.");
        IReadOnlyDictionary<string, object?> configuration = ReadExtensions(config, "extensions", ReaderSettingsExtensionScope.Configuration, contributors);
        IReadOnlyDictionary<string, object?>? inventory = null;
        if (mutable["inventory"] is JsonObject inventoryNode)
        {
            inventory = ReadExtensions(inventoryNode, "extensions", ReaderSettingsExtensionScope.Inventory, contributors);
        }

        ReaderSettings standard = JsonSerializer.Deserialize<ReaderSettings>(mutable, Options) ?? new ReaderSettings();
        return standard with
        {
            Extensions = root,
            Configuration = standard.Configuration with { Extensions = configuration },
            Inventory = standard.Inventory is null ? null : standard.Inventory with { Extensions = inventory ?? EmptyExtensions() }
        };
    }

    private static void WriteExtensions(JsonObject owner, string property, ReaderSettingsExtensionScope scope,
        IReadOnlyDictionary<string, object?> values, IEnumerable<IReaderSettingsSerializationContributor> contributors)
    {
        JsonObject extensions = new();
        foreach ((string key, object? value) in values)
        {
            IReaderSettingsSerializationContributor contributor = contributors.FirstOrDefault(candidate => candidate.CanHandle(scope, key, value))
                ?? throw new NotSupportedException($"No strongly typed settings serializer is registered for extension '{key}'.");
            extensions[key] = contributor.Serialize(scope, key, value);
        }
        owner[property] = extensions;
    }

    private static IReadOnlyDictionary<string, object?> ReadExtensions(JsonObject owner, string property, ReaderSettingsExtensionScope scope,
        IEnumerable<IReaderSettingsSerializationContributor> contributors)
    {
        JsonObject extensions = owner[property]?.AsObject() ?? new JsonObject();
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach ((string key, JsonNode? value) in extensions)
        {
            IReaderSettingsSerializationContributor contributor = contributors.FirstOrDefault(candidate => candidate.CanHandle(scope, key, null))
                ?? throw new NotSupportedException($"No strongly typed settings serializer is registered for extension '{key}'.");
            values.Add(key, contributor.Deserialize(scope, key, value ?? throw new JsonException($"Extension '{key}' cannot be null.")));
        }
        owner[property] = new JsonObject();
        return new System.Collections.ObjectModel.ReadOnlyDictionary<string, object?>(values);
    }

    private static IReadOnlyDictionary<string, object?> EmptyExtensions()
        => new System.Collections.ObjectModel.ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>());
}
