using LlrpNet.Protocol.Messages;
using LlrpNet.Protocol.Parameters.V1_0_1;

namespace LlrpNet.Protocol.Messages.V1_0_1;

/// <summary>
/// Stops one active ROSpec on an LLRP 1.0.1 reader.
/// </summary>
/// <param name="MessageId">The message correlation identifier.</param>
/// <param name="RoSpecId">The nonzero ROSpec identifier.</param>
public sealed record StopRoSpec(uint MessageId, uint RoSpecId) : ILlrpMessage
{
    /// <summary>
    /// The LLRP wire message type.
    /// </summary>
    public const ushort MessageType = 23;
}

/// <summary>
/// Reports the result of an LLRP 1.0.1 <see cref="StopRoSpec"/> request.
/// </summary>
public sealed class StopRoSpecResponse : ILlrpMessage
{
    /// <summary>
    /// The LLRP wire message type.
    /// </summary>
    public const ushort MessageType = 33;

    /// <summary>
    /// Initializes a new instance of the <see cref="StopRoSpecResponse"/> class.
    /// </summary>
    /// <param name="messageId">The message correlation identifier.</param>
    /// <param name="status">The required operation status.</param>
    public StopRoSpecResponse(uint messageId, LlrpStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        MessageId = messageId;
        Status = status;
    }

    /// <inheritdoc />
    public uint MessageId { get; }

    /// <summary>
    /// Gets the required operation status.
    /// </summary>
    public LlrpStatus Status { get; }
}
