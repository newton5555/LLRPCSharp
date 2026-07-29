using LlrpNet.Protocol.Parameters;
using LlrpSdk.Extensions;
using LlrpSdk.Extensions.Impinj.Enumerations.V1_0_1;
using LlrpSdk.Extensions.Impinj.Parameters.V1_0_1;

namespace LlrpSdk.Extensions.Impinj;

/// <summary>Requests optional Impinj fields in reports from an SDK-managed inventory operation.</summary>
/// <remarks>
/// Set this value under <see cref="ExtensionKey"/> in <see cref="InventorySettings.Extensions"/>. The reader's
/// concrete model and firmware must have a verified capability profile before any requested field is sent.
/// </remarks>
public sealed record ImpinjInventoryReportOptions
{
    /// <summary>Gets the stable <see cref="InventorySettings.Extensions"/> key for this value.</summary>
    public const string ExtensionKey = "impinj.inventoryReport";

    /// <summary>Gets whether serialized TID should be requested.</summary>
    public bool IncludeSerializedTid { get; init; }

    /// <summary>Gets whether RF phase angle should be requested.</summary>
    public bool IncludeRfPhaseAngle { get; init; }

    /// <summary>Gets whether Impinj peak RSSI should be requested.</summary>
    public bool IncludePeakRssi { get; init; }

    internal bool HasRequestedFields => IncludeSerializedTid || IncludeRfPhaseAngle || IncludePeakRssi;
}

/// <summary>Builds the generated Impinj report selector only after a verified capability decision.</summary>
public static class ImpinjInventoryReportConfigurator
{
    /// <summary>Builds the Impinj custom items for the standard ROSpec report specification.</summary>
    public static IReadOnlyList<ILlrpParameter> BuildCustomItems(
        ReaderExtensionMatchContext reader,
        ImpinjInventoryReportOptions options)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(options);
        if (!options.HasRequestedFields)
        {
            return [];
        }

        ImpinjInventoryCapabilities capabilities = ImpinjInventoryCapabilityCatalog.Get(reader);
        if (!capabilities.SupportsTagReportContentSelector)
        {
            throw new NotSupportedException(
                $"ImpinjTagReportContentSelector is unavailable: {capabilities.Reason}");
        }
        if (options.IncludeSerializedTid && !capabilities.SupportsSerializedTid)
        {
            throw new NotSupportedException(
                $"Impinj serialized TID reports are unavailable: {capabilities.Reason}");
        }
        if (options.IncludeRfPhaseAngle && !capabilities.SupportsRfPhaseAngle)
        {
            throw new NotSupportedException(
                $"Impinj RF phase angle reports are unavailable: {capabilities.Reason}");
        }
        if (options.IncludePeakRssi && !capabilities.SupportsPeakRssi)
        {
            throw new NotSupportedException(
                $"Impinj peak RSSI reports are unavailable: {capabilities.Reason}");
        }

        return
        [
            new ImpinjTagReportContentSelector(
                options.IncludeSerializedTid
                    ? new ImpinjEnableSerializedTID(ImpinjSerializedTIDMode.Enabled, [])
                    : null,
                options.IncludeRfPhaseAngle
                    ? new ImpinjEnableRFPhaseAngle(ImpinjRFPhaseAngleMode.Enabled, [])
                    : null,
                options.IncludePeakRssi
                    ? new ImpinjEnablePeakRSSI(ImpinjPeakRSSIMode.Enabled, [])
                    : null,
                ImpinjEnableGPSCoordinates: null,
                ImpinjEnableOptimizedRead: null,
                ImpinjEnableRFDopplerFrequency: null,
                ImpinjEnableTxPower: null,
                ImpinjEnableXPCWords: null,
                ImpinjEnableCRHandle: null,
                ImpinjEnableID: null,
                ImpinjEnableEnhancedIntegra: null,
                ImpinjEnableEndpointICVerification: null,
                CustomItems: [])
        ];
    }
}
