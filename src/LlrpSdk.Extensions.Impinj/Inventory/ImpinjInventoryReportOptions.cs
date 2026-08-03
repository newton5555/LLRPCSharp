using LlrpNet.Protocol.Parameters;
using LlrpSdk.Extensions;
using LlrpNet.Protocol.Impinj.Enumerations.V1_0_1;
using LlrpNet.Protocol.Impinj.Parameters.V1_0_1;

using LlrpSdk;

using LlrpNet.Protocol.Parameters.V1_0_1;

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

    public bool IncludeGpsCoordinates { get; init; }
    public bool IncludeOptimizedRead { get; init; }
    public bool IncludeRfDopplerFrequency { get; init; }
    public bool IncludeTxPower { get; init; }
    public bool IncludeXpcWords { get; init; }
    public bool IncludeCrHandle { get; init; }
    public bool IncludeId { get; init; }
    public bool IncludeEnhancedIntegra { get; init; }
    public bool IncludeEndpointIcVerification { get; init; }

    /// <summary>Allows fields without a verified model/firmware profile to be sent explicitly.</summary>
    /// <remarks>Use only after validating the target reader firmware. The default remains fail-closed.</remarks>
    public bool AllowUnverifiedFields { get; init; }

    /// <summary>Gets the optional optimized reads embedded in the report selector (at most two).</summary>
    public IReadOnlyList<ImpinjOptimizedReadOperation> OptimizedReads { get; init; } = [];

    internal bool HasRequestedFields => IncludeSerializedTid || IncludeRfPhaseAngle || IncludePeakRssi ||
        IncludeGpsCoordinates || IncludeOptimizedRead || OptimizedReads.Count != 0 || IncludeRfDopplerFrequency || IncludeTxPower ||
        IncludeXpcWords || IncludeCrHandle || IncludeId || IncludeEnhancedIntegra || IncludeEndpointIcVerification;
}

/// <summary>Describes one Impinj optimized-read operation returned with each tag report.</summary>
public sealed record ImpinjOptimizedReadOperation(
    ushort OpSpecId,
    TagMemoryBank MemoryBank,
    ushort WordPointer,
    ushort WordCount,
    uint AccessPassword = 0);

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
        if (!capabilities.SupportsTagReportContentSelector && !options.AllowUnverifiedFields)
        {
            throw new NotSupportedException(
                $"ImpinjTagReportContentSelector is unavailable: {capabilities.Reason}");
        }
        if (options.IncludeSerializedTid && !capabilities.SupportsSerializedTid && !options.AllowUnverifiedFields)
        {
            throw new NotSupportedException(
                $"Impinj serialized TID reports are unavailable: {capabilities.Reason}");
        }
        if (options.IncludeRfPhaseAngle && !capabilities.SupportsRfPhaseAngle && !options.AllowUnverifiedFields)
        {
            throw new NotSupportedException(
                $"Impinj RF phase angle reports are unavailable: {capabilities.Reason}");
        }
        if (options.IncludePeakRssi && !capabilities.SupportsPeakRssi && !options.AllowUnverifiedFields)
        {
            throw new NotSupportedException(
                $"Impinj peak RSSI reports are unavailable: {capabilities.Reason}");
        }
        EnsureSupported(options.IncludeGpsCoordinates, capabilities.SupportsGpsCoordinates, "GPS coordinate", capabilities.Reason, options.AllowUnverifiedFields);
        EnsureSupported(options.IncludeOptimizedRead || options.OptimizedReads.Count != 0, capabilities.SupportsOptimizedRead, "optimized read", capabilities.Reason, options.AllowUnverifiedFields);
        EnsureSupported(options.IncludeRfDopplerFrequency, capabilities.SupportsRfDopplerFrequency, "RF Doppler frequency", capabilities.Reason, options.AllowUnverifiedFields);
        EnsureSupported(options.IncludeTxPower, capabilities.SupportsTxPower, "transmit power", capabilities.Reason, options.AllowUnverifiedFields);
        EnsureSupported(options.IncludeXpcWords, capabilities.SupportsXpcWords, "XPC words", capabilities.Reason, options.AllowUnverifiedFields);
        EnsureSupported(options.IncludeCrHandle, capabilities.SupportsCrHandle, "CR handle", capabilities.Reason, options.AllowUnverifiedFields);
        EnsureSupported(options.IncludeId, capabilities.SupportsId, "ID", capabilities.Reason, options.AllowUnverifiedFields);
        EnsureSupported(options.IncludeEnhancedIntegra, capabilities.SupportsEnhancedIntegra, "Enhanced Integra", capabilities.Reason, options.AllowUnverifiedFields);
        EnsureSupported(options.IncludeEndpointIcVerification, capabilities.SupportsEndpointIcVerification, "Endpoint IC verification", capabilities.Reason, options.AllowUnverifiedFields);

        if (options.OptimizedReads.Count > 2)
        {
            throw new ArgumentException("Impinj optimized read supports at most two C1G2Read operations.", nameof(options));
        }
        var optimizedReads = options.OptimizedReads.Select(static operation => new C1G2Read(
            operation.OpSpecId,
            operation.AccessPassword,
            (byte)operation.MemoryBank,
            operation.WordPointer,
            operation.WordCount)).ToArray();

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
                ImpinjEnableGPSCoordinates: options.IncludeGpsCoordinates ? new ImpinjEnableGPSCoordinates(ImpinjGPSCoordinatesMode.Enabled, []) : null,
                ImpinjEnableOptimizedRead: options.IncludeOptimizedRead || optimizedReads.Length != 0
                    ? new ImpinjEnableOptimizedRead(ImpinjOptimizedReadMode.Enabled, optimizedReads, [])
                    : null,
                ImpinjEnableRFDopplerFrequency: options.IncludeRfDopplerFrequency ? new ImpinjEnableRFDopplerFrequency(ImpinjRFDopplerFrequencyMode.Enabled, []) : null,
                ImpinjEnableTxPower: options.IncludeTxPower ? new ImpinjEnableTxPower(ImpinjTxPowerReportingModeEnum.Enabled, []) : null,
                ImpinjEnableXPCWords: options.IncludeXpcWords ? new ImpinjEnableXPCWords(ImpinjXPCWordsMode.Enabled, []) : null,
                ImpinjEnableCRHandle: options.IncludeCrHandle ? new ImpinjEnableCRHandle(ImpinjCRHandleMode.Enabled, []) : null,
                ImpinjEnableID: options.IncludeId ? new ImpinjEnableID(ImpinjIDMode.Enabled, []) : null,
                ImpinjEnableEnhancedIntegra: options.IncludeEnhancedIntegra ? new ImpinjEnableEnhancedIntegra(ImpinjEnhancedIntegraMode.Enabled, []) : null,
                ImpinjEnableEndpointICVerification: options.IncludeEndpointIcVerification ? new ImpinjEnableEndpointICVerification(ImpinjEndpointICVerificationReportMode.Enabled, []) : null,
                CustomItems: [])
        ];
    }

    private static void EnsureSupported(bool requested, bool supported, string field, string reason, bool allowUnverified)
    {
        if (requested && !supported && !allowUnverified)
        {
            throw new NotSupportedException($"Impinj {field} reports are unavailable: {reason}");
        }
    }
}
