using V101Parameters = LlrpNet.Protocol.Parameters.V1_0_1;
using V101Messages = LlrpNet.Protocol.Messages.V1_0_1;
using V101Enumerations = LlrpNet.Protocol.Enumerations.V1_0_1;
using LlrpNet.Protocol.Enumerations.V1_0_1;
using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Messages.V1_0_1;
using LlrpNet.Protocol.Parameters.V1_0_1;
using LlrpNet.Protocol.Registry;
using LlrpNet.Protocol.Registry.V1_0_1;

namespace LlrpNet.Protocol.Tests.Messages.V1_0_1;

public sealed class ErrorMessageTests
{
    private const uint MessageId = 0x01020304;

    [Fact]
    public void EncodeAndDecode_MatchesNormativeWireLayout()
    {
        LlrpCodecRegistry registry = CreateRegistry();
        byte[] expected =
        [
            0x04, 0x64,
            0x00, 0x00, 0x00, 0x12,
            0x01, 0x02, 0x03, 0x04,
            0x01, 0x1F, 0x00, 0x08,
            0x00, 0x00, 0x00, 0x00,
        ];
        var message = new V101Messages.ERROR_MESSAGE(
            MessageId,
            new V101Parameters.LLRPStatus(V101Enumerations.StatusCode.M_Success, string.Empty, null, null));

        byte[] encoded = registry.EncodeMessage(LlrpProtocolVersion.Version101, message);
        var decoded = Assert.IsType<V101Messages.ERROR_MESSAGE>(registry.DecodeMessage(expected));

        Assert.Equal(expected, encoded);
        Assert.Equal(MessageId, decoded.MessageId);
        Assert.Equal(V101Enumerations.StatusCode.M_Success, decoded.LLRPStatus.StatusCode);
    }

    [Fact]
    public void Decode_RejectsMissingWrongDuplicateAndTruncatedStatus()
    {
        LlrpCodecRegistry registry = CreateRegistry();
        byte[] status = registry.EncodeParameter(
            LlrpProtocolVersion.Version101,
            new V101Parameters.LLRPStatus(V101Enumerations.StatusCode.M_Success, string.Empty, null, null));

        LlrpProtocolException missing = Assert.Throws<LlrpProtocolException>(
            () => registry.DecodeMessage(CreateFrame()));
        LlrpProtocolException wrong = Assert.Throws<LlrpProtocolException>(
            () => registry.DecodeMessage(CreateFrame([0x00, 0xB1, 0x00, 0x04])));
        LlrpProtocolException duplicate = Assert.Throws<LlrpProtocolException>(
            () => registry.DecodeMessage(CreateFrame(status, status)));
        LlrpProtocolException truncated = Assert.Throws<LlrpProtocolException>(
            () => registry.DecodeMessage(CreateFrame([0x01, 0x1F, 0x00, 0x08])));

        Assert.Equal(LlrpProtocolErrorCode.InvalidParameterEncoding, missing.ErrorCode);
        // The generated message codec reports a valid-but-unexpected nested parameter
        // as an invalid parameter sequence rather than as an invalid wire type.
        Assert.Equal(LlrpProtocolErrorCode.InvalidParameterEncoding, wrong.ErrorCode);
        Assert.Equal(LlrpProtocolErrorCode.InvalidParameterEncoding, duplicate.ErrorCode);
        Assert.Equal(LlrpProtocolErrorCode.TruncatedData, truncated.ErrorCode);
    }

    [Fact]
    public void Constructor_RejectsNullStatus()
    {
        LlrpCodecRegistry registry = CreateRegistry();
        V101Messages.ERROR_MESSAGE message= new(MessageId, null!);

        Assert.Throws<ArgumentNullException>(
            () => registry.EncodeMessage(LlrpProtocolVersion.Version101, message));
    }

    private static LlrpCodecRegistry CreateRegistry()
    {
        var registry = new LlrpCodecRegistry();
        Llrp101StandardModule.Register(registry);
        return registry;
    }

    private static byte[] CreateFrame(params byte[][] payloadParts)
    {
        int payloadLength = payloadParts.Sum(static part => part.Length);
        int frameLength = checked(LlrpMessageHeader.EncodedLength + payloadLength);
        var frame = new byte[frameLength];
        new LlrpMessageHeader(
            LlrpProtocolVersion.Version101,
            V101Messages.ERROR_MESSAGE.MessageType,
            (uint)frameLength,
            MessageId).Encode(frame);

        int offset = LlrpMessageHeader.EncodedLength;
        foreach (byte[] payloadPart in payloadParts)
        {
            payloadPart.CopyTo(frame.AsSpan(offset));
            offset += payloadPart.Length;
        }

        return frame;
    }
}
