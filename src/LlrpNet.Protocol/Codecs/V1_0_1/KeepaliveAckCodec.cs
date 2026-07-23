using LlrpNet.Protocol.Messages.V1_0_1;

namespace LlrpNet.Protocol.Codecs.V1_0_1;

internal sealed class KeepaliveAckCodec : Llrp101EmptyMessageCodec<KeepaliveAck>
{
    protected override ushort WireMessageType => KeepaliveAck.MessageType;

    protected override KeepaliveAck Create(uint messageId)
    {
        return new KeepaliveAck(messageId);
    }
}
