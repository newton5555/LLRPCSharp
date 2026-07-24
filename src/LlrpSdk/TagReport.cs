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
    ushort? AntennaId,
    sbyte? PeakRssi,
    ushort? ChannelIndex,
    ushort? SeenCount);
