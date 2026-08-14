using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Parameters;
using LlrpNet.Protocol.Parameters.V2_0;
using LlrpNet.Protocol.Registry;
using LlrpNet.Protocol.Registry.V2_0;

namespace LlrpNet.Protocol.Tests.V2_0;

public sealed class Llrp20ProtocolModuleTests
{
    [Fact]
    public void RegisterAndEncode_C1G2Challenge_RoundTrips()
    {
        var registry = new LlrpCodecRegistry();
        Llrp20StandardModule.Register(registry);

        var challenge = new C1G2Challenge(
            L: true,
            E: false,
            CSI: 3,
            MsgLen: 16,
            Message: [true, false, true, false],
            ExtendOnTime: 42);

        byte[] encoded = registry.EncodeParameter(LlrpProtocolVersion.Version20, challenge);
        Assert.NotEmpty(encoded);

        LlrpParameterDecodeResult result = registry.DecodeParameter(LlrpProtocolVersion.Version20, encoded);
        var decoded = Assert.IsType<C1G2Challenge>(result.Parameter);
        Assert.Equal(challenge.L, decoded.L);
        Assert.Equal(challenge.E, decoded.E);
        Assert.Equal(challenge.CSI, decoded.CSI);
        Assert.Equal(challenge.MsgLen, decoded.MsgLen);
        Assert.Equal(challenge.ExtendOnTime, decoded.ExtendOnTime);
        Assert.Equal(challenge.Message, decoded.Message);
    }

    [Fact]
    public void RegisterAndEncode_KeepaliveMessage_RoundTrips()
    {
        var registry = new LlrpCodecRegistry();
        Llrp20StandardModule.Register(registry);

        var message = new LlrpNet.Protocol.Messages.V2_0.KEEPALIVE(9);
        byte[] frame = registry.EncodeMessage(LlrpProtocolVersion.Version20, message);
        LlrpNet.Protocol.Messages.ILlrpMessage decoded = registry.DecodeMessage(frame);

        var roundTripped = Assert.IsType<LlrpNet.Protocol.Messages.V2_0.KEEPALIVE>(decoded);
        Assert.Equal(9U, roundTripped.MessageId);
    }

    [Fact]
    public void VersionKeyedRegistration_DoesNotCollideWithOlderVersions()
    {
        var registry = new LlrpCodecRegistry();
        LlrpNet.Protocol.Registry.V1_0_1.V1_0_1ProtocolModule.Register(registry);
        LlrpNet.Protocol.Registry.V1_1.Llrp11StandardModule.Register(registry);
        Llrp20StandardModule.Register(registry);

        byte[] v20 = registry.EncodeParameter(
            LlrpProtocolVersion.Version20,
            new C1G2Challenge(true, false, 3, 16, [], 0));
        byte[] v11 = registry.EncodeParameter(
            LlrpProtocolVersion.Version11,
            new LlrpNet.Protocol.Parameters.V1_1.C1G2_PC(0x3000));

        Assert.NotEmpty(v20);
        Assert.NotEmpty(v11);
    }
}
