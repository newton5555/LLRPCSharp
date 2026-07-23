using LlrpNet.Core.Protocol;

namespace LlrpNet.Protocol.Parameters;

/// <summary>
/// Represents the one-octet header of a type-value LLRP parameter.
/// </summary>
/// <param name="ParameterType">The seven-bit fixed-length TV parameter type.</param>
public readonly record struct LlrpTvParameterHeader(byte ParameterType)
{
    /// <summary>
    /// The encoded TV header length in octets.
    /// </summary>
    public const int EncodedLength = 1;

    /// <summary>
    /// The largest value representable by the seven-bit TV type field.
    /// </summary>
    public const byte MaximumParameterType = 0x7F;

    private const byte TvMarker = 0x80;

    /// <summary>
    /// Parses and validates a TV parameter header.
    /// </summary>
    /// <param name="source">A span beginning at the first octet of a TV parameter.</param>
    /// <returns>The decoded header.</returns>
    /// <exception cref="LlrpProtocolException">The input is truncated or does not contain a valid TV marker and type.</exception>
    public static LlrpTvParameterHeader Decode(ReadOnlySpan<byte> source)
    {
        if (source.IsEmpty)
        {
            throw new LlrpProtocolException(
                LlrpProtocolErrorCode.TruncatedData,
                "An LLRP TV parameter header requires one octet, but no data are available.");
        }

        byte encodedHeader = source[0];
        if ((encodedHeader & TvMarker) == 0)
        {
            throw new LlrpProtocolException(
                LlrpProtocolErrorCode.InvalidParameterEncoding,
                "The most-significant bit in an LLRP TV parameter header must be one.");
        }

        byte parameterType = (byte)(encodedHeader & MaximumParameterType);
        ValidateParameterType(parameterType);
        return new LlrpTvParameterHeader(parameterType);
    }

    /// <summary>
    /// Encodes this TV header.
    /// </summary>
    /// <param name="destination">The destination span.</param>
    /// <exception cref="ArgumentException">The destination is empty.</exception>
    /// <exception cref="LlrpProtocolException">The type is reserved or cannot fit in seven bits.</exception>
    public void Encode(Span<byte> destination)
    {
        if (destination.IsEmpty)
        {
            throw new ArgumentException("The destination must contain at least one octet.", nameof(destination));
        }

        ValidateParameterType(ParameterType);
        destination[0] = (byte)(TvMarker | ParameterType);
    }

    private static void ValidateParameterType(byte parameterType)
    {
        if (parameterType is 0 or > MaximumParameterType)
        {
            throw new LlrpProtocolException(
                LlrpProtocolErrorCode.InvalidParameterType,
                $"TV parameter type {parameterType} is outside the encodable range 1..{MaximumParameterType}.");
        }
    }
}

