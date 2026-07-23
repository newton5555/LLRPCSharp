using LlrpNet.Protocol.Messages.V1_0_1;

namespace LlrpNet.Protocol.Codecs.V1_0_1;

internal sealed class KeepaliveCodec : Llrp101EmptyMessageCodec<Keepalive>
{
    protected override ushort WireMessageType => Keepalive.MessageType;

    protected override Keepalive Create(uint messageId)
    {
        return new Keepalive(messageId);
    }
}
