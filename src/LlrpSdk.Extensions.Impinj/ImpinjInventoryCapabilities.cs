using LlrpNet.Core.Protocol;
using LlrpSdk.Extensions;

namespace LlrpSdk.Extensions.Impinj;

/// <summary>Describes verified vendor inventory features for one concrete Impinj reader identity.</summary>
public sealed record ImpinjInventoryCapabilities(
    bool SupportsTagReportContentSelector,
    bool SupportsSerializedTid,
    bool SupportsRfPhaseAngle,
    bool SupportsPeakRssi,
    bool SupportsTagPopulationEstimation,
    bool SupportsGpsCoordinates,
    bool SupportsOptimizedRead,
    bool SupportsRfDopplerFrequency,
    bool SupportsTxPower,
    bool SupportsXpcWords,
    bool SupportsCrHandle,
    bool SupportsId,
    bool SupportsEnhancedIntegra,
    bool SupportsEndpointIcVerification,
    bool SupportsTagFilterVerification,
    bool SupportsTruncatedReply,
    bool SupportsGen2X,
    bool SupportsRampUpPowerBoost,
    string Reason);

/// <summary>
/// Provides conservative, evidence-backed capability decisions for Impinj inventory extensions.
/// </summary>
/// <remarks>
/// The presence of a generated custom parameter proves only that it can be encoded. It does not prove that a reader
/// model or firmware accepts the parameter. Unknown identities therefore default to no optional vendor extensions.
/// </remarks>
public static class ImpinjInventoryCapabilityCatalog
{
    /// <summary>Gets the verified inventory capabilities for one reader identity and negotiated protocol version.</summary>
    public static ImpinjInventoryCapabilities Get(ReaderExtensionMatchContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.ManufacturerId != ImpinjReaderExtension.ManufacturerId ||
            context.ProtocolVersion != LlrpProtocolVersion.Version101)
        {
            return Unknown;
        }

        if (context.ModelId == 2_001_002 &&
            context.FirmwareVersion.StartsWith("6.4.1.", StringComparison.Ordinal))
        {
            return R420Firmware641;
        }

        return Unknown;
    }

    private static ImpinjInventoryCapabilities R420Firmware641 { get; } = new(
        SupportsTagReportContentSelector: true,
        SupportsSerializedTid: true,
        SupportsRfPhaseAngle: true,
        SupportsPeakRssi: true,
        SupportsTagPopulationEstimation: false,
        SupportsGpsCoordinates: false,
        SupportsOptimizedRead: false,
        SupportsRfDopplerFrequency: false,
        SupportsTxPower: false,
        SupportsXpcWords: false,
        SupportsCrHandle: false,
        SupportsId: false,
        SupportsEnhancedIntegra: false,
        SupportsEndpointIcVerification: false,
        SupportsTagFilterVerification: true,
        SupportsTruncatedReply: false,
        SupportsGen2X: false,
        SupportsRampUpPowerBoost: false,
        Reason: "SDK verification confirmed Serialized TID, RF Phase Angle, and Peak RSSI report fields; tag population estimation was rejected by this firmware.");

    private static ImpinjInventoryCapabilities Unknown { get; } = new(
        SupportsTagReportContentSelector: false,
        SupportsSerializedTid: false,
        SupportsRfPhaseAngle: false,
        SupportsPeakRssi: false,
        SupportsTagPopulationEstimation: false,
        SupportsGpsCoordinates: false,
        SupportsOptimizedRead: false,
        SupportsRfDopplerFrequency: false,
        SupportsTxPower: false,
        SupportsXpcWords: false,
        SupportsCrHandle: false,
        SupportsId: false,
        SupportsEnhancedIntegra: false,
        SupportsEndpointIcVerification: false,
        SupportsTagFilterVerification: false,
        SupportsTruncatedReply: false,
        SupportsGen2X: false,
        SupportsRampUpPowerBoost: false,
        Reason: "No verified inventory capability profile matches this reader.");
}
