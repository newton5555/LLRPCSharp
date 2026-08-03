namespace LlrpSdk;

/// <summary>High-level reader intent spanning device configuration and optional managed inventory.</summary>
public sealed record ReaderSettings
{
    public ReaderConfiguration Configuration { get; init; } = new();
    public InventorySettings? Inventory { get; init; }
    public IReadOnlyDictionary<string, object?> Extensions { get; init; } =
        new System.Collections.ObjectModel.ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>());

    /// <summary>Creates settings through the optional fluent configuration helper.</summary>
    public static ReaderSettings Create(Action<ReaderSettingsBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new ReaderSettingsBuilder();
        configure(builder);
        return builder.Build();
    }

    /// <summary>Returns an edited copy while preserving fields not changed by the helper.</summary>
    public ReaderSettings Edit(Action<ReaderSettingsBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new ReaderSettingsBuilder(this);
        configure(builder);
        return builder.Build();
    }
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
