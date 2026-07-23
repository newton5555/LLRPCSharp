using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Messages;

namespace LlrpNet.Protocol.Codecs;

/// <summary>
/// Provides type-erased operations for one registered CUSTOM_MESSAGE vendor/subtype mapping.
/// </summary>
/// <remarks>
/// The registry owns the common message header, VendorId, and MessageSubtype fields. Implementations
/// decode and encode only the vendor-defined Data bytes that follow those fields.
/// </remarks>
public interface ILlrpCustomMessageCodec
{
    /// <summary>
    /// Gets the exact CLR message type accepted and produced by this codec.
    /// </summary>
    public Type ValueType { get; }

    /// <summary>
    /// Decodes the exact vendor-defined Data bytes for a custom message.
    /// </summary>
    /// <param name="version">The active protocol version.</param>
    /// <param name="messageId">The identifier from the common message header.</param>
    /// <param name="data">The complete vendor-defined Data excluding VendorId and MessageSubtype.</param>
    /// <returns>The decoded typed custom message.</returns>
    public ILlrpMessage Decode(
        LlrpProtocolVersion version,
        uint messageId,
        ReadOnlySpan<byte> data);

    /// <summary>
    /// Gets the encoded length of only the vendor-defined Data bytes.
    /// </summary>
    /// <param name="version">The target protocol version.</param>
    /// <param name="message">A message whose runtime type is exactly <see cref="ValueType"/>.</param>
    /// <returns>The vendor-defined Data length, excluding registry-owned metadata.</returns>
    public int GetEncodedDataLength(LlrpProtocolVersion version, ILlrpMessage message);

    /// <summary>
    /// Encodes only the vendor-defined Data bytes into an exactly scoped destination.
    /// </summary>
    /// <param name="version">The target protocol version.</param>
    /// <param name="message">A message whose runtime type is exactly <see cref="ValueType"/>.</param>
    /// <param name="destination">The exact vendor-defined Data destination.</param>
    /// <returns>The number of Data octets written.</returns>
    public int EncodeData(
        LlrpProtocolVersion version,
        ILlrpMessage message,
        Span<byte> destination);
}
