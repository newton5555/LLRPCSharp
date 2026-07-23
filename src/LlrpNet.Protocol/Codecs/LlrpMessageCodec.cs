using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Messages;

namespace LlrpNet.Protocol.Codecs;

/// <summary>
/// Supplies a strongly typed implementation surface while exposing type-erased registry operations.
/// </summary>
/// <typeparam name="TMessage">The exact message CLR type handled by the codec.</typeparam>
public abstract class LlrpMessageCodec<TMessage> : ILlrpMessageCodec
    where TMessage : ILlrpMessage
{
    /// <inheritdoc />
    public Type ValueType => typeof(TMessage);

    /// <summary>
    /// Decodes an exact message payload.
    /// </summary>
    /// <param name="header">The validated common message header.</param>
    /// <param name="payload">The complete payload excluding the common header.</param>
    /// <returns>The decoded strongly typed message.</returns>
    public abstract TMessage Decode(LlrpMessageHeader header, ReadOnlySpan<byte> payload);

    /// <summary>
    /// Gets the encoded payload length for a strongly typed message.
    /// </summary>
    /// <param name="version">The target protocol version.</param>
    /// <param name="message">The message to measure.</param>
    /// <returns>The payload length excluding the common header.</returns>
    public abstract int GetEncodedPayloadLength(LlrpProtocolVersion version, TMessage message);

    /// <summary>
    /// Encodes a strongly typed message payload into an exactly scoped destination.
    /// </summary>
    /// <param name="version">The target protocol version.</param>
    /// <param name="message">The message to encode.</param>
    /// <param name="destination">The exact payload destination.</param>
    /// <returns>The number of octets written.</returns>
    public abstract int Encode(LlrpProtocolVersion version, TMessage message, Span<byte> destination);

    ILlrpMessage ILlrpMessageCodec.Decode(LlrpMessageHeader header, ReadOnlySpan<byte> payload)
    {
        TMessage message = Decode(header, payload);
        return message is null
            ? throw new InvalidOperationException($"Codec {GetType().FullName} returned a null message.")
            : message;
    }

    int ILlrpMessageCodec.GetEncodedPayloadLength(LlrpProtocolVersion version, ILlrpMessage message)
    {
        return GetEncodedPayloadLength(version, RequireMessage(message));
    }

    int ILlrpMessageCodec.Encode(
        LlrpProtocolVersion version,
        ILlrpMessage message,
        Span<byte> destination)
    {
        return Encode(version, RequireMessage(message), destination);
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
