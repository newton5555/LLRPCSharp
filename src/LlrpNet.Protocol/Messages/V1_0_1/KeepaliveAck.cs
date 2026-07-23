using LlrpNet.Protocol.Messages;

namespace LlrpNet.Protocol.Messages.V1_0_1;

/// <summary>
/// Represents the payload-free LLRP 1.0.1 KEEPALIVE_ACK message.
/// </summary>
/// <param name="MessageId">The message correlation identifier copied from the KEEPALIVE.</param>
public sealed record KeepaliveAck(uint MessageId) : ILlrpMessage
{
    /// <summary>
    /// The LLRP wire message type.
    /// </summary>
    public const ushort MessageType = 72;
}
