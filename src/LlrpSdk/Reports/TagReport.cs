namespace LlrpSdk;

/// <summary>
/// Represents one tag observation projected from a version-specific LLRP access report.
/// </summary>
/// <remarks>
/// The value is intentionally independent from generated protocol parameter types. Vendor-specific report data is
/// added by the extension pipeline in a later milestone.
/// </remarks>
public sealed record TagReport(
    ReadOnlyMemory<byte> ElectronicProductCode,
    uint? RoSpecId,
    ushort? SpecIndex,
    ushort? InventoryParameterSpecId,
    ushort? AntennaId,
    sbyte? PeakRssi,
    ushort? ChannelIndex,
    TagTimestamp? FirstSeen,
    TagTimestamp? LastSeen,
    ushort? SeenCount,
    uint? AccessSpecId,
    IReadOnlyList<TagAccessOperationResult>? AccessOperationResults = null,
    IReadOnlyDictionary<string, object?>? Extensions = null,
    int? EpcBitLength = null)
{
    /// <summary>
    /// Gets the EPC as an uppercase hexadecimal string, or <see cref="string.Empty"/> when no EPC is present.
    /// </summary>
    /// <remarks>
    /// The string is derived directly from <see cref="ElectronicProductCode"/>; when the EPC bit length is not a
    /// multiple of eight, the final byte may contain padding bits and <see cref="EpcBitLength"/> carries the exact
    /// number of significant bits.
    /// </remarks>
    public string EpcHex => ElectronicProductCode.IsEmpty ? string.Empty : Convert.ToHexString(ElectronicProductCode.Span);
}
