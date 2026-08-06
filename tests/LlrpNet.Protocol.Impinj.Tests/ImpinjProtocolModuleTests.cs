using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Impinj.Enumerations.V1_0_1;
using LlrpNet.Protocol.Impinj.Parameters.V1_0_1;
using LlrpNet.Protocol.Impinj.Registry.V1_0_1;
using LlrpNet.Protocol.Parameters;
using LlrpNet.Protocol.Registry;

namespace LlrpNet.Protocol.Impinj.Tests;

public sealed class ImpinjProtocolModuleTests
{
    [Fact]
    public void RegisterAndEncode_WorksWithoutReferencingTheSdk()
    {
        var registry = new LlrpCodecRegistry();

        ImpinjProtocolModule.Register(registry);

        var encoded = registry.EncodeParameter(
            LlrpProtocolVersion.Version101,
            new ImpinjRequestedData(
                ImpinjRequestedDataType.All_Capabilities,
                Array.Empty<ILlrpParameter>()));

        Assert.NotEmpty(encoded);
        Assert.Equal(
            ImpinjRequestedData.TypeNumber,
            (ushort)(((encoded[0] & 0x03) << 8) | encoded[1]));
    }
}
