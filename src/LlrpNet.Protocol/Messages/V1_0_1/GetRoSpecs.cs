using System.Collections.ObjectModel;
using LlrpNet.Protocol.Messages;
using LlrpNet.Protocol.Parameters;
using LlrpNet.Protocol.Parameters.V1_0_1;

namespace LlrpNet.Protocol.Messages.V1_0_1;

/// <summary>
/// Requests all ROSpecs configured on an LLRP 1.0.1 reader.
/// </summary>
/// <param name="MessageId">The message correlation identifier.</param>
public sealed record GetRoSpecs(uint MessageId) : ILlrpMessage
{
    /// <summary>
    /// The LLRP wire message type.
    /// </summary>
    public const ushort MessageType = 26;
}

/// <summary>
/// Returns all ROSpecs configured on an LLRP 1.0.1 reader.
/// </summary>
public sealed class GetRoSpecsResponse : ILlrpMessage
{
    /// <summary>
    /// The LLRP wire message type.
    /// </summary>
    public const ushort MessageType = 36;

    private readonly ReadOnlyCollection<ILlrpParameter> _roSpecs;

    /// <summary>
    /// Initializes a new instance of the <see cref="GetRoSpecsResponse"/> class.
    /// </summary>
    /// <param name="messageId">The message correlation identifier.</param>
    /// <param name="status">The required operation status.</param>
    /// <param name="roSpecs">Zero or more ROSpec parameters (wire type 177).</param>
    public GetRoSpecsResponse(
        uint messageId,
        LlrpStatus status,
        IEnumerable<ILlrpParameter>? roSpecs = null)
    {
        ArgumentNullException.ThrowIfNull(status);
        ILlrpParameter[] parameterArray = roSpecs?.ToArray() ?? [];
        if (parameterArray.Any(static parameter => parameter is null))
        {
            throw new ArgumentException("A ROSpec collection cannot contain null entries.", nameof(roSpecs));
        }

        MessageId = messageId;
        Status = status;
        _roSpecs = Array.AsReadOnly(parameterArray);
    }

    /// <inheritdoc />
    public uint MessageId { get; }

    /// <summary>
    /// Gets the required operation status.
    /// </summary>
    public LlrpStatus Status { get; }

    /// <summary>
    /// Gets the ROSpec parameters in their original wire order.
    /// </summary>
    public IReadOnlyList<ILlrpParameter> RoSpecs => _roSpecs;
}
