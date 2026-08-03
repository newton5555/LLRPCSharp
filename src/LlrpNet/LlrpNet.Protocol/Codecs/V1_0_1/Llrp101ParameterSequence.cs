using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Parameters;
using LlrpNet.Protocol.Registry;

namespace LlrpNet.Protocol.Codecs.V1_0_1;

internal static class Llrp101ParameterSequence
{
    public static List<ILlrpParameter> Decode(
        LlrpCodecRegistry registry,
        LlrpProtocolVersion version,
        ReadOnlySpan<byte> source)
    {
        var parameters = new List<ILlrpParameter>();
        int offset = 0;
        while (offset < source.Length)
        {
            LlrpParameterDecodeResult result = registry.DecodeParameter(version, source[offset..]);
            if (result.BytesConsumed <= 0 || result.BytesConsumed > source.Length - offset)
            {
                throw new InvalidOperationException(
                    $"The parameter registry returned invalid consumed length {result.BytesConsumed}.");
            }

            parameters.Add(result.Parameter);
            offset += result.BytesConsumed;
        }

        return parameters;
    }

    public static int GetEncodedLength(
        LlrpCodecRegistry registry,
        LlrpProtocolVersion version,
        IReadOnlyList<ILlrpParameter> parameters)
    {
        int length = 0;
        foreach (ILlrpParameter parameter in parameters)
        {
            length = checked(length + registry.GetEncodedParameterLength(version, parameter));
        }

        return length;
    }

    public static int Encode(
        LlrpCodecRegistry registry,
        LlrpProtocolVersion version,
        IReadOnlyList<ILlrpParameter> parameters,
        Span<byte> destination)
    {
        int offset = 0;
        foreach (ILlrpParameter parameter in parameters)
        {
            int written = registry.EncodeParameter(version, parameter, destination[offset..]);
            if (written <= 0 || written > destination.Length - offset)
            {
                throw new InvalidOperationException(
                    $"The parameter registry returned invalid encoded length {written}.");
            }

            offset += written;
        }

        return offset;
    }
}
