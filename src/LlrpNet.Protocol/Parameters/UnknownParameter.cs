using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Codecs;

namespace LlrpNet.Protocol.Parameters;

/// <summary>
/// Preserves an unregistered TLV parameter and its exact value bytes.
/// </summary>
/// <remarks>
/// Unknown TV parameters cannot be represented safely because the TV header contains no length.
/// </remarks>
public sealed class UnknownParameter : ILlrpParameter
{
    private readonly byte[] _data;

    /// <summary>
    /// Initializes a new instance of the <see cref="UnknownParameter"/> class.
    /// </summary>
    /// <param name="version">The protocol version under which the parameter was decoded.</param>
    /// <param name="parameterType">The unregistered TLV wire type.</param>
    /// <param name="data">The uninterpreted TLV value, excluding its four-octet header.</param>
    public UnknownParameter(
        LlrpProtocolVersion version,
        ushort parameterType,
        ReadOnlySpan<byte> data)
    {
        LlrpCodecValidation.ValidateVersion(version, nameof(version));
        LlrpCodecValidation.ValidateTlvParameterType(parameterType, nameof(parameterType));

        if (parameterType == RawCustomParameter.CustomParameterType)
        {
            throw new ArgumentOutOfRangeException(
                nameof(parameterType),
                parameterType,
                $"TLV parameter type {RawCustomParameter.CustomParameterType} must be represented by {nameof(RawCustomParameter)}.");
        }

        LlrpCodecValidation.ValidateTlvPayloadLength(data.Length, nameof(data));
        Version = version;
        ParameterType = parameterType;
        _data = data.ToArray();
    }

    /// <summary>
    /// Gets the protocol version under which this parameter was decoded.
    /// </summary>
    public LlrpProtocolVersion Version { get; }

    /// <summary>
    /// Gets the unregistered TLV wire type.
    /// </summary>
    public ushort ParameterType { get; }

    /// <summary>
    /// Gets the exact uninterpreted TLV value, excluding its header.
    /// </summary>
    public ReadOnlyMemory<byte> Data => _data;
}
