namespace LlrpSdk;

using LlrpSdk.Extensions;

/// <summary>
/// Internal, capability-resolved defaults used only when a known reader requires a fully explicit AISpec.
/// </summary>
/// <remarks>
/// This is deliberately not part of <see cref="InventorySettings"/>. Applications express inventory intent there;
/// this type carries compatibility details derived from the connected reader's identity and capability tables.
/// </remarks>
public sealed record InventoryCompilationDefaults(
    IReadOnlyList<ushort> AntennaIds,
    ushort ReceiverSensitivityIndex,
    ushort TransmitPowerIndex,
    ushort HopTableId,
    ushort ChannelIndex);

/// <summary>Supplies a model-specific, capability-resolved standard inventory baseline.</summary>
/// <remarks>
/// Implement this in a reader extension when a device needs explicit standard AISpec values beyond the core
/// sparse baseline. It does not imply a vendor custom Message or Parameter definition.
/// </remarks>
public interface IInventoryProfileContributor
{
    /// <summary>Gets the stable extension identifier used for duplicate detection.</summary>
    public string Id { get; }

    /// <summary>Returns the standard inventory compilation defaults for this connected reader, if any.</summary>
    public InventoryCompilationDefaults? GetCompilationDefaults(InventoryContributionContext context);
}
