using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Parameters;

namespace LlrpNet.Protocol.Codecs;

/// <summary>
/// Supplies a strongly typed implementation surface while exposing type-erased registry operations.
/// </summary>
/// <typeparam name="TParameter">The exact parameter CLR type handled by the codec.</typeparam>
public abstract class LlrpParameterCodec<TParameter> : ILlrpParameterCodec
    where TParameter : ILlrpParameter
{
    /// <inheritdoc />
    public Type ValueType => typeof(TParameter);

    /// <summary>
    /// Decodes an exact parameter value.
    /// </summary>
    /// <param name="version">The active protocol version.</param>
    /// <param name="payload">The complete value excluding the TV or TLV header.</param>
    /// <returns>The decoded strongly typed parameter.</returns>
    public abstract TParameter Decode(LlrpProtocolVersion version, ReadOnlySpan<byte> payload);

    /// <summary>
    /// Gets the encoded value length for a strongly typed parameter.
    /// </summary>
    /// <param name="version">The target protocol version.</param>
    /// <param name="parameter">The parameter to measure.</param>
    /// <returns>The value length excluding the TV or TLV header.</returns>
    public abstract int GetEncodedPayloadLength(LlrpProtocolVersion version, TParameter parameter);

    /// <summary>
    /// Encodes a strongly typed parameter value into an exactly scoped destination.
    /// </summary>
    /// <param name="version">The target protocol version.</param>
    /// <param name="parameter">The parameter to encode.</param>
    /// <param name="destination">The exact parameter-value destination.</param>
    /// <returns>The number of octets written.</returns>
    public abstract int Encode(
        LlrpProtocolVersion version,
        TParameter parameter,
        Span<byte> destination);

    ILlrpParameter ILlrpParameterCodec.Decode(
        LlrpProtocolVersion version,
        ReadOnlySpan<byte> payload)
    {
        TParameter parameter = Decode(version, payload);
        return parameter is null
            ? throw new InvalidOperationException($"Codec {GetType().FullName} returned a null parameter.")
            : parameter;
    }

    int ILlrpParameterCodec.GetEncodedPayloadLength(
        LlrpProtocolVersion version,
        ILlrpParameter parameter)
    {
        return GetEncodedPayloadLength(version, RequireParameter(parameter));
    }

    int ILlrpParameterCodec.Encode(
        LlrpProtocolVersion version,
        ILlrpParameter parameter,
        Span<byte> destination)
    {
        return Encode(version, RequireParameter(parameter), destination);
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
