using System.Collections.ObjectModel;

namespace LlrpSdk;

/// <summary>Lightweight helper that builds the canonical <see cref="InventorySettings"/> record.</summary>
public sealed class InventorySettingsBuilder
{
    private InventorySettings settings;
    private readonly Dictionary<string, object?> extensions;

    public InventorySettingsBuilder()
        : this(new InventorySettings())
    {
    }

    internal InventorySettingsBuilder(InventorySettings source)
    {
        ArgumentNullException.ThrowIfNull(source);
        settings = source;
        extensions = new Dictionary<string, object?>(source.Extensions, StringComparer.Ordinal);
    }

    public InventorySettingsBuilder Antennas(params ushort[] antennaIds)
    {
        ArgumentNullException.ThrowIfNull(antennaIds);
        settings = settings with { AntennaIds = Array.AsReadOnly(antennaIds.ToArray()) };
        return this;
    }

    public InventorySettingsBuilder Session(byte value)
    {
        settings = settings with { Session = value };
        return this;
    }

    public InventorySettingsBuilder Population(ushort value)
    {
        settings = settings with { TagPopulationEstimate = value };
        return this;
    }

    public InventorySettingsBuilder Mode(ushort modeIndex, ushort tari = 0)
    {
        settings = settings with { ModeIndex = modeIndex, Tari = tari };
        return this;
    }

    public InventorySettingsBuilder Priority(byte value)
    {
        settings = settings with { Priority = value };
        return this;
    }

    public InventorySettingsBuilder ReportEveryTag() => ReportEvery(1);

    public InventorySettingsBuilder ReportEvery(ushort tagCount)
    {
        settings = settings with
        {
            ReportEveryNTags = tagCount,
            Report = settings.Report with { Trigger = InventoryReportTrigger.UponNTagsOrEndOfAiSpec },
        };
        return this;
    }

    public InventorySettingsBuilder BatchAfterStop()
    {
        settings = settings with
        {
            ReportEveryNTags = 0,
            Report = settings.Report with { Trigger = InventoryReportTrigger.UponNTagsOrEndOfRoSpec },
        };
        return this;
    }

    public InventorySettingsBuilder ReadTid(
        ushort words = 6,
        ushort wordPointer = 0,
        string accessPassword = "00000000") =>
        ReadAttachedData(TagMemoryBank.Tid, wordPointer, words, accessPassword);

    public InventorySettingsBuilder ReadAttachedData(
        TagMemoryBank memoryBank,
        ushort wordPointer,
        ushort words,
        string accessPassword = "00000000")
    {
        settings = settings with
        {
            AttachedData = new AttachedDataOptions
            {
                Enabled = true,
                MemoryBank = (ushort)memoryBank,
                WordPointer = wordPointer,
                WordCount = words,
                AccessPassword = accessPassword,
            },
        };
        return this;
    }

    public InventorySettingsBuilder WithoutAttachedData()
    {
        settings = settings with { AttachedData = new AttachedDataOptions() };
        return this;
    }

    /// <summary>Sets one typed extension value. Vendor packages provide fluent wrappers over this method.</summary>
    public InventorySettingsBuilder SetExtension(string key, object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        extensions[key] = value;
        return this;
    }

    /// <summary>Gets an existing typed extension value while editing settings.</summary>
    public bool TryGetExtension<T>(string key, out T? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (extensions.TryGetValue(key, out object? candidate) && candidate is T typed)
        {
            value = typed;
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>Builds the immutable canonical settings record.</summary>
    public InventorySettings Build() => settings with
    {
        Extensions = extensions.Count == 0
            ? EmptyExtensions
            : new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>(extensions, StringComparer.Ordinal)),
    };

    private static IReadOnlyDictionary<string, object?> EmptyExtensions { get; } =
        new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>());
}
