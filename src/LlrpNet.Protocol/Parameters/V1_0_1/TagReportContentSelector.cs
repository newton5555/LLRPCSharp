using System.Collections.ObjectModel;

namespace LlrpNet.Protocol.Parameters.V1_0_1;

/// <summary>
/// Selects optional metadata included in LLRP 1.0.1 tag reports.
/// </summary>
public sealed class TagReportContentSelector : ILlrpParameter
{
    /// <summary>The LLRP TLV parameter type.</summary>
    public const ushort ParameterType = 238;

    private readonly ReadOnlyCollection<ILlrpParameter> _airProtocolEpcMemorySelectors;

    /// <summary>
    /// Initializes a tag-report content selector.
    /// </summary>
    public TagReportContentSelector(
        bool enableRoSpecId = false,
        bool enableSpecIndex = false,
        bool enableInventoryParameterSpecId = false,
        bool enableAntennaId = false,
        bool enableChannelIndex = false,
        bool enablePeakRssi = false,
        bool enableFirstSeenTimestamp = false,
        bool enableLastSeenTimestamp = false,
        bool enableTagSeenCount = false,
        bool enableAccessSpecId = false,
        IEnumerable<ILlrpParameter>? airProtocolEpcMemorySelectors = null)
    {
        ILlrpParameter[] selectorArray = airProtocolEpcMemorySelectors?.ToArray() ?? [];
        if (selectorArray.Any(static parameter => parameter is null))
        {
            throw new ArgumentException(
                "An air-protocol EPC memory selector collection cannot contain null entries.",
                nameof(airProtocolEpcMemorySelectors));
        }

        EnableRoSpecId = enableRoSpecId;
        EnableSpecIndex = enableSpecIndex;
        EnableInventoryParameterSpecId = enableInventoryParameterSpecId;
        EnableAntennaId = enableAntennaId;
        EnableChannelIndex = enableChannelIndex;
        EnablePeakRssi = enablePeakRssi;
        EnableFirstSeenTimestamp = enableFirstSeenTimestamp;
        EnableLastSeenTimestamp = enableLastSeenTimestamp;
        EnableTagSeenCount = enableTagSeenCount;
        EnableAccessSpecId = enableAccessSpecId;
        _airProtocolEpcMemorySelectors = Array.AsReadOnly(selectorArray);
    }

    /// <summary>Gets whether ROSpecID is included.</summary>
    public bool EnableRoSpecId { get; }

    /// <summary>Gets whether SpecIndex is included.</summary>
    public bool EnableSpecIndex { get; }

    /// <summary>Gets whether InventoryParameterSpecID is included.</summary>
    public bool EnableInventoryParameterSpecId { get; }

    /// <summary>Gets whether AntennaID is included.</summary>
    public bool EnableAntennaId { get; }

    /// <summary>Gets whether ChannelIndex is included.</summary>
    public bool EnableChannelIndex { get; }

    /// <summary>Gets whether PeakRSSI is included.</summary>
    public bool EnablePeakRssi { get; }

    /// <summary>Gets whether FirstSeenTimestamp is included.</summary>
    public bool EnableFirstSeenTimestamp { get; }

    /// <summary>Gets whether LastSeenTimestamp is included.</summary>
    public bool EnableLastSeenTimestamp { get; }

    /// <summary>Gets whether TagSeenCount is included.</summary>
    public bool EnableTagSeenCount { get; }

    /// <summary>Gets whether AccessSpecID is included.</summary>
    public bool EnableAccessSpecId { get; }

    /// <summary>Gets AirProtocolEPCMemorySelector choices in wire order.</summary>
    public IReadOnlyList<ILlrpParameter> AirProtocolEpcMemorySelectors => _airProtocolEpcMemorySelectors;
}
