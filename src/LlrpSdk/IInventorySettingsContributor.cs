using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Parameters;

namespace LlrpSdk.Extensions;

/// <summary>Restores vendor-owned inventory intent from the custom parameters of the SDK-reserved ROSpec.</summary>
public interface IInventorySettingsContributor
{
    /// <summary>Gets the stable contributor identifier.</summary>
    public string Id { get; }

    /// <summary>Projects recognized custom inventory parameters into extension-owned high-level values.</summary>
    public void ContributeQuery(InventorySettingsContributionContext context, InventorySettingsExtensionBuilder extensions);
}

/// <summary>Supplies a managed ROSpec's custom items and reader facts to one reverse inventory contributor.</summary>
public sealed record InventorySettingsContributionContext(
    ReaderIdentity Identity,
    ReaderCapabilities Capabilities,
    LlrpProtocolVersion ProtocolVersion,
    IReadOnlyList<ILlrpParameter> RoReportSpecCustomItems,
    IReadOnlyList<ILlrpParameter> C1G2InventoryCommandCustomItems);

/// <summary>Collects typed vendor inventory values under stable extension keys.</summary>
public sealed class InventorySettingsExtensionBuilder
{
    private readonly Dictionary<string, object?> values = new(StringComparer.Ordinal);

    public void Add(string key, object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (!values.TryAdd(key, value))
        {
            throw new InvalidOperationException($"An inventory extension value is already registered for key '{key}'.");
        }
    }

    internal IReadOnlyDictionary<string, object?> Build() => values.Count == 0
        ? Empty
        : new System.Collections.ObjectModel.ReadOnlyDictionary<string, object?>(values);

    private static IReadOnlyDictionary<string, object?> Empty { get; } =
        new System.Collections.ObjectModel.ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>());
}
