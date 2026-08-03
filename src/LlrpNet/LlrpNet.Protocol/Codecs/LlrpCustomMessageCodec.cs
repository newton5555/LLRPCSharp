using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Messages;

namespace LlrpNet.Protocol.Codecs;

/// <summary>
/// Supplies a strongly typed custom-message surface while exposing type-erased registry operations.
/// </summary>
/// <typeparam name="TMessage">The exact custom-message CLR type handled by the codec.</typeparam>
public abstract class LlrpCustomMessageCodec<TMessage> : ILlrpCustomMessageCodec
    where TMessage : ILlrpMessage
{
    /// <inheritdoc />
    public Type ValueType => typeof(TMessage);

    /// <summary>
    /// Decodes only the vendor-defined Data bytes for a strongly typed custom message.
    /// </summary>
    /// <param name="version">The active protocol version.</param>
    /// <param name="messageId">The common-header message identifier.</param>
    /// <param name="data">The complete vendor-defined Data bytes.</param>
    /// <returns>The decoded custom message.</returns>
    public abstract TMessage Decode(
        LlrpProtocolVersion version,
        uint messageId,
        ReadOnlySpan<byte> data);

    /// <summary>
    /// Gets the encoded length of only the vendor-defined Data bytes.
    /// </summary>
    /// <param name="version">The target protocol version.</param>
    /// <param name="message">The message to measure.</param>
    /// <returns>The vendor-defined Data length.</returns>
    public abstract int GetEncodedDataLength(LlrpProtocolVersion version, TMessage message);

    /// <summary>
    /// Encodes only the vendor-defined Data bytes.
    /// </summary>
    /// <param name="version">The target protocol version.</param>
    /// <param name="message">The message to encode.</param>
    /// <param name="destination">The exact vendor-defined Data destination.</param>
    /// <returns>The number of Data octets written.</returns>
    public abstract int EncodeData(
        LlrpProtocolVersion version,
        TMessage message,
        Span<byte> destination);

    ILlrpMessage ILlrpCustomMessageCodec.Decode(
        LlrpProtocolVersion version,
        uint messageId,
        ReadOnlySpan<byte> data)
    {
        TMessage message = Decode(version, messageId, data);
        return message is null
            ? throw new InvalidOperationException($"Codec {GetType().FullName} returned a null custom message.")
            : message;
    }

    int ILlrpCustomMessageCodec.GetEncodedDataLength(
        LlrpProtocolVersion version,
        ILlrpMessage message)
    {
        return GetEncodedDataLength(version, RequireMessage(message));
    }

    int ILlrpCustomMessageCodec.EncodeData(
        LlrpProtocolVersion version,
        ILlrpMessage message,
        Span<byte> destination)
    {
        return EncodeData(version, RequireMessage(message), destination);
    }

    private static TMessage RequireMessage(ILlrpMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return message is TMessage typedMessage && message.GetType() == typeof(TMessage)
            ? typedMessage
            : throw new ArgumentException(
                $"Codec for {typeof(TMessage).FullName} cannot process {message.GetType().FullName}.",
                nameof(message));
    }
}
