using System.Buffers.Binary;

namespace LlrpNet.Core.Protocol;

/// <summary>
/// Represents the fixed ten-octet header shared by all LLRP messages.
/// </summary>
/// <param name="Version">The protocol version encoded in the three-bit version field.</param>
/// <param name="MessageType">The ten-bit LLRP message type.</param>
/// <param name="MessageLength">The total message length, including this header.</param>
/// <param name="MessageId">The channel-local request/response correlation identifier.</param>
public readonly record struct LlrpMessageHeader(
    LlrpProtocolVersion Version,
    ushort MessageType,
    uint MessageLength,
    uint MessageId)
{
    /// <summary>
    /// The encoded length of an LLRP message header in octets.
    /// </summary>
    public const int EncodedLength = 10;

    /// <summary>
    /// The largest value that fits in the ten-bit message type field.
    /// </summary>
    public const ushort MaximumMessageType = 0x03FF;

    private const ushort ReservedBitsMask = 0xE000;
    private const ushort VersionMask = 0x1C00;
    private const int VersionShift = 10;

    /// <summary>
    /// Parses and validates an LLRP message header.
    /// </summary>
    /// <param name="source">A span beginning at the first octet of a complete header.</param>
    /// <returns>The decoded header.</returns>
    /// <exception cref="LlrpProtocolException">
    /// The source is truncated or contains invalid reserved bits, version, type, or length fields.
    /// </exception>
    public static LlrpMessageHeader Decode(ReadOnlySpan<byte> source)
    {
        if (source.Length < EncodedLength)
        {
            throw new LlrpProtocolException(
                LlrpProtocolErrorCode.TruncatedData,
                $"An LLRP message header requires {EncodedLength} octets, but only {source.Length} are available.");
        }

        ushort versionAndType = BinaryPrimitives.ReadUInt16BigEndian(source);
        if ((versionAndType & ReservedBitsMask) != 0)
        {
            throw new LlrpProtocolException(
                LlrpProtocolErrorCode.InvalidReservedBits,
                "The reserved bits in an LLRP message header must be zero.");
        }

        var version = (LlrpProtocolVersion)((versionAndType & VersionMask) >> VersionShift);
        ValidateVersion(version);

        ushort messageType = (ushort)(versionAndType & MaximumMessageType);
        uint messageLength = BinaryPrimitives.ReadUInt32BigEndian(source[2..]);
        if (messageLength < EncodedLength)
        {
            throw new LlrpProtocolException(
                LlrpProtocolErrorCode.InvalidMessageLength,
                $"The encoded LLRP message length {messageLength} is smaller than the {EncodedLength}-octet header.");
        }

        uint messageId = BinaryPrimitives.ReadUInt32BigEndian(source[6..]);
        return new LlrpMessageHeader(version, messageType, messageLength, messageId);
    }

    /// <summary>
    /// Encodes this header in LLRP network byte order.
    /// </summary>
    /// <param name="destination">The destination span.</param>
    /// <exception cref="ArgumentException">The destination is shorter than ten octets.</exception>
    /// <exception cref="LlrpProtocolException">A field cannot be encoded as a valid LLRP header.</exception>
    public void Encode(Span<byte> destination)
    {
        if (destination.Length < EncodedLength)
        {
            throw new ArgumentException(
                $"The destination must contain at least {EncodedLength} octets.",
                nameof(destination));
        }

        ValidateVersion(Version);
        if (MessageType > MaximumMessageType)
        {
            throw new LlrpProtocolException(
                LlrpProtocolErrorCode.InvalidMessageType,
                $"Message type {MessageType} does not fit in the ten-bit LLRP message type field.");
        }

        if (MessageLength < EncodedLength)
        {
            throw new LlrpProtocolException(
                LlrpProtocolErrorCode.InvalidMessageLength,
                $"An LLRP message length cannot be smaller than {EncodedLength} octets.");
        }

        ushort versionAndType = (ushort)(((ushort)Version << VersionShift) | MessageType);
        BinaryPrimitives.WriteUInt16BigEndian(destination, versionAndType);
        BinaryPrimitives.WriteUInt32BigEndian(destination[2..], MessageLength);
        BinaryPrimitives.WriteUInt32BigEndian(destination[6..], MessageId);
    }

    private static void ValidateVersion(LlrpProtocolVersion version)
    {
        if (version is < LlrpProtocolVersion.Version101 or > LlrpProtocolVersion.Version20)
        {
            throw new LlrpProtocolException(
                LlrpProtocolErrorCode.UnsupportedVersion,
                $"LLRP header version value {(byte)version} is not supported.");
        }
    }
}

