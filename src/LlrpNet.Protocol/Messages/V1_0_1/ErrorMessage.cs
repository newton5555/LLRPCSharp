using LlrpNet.Protocol.Messages;
using LlrpNet.Protocol.Parameters.V1_0_1;

namespace LlrpNet.Protocol.Messages.V1_0_1;

/// <summary>
/// Reports an LLRP 1.0.1 protocol or operation error for a correlated request.
/// </summary>
public sealed class ErrorMessage : ILlrpMessage
{
    /// <summary>
    /// The LLRP wire message type.
    /// </summary>
    public const ushort MessageType = 100;

    /// <summary>
    /// Initializes a new instance of the <see cref="ErrorMessage"/> class.
    /// </summary>
    /// <param name="messageId">The message correlation identifier.</param>
    /// <param name="status">The required error status.</param>
    public ErrorMessage(uint messageId, LlrpStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        MessageId = messageId;
        Status = status;
    }

    /// <inheritdoc />
    public uint MessageId { get; }

    /// <summary>
    /// Gets the required error status.
    /// </summary>
    public LlrpStatus Status { get; }
}
