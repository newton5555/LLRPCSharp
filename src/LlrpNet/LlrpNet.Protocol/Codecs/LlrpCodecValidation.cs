using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Parameters;

namespace LlrpNet.Protocol.Codecs;

internal static class LlrpCodecValidation
{
    public const int MaximumTlvPayloadLength = ushort.MaxValue - LlrpTlvParameterHeader.EncodedLength;

    public static void ValidateVersion(LlrpProtocolVersion version, string parameterName)
    {
        if (version is < LlrpProtocolVersion.Version101 or > LlrpProtocolVersion.Version20)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                version,
                $"LLRP protocol version value {(byte)version} is not supported.");
        }
    }

    public static void ValidateMessageType(ushort messageType, string parameterName)
    {
        if (messageType > LlrpMessageHeader.MaximumMessageType)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                messageType,
                $"Message type {messageType} does not fit in the ten-bit LLRP field.");
        }
    }

    public static void ValidateTlvParameterType(ushort parameterType, string parameterName)
    {
        if (parameterType is < LlrpTlvParameterHeader.MinimumParameterType or > LlrpTlvParameterHeader.MaximumParameterType)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                parameterType,
                $"TLV parameter type {parameterType} is outside the wire range " +
                $"{LlrpTlvParameterHeader.MinimumParameterType}..{LlrpTlvParameterHeader.MaximumParameterType}.");
        }
    }

    public static void ValidateTvParameterType(byte parameterType, string parameterName)
    {
        if (parameterType is 0 or > LlrpTvParameterHeader.MaximumParameterType)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                parameterType,
                $"TV parameter type {parameterType} is outside the wire range " +
                $"1..{LlrpTvParameterHeader.MaximumParameterType}.");
        }
    }

    public static void ValidateTlvPayloadLength(int payloadLength, string parameterName)
    {
        if (payloadLength is < 0 or > MaximumTlvPayloadLength)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                payloadLength,
                $"A TLV value cannot exceed {MaximumTlvPayloadLength} octets.");
        }
    }
}
