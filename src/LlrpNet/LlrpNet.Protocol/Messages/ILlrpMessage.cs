namespace LlrpNet.Protocol.Messages;

/// <summary>
/// Marks a strongly typed LLRP message that can participate in request/response correlation.
/// </summary>
public interface ILlrpMessage
{
    /// <summary>
    /// Gets the message identifier carried by the common LLRP message header.
    /// </summary>
    public uint MessageId { get; }
}
