using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Parameters;

namespace LlrpNet.Protocol.Codecs;

/// <summary>
/// Supplies a strongly typed custom-parameter surface while exposing type-erased registry operations.
/// </summary>
/// <typeparam name="TParameter">The exact custom-parameter CLR type handled by the codec.</typeparam>
public abstract class LlrpCustomParameterCodec<TParameter> : ILlrpCustomParameterCodec
    where TParameter : ILlrpParameter
{
    /// <inheritdoc />
    public Type ValueType => typeof(TParameter);

    /// <summary>
    /// Decodes only the vendor-defined Data bytes for a strongly typed custom parameter.
    /// </summary>
    /// <param name="version">The active protocol version.</param>
    /// <param name="data">The complete vendor-defined Data bytes.</param>
    /// <returns>The decoded custom parameter.</returns>
    public abstract TParameter Decode(LlrpProtocolVersion version, ReadOnlySpan<byte> data);

    /// <summary>
    /// Gets the encoded length of only the vendor-defined Data bytes.
    /// </summary>
    /// <param name="version">The target protocol version.</param>
    /// <param name="parameter">The parameter to measure.</param>
    /// <returns>The vendor-defined Data length.</returns>
    public abstract int GetEncodedDataLength(LlrpProtocolVersion version, TParameter parameter);

    /// <summary>
    /// Encodes only the vendor-defined Data bytes.
    /// </summary>
    /// <param name="version">The target protocol version.</param>
    /// <param name="parameter">The parameter to encode.</param>
    /// <param name="destination">The exact vendor-defined Data destination.</param>
    /// <returns>The number of Data octets written.</returns>
    public abstract int EncodeData(
        LlrpProtocolVersion version,
        TParameter parameter,
        Span<byte> destination);

    ILlrpParameter ILlrpCustomParameterCodec.Decode(
        LlrpProtocolVersion version,
        ReadOnlySpan<byte> data)
    {
        TParameter parameter = Decode(version, data);
        return parameter is null
            ? throw new InvalidOperationException($"Codec {GetType().FullName} returned a null custom parameter.")
            : parameter;
    }

    int ILlrpCustomParameterCodec.GetEncodedDataLength(
        LlrpProtocolVersion version,
        ILlrpParameter parameter)
    {
        return GetEncodedDataLength(version, RequireParameter(parameter));
    }

    int ILlrpCustomParameterCodec.EncodeData(
        LlrpProtocolVersion version,
        ILlrpParameter parameter,
        Span<byte> destination)
    {
        return EncodeData(version, RequireParameter(parameter), destination);
    }

    private static TParameter RequireParameter(ILlrpParameter parameter)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        return parameter is TParameter typedParameter && parameter.GetType() == typeof(TParameter)
            ? typedParameter
            : throw new ArgumentException(
                $"Codec for {typeof(TParameter).FullName} cannot process {parameter.GetType().FullName}.",
                nameof(parameter));
    }
}
