using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Parameters;

namespace LlrpNet.Protocol.Codecs;

/// <summary>
/// Provides the type-erased operations used by an <c>LlrpCodecRegistry</c> for one parameter CLR type.
/// </summary>
public interface ILlrpParameterCodec
{
    /// <summary>
    /// Gets the exact CLR parameter type accepted and produced by this codec.
    /// </summary>
    public Type ValueType { get; }

    /// <summary>
    /// Decodes an exact parameter value, excluding its TV or TLV header.
    /// </summary>
    /// <param name="version">The active protocol version.</param>
    /// <param name="payload">The complete parameter value scoped by its wire boundary.</param>
    /// <returns>The decoded parameter.</returns>
    public ILlrpParameter Decode(LlrpProtocolVersion version, ReadOnlySpan<byte> payload);

    /// <summary>
    /// Gets the number of value octets required to encode <paramref name="parameter"/>.
    /// </summary>
    /// <param name="version">The target protocol version.</param>
    /// <param name="parameter">A parameter whose runtime type is exactly <see cref="ValueType"/>.</param>
    /// <returns>The encoded value length, excluding the TV or TLV header.</returns>
    public int GetEncodedPayloadLength(LlrpProtocolVersion version, ILlrpParameter parameter);

    /// <summary>
    /// Encodes only the parameter value into an exactly scoped destination.
    /// </summary>
    /// <param name="version">The target protocol version.</param>
    /// <param name="parameter">A parameter whose runtime type is exactly <see cref="ValueType"/>.</param>
    /// <param name="destination">The exact parameter-value destination.</param>
    /// <returns>The number of octets written.</returns>
    public int Encode(LlrpProtocolVersion version, ILlrpParameter parameter, Span<byte> destination);
}
