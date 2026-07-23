using LlrpNet.Protocol.Messages;
using LlrpNet.Protocol.Parameters;
using LlrpNet.Protocol.Parameters.V1_0_1;

namespace LlrpNet.Protocol.Messages.V1_0_1;

/// <summary>
/// Adds one ROSpec to an LLRP 1.0.1 reader.
/// </summary>
public sealed class AddRoSpec : ILlrpMessage
{
    /// <summary>
    /// The LLRP wire message type.
    /// </summary>
    public const ushort MessageType = 20;

    /// <summary>
    /// Initializes a new instance of the <see cref="AddRoSpec"/> class.
    /// </summary>
    /// <param name="messageId">The message correlation identifier.</param>
    /// <param name="roSpec">The required ROSpec parameter (wire type 177).</param>
    public AddRoSpec(uint messageId, ILlrpParameter roSpec)
    {
        ArgumentNullException.ThrowIfNull(roSpec);
        MessageId = messageId;
        RoSpec = roSpec;
    }

    /// <inheritdoc />
    public uint MessageId { get; }

    /// <summary>
    /// Gets the required ROSpec parameter.
    /// </summary>
    public ILlrpParameter RoSpec { get; }
}

/// <summary>
/// Reports the result of an LLRP 1.0.1 <see cref="AddRoSpec"/> request.
/// </summary>
public sealed class AddRoSpecResponse : ILlrpMessage
{
    /// <summary>
    /// The LLRP wire message type.
    /// </summary>
    public const ushort MessageType = 30;

    /// <summary>
    /// Initializes a new instance of the <see cref="AddRoSpecResponse"/> class.
    /// </summary>
    /// <param name="messageId">The message correlation identifier.</param>
    /// <param name="status">The required operation status.</param>
    public AddRoSpecResponse(uint messageId, LlrpStatus status)
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
