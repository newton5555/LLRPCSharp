using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Parameters;

namespace LlrpNet.Protocol.Codecs;

/// <summary>
/// Provides type-erased operations for one registered custom-parameter vendor/subtype mapping.
/// </summary>
/// <remarks>
/// The registry owns the TLV header, VendorId, and ParameterSubtype fields. Implementations decode and
/// encode only the vendor-defined Data bytes that follow those fields.
/// </remarks>
public interface ILlrpCustomParameterCodec
{
    /// <summary>
    /// Gets the exact CLR parameter type accepted and produced by this codec.
    /// </summary>
    public Type ValueType { get; }

    /// <summary>
    /// Decodes the exact vendor-defined Data bytes for a custom parameter.
    /// </summary>
    /// <param name="version">The active protocol version.</param>
    /// <param name="data">The complete vendor-defined Data excluding VendorId and ParameterSubtype.</param>
    /// <returns>The decoded typed custom parameter.</returns>
    public ILlrpParameter Decode(LlrpProtocolVersion version, ReadOnlySpan<byte> data);

    /// <summary>
    /// Gets the encoded length of only the vendor-defined Data bytes.
    /// </summary>
    /// <param name="version">The target protocol version.</param>
    /// <param name="parameter">A parameter whose runtime type is exactly <see cref="ValueType"/>.</param>
    /// <returns>The vendor-defined Data length, excluding registry-owned metadata.</returns>
    public int GetEncodedDataLength(LlrpProtocolVersion version, ILlrpParameter parameter);

    /// <summary>
    /// Encodes only the vendor-defined Data bytes into an exactly scoped destination.
    /// </summary>
    /// <param name="version">The target protocol version.</param>
    /// <param name="parameter">A parameter whose runtime type is exactly <see cref="ValueType"/>.</param>
    /// <param name="destination">The exact vendor-defined Data destination.</param>
    /// <returns>The number of Data octets written.</returns>
    public int EncodeData(
        LlrpProtocolVersion version,
        ILlrpParameter parameter,
        Span<byte> destination);
}
