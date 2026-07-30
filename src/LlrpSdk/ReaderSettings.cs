namespace LlrpSdk;

/// <summary>High-level reader intent spanning device configuration and optional managed inventory.</summary>
public sealed record ReaderSettings
{
    public ReaderConfiguration Configuration { get; init; } = new();
    public InventorySettings? Inventory { get; init; }
    public IReadOnlyDictionary<string, object?> Extensions { get; init; } =
        new System.Collections.ObjectModel.ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>());
}

/// <summary>Device facts returned by <see cref="LlrpReader.QuerySettingsAsync"/>.</summary>
public sealed record ReaderSettingsSnapshot(ReaderSettings Settings, InventorySnapshot? Inventory);

/// <summary>Describes a reader-resident SDK inventory resource.</summary>
public sealed record InventorySnapshot(InventorySettings Settings, InventoryRuntimeState State);

public enum InventoryRuntimeState
{
    Disabled,
    Enabled,
    Running,
}
