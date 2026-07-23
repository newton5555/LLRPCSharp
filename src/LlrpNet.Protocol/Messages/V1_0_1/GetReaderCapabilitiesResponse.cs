using System.Collections.ObjectModel;
using LlrpNet.Protocol.Messages;
using LlrpNet.Protocol.Parameters;
using LlrpNet.Protocol.Parameters.V1_0_1;

namespace LlrpNet.Protocol.Messages.V1_0_1;

/// <summary>
/// Returns the requested LLRP 1.0.1 reader capabilities and operation status.
/// </summary>
public sealed class GetReaderCapabilitiesResponse : ILlrpMessage
{
    /// <summary>
    /// The LLRP wire message type.
    /// </summary>
    public const ushort MessageType = 11;

    private readonly ReadOnlyCollection<ILlrpParameter> _parameters;

    /// <summary>
    /// Initializes a new instance of the <see cref="GetReaderCapabilitiesResponse"/> class.
    /// </summary>
    /// <param name="messageId">The message correlation identifier.</param>
    /// <param name="status">The required response status.</param>
    /// <param name="parameters">
    /// Optional capability singleton parameters in schema order, followed by zero or more Custom parameters.
    /// </param>
    public GetReaderCapabilitiesResponse(
        uint messageId,
        LlrpStatus status,
        IEnumerable<ILlrpParameter>? parameters = null)
    {
        ArgumentNullException.ThrowIfNull(status);
        ILlrpParameter[] parameterArray = parameters?.ToArray() ?? [];
        if (parameterArray.Any(static parameter => parameter is null))
        {
            throw new ArgumentException("A capability parameter collection cannot contain null entries.", nameof(parameters));
        }

        MessageId = messageId;
        Status = status;
        _parameters = Array.AsReadOnly(parameterArray);
    }

    /// <inheritdoc />
    public uint MessageId { get; }

    /// <summary>
    /// Gets the required response status.
    /// </summary>
    public LlrpStatus Status { get; }

    /// <summary>
    /// Gets capability singleton and Custom parameters in their supplied order.
    /// The LLRP 1.0.1 codec requires schema order when encoding.
    /// </summary>
    public IReadOnlyList<ILlrpParameter> Parameters => _parameters;
}
