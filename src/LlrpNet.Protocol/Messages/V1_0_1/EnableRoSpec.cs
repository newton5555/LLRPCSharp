using LlrpNet.Protocol.Messages;
using LlrpNet.Protocol.Parameters.V1_0_1;

namespace LlrpNet.Protocol.Messages.V1_0_1;

/// <summary>
/// Enables one or all ROSpecs on an LLRP 1.0.1 reader.
/// </summary>
/// <param name="MessageId">The message correlation identifier.</param>
/// <param name="RoSpecId">The ROSpec identifier; zero selects all ROSpecs.</param>
public sealed record EnableRoSpec(uint MessageId, uint RoSpecId) : ILlrpMessage
{
    /// <summary>
    /// The LLRP wire message type.
    /// </summary>
    public const ushort MessageType = 24;
}

/// <summary>
/// Reports the result of an LLRP 1.0.1 <see cref="EnableRoSpec"/> request.
/// </summary>
public sealed class EnableRoSpecResponse : ILlrpMessage
{
    /// <summary>
    /// The LLRP wire message type.
    /// </summary>
    public const ushort MessageType = 34;

    /// <summary>
    /// Initializes a new instance of the <see cref="EnableRoSpecResponse"/> class.
    /// </summary>
    /// <param name="messageId">The message correlation identifier.</param>
    /// <param name="status">The required operation status.</param>
    public EnableRoSpecResponse(uint messageId, LlrpStatus status)
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
