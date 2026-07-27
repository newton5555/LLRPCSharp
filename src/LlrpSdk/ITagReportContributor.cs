using LlrpNet.Protocol.Parameters;

namespace LlrpSdk.Extensions;

/// <summary>Projects vendor-specific tag-report data into the SDK-level extension bag.</summary>
/// <remarks>
/// A contributor participates only when the same object is also an active <see cref="IReaderExtension"/>.
/// </remarks>
public interface ITagReportContributor
{
    /// <summary>Gets the stable extension key written to <see cref="TagReport.Extensions"/>.</summary>
    public string Id { get; }

    /// <summary>Adds a vendor projection for one decoded tag report.</summary>
    public void Contribute(TagReportContributionContext context, TagReportExtensionBuilder extensions);
}

/// <summary>Supplies the normalized tag and decoded vendor parameters to one report contributor.</summary>
public sealed record TagReportContributionContext(
    TagReport Report,
    IReadOnlyList<ILlrpParameter> CustomItems);

/// <summary>Collects typed, vendor-owned values for one tag report.</summary>
public sealed class TagReportExtensionBuilder
{
    private readonly Dictionary<string, object?> values = new(StringComparer.Ordinal);

    /// <summary>Adds one value under a contributor-owned key.</summary>
    public void Add(string key, object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (!values.TryAdd(key, value))
        {
            throw new InvalidOperationException($"A tag-report extension value is already registered for key '{key}'.");
        }
    }

    internal IReadOnlyDictionary<string, object?> Build() =>
        values.Count == 0 ? Empty : new System.Collections.ObjectModel.ReadOnlyDictionary<string, object?>(values);

    private static IReadOnlyDictionary<string, object?> Empty { get; } =
        new System.Collections.ObjectModel.ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>());
}
