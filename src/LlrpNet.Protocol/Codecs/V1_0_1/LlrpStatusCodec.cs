using System.Buffers.Binary;
using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Parameters;
using LlrpNet.Protocol.Parameters.V1_0_1;
using LlrpNet.Protocol.Registry;

namespace LlrpNet.Protocol.Codecs.V1_0_1;

internal sealed class LlrpStatusCodec(LlrpCodecRegistry registry)
    : LlrpParameterCodec<LlrpStatus>
{
    private const ushort FieldErrorParameterType = 288;
    private const ushort ParameterErrorParameterType = 289;

    private readonly LlrpCodecRegistry _registry = registry ?? throw new ArgumentNullException(nameof(registry));

    public override LlrpStatus Decode(
        LlrpProtocolVersion version,
        ReadOnlySpan<byte> payload)
    {
        Llrp101CodecValidation.ValidateVersion(version);
        if (payload.Length < sizeof(ushort))
        {
            throw new LlrpProtocolException(
                LlrpProtocolErrorCode.TruncatedData,
                "LLRPStatus requires a two-octet StatusCode field.");
        }

        var statusCode = (LlrpStatusCode)BinaryPrimitives.ReadUInt16BigEndian(payload);
        if (!LlrpStatus.IsDefined(statusCode))
        {
            throw new LlrpProtocolException(
                LlrpProtocolErrorCode.InvalidParameterEncoding,
                $"LLRPStatus code {(ushort)statusCode} is not defined by LLRP 1.0.1.");
        }

        string errorDescription = Llrp101Utf8.Decode(
            payload[sizeof(ushort)..],
            nameof(LlrpStatus.ErrorDescription),
            out int descriptionLength);
        int offset = sizeof(ushort) + descriptionLength;
        List<ILlrpParameter> errorParameters = DecodeErrorParameters(version, payload[offset..]);
        return new LlrpStatus(statusCode, errorDescription, errorParameters);
    }

    public override int GetEncodedPayloadLength(
        LlrpProtocolVersion version,
        LlrpStatus parameter)
    {
        Llrp101CodecValidation.ValidateVersion(version);
        ValidateStatusCode(parameter.StatusCode);
        ValidateErrorParameterSequence(version, parameter.ErrorParameters);
        return checked(
            sizeof(ushort)
            + Llrp101Utf8.GetEncodedLength(
                parameter.ErrorDescription,
                nameof(LlrpStatus.ErrorDescription))
            + Llrp101ParameterSequence.GetEncodedLength(
                _registry,
                version,
                parameter.ErrorParameters));
    }

    public override int Encode(
        LlrpProtocolVersion version,
        LlrpStatus parameter,
        Span<byte> destination)
    {
        int expectedLength = GetEncodedPayloadLength(version, parameter);
        if (destination.Length != expectedLength)
        {
            throw new ArgumentException(
                $"LLRPStatus requires an exact {expectedLength}-octet payload destination.",
                nameof(destination));
        }

        BinaryPrimitives.WriteUInt16BigEndian(destination, (ushort)parameter.StatusCode);
        int offset = sizeof(ushort);
        offset += Llrp101Utf8.Encode(
            parameter.ErrorDescription,
            destination[offset..],
            nameof(LlrpStatus.ErrorDescription));
        offset += Llrp101ParameterSequence.Encode(
            _registry,
            version,
            parameter.ErrorParameters,
            destination[offset..]);
        return offset;
    }

    private List<ILlrpParameter> DecodeErrorParameters(
        LlrpProtocolVersion version,
        ReadOnlySpan<byte> source)
    {
        var parameters = new List<ILlrpParameter>();
        int offset = 0;
        ushort lastType = 0;
        while (offset < source.Length)
        {
            LlrpTlvParameterHeader header = LlrpTlvParameterHeader.Decode(source[offset..]);
            ValidateErrorParameterTypeAndOrder(header.ParameterType, lastType, forEncoding: false);

            LlrpParameterDecodeResult result = _registry.DecodeParameter(version, source[offset..]);
            if (result.BytesConsumed <= 0 || result.BytesConsumed > source.Length - offset)
            {
                throw new InvalidOperationException(
                    $"The parameter registry returned invalid consumed length {result.BytesConsumed}.");
            }

            parameters.Add(result.Parameter);
            lastType = header.ParameterType;
            offset += result.BytesConsumed;
        }

        return parameters;
    }

    private void ValidateErrorParameterSequence(
        LlrpProtocolVersion version,
        IReadOnlyList<ILlrpParameter> parameters)
    {
        ushort lastType = 0;
        foreach (ILlrpParameter parameter in parameters)
        {
            byte[] encoded = _registry.EncodeParameter(version, parameter);
            LlrpTlvParameterHeader header;
            try
            {
                header = LlrpTlvParameterHeader.Decode(encoded);
            }
            catch (LlrpProtocolException exception)
            {
                throw new ArgumentException(
                    "LLRPStatus error parameters must use TLV encoding.",
                    nameof(parameters),
                    exception);
            }

            ValidateErrorParameterTypeAndOrder(header.ParameterType, lastType, forEncoding: true);
            lastType = header.ParameterType;
        }
    }

    private static void ValidateErrorParameterTypeAndOrder(
        ushort parameterType,
        ushort previousType,
        bool forEncoding)
    {
        bool valid = parameterType is FieldErrorParameterType or ParameterErrorParameterType
            && previousType < parameterType;
        if (valid)
        {
            return;
        }

        string message =
            "LLRPStatus may contain at most one FieldError (type 288), followed by at most one " +
            "ParameterError (type 289).";
        if (forEncoding)
        {
            throw new ArgumentException(message, "parameters");
        }

        throw new LlrpProtocolException(
            LlrpProtocolErrorCode.InvalidParameterEncoding,
            message);
    }

    private static void ValidateStatusCode(LlrpStatusCode statusCode)
    {
        if (!LlrpStatus.IsDefined(statusCode))
        {
            throw new ArgumentOutOfRangeException(
                nameof(statusCode),
                statusCode,
                "The status code must be defined by LLRP 1.0.1.");
        }
    }
}
