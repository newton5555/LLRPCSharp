using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Messages;
using LlrpNet.Protocol.Parameters;
using LlrpNet.Protocol.Registry;
using LlrpNet.Protocol.Zebra.Messages.V1_0_1;
using LlrpNet.Protocol.Zebra.Parameters.V1_0_1;
using LlrpNet.Protocol.Zebra.Registry.V1_0_1;

namespace LlrpNet.Protocol.Zebra.Tests;

public sealed class ZebraProtocolModuleTests
{
    [Fact]
    public void RegisterAndEncodeCustomParameter_WorksWithoutReferencingTheSdk()
    {
        var registry = new LlrpCodecRegistry();
        ZebraProtocolModule.Register(registry);

        byte[] encoded = registry.EncodeParameter(
            LlrpProtocolVersion.Version101,
            new MotoGeneralRequestCapabilities(5));

        Assert.NotEmpty(encoded);
        Assert.Equal(
            MotoGeneralRequestCapabilities.TypeNumber,
            (ushort)(((encoded[0] & 0x03) << 8) | encoded[1]));
    }

    [Fact]
    public void EncodeDecodeCustomMessage_RoundTrips()
    {
        var registry = new LlrpCodecRegistry();
        ZebraProtocolModule.Register(registry);

        var message = new MOTO_PURGE_TAGS(7, PurgeTagEventStateOnly: false, Data: ReadOnlyMemory<byte>.Empty);
        byte[] frame = registry.EncodeMessage(LlrpProtocolVersion.Version101, message);
        ILlrpMessage decoded = registry.DecodeMessage(frame);

        var roundTripped = Assert.IsType<MOTO_PURGE_TAGS>(decoded);
        Assert.Equal(7U, roundTripped.MessageId);
        Assert.False(roundTripped.PurgeTagEventStateOnly);
        Assert.True(roundTripped.Data.IsEmpty);
    }

    [Fact]
    public void RegisterCustomParameters_CoexistWithStandardCodecs()
    {
        var registry = new LlrpCodecRegistry();
        LlrpNet.Protocol.Registry.V1_0_1.V1_0_1ProtocolModule.Register(registry);
        ZebraProtocolModule.Register(registry);

        byte[] standard = registry.EncodeParameter(
            LlrpProtocolVersion.Version101,
            new LlrpNet.Protocol.Parameters.V1_0_1.ROSpecID(14150));

        byte[] custom = registry.EncodeParameter(
            LlrpProtocolVersion.Version101,
            new MotoRadioPowerState(true));

        Assert.NotEmpty(standard);
        Assert.NotEmpty(custom);
        Assert.NotEqual(Convert.ToHexString(standard), Convert.ToHexString(custom));
    }
}
