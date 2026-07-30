using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Parameters.V1_1;
using LlrpNet.Protocol.Registry;
using LlrpNet.Protocol.Registry.V1_1;
using V11I = LlrpNet.Protocol.Enumerations.V1_1.C1G2TagInventoryStateAwareI;
using V11S = LlrpNet.Protocol.Enumerations.V1_1.C1G2TagInventoryStateAwareS;

namespace LlrpNet.Protocol.Tests.Parameters.V1_1;

public sealed class C1G2TagInventoryStateAwareSingulationActionTests
{
    [Fact]
    public void EncodeAndDecode_SAll_RoundTripsAsThirdBit()
    {
        var registry = new LlrpCodecRegistry();
        Llrp11StandardModule.Register(registry);
        var parameter = new C1G2TagInventoryStateAwareSingulationAction(
            V11I.State_B,
            V11S.SL,
            SAll: true);

        byte[] encoded = registry.EncodeParameter(LlrpProtocolVersion.Version11, parameter);
        var decoded = Assert.IsType<C1G2TagInventoryStateAwareSingulationAction>(
            registry.DecodeParameter(LlrpProtocolVersion.Version11, encoded).Parameter);

        Assert.Equal(new byte[] { 0x01, 0x51, 0x00, 0x05, 0xA0 }, encoded);
        Assert.Equal(parameter, decoded);
    }
}
