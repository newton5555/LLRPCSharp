using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Codecs;

namespace LlrpNet.Protocol.Parameters;

/// <summary>
/// Preserves an unregistered custom parameter's vendor identifier, subtype, and vendor data.
/// </summary>
public sealed class RawCustomParameter : ILlrpParameter
{
    /// <summary>
    /// The wire type reserved by LLRP for custom parameters.
    /// </summary>
    public const ushort CustomParameterType = 1023;

    /// <summary>
    /// The number of value octets preceding the vendor-defined data.
    /// </summary>
    public const int MetadataLength = 8;

    private readonly byte[] _data;

    /// <summary>
    /// Initializes a new instance of the <see cref="RawCustomParameter"/> class.
    /// </summary>
    /// <param name="version">The protocol version under which the parameter was decoded.</param>
    /// <param name="vendorId">The IANA Private Enterprise Number identifying the vendor.</param>
    /// <param name="subtype">The vendor-defined parameter subtype.</param>
    /// <param name="data">The remaining uninterpreted vendor data.</param>
    public RawCustomParameter(
        LlrpProtocolVersion version,
        uint vendorId,
        uint subtype,
        ReadOnlySpan<byte> data)
    {
        LlrpCodecValidation.ValidateVersion(version, nameof(version));
        LlrpCodecValidation.ValidateTlvPayloadLength(checked(MetadataLength + data.Length), nameof(data));

        Version = version;
        VendorId = vendorId;
        Subtype = subtype;
        _data = data.ToArray();
    }

    /// <summary>
    /// Gets the protocol version under which this parameter was decoded.
    /// </summary>
    public LlrpProtocolVersion Version { get; }

    /// <summary>
    /// Gets the IANA Private Enterprise Number identifying the vendor.
    /// </summary>
    public uint VendorId { get; }

    /// <summary>
    /// Gets the vendor-defined parameter subtype.
    /// </summary>
    public uint Subtype { get; }

    /// <summary>
    /// Gets the exact uninterpreted vendor data following the vendor identifier and subtype.
    /// </summary>
    public ReadOnlyMemory<byte> Data => _data;
}
