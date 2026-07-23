using LlrpNet.Protocol.Messages.V1_0_1;
using LlrpNet.Protocol.Parameters.V1_0_1;
using LlrpNet.Protocol.Registry;

namespace LlrpNet.Protocol.Codecs.V1_0_1;

internal sealed class ErrorMessageCodec(LlrpCodecRegistry registry)
    : Llrp101StatusMessageCodec<ErrorMessage>(registry)
{
    protected override ushort WireMessageType => ErrorMessage.MessageType;

    protected override ErrorMessage Create(uint messageId, LlrpStatus status)
    {
        return new ErrorMessage(messageId, status);
    }

    protected override LlrpStatus GetStatus(ErrorMessage message)
    {
        return message.Status;
    }
}
