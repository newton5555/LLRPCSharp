using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Messages.V1_0_1;
using LlrpNet.Protocol.Parameters;
using LlrpNet.Protocol.Parameters.V1_0_1;
using LlrpNet.Protocol.Registry;

namespace LlrpNet.Protocol.Codecs.V1_0_1;

internal sealed class GetReaderCapabilitiesResponseCodec(LlrpCodecRegistry registry)
    : LlrpMessageCodec<GetReaderCapabilitiesResponse>
{
    private readonly LlrpCodecRegistry _registry = registry ?? throw new ArgumentNullException(nameof(registry));

    public override GetReaderCapabilitiesResponse Decode(
        LlrpMessageHeader header,
        ReadOnlySpan<byte> payload)
    {
        Llrp101CodecValidation.ValidateHeader(
            header,
            GetReaderCapabilitiesResponse.MessageType,
            payload.Length);
        if (payload.IsEmpty)
        {
            throw new LlrpProtocolException(
                LlrpProtocolErrorCode.InvalidParameterEncoding,
                "GET_READER_CAPABILITIES_RESPONSE requires LLRPStatus as its first parameter.");
        }

        if ((payload[0] & 0x80) != 0)
        {
            throw new LlrpProtocolException(
                LlrpProtocolErrorCode.InvalidParameterEncoding,
                "GET_READER_CAPABILITIES_RESPONSE requires a TLV LLRPStatus as its first parameter.");
        }

        LlrpTlvParameterHeader statusHeader = LlrpTlvParameterHeader.Decode(payload);
        if (statusHeader.ParameterType != LlrpStatus.ParameterType)
        {
            throw new LlrpProtocolException(
                LlrpProtocolErrorCode.InvalidParameterType,
                $"GET_READER_CAPABILITIES_RESPONSE requires parameter type {LlrpStatus.ParameterType} first, " +
                $"but found type {statusHeader.ParameterType}.");
        }

        LlrpParameterDecodeResult statusResult = _registry.DecodeParameter(
            LlrpProtocolVersion.Version101,
            payload);
        if (statusResult.Parameter is not LlrpStatus status)
        {
            throw new LlrpProtocolException(
                LlrpProtocolErrorCode.InvalidParameterEncoding,
                "The required first LLRPStatus parameter has no LLRP 1.0.1 typed registration.");
        }

        if (statusResult.BytesConsumed <= 0 || statusResult.BytesConsumed > payload.Length)
        {
            throw new InvalidOperationException(
                $"The parameter registry returned invalid consumed length {statusResult.BytesConsumed}.");
        }

        List<ILlrpParameter> parameters = Llrp101ParameterSequence.Decode(
            _registry,
            LlrpProtocolVersion.Version101,
            payload[statusResult.BytesConsumed..]);
        ValidateCapabilityParameterSequence(
            LlrpProtocolVersion.Version101,
            parameters,
            forEncoding: false);
        return new GetReaderCapabilitiesResponse(header.MessageId, status, parameters);
    }

    public override int GetEncodedPayloadLength(
        LlrpProtocolVersion version,
        GetReaderCapabilitiesResponse message)
    {
        Llrp101CodecValidation.ValidateVersion(version);
        ValidateCapabilityParameterSequence(version, message.Parameters, forEncoding: true);
        return checked(
            _registry.GetEncodedParameterLength(version, message.Status)
            + Llrp101ParameterSequence.GetEncodedLength(
                _registry,
                version,
                message.Parameters));
    }

    public override int Encode(
        LlrpProtocolVersion version,
        GetReaderCapabilitiesResponse message,
        Span<byte> destination)
    {
        int expectedLength = GetEncodedPayloadLength(version, message);
        if (destination.Length != expectedLength)
        {
            throw new ArgumentException(
                $"GET_READER_CAPABILITIES_RESPONSE requires an exact {expectedLength}-octet payload destination.",
                nameof(destination));
        }

        int offset = _registry.EncodeParameter(version, message.Status, destination);
        offset += Llrp101ParameterSequence.Encode(
            _registry,
            version,
            message.Parameters,
            destination[offset..]);
        return offset;
    }

    private void ValidateCapabilityParameterSequence(
        LlrpProtocolVersion version,
        IReadOnlyList<ILlrpParameter> parameters,
        bool forEncoding)
    {
        const ushort llrpCapabilitiesParameterType = 142;
        const ushort regulatoryCapabilitiesParameterType = 143;
        const ushort c1G2LlrpCapabilitiesParameterType = 327;

        int previousSlot = -1;
        var seen = new bool[4];
        foreach (ILlrpParameter parameter in parameters)
        {
            ushort parameterType = _registry.GetParameterWireType(version, parameter);
            int slot = parameterType switch
            {
                GeneralDeviceCapabilities.ParameterType => 0,
                llrpCapabilitiesParameterType => 1,
                regulatoryCapabilitiesParameterType => 2,
                c1G2LlrpCapabilitiesParameterType => 3,
                RawCustomParameter.CustomParameterType => 4,
                _ => -1,
            };

            bool duplicateSingleton = slot is >= 0 and < 4 && seen[slot];
            if (slot < previousSlot || slot < 0 || duplicateSingleton)
            {
                ThrowInvalidCapabilitySequence(forEncoding);
            }

            if (slot < 4)
            {
                seen[slot] = true;
            }

            previousSlot = slot;
        }
    }

    private static void ThrowInvalidCapabilitySequence(bool forEncoding)
    {
        const string message =
            "GET_READER_CAPABILITIES_RESPONSE parameters must contain at most one each of " +
            "GeneralDeviceCapabilities (137), LLRPCapabilities (142), RegulatoryCapabilities (143), " +
            "and C1G2LLRPCapabilities (327), in that order, followed only by Custom parameters (1023).";
        if (forEncoding)
        {
            throw new ArgumentException(message, "parameters");
        }

        throw new LlrpProtocolException(
            LlrpProtocolErrorCode.InvalidParameterEncoding,
            message);
    }
}
