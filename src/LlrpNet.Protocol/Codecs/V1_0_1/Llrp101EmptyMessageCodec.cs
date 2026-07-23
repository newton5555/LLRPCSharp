using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Messages;

namespace LlrpNet.Protocol.Codecs.V1_0_1;

internal abstract class Llrp101EmptyMessageCodec<TMessage> : LlrpMessageCodec<TMessage>
    where TMessage : ILlrpMessage
{
    protected abstract ushort WireMessageType { get; }

    protected abstract TMessage Create(uint messageId);

    public sealed override TMessage Decode(
        LlrpMessageHeader header,
        ReadOnlySpan<byte> payload)
    {
        Llrp101CodecValidation.ValidateHeader(header, WireMessageType, payload.Length);
        if (!payload.IsEmpty)
        {
            throw new LlrpProtocolException(
                LlrpProtocolErrorCode.InvalidMessageLength,
                $"LLRP 1.0.1 message type {WireMessageType} does not permit a payload.");
        }

        return Create(header.MessageId);
    }

    public sealed override int GetEncodedPayloadLength(
        LlrpProtocolVersion version,
        TMessage message)
    {
        Llrp101CodecValidation.ValidateVersion(version);
        return 0;
    }

    public sealed override int Encode(
        LlrpProtocolVersion version,
        TMessage message,
        Span<byte> destination)
    {
        Llrp101CodecValidation.ValidateVersion(version);
        if (!destination.IsEmpty)
        {
            throw new ArgumentException(
                $"LLRP 1.0.1 message type {WireMessageType} requires an empty payload destination.",
                nameof(destination));
        }

        return 0;
    }
}
