using System.Buffers.Binary;
using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Messages;
using LlrpNet.Protocol.Messages.V1_0_1;
using LlrpNet.Protocol.Parameters;
using LlrpNet.Protocol.Parameters.V1_0_1;
using LlrpNet.Protocol.Registry;

namespace LlrpNet.Protocol.Codecs.V1_0_1;

internal sealed class AddRoSpecCodec(LlrpCodecRegistry registry)
    : LlrpMessageCodec<AddRoSpec>
{
    private const ushort RoSpecParameterType = 177;

    private readonly LlrpCodecRegistry _registry = registry ?? throw new ArgumentNullException(nameof(registry));

    public override AddRoSpec Decode(
        LlrpMessageHeader header,
        ReadOnlySpan<byte> payload)
    {
        Llrp101CodecValidation.ValidateHeader(header, AddRoSpec.MessageType, payload.Length);
        if (payload.IsEmpty)
        {
            throw new LlrpProtocolException(
                LlrpProtocolErrorCode.InvalidParameterEncoding,
                "ADD_ROSPEC requires exactly one ROSpec parameter (type 177).");
        }

        if ((payload[0] & 0x80) != 0)
        {
            throw new LlrpProtocolException(
                LlrpProtocolErrorCode.InvalidParameterEncoding,
                "ADD_ROSPEC requires a TLV ROSpec parameter (type 177).");
        }

        LlrpTlvParameterHeader parameterHeader = LlrpTlvParameterHeader.Decode(payload);
        if (parameterHeader.ParameterType != RoSpecParameterType)
        {
            throw new LlrpProtocolException(
                LlrpProtocolErrorCode.InvalidParameterType,
                $"ADD_ROSPEC requires parameter type {RoSpecParameterType}, " +
                $"but found type {parameterHeader.ParameterType}.");
        }

        LlrpParameterDecodeResult result = _registry.DecodeParameter(
            LlrpProtocolVersion.Version101,
            payload);
        if (result.BytesConsumed != payload.Length)
        {
            throw new LlrpProtocolException(
                LlrpProtocolErrorCode.InvalidParameterEncoding,
                "ADD_ROSPEC requires exactly one ROSpec parameter and permits no trailing data.");
        }

        return new AddRoSpec(header.MessageId, result.Parameter);
    }

    public override int GetEncodedPayloadLength(
        LlrpProtocolVersion version,
        AddRoSpec message)
    {
        Llrp101CodecValidation.ValidateVersion(version);
        ValidateRoSpec(version, message.RoSpec);
        return _registry.GetEncodedParameterLength(version, message.RoSpec);
    }

    public override int Encode(
        LlrpProtocolVersion version,
        AddRoSpec message,
        Span<byte> destination)
    {
        int expectedLength = GetEncodedPayloadLength(version, message);
        if (destination.Length != expectedLength)
        {
            throw new ArgumentException(
                $"ADD_ROSPEC requires an exact {expectedLength}-octet payload destination.",
                nameof(destination));
        }

        return _registry.EncodeParameter(version, message.RoSpec, destination);
    }

    private void ValidateRoSpec(
        LlrpProtocolVersion version,
        ILlrpParameter roSpec)
    {
        ushort parameterType = _registry.GetParameterWireType(version, roSpec);
        if (parameterType != RoSpecParameterType)
        {
            throw new ArgumentException(
                $"ADD_ROSPEC requires exactly one ROSpec parameter (type {RoSpecParameterType}).",
                nameof(roSpec));
        }
    }
}

internal abstract class RoSpecIdMessageCodec<TMessage> : LlrpMessageCodec<TMessage>
    where TMessage : ILlrpMessage
{
    private const int PayloadLength = sizeof(uint);

    protected abstract ushort WireMessageType { get; }

    protected abstract TMessage Create(uint messageId, uint roSpecId);

    protected abstract uint GetRoSpecId(TMessage message);

    public sealed override TMessage Decode(
        LlrpMessageHeader header,
        ReadOnlySpan<byte> payload)
    {
        Llrp101CodecValidation.ValidateHeader(header, WireMessageType, payload.Length);
        if (payload.Length < PayloadLength)
        {
            throw new LlrpProtocolException(
                LlrpProtocolErrorCode.TruncatedData,
                $"LLRP 1.0.1 message type {WireMessageType} requires a four-octet ROSpecID field.");
        }

        if (payload.Length != PayloadLength)
        {
            throw new LlrpProtocolException(
                LlrpProtocolErrorCode.InvalidMessageLength,
                $"LLRP 1.0.1 message type {WireMessageType} requires exactly four payload octets.");
        }

        uint roSpecId = BinaryPrimitives.ReadUInt32BigEndian(payload);
        return Create(header.MessageId, roSpecId);
    }

    public sealed override int GetEncodedPayloadLength(
        LlrpProtocolVersion version,
        TMessage message)
    {
        Llrp101CodecValidation.ValidateVersion(version);
        return PayloadLength;
    }

    public sealed override int Encode(
        LlrpProtocolVersion version,
        TMessage message,
        Span<byte> destination)
    {
        Llrp101CodecValidation.ValidateVersion(version);
        if (destination.Length != PayloadLength)
        {
            throw new ArgumentException(
                $"LLRP 1.0.1 message type {WireMessageType} requires an exact four-octet payload destination.",
                nameof(destination));
        }

        BinaryPrimitives.WriteUInt32BigEndian(destination, GetRoSpecId(message));
        return PayloadLength;
    }
}

internal sealed class DeleteRoSpecCodec : RoSpecIdMessageCodec<DeleteRoSpec>
{
    protected override ushort WireMessageType => DeleteRoSpec.MessageType;

    protected override DeleteRoSpec Create(uint messageId, uint roSpecId)
    {
        return new DeleteRoSpec(messageId, roSpecId);
    }

    protected override uint GetRoSpecId(DeleteRoSpec message)
    {
        return message.RoSpecId;
    }
}

internal sealed class StartRoSpecCodec : RoSpecIdMessageCodec<StartRoSpec>
{
    protected override ushort WireMessageType => StartRoSpec.MessageType;

    protected override StartRoSpec Create(uint messageId, uint roSpecId)
    {
        return new StartRoSpec(messageId, roSpecId);
    }

    protected override uint GetRoSpecId(StartRoSpec message)
    {
        return message.RoSpecId;
    }
}

internal sealed class StopRoSpecCodec : RoSpecIdMessageCodec<StopRoSpec>
{
    protected override ushort WireMessageType => StopRoSpec.MessageType;

    protected override StopRoSpec Create(uint messageId, uint roSpecId)
    {
        return new StopRoSpec(messageId, roSpecId);
    }

    protected override uint GetRoSpecId(StopRoSpec message)
    {
        return message.RoSpecId;
    }
}

internal sealed class EnableRoSpecCodec : RoSpecIdMessageCodec<EnableRoSpec>
{
    protected override ushort WireMessageType => EnableRoSpec.MessageType;

    protected override EnableRoSpec Create(uint messageId, uint roSpecId)
    {
        return new EnableRoSpec(messageId, roSpecId);
    }

    protected override uint GetRoSpecId(EnableRoSpec message)
    {
        return message.RoSpecId;
    }
}

internal sealed class DisableRoSpecCodec : RoSpecIdMessageCodec<DisableRoSpec>
{
    protected override ushort WireMessageType => DisableRoSpec.MessageType;

    protected override DisableRoSpec Create(uint messageId, uint roSpecId)
    {
        return new DisableRoSpec(messageId, roSpecId);
    }

    protected override uint GetRoSpecId(DisableRoSpec message)
    {
        return message.RoSpecId;
    }
}

internal sealed class GetRoSpecsCodec : Llrp101EmptyMessageCodec<GetRoSpecs>
{
    protected override ushort WireMessageType => GetRoSpecs.MessageType;

    protected override GetRoSpecs Create(uint messageId)
    {
        return new GetRoSpecs(messageId);
    }
}

internal abstract class Llrp101StatusMessageCodec<TMessage>(LlrpCodecRegistry registry)
    : LlrpMessageCodec<TMessage>
    where TMessage : ILlrpMessage
{
    private readonly LlrpCodecRegistry _registry = registry ?? throw new ArgumentNullException(nameof(registry));

    protected abstract ushort WireMessageType { get; }

    protected abstract TMessage Create(uint messageId, LlrpStatus status);

    protected abstract LlrpStatus GetStatus(TMessage message);

    public sealed override TMessage Decode(
        LlrpMessageHeader header,
        ReadOnlySpan<byte> payload)
    {
        Llrp101CodecValidation.ValidateHeader(header, WireMessageType, payload.Length);
        LlrpStatus status = Llrp101StatusMessageCodecHelpers.DecodeRequiredStatus(
            _registry,
            payload,
            WireMessageType,
            out int bytesConsumed);
        if (bytesConsumed != payload.Length)
        {
            throw new LlrpProtocolException(
                LlrpProtocolErrorCode.InvalidParameterEncoding,
                $"LLRP 1.0.1 response type {WireMessageType} requires exactly one LLRPStatus parameter.");
        }

        return Create(header.MessageId, status);
    }

    public sealed override int GetEncodedPayloadLength(
        LlrpProtocolVersion version,
        TMessage message)
    {
        Llrp101CodecValidation.ValidateVersion(version);
        return _registry.GetEncodedParameterLength(version, GetStatus(message));
    }

    public sealed override int Encode(
        LlrpProtocolVersion version,
        TMessage message,
        Span<byte> destination)
    {
        int expectedLength = GetEncodedPayloadLength(version, message);
        if (destination.Length != expectedLength)
        {
            throw new ArgumentException(
                $"LLRP 1.0.1 response type {WireMessageType} requires an exact " +
                $"{expectedLength}-octet payload destination.",
                nameof(destination));
        }

        return _registry.EncodeParameter(version, GetStatus(message), destination);
    }
}

internal sealed class AddRoSpecResponseCodec(LlrpCodecRegistry registry)
    : Llrp101StatusMessageCodec<AddRoSpecResponse>(registry)
{
    protected override ushort WireMessageType => AddRoSpecResponse.MessageType;

    protected override AddRoSpecResponse Create(uint messageId, LlrpStatus status)
    {
        return new AddRoSpecResponse(messageId, status);
    }

    protected override LlrpStatus GetStatus(AddRoSpecResponse message)
    {
        return message.Status;
    }
}

internal sealed class DeleteRoSpecResponseCodec(LlrpCodecRegistry registry)
    : Llrp101StatusMessageCodec<DeleteRoSpecResponse>(registry)
{
    protected override ushort WireMessageType => DeleteRoSpecResponse.MessageType;

    protected override DeleteRoSpecResponse Create(uint messageId, LlrpStatus status)
    {
        return new DeleteRoSpecResponse(messageId, status);
    }

    protected override LlrpStatus GetStatus(DeleteRoSpecResponse message)
    {
        return message.Status;
    }
}

internal sealed class StartRoSpecResponseCodec(LlrpCodecRegistry registry)
    : Llrp101StatusMessageCodec<StartRoSpecResponse>(registry)
{
    protected override ushort WireMessageType => StartRoSpecResponse.MessageType;

    protected override StartRoSpecResponse Create(uint messageId, LlrpStatus status)
    {
        return new StartRoSpecResponse(messageId, status);
    }

    protected override LlrpStatus GetStatus(StartRoSpecResponse message)
    {
        return message.Status;
    }
}

internal sealed class StopRoSpecResponseCodec(LlrpCodecRegistry registry)
    : Llrp101StatusMessageCodec<StopRoSpecResponse>(registry)
{
    protected override ushort WireMessageType => StopRoSpecResponse.MessageType;

    protected override StopRoSpecResponse Create(uint messageId, LlrpStatus status)
    {
        return new StopRoSpecResponse(messageId, status);
    }

    protected override LlrpStatus GetStatus(StopRoSpecResponse message)
    {
        return message.Status;
    }
}

internal sealed class EnableRoSpecResponseCodec(LlrpCodecRegistry registry)
    : Llrp101StatusMessageCodec<EnableRoSpecResponse>(registry)
{
    protected override ushort WireMessageType => EnableRoSpecResponse.MessageType;

    protected override EnableRoSpecResponse Create(uint messageId, LlrpStatus status)
    {
        return new EnableRoSpecResponse(messageId, status);
    }

    protected override LlrpStatus GetStatus(EnableRoSpecResponse message)
    {
        return message.Status;
    }
}

internal sealed class DisableRoSpecResponseCodec(LlrpCodecRegistry registry)
    : Llrp101StatusMessageCodec<DisableRoSpecResponse>(registry)
{
    protected override ushort WireMessageType => DisableRoSpecResponse.MessageType;

    protected override DisableRoSpecResponse Create(uint messageId, LlrpStatus status)
    {
        return new DisableRoSpecResponse(messageId, status);
    }

    protected override LlrpStatus GetStatus(DisableRoSpecResponse message)
    {
        return message.Status;
    }
}

internal sealed class GetRoSpecsResponseCodec(LlrpCodecRegistry registry)
    : LlrpMessageCodec<GetRoSpecsResponse>
{
    private const ushort RoSpecParameterType = 177;

    private readonly LlrpCodecRegistry _registry = registry ?? throw new ArgumentNullException(nameof(registry));

    public override GetRoSpecsResponse Decode(
        LlrpMessageHeader header,
        ReadOnlySpan<byte> payload)
    {
        Llrp101CodecValidation.ValidateHeader(header, GetRoSpecsResponse.MessageType, payload.Length);
        LlrpStatus status = Llrp101StatusMessageCodecHelpers.DecodeRequiredStatus(
            _registry,
            payload,
            GetRoSpecsResponse.MessageType,
            out int bytesConsumed);
        List<ILlrpParameter> roSpecs = Llrp101ParameterSequence.Decode(
            _registry,
            LlrpProtocolVersion.Version101,
            payload[bytesConsumed..]);
        ValidateRoSpecs(LlrpProtocolVersion.Version101, roSpecs, forEncoding: false);
        return new GetRoSpecsResponse(header.MessageId, status, roSpecs);
    }

    public override int GetEncodedPayloadLength(
        LlrpProtocolVersion version,
        GetRoSpecsResponse message)
    {
        Llrp101CodecValidation.ValidateVersion(version);
        ValidateRoSpecs(version, message.RoSpecs, forEncoding: true);
        return checked(
            _registry.GetEncodedParameterLength(version, message.Status)
            + Llrp101ParameterSequence.GetEncodedLength(_registry, version, message.RoSpecs));
    }

    public override int Encode(
        LlrpProtocolVersion version,
        GetRoSpecsResponse message,
        Span<byte> destination)
    {
        int expectedLength = GetEncodedPayloadLength(version, message);
        if (destination.Length != expectedLength)
        {
            throw new ArgumentException(
                $"GET_ROSPECS_RESPONSE requires an exact {expectedLength}-octet payload destination.",
                nameof(destination));
        }

        int offset = _registry.EncodeParameter(version, message.Status, destination);
        offset += Llrp101ParameterSequence.Encode(
            _registry,
            version,
            message.RoSpecs,
            destination[offset..]);
        return offset;
    }

    private void ValidateRoSpecs(
        LlrpProtocolVersion version,
        IReadOnlyList<ILlrpParameter> roSpecs,
        bool forEncoding)
    {
        foreach (ILlrpParameter roSpec in roSpecs)
        {
            ushort parameterType = _registry.GetParameterWireType(version, roSpec);
            if (parameterType == RoSpecParameterType)
            {
                continue;
            }

            const string message =
                "GET_ROSPECS_RESPONSE may contain only ROSpec parameters (type 177) after LLRPStatus.";
            if (forEncoding)
            {
                throw new ArgumentException(message, nameof(roSpecs));
            }

            throw new LlrpProtocolException(
                LlrpProtocolErrorCode.InvalidParameterEncoding,
                message);
        }
    }
}

internal static class Llrp101StatusMessageCodecHelpers
{
    public static LlrpStatus DecodeRequiredStatus(
        LlrpCodecRegistry registry,
        ReadOnlySpan<byte> payload,
        ushort responseMessageType,
        out int bytesConsumed)
    {
        if (payload.IsEmpty)
        {
            throw new LlrpProtocolException(
                LlrpProtocolErrorCode.InvalidParameterEncoding,
                $"LLRP 1.0.1 response type {responseMessageType} requires LLRPStatus as its first parameter.");
        }

        if ((payload[0] & 0x80) != 0)
        {
            throw new LlrpProtocolException(
                LlrpProtocolErrorCode.InvalidParameterEncoding,
                $"LLRP 1.0.1 response type {responseMessageType} requires a TLV LLRPStatus first.");
        }

        LlrpTlvParameterHeader statusHeader = LlrpTlvParameterHeader.Decode(payload);
        if (statusHeader.ParameterType != LlrpStatus.ParameterType)
        {
            throw new LlrpProtocolException(
                LlrpProtocolErrorCode.InvalidParameterType,
                $"LLRP 1.0.1 response type {responseMessageType} requires parameter type " +
                $"{LlrpStatus.ParameterType} first, but found type {statusHeader.ParameterType}.");
        }

        LlrpParameterDecodeResult result = registry.DecodeParameter(
            LlrpProtocolVersion.Version101,
            payload);
        if (result.Parameter is not LlrpStatus status)
        {
            throw new LlrpProtocolException(
                LlrpProtocolErrorCode.InvalidParameterEncoding,
                "The required first LLRPStatus parameter has no LLRP 1.0.1 typed registration.");
        }

        if (result.BytesConsumed <= 0 || result.BytesConsumed > payload.Length)
        {
            throw new InvalidOperationException(
                $"The parameter registry returned invalid consumed length {result.BytesConsumed}.");
        }

        bytesConsumed = result.BytesConsumed;
        return status;
    }
}
