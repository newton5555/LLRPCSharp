using LlrpNet.Core.Protocol;

namespace LlrpNet.Protocol.Codecs.V1_0_1;

internal static class Llrp101CodecValidation
{
    public static void ValidateHeader(
        LlrpMessageHeader header,
        ushort expectedMessageType,
        int payloadLength)
    {
        if (header.Version != LlrpProtocolVersion.Version101)
        {
            throw new LlrpProtocolException(
                LlrpProtocolErrorCode.UnsupportedVersion,
                $"Message type {expectedMessageType} is an LLRP 1.0.1 codec, but the header version is {header.Version}.");
        }

        if (header.MessageType != expectedMessageType)
        {
            throw new LlrpProtocolException(
                LlrpProtocolErrorCode.InvalidMessageType,
                $"The codec for message type {expectedMessageType} cannot decode type {header.MessageType}.");
        }

        uint expectedLength = checked((uint)(LlrpMessageHeader.EncodedLength + payloadLength));
        if (header.MessageLength != expectedLength)
        {
            throw new LlrpProtocolException(
                LlrpProtocolErrorCode.InvalidMessageLength,
                $"Message type {expectedMessageType} declares length {header.MessageLength}, " +
                $"but its supplied payload requires length {expectedLength}.");
        }
    }

    public static void ValidateVersion(LlrpProtocolVersion version)
    {
        if (version != LlrpProtocolVersion.Version101)
        {
            throw new ArgumentOutOfRangeException(
                nameof(version),
                version,
                "This codec supports only LLRP 1.0.1.");
        }
    }
}
