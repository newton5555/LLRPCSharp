using System.Buffers.Binary;
using LlrpNet.Core.Protocol;

namespace LlrpNet.Protocol.Parameters;

/// <summary>
/// Represents the four-octet header of a type-length-value LLRP parameter.
/// </summary>
/// <param name="ParameterType">The ten-bit TLV parameter type.</param>
/// <param name="ParameterLength">The complete parameter length, including this header.</param>
public readonly record struct LlrpTlvParameterHeader(
    ushort ParameterType,
    ushort ParameterLength)
{
    /// <summary>
    /// The encoded TLV header length in octets.
    /// </summary>
    public const int EncodedLength = 4;

    /// <summary>
    /// The first value reserved for TLV parameters.
    /// </summary>
    public const ushort MinimumParameterType = 128;

    /// <summary>
    /// The largest value representable by the ten-bit TLV type field.
    /// </summary>
    public const ushort MaximumParameterType = 0x03FF;

    private const ushort ReservedBitsMask = 0xFC00;

    /// <summary>
    /// Parses and validates a TLV parameter header.
    /// </summary>
    /// <param name="source">A span beginning at the first octet of a TLV parameter.</param>
    /// <returns>The decoded header.</returns>
    /// <exception cref="LlrpProtocolException">The input is truncated or contains an invalid header.</exception>
    public static LlrpTlvParameterHeader Decode(ReadOnlySpan<byte> source)
    {
        if (source.Length < EncodedLength)
        {
            throw new LlrpProtocolException(
                LlrpProtocolErrorCode.TruncatedData,
                $"An LLRP TLV parameter header requires {EncodedLength} octets, but only {source.Length} are available.");
        }

        ushort reservedAndType = BinaryPrimitives.ReadUInt16BigEndian(source);
        if ((reservedAndType & ReservedBitsMask) != 0)
        {
            throw new LlrpProtocolException(
                LlrpProtocolErrorCode.InvalidParameterEncoding,
                "The TLV marker and reserved bits in an LLRP parameter header must be zero.");
        }

        ushort parameterType = (ushort)(reservedAndType & MaximumParameterType);
        ValidateParameterType(parameterType);

        ushort parameterLength = BinaryPrimitives.ReadUInt16BigEndian(source[2..]);
        ValidateParameterLength(parameterLength);
        return new LlrpTlvParameterHeader(parameterType, parameterLength);
    }

    /// <summary>
    /// Encodes this TLV header in network byte order.
    /// </summary>
    /// <param name="destination">The destination span.</param>
    /// <exception cref="ArgumentException">The destination is shorter than four octets.</exception>
    /// <exception cref="LlrpProtocolException">A field is outside the valid TLV range.</exception>
    public void Encode(Span<byte> destination)
    {
        if (destination.Length < EncodedLength)
        {
            throw new ArgumentException(
                $"The destination must contain at least {EncodedLength} octets.",
                nameof(destination));
        }

        ValidateParameterType(ParameterType);
        ValidateParameterLength(ParameterLength);
        BinaryPrimitives.WriteUInt16BigEndian(destination, ParameterType);
        BinaryPrimitives.WriteUInt16BigEndian(destination[2..], ParameterLength);
    }

    private static void ValidateParameterType(ushort parameterType)
    {
        if (parameterType is < MinimumParameterType or > MaximumParameterType)
        {
            throw new LlrpProtocolException(
                LlrpProtocolErrorCode.InvalidParameterType,
                $"TLV parameter type {parameterType} is outside the encodable range {MinimumParameterType}..{MaximumParameterType}.");
        }
    }

    private static void ValidateParameterLength(ushort parameterLength)
    {
        if (parameterLength < EncodedLength)
        {
            throw new LlrpProtocolException(
                LlrpProtocolErrorCode.InvalidParameterLength,
                $"A TLV parameter length cannot be smaller than {EncodedLength} octets.");
        }
    }
}

