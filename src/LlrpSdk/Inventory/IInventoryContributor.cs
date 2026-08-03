using LlrpNet.Protocol.Parameters;
using LlrpNet.Core.Protocol;

namespace LlrpSdk.Extensions;

/// <summary>Contributes vendor-specific parameters to an SDK-managed inventory ROSpec.</summary>
public interface IInventoryContributor
{
    /// <summary>Gets the stable contributor identifier.</summary>
    public string Id { get; }

    /// <summary>Adds optional vendor parameters for one managed inventory operation.</summary>
    public void Contribute(InventoryContributionContext context, InventoryExtensionBuilder extensions);
}

/// <summary>Supplies inventory intent and initialized reader facts to an active contributor.</summary>
/// <remarks>
/// Contributors use identity, firmware, capabilities, and the negotiated protocol version to decide whether a
/// vendor parameter is safe for this concrete reader. They must not infer support solely from a registered codec.
/// </remarks>
public sealed record InventoryContributionContext(
    InventorySettings Settings,
    ReaderIdentity Identity,
    ReaderCapabilities Capabilities,
    LlrpProtocolVersion ProtocolVersion);

/// <summary>Collects vendor parameters for supported locations in the standard ROSpec graph.</summary>
public sealed class InventoryExtensionBuilder
{
    private readonly List<ILlrpParameter> roReportSpecCustomItems = [];
    private readonly List<ILlrpParameter> c1G2InventoryCommandCustomItems = [];

    /// <summary>Adds a custom parameter to the generated standard <c>ROReportSpec</c>.</summary>
    public void AddRoReportSpecCustomItem(ILlrpParameter item)
    {
        ArgumentNullException.ThrowIfNull(item);
        roReportSpecCustomItems.Add(item);
    }

    /// <summary>Adds a custom parameter to the generated standard <c>C1G2InventoryCommand</c>.</summary>
    public void AddC1G2InventoryCommandCustomItem(ILlrpParameter item)
    {
        ArgumentNullException.ThrowIfNull(item);
        c1G2InventoryCommandCustomItems.Add(item);
    }

    internal IReadOnlyList<ILlrpParameter> RoReportSpecCustomItems =>
        roReportSpecCustomItems.Count == 0 ? [] : roReportSpecCustomItems.AsReadOnly();

    internal IReadOnlyList<ILlrpParameter> C1G2InventoryCommandCustomItems =>
        c1G2InventoryCommandCustomItems.Count == 0 ? [] : c1G2InventoryCommandCustomItems.AsReadOnly();
}
