using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Codecs;

namespace LlrpNet.Protocol.Messages;

/// <summary>
/// Preserves an unregistered LLRP message without interpreting its payload.
/// </summary>
public sealed class UnknownMessage : ILlrpMessage
{
    private readonly byte[] _payload;

    /// <summary>
    /// Initializes a new instance of the <see cref="UnknownMessage"/> class.
    /// </summary>
    /// <param name="version">The protocol version from the message header.</param>
    /// <param name="messageType">The ten-bit wire message type.</param>
    /// <param name="messageId">The message correlation identifier.</param>
    /// <param name="payload">The uninterpreted message payload.</param>
    public UnknownMessage(
        LlrpProtocolVersion version,
        ushort messageType,
        uint messageId,
        ReadOnlySpan<byte> payload)
    {
        LlrpCodecValidation.ValidateVersion(version, nameof(version));
        LlrpCodecValidation.ValidateMessageType(messageType, nameof(messageType));

        if (messageType == RawCustomMessage.CustomMessageType)
        {
            throw new ArgumentOutOfRangeException(
                nameof(messageType),
                messageType,
                $"Message type {RawCustomMessage.CustomMessageType} must be represented by {nameof(RawCustomMessage)}.");
        }

        Version = version;
        MessageType = messageType;
        MessageId = messageId;
        _payload = payload.ToArray();
    }

    /// <summary>
    /// Gets the protocol version with which this message was received.
    /// </summary>
    public LlrpProtocolVersion Version { get; }

    /// <summary>
    /// Gets the unregistered ten-bit wire message type.
    /// </summary>
    public ushort MessageType { get; }

    /// <inheritdoc />
    public uint MessageId { get; }

    /// <summary>
    /// Gets the exact uninterpreted payload following the common message header.
    /// </summary>
    public ReadOnlyMemory<byte> Payload => _payload;
}
