using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Messages.V1_0_1;
using LlrpNet.Protocol.Parameters;
using LlrpNet.Protocol.Registry;

namespace LlrpNet.Protocol.Codecs.V1_0_1;

internal sealed class GetReaderCapabilitiesCodec(LlrpCodecRegistry registry)
    : LlrpMessageCodec<GetReaderCapabilities>
{
    private readonly LlrpCodecRegistry _registry = registry ?? throw new ArgumentNullException(nameof(registry));

    public override GetReaderCapabilities Decode(
        LlrpMessageHeader header,
        ReadOnlySpan<byte> payload)
    {
        Llrp101CodecValidation.ValidateHeader(header, GetReaderCapabilities.MessageType, payload.Length);
        if (payload.IsEmpty)
        {
            throw new LlrpProtocolException(
                LlrpProtocolErrorCode.InvalidMessageLength,
                "GET_READER_CAPABILITIES requires its one-octet RequestedData field.");
        }

        var requestedData = (GetReaderCapabilitiesRequestedData)payload[0];
        if (!GetReaderCapabilities.IsDefined(requestedData))
        {
            throw new LlrpProtocolException(
                LlrpProtocolErrorCode.InvalidParameterEncoding,
                $"GET_READER_CAPABILITIES RequestedData value {payload[0]} is not defined by LLRP 1.0.1.");
        }

        List<ILlrpParameter> parameters = Llrp101ParameterSequence.Decode(
            _registry,
            LlrpProtocolVersion.Version101,
            payload[1..]);
        ValidateParameterSequence(
            LlrpProtocolVersion.Version101,
            parameters,
            forEncoding: false);

        return new GetReaderCapabilities(header.MessageId, requestedData, parameters);
    }

    public override int GetEncodedPayloadLength(
        LlrpProtocolVersion version,
        GetReaderCapabilities message)
    {
        Llrp101CodecValidation.ValidateVersion(version);
        ValidateRequestedData(message.RequestedData);
        ValidateParameterSequence(version, message.Parameters, forEncoding: true);

        int payloadLength = 1;
        foreach (ILlrpParameter parameter in message.Parameters)
        {
            int parameterLength = _registry.GetEncodedParameterLength(version, parameter);
            payloadLength = checked(payloadLength + parameterLength);
        }

        return payloadLength;
    }

    public override int Encode(
        LlrpProtocolVersion version,
        GetReaderCapabilities message,
        Span<byte> destination)
    {
        int expectedLength = GetEncodedPayloadLength(version, message);
        if (destination.Length != expectedLength)
        {
            throw new ArgumentException(
                $"GET_READER_CAPABILITIES requires an exact {expectedLength}-octet payload destination.",
                nameof(destination));
        }

        destination[0] = (byte)message.RequestedData;
        int offset = 1;
        foreach (ILlrpParameter parameter in message.Parameters)
        {
            int written = _registry.EncodeParameter(version, parameter, destination[offset..]);
            offset += written;
        }

        return offset;
    }

    private static void ValidateRequestedData(GetReaderCapabilitiesRequestedData requestedData)
    {
        if (!GetReaderCapabilities.IsDefined(requestedData))
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestedData),
                requestedData,
                "RequestedData must be an LLRP 1.0.1 enumeration value from 0 through 4.");
        }
    }

    private void ValidateParameterSequence(
        LlrpProtocolVersion version,
        IReadOnlyList<ILlrpParameter> parameters,
        bool forEncoding)
    {
        foreach (ILlrpParameter parameter in parameters)
        {
            ushort parameterType = _registry.GetParameterWireType(version, parameter);
            if (parameterType == RawCustomParameter.CustomParameterType)
            {
                continue;
            }

            const string message =
                "GET_READER_CAPABILITIES may contain only trailing Custom parameters (type 1023).";
            if (forEncoding)
            {
                throw new ArgumentException(message, nameof(parameters));
            }

            throw new LlrpProtocolException(
                LlrpProtocolErrorCode.InvalidParameterEncoding,
                message);
        }
    }
}
