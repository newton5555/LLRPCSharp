using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LlrpSdk;

/// <summary>
/// Provides JSON serialization and deserialization helpers for <see cref="ReaderSettings"/>.
/// </summary>
public static class ReaderSettingsSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Deserializes a JSON string into a <see cref="ReaderSettings"/> instance.
    /// </summary>
    public static ReaderSettings DeserializeFromJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        ReaderSettings settings = JsonSerializer.Deserialize<ReaderSettings>(json, Options) ?? new ReaderSettings();
        EnsureStandardOnly(settings);
        return settings;
    }

    /// <summary>
    /// Serializes a <see cref="ReaderSettings"/> instance to a JSON string.
    /// </summary>
    public static string SerializeToJson(ReaderSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        EnsureStandardOnly(settings);
        return JsonSerializer.Serialize(settings, Options);
    }

    /// <summary>
    /// Loads a <see cref="ReaderSettings"/> instance from a JSON file path.
    /// </summary>
    public static ReaderSettings LoadFromFile(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"ReaderSettings configuration file not found at '{filePath}'.", filePath);
        }

        string content = File.ReadAllText(filePath);
        return DeserializeFromJson(content);
    }

    /// <summary>Saves standard inventory settings to a JSON file.</summary>
    /// <remarks>
    /// Vendor-owned extension values require an extension-specific profile serializer before they can be
    /// safely persisted and restored.
    /// </remarks>
    public static void SaveToFile(string filePath, ReaderSettings settings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(settings);
        File.WriteAllText(filePath, SerializeToJson(settings));
    }

    private static void EnsureStandardOnly(ReaderSettings settings)
    {
        if (settings.Extensions.Count != 0)
        {
            throw new NotSupportedException(
                "ReaderSettings JSON only supports standard inventory settings. " +
                "Vendor extension values must be persisted through that extension's typed profile serializer.");
        }
    }
}
