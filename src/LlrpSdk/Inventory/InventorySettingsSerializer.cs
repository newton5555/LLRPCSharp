using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LlrpSdk;

/// <summary>
/// Provides JSON serialization and deserialization helpers for <see cref="InventorySettings"/>.
/// </summary>
public static class InventorySettingsSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Deserializes a JSON string into an <see cref="InventorySettings"/> instance.
    /// </summary>
    public static InventorySettings DeserializeFromJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        InventorySettings settings = JsonSerializer.Deserialize<InventorySettings>(json, Options) ?? new InventorySettings();
        EnsureStandardOnly(settings);
        return settings;
    }

    /// <summary>
    /// Serializes an <see cref="InventorySettings"/> instance to a JSON string.
    /// </summary>
    public static string SerializeToJson(InventorySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        EnsureStandardOnly(settings);
        return JsonSerializer.Serialize(settings, Options);
    }

    /// <summary>
    /// Loads an <see cref="InventorySettings"/> instance from a JSON file path.
    /// </summary>
    public static InventorySettings LoadFromFile(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"InventorySettings configuration file not found at '{filePath}'.", filePath);
        }

        string content = File.ReadAllText(filePath);
        return DeserializeFromJson(content);
    }

    /// <summary>Saves standard inventory settings to a JSON file.</summary>
    /// <remarks>
    /// Vendor-owned extension values require an extension-specific profile serializer before they can be
    /// safely persisted and restored.
    /// </remarks>
    public static void SaveToFile(string filePath, InventorySettings settings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(settings);
        File.WriteAllText(filePath, SerializeToJson(settings));
    }

    private static void EnsureStandardOnly(InventorySettings settings)
    {
        if (settings.Extensions.Count != 0)
        {
            throw new NotSupportedException(
                "InventorySettings JSON only supports standard inventory settings. " +
                "Vendor extension values must be persisted through that extension's typed profile serializer.");
        }
    }
}
