using System.Buffers.Binary;
using System.Text;
using LlrpNet.Core.Protocol;

namespace LlrpNet.Protocol.Codecs.V1_0_1;

internal static class Llrp101Utf8
{
    private static readonly UTF8Encoding StrictEncoding = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static string Decode(
        ReadOnlySpan<byte> source,
        string fieldName,
        out int bytesConsumed)
    {
        if (source.Length < sizeof(ushort))
        {
            throw new LlrpProtocolException(
                LlrpProtocolErrorCode.TruncatedData,
                $"The {fieldName} utf8v field requires a two-octet byte-length prefix.");
        }

        ushort byteLength = BinaryPrimitives.ReadUInt16BigEndian(source);
        if (source.Length - sizeof(ushort) < byteLength)
        {
            throw new LlrpProtocolException(
                LlrpProtocolErrorCode.TruncatedData,
                $"The {fieldName} utf8v field declares {byteLength} UTF-8 octets, " +
                $"but only {source.Length - sizeof(ushort)} are available.");
        }

        try
        {
            bytesConsumed = sizeof(ushort) + byteLength;
            return StrictEncoding.GetString(source.Slice(sizeof(ushort), byteLength));
        }
        catch (DecoderFallbackException)
        {
            throw new LlrpProtocolException(
                LlrpProtocolErrorCode.InvalidParameterEncoding,
                $"The {fieldName} utf8v field contains malformed UTF-8.");
        }
    }

    public static int GetEncodedLength(string value, string fieldName)
    {
        ArgumentNullException.ThrowIfNull(value);

        int byteLength;
        try
        {
            byteLength = StrictEncoding.GetByteCount(value);
        }
        catch (EncoderFallbackException exception)
        {
            throw new ArgumentException(
                $"The {fieldName} value contains an unpaired UTF-16 surrogate and cannot be encoded as UTF-8.",
                fieldName,
                exception);
        }

        if (byteLength > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                fieldName,
                byteLength,
                $"The {fieldName} UTF-8 representation cannot exceed {ushort.MaxValue} octets.");
        }

        return sizeof(ushort) + byteLength;
    }

    public static int Encode(
        string value,
        Span<byte> destination,
        string fieldName)
    {
        int encodedLength = GetEncodedLength(value, fieldName);
        if (destination.Length < encodedLength)
        {
            throw new ArgumentException(
                $"The {fieldName} destination requires at least {encodedLength} octets.",
                nameof(destination));
        }

        int byteLength = encodedLength - sizeof(ushort);
        BinaryPrimitives.WriteUInt16BigEndian(destination, (ushort)byteLength);
        StrictEncoding.GetBytes(value, destination[sizeof(ushort)..encodedLength]);
        return encodedLength;
    }
}
