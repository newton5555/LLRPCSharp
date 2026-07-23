using System.Collections.ObjectModel;
using LlrpNet.Protocol.Messages;
using LlrpNet.Protocol.Parameters;

namespace LlrpNet.Protocol.Messages.V1_0_1;

/// <summary>
/// Requests one or all reader capability categories using LLRP 1.0.1.
/// </summary>
public sealed class GetReaderCapabilities : ILlrpMessage
{
    /// <summary>
    /// The LLRP wire message type.
    /// </summary>
    public const ushort MessageType = 1;

    private readonly ReadOnlyCollection<ILlrpParameter> _parameters;

    /// <summary>
    /// Initializes a new instance of the <see cref="GetReaderCapabilities"/> class.
    /// </summary>
    /// <param name="messageId">The message correlation identifier.</param>
    /// <param name="requestedData">The requested capability category.</param>
    /// <param name="parameters">Optional trailing Custom parameters.</param>
    public GetReaderCapabilities(
        uint messageId,
        GetReaderCapabilitiesRequestedData requestedData,
        IEnumerable<ILlrpParameter>? parameters = null)
    {
        if (!IsDefined(requestedData))
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestedData),
                requestedData,
                "The requested capability category must be an LLRP 1.0.1 enumeration value from 0 through 4.");
        }

        ILlrpParameter[] parameterArray = parameters?.ToArray() ?? [];
        if (parameterArray.Any(static parameter => parameter is null))
        {
            throw new ArgumentException("A trailing parameter collection cannot contain null entries.", nameof(parameters));
        }

        MessageId = messageId;
        RequestedData = requestedData;
        _parameters = Array.AsReadOnly(parameterArray);
    }

    /// <inheritdoc />
    public uint MessageId { get; }

    /// <summary>
    /// Gets the requested capability category.
    /// </summary>
    public GetReaderCapabilitiesRequestedData RequestedData { get; }

    /// <summary>
    /// Gets the trailing Custom parameters in their original wire order.
    /// </summary>
    public IReadOnlyList<ILlrpParameter> Parameters => _parameters;

    internal static bool IsDefined(GetReaderCapabilitiesRequestedData value)
    {
        return value is >= GetReaderCapabilitiesRequestedData.All
            and <= GetReaderCapabilitiesRequestedData.LlrpAirProtocolCapabilities;
    }
}
