namespace LlrpSdk;

/// <summary>Lightweight helper that builds the canonical <see cref="ReaderSettings"/> record.</summary>
public sealed class ReaderSettingsBuilder
{
    private ReaderConfiguration configuration;
    private InventorySettings? inventory;
    private IReadOnlyDictionary<string, object?> extensions;

    public ReaderSettingsBuilder()
        : this(new ReaderSettings())
    {
    }

    internal ReaderSettingsBuilder(ReaderSettings source)
    {
        ArgumentNullException.ThrowIfNull(source);
        configuration = source.Configuration;
        inventory = source.Inventory;
        extensions = source.Extensions;
    }

    /// <summary>Replaces the reader-global configuration.</summary>
    public ReaderSettingsBuilder Configuration(ReaderConfiguration value)
    {
        configuration = value ?? throw new ArgumentNullException(nameof(value));
        return this;
    }

    /// <summary>Creates or edits the managed inventory portion of these settings.</summary>
    public ReaderSettingsBuilder Inventory(Action<InventorySettingsBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = inventory is null ? new InventorySettingsBuilder() : new InventorySettingsBuilder(inventory);
        configure(builder);
        inventory = builder.Build();
        return this;
    }

    /// <summary>Replaces the managed inventory portion with a canonical settings record.</summary>
    public ReaderSettingsBuilder Inventory(InventorySettings value)
    {
        inventory = value ?? throw new ArgumentNullException(nameof(value));
        return this;
    }

    /// <summary>Removes managed inventory intent so applying the result changes reader configuration only.</summary>
    public ReaderSettingsBuilder WithoutInventory()
    {
        inventory = null;
        return this;
    }

    /// <summary>Builds the immutable canonical settings record.</summary>
    public ReaderSettings Build() => new()
    {
        Configuration = configuration,
        Inventory = inventory,
        Extensions = extensions,
    };
}
