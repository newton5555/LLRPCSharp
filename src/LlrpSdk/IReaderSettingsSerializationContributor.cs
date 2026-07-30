using System.Text.Json.Nodes;

namespace LlrpSdk;

/// <summary>Provides a strongly typed, versioned JSON representation for a vendor high-level settings extension.</summary>
public interface IReaderSettingsSerializationContributor
{
    /// <summary>Returns whether this contributor owns the extension value at the specified settings scope and key.</summary>
    public bool CanHandle(ReaderSettingsExtensionScope scope, string key, object? value);

    /// <summary>Writes the extension value as a versioned JSON document node.</summary>
    public JsonNode Serialize(ReaderSettingsExtensionScope scope, string key, object? value);

    /// <summary>Reads the extension value from its versioned JSON document node.</summary>
    public object? Deserialize(ReaderSettingsExtensionScope scope, string key, JsonNode value);
}

/// <summary>Identifies the Settings submodel that owns an extension value.</summary>
public enum ReaderSettingsExtensionScope
{
    Reader,
    Configuration,
    Inventory,
}
