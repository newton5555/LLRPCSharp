using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Parameters;
using LlrpNet.Protocol.Registry;

namespace LlrpNet.Protocol.Codecs.V1_0_1;

internal static class RoSpecGraphCodecHelpers
{
    public static ushort GetWireType(
        LlrpCodecRegistry registry,
        LlrpProtocolVersion version,
        ILlrpParameter parameter)
    {
        return registry.GetParameterWireType(version, parameter);
    }

    public static void ValidateWireType(
        LlrpCodecRegistry registry,
        LlrpProtocolVersion version,
        ILlrpParameter parameter,
        ushort expectedType,
        string description)
    {
        ushort actualType = GetWireType(registry, version, parameter);
        if (actualType != expectedType)
        {
            throw new ArgumentException(
                $"{description} requires parameter type {expectedType}, but found type {actualType}.",
                nameof(parameter));
        }
    }

    public static void ValidateCustomParameters(
        LlrpCodecRegistry registry,
        LlrpProtocolVersion version,
        IReadOnlyList<ILlrpParameter> parameters,
        string owner)
    {
        foreach (ILlrpParameter parameter in parameters)
        {
            ushort parameterType = GetWireType(registry, version, parameter);
            if (parameterType != RawCustomParameter.CustomParameterType)
            {
                throw new ArgumentException(
                    $"{owner} permits only Custom parameters (type {RawCustomParameter.CustomParameterType}) " +
                    $"in its Custom slot, but found type {parameterType}.",
                    nameof(parameters));
            }
        }
    }

    public static TParameter RequireTyped<TParameter>(
        ILlrpParameter parameter,
        ushort expectedType,
        string owner)
        where TParameter : class, ILlrpParameter
    {
        if (parameter is TParameter typed)
        {
            return typed;
        }

        throw new LlrpProtocolException(
            LlrpProtocolErrorCode.InvalidParameterEncoding,
            $"{owner} requires the registered typed representation for parameter type {expectedType}.");
    }

    public static void ThrowInvalidSequence(string message, bool forEncoding)
    {
        if (forEncoding)
        {
            throw new ArgumentException(message, "parameters");
        }

        throw new LlrpProtocolException(
            LlrpProtocolErrorCode.InvalidParameterEncoding,
            message);
    }
}
