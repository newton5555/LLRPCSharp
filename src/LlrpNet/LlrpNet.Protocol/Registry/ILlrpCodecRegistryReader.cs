using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Messages;
using LlrpNet.Protocol.Parameters;

namespace LlrpNet.Protocol.Registry;

/// <summary>
/// Exposes the read-only codec operations of an <see cref="LlrpCodecRegistry"/>: decoding and
/// encoding complete messages and parameters against the registered codecs. Codec registration
/// remains a pre-connection configuration-time concern and is not part of this view.
/// </summary>
public interface ILlrpCodecRegistryReader
{
    /// <summary>Decodes a complete LLRP frame using its common header.</summary>
    public ILlrpMessage DecodeMessage(ReadOnlySpan<byte> frame);

    /// <summary>Decodes an exact payload using its already parsed common header.</summary>
    public ILlrpMessage DecodeMessage(LlrpMessageHeader header, ReadOnlySpan<byte> payload);

    /// <summary>Calculates the complete wire length of a message using its exact CLR-type registration.</summary>
    public int GetEncodedMessageLength(LlrpProtocolVersion version, ILlrpMessage message);

    /// <summary>Encodes a complete LLRP message into a caller-provided destination.</summary>
    public int EncodeMessage(LlrpProtocolVersion version, ILlrpMessage message, Span<byte> destination);

    /// <summary>Allocates and encodes one complete LLRP message.</summary>
    public byte[] EncodeMessage(LlrpProtocolVersion version, ILlrpMessage message);

    /// <summary>Decodes one parameter from the beginning of a buffer.</summary>
    public LlrpParameterDecodeResult DecodeParameter(LlrpProtocolVersion version, ReadOnlySpan<byte> source);

    /// <summary>Calculates the complete wire length of a parameter using its exact CLR-type registration.</summary>
    public int GetEncodedParameterLength(LlrpProtocolVersion version, ILlrpParameter parameter);

    /// <summary>Resolves the complete wire identity that would be used to encode a parameter.</summary>
    public LlrpParameterWireIdentity GetParameterWireIdentity(LlrpProtocolVersion version, ILlrpParameter parameter);

    /// <summary>Encodes a complete LLRP TV or TLV parameter into a caller-provided destination.</summary>
    public int EncodeParameter(LlrpProtocolVersion version, ILlrpParameter parameter, Span<byte> destination);

    /// <summary>Allocates and encodes one complete LLRP TV or TLV parameter.</summary>
    public byte[] EncodeParameter(LlrpProtocolVersion version, ILlrpParameter parameter);
}
