using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Messages;

namespace LlrpNet.Protocol.Codecs;

/// <summary>
/// Provides the type-erased operations used by an <c>LlrpCodecRegistry</c> for one message CLR type.
/// </summary>
public interface ILlrpMessageCodec
{
    /// <summary>
    /// Gets the exact CLR message type accepted and produced by this codec.
    /// </summary>
    public Type ValueType { get; }

    /// <summary>
    /// Decodes an exact message payload, excluding the common message header.
    /// </summary>
    /// <param name="header">The validated common message header.</param>
    /// <param name="payload">The complete payload scoped by <paramref name="header"/>.</param>
    /// <returns>The decoded message.</returns>
    public ILlrpMessage Decode(LlrpMessageHeader header, ReadOnlySpan<byte> payload);

    /// <summary>
    /// Gets the number of payload octets required to encode <paramref name="message"/>.
    /// </summary>
    /// <param name="version">The target protocol version.</param>
    /// <param name="message">A message whose runtime type is exactly <see cref="ValueType"/>.</param>
    /// <returns>The encoded payload length, excluding the common message header.</returns>
    public int GetEncodedPayloadLength(LlrpProtocolVersion version, ILlrpMessage message);

    /// <summary>
    /// Encodes only the message payload into an exactly scoped destination.
    /// </summary>
    /// <param name="version">The target protocol version.</param>
    /// <param name="message">A message whose runtime type is exactly <see cref="ValueType"/>.</param>
    /// <param name="destination">The exact payload destination.</param>
    /// <returns>The number of octets written.</returns>
    public int Encode(LlrpProtocolVersion version, ILlrpMessage message, Span<byte> destination);
}
