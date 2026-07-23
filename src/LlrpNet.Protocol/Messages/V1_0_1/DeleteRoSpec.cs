using LlrpNet.Protocol.Messages;
using LlrpNet.Protocol.Parameters.V1_0_1;

namespace LlrpNet.Protocol.Messages.V1_0_1;

/// <summary>
/// Deletes one or all ROSpecs from an LLRP 1.0.1 reader.
/// </summary>
/// <param name="MessageId">The message correlation identifier.</param>
/// <param name="RoSpecId">The ROSpec identifier; zero selects all ROSpecs.</param>
public sealed record DeleteRoSpec(uint MessageId, uint RoSpecId) : ILlrpMessage
{
    /// <summary>
    /// The LLRP wire message type.
    /// </summary>
    public const ushort MessageType = 21;
}

/// <summary>
/// Reports the result of an LLRP 1.0.1 <see cref="DeleteRoSpec"/> request.
/// </summary>
public sealed class DeleteRoSpecResponse : ILlrpMessage
{
    /// <summary>
    /// The LLRP wire message type.
    /// </summary>
    public const ushort MessageType = 31;

    /// <summary>
    /// Initializes a new instance of the <see cref="DeleteRoSpecResponse"/> class.
    /// </summary>
    /// <param name="messageId">The message correlation identifier.</param>
    /// <param name="status">The required operation status.</param>
    public DeleteRoSpecResponse(uint messageId, LlrpStatus status)
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
