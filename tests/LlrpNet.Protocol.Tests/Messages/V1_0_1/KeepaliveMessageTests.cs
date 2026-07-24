using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Messages.V1_0_1;
using LlrpNet.Protocol.Registry;
using LlrpNet.Protocol.Registry.V1_0_1;

namespace LlrpNet.Protocol.Tests.Messages.V1_0_1;

public sealed class KeepaliveMessageTests
{
    [Fact]
    public void Keepalive_EncodeAndDecode_MatchesNormativeBytes()
    {
        LlrpCodecRegistry registry = CreateRegistry();
        byte[] expected = [0x04, 0x3E, 0x00, 0x00, 0x00, 0x0A, 0x01, 0x02, 0x03, 0x04];

        byte[] encoded = registry.EncodeMessage(
            LlrpProtocolVersion.Version101,
            new Keepalive(0x01020304));
        var decoded = Assert.IsType<Keepalive>(registry.DecodeMessage(expected));

        Assert.Equal(expected, encoded);
        Assert.Equal((uint)0x01020304, decoded.MessageId);
    }

    [Fact]
    public void KeepaliveAck_EncodeAndDecode_MatchesNormativeBytes()
    {
        LlrpCodecRegistry registry = CreateRegistry();
        byte[] expected = [0x04, 0x48, 0x00, 0x00, 0x00, 0x0A, 0x01, 0x02, 0x03, 0x04];

        byte[] encoded = registry.EncodeMessage(
            LlrpProtocolVersion.Version101,
            new KeepaliveAck(0x01020304));
        var decoded = Assert.IsType<KeepaliveAck>(registry.DecodeMessage(expected));

        Assert.Equal(expected, encoded);
        Assert.Equal((uint)0x01020304, decoded.MessageId);
    }

    [Theory]
    [InlineData(0x3E)]
    [InlineData(0x48)]
    public void Decode_RejectsPayloadForPayloadFreeMessages(byte messageType)
    {
        LlrpCodecRegistry registry = CreateRegistry();
        byte[] frame =
        [
            0x04, messageType,
            0x00, 0x00, 0x00, 0x0B,
            0x01, 0x02, 0x03, 0x04,
            0x00,
        ];

        LlrpProtocolException exception = Assert.Throws<LlrpProtocolException>(
            () => registry.DecodeMessage(frame));

        Assert.Equal(LlrpProtocolErrorCode.InvalidParameterEncoding, exception.ErrorCode);
    }

    private static LlrpCodecRegistry CreateRegistry()
    {
        var registry = new LlrpCodecRegistry();
        Llrp101StandardModule.Register(registry);
        return registry;
    }
}
