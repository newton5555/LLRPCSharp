using LlrpNet.Protocol.Parameters;

namespace LlrpSdk.Extensions;

/// <summary>Projects and compiles vendor-specific reader configuration for an active reader extension.</summary>
public interface IReaderSettingsContributor
{
    /// <summary>Gets the stable extension key written to <see cref="ReaderConfiguration.Extensions"/>.</summary>
    public string Id { get; }

    /// <summary>Builds custom parameters requesting vendor settings in a reader-configuration query.</summary>
    public IReadOnlyList<ILlrpParameter> BuildQueryParameters();

    /// <summary>Projects decoded custom parameters from a reader-configuration response.</summary>
    public void ContributeQuery(ReaderSettingsContributionContext context, ReaderConfigurationExtensionBuilder extensions);

    /// <summary>Builds custom parameters for one reader-configuration apply operation.</summary>
    public IReadOnlyList<ILlrpParameter> BuildApplyParameters(ReaderConfiguration configuration);
}

/// <summary>Supplies normalized configuration and decoded custom parameters to one settings contributor.</summary>
public sealed record ReaderSettingsContributionContext(
    ReaderConfiguration Configuration,
    IReadOnlyList<ILlrpParameter> CustomItems);

/// <summary>Collects typed, vendor-owned configuration values.</summary>
public sealed class ReaderConfigurationExtensionBuilder
{
    private readonly Dictionary<string, object?> values = new(StringComparer.Ordinal);

    /// <summary>Adds one value under a contributor-owned key.</summary>
    public void Add(string key, object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (!values.TryAdd(key, value))
        {
            throw new InvalidOperationException($"A reader-configuration extension value is already registered for key '{key}'.");
        }
    }

    internal IReadOnlyDictionary<string, object?> Build() =>
        values.Count == 0 ? Empty : new System.Collections.ObjectModel.ReadOnlyDictionary<string, object?>(values);

    private static IReadOnlyDictionary<string, object?> Empty { get; } =
        new System.Collections.ObjectModel.ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>());
}
