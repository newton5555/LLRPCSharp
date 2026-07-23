using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Codecs;

namespace LlrpNet.Protocol.Messages;

/// <summary>
/// Preserves an unregistered CUSTOM_MESSAGE vendor identifier, subtype, and vendor-defined data.
/// </summary>
public sealed class RawCustomMessage : ILlrpMessage
{
    /// <summary>
    /// The LLRP wire type reserved for CUSTOM_MESSAGE.
    /// </summary>
    public const ushort CustomMessageType = 1023;

    /// <summary>
    /// The number of payload octets preceding the vendor-defined Data.
    /// </summary>
    public const int MetadataLength = 5;

    private readonly byte[] _data;

    /// <summary>
    /// Initializes a new instance of the <see cref="RawCustomMessage"/> class.
    /// </summary>
    /// <param name="version">The protocol version under which the message was decoded.</param>
    /// <param name="messageId">The common-header correlation identifier.</param>
    /// <param name="vendorId">The IANA Private Enterprise Number identifying the vendor.</param>
    /// <param name="messageSubtype">The vendor-defined one-octet message subtype.</param>
    /// <param name="data">The remaining uninterpreted vendor-defined Data.</param>
    public RawCustomMessage(
        LlrpProtocolVersion version,
        uint messageId,
        uint vendorId,
        byte messageSubtype,
        ReadOnlySpan<byte> data)
    {
        LlrpCodecValidation.ValidateVersion(version, nameof(version));

        Version = version;
        MessageId = messageId;
        VendorId = vendorId;
        MessageSubtype = messageSubtype;
        _data = data.ToArray();
    }

    /// <summary>
    /// Gets the protocol version under which this message was decoded.
    /// </summary>
    public LlrpProtocolVersion Version { get; }

    /// <inheritdoc />
    public uint MessageId { get; }

    /// <summary>
    /// Gets the IANA Private Enterprise Number identifying the vendor.
    /// </summary>
    public uint VendorId { get; }

    /// <summary>
    /// Gets the vendor-defined one-octet message subtype.
    /// </summary>
    public byte MessageSubtype { get; }

    /// <summary>
    /// Gets the exact uninterpreted vendor-defined Data following the metadata.
    /// </summary>
    public ReadOnlyMemory<byte> Data => _data;
}
