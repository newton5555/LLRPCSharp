using LlrpNet.Protocol.Parameters;

namespace LlrpNet.Protocol.Registry;

/// <summary>
/// Identifies the wire representation selected for an LLRP parameter.
/// </summary>
/// <param name="ParameterType">The seven-bit TV or ten-bit TLV parameter type.</param>
/// <param name="Encoding">The header encoding used for the parameter.</param>
/// <param name="VendorId">The custom-parameter vendor identifier, when applicable.</param>
/// <param name="ParameterSubtype">The custom-parameter subtype, when applicable.</param>
public readonly record struct LlrpParameterWireIdentity(
    ushort ParameterType,
    LlrpParameterEncoding Encoding,
    uint? VendorId,
    uint? ParameterSubtype);
