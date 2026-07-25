using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Registry;
using LlrpSdk.Extensions;
using LlrpSdk.Extensions.Impinj.Messages.V1_0_1;

namespace LlrpSdk.Extensions.Impinj.Tests;

public sealed class ImpinjExtensionTests
{
    [Fact]
    public void ImpinjProtocolModule_RegistersCustomCodecsInRegistry()
    {
        var registry = new LlrpCodecRegistry();
        ImpinjProtocolModule.Instance.Register(registry);

        var original = new IMPINJ_ENABLE_EXTENSIONS(
            MessageId: 100,
            CustomItems: Array.Empty<LlrpNet.Protocol.Parameters.ILlrpParameter>());

        byte[] encoded = registry.EncodeMessage(LlrpProtocolVersion.Version101, original);
        Assert.NotNull(encoded);
        Assert.True(encoded.Length > 0);
    }

    [Fact]
    public void ImpinjReaderExtension_MatchesImpinjManufacturerAndVersion101()
    {
        var extension = ImpinjReaderExtension.Instance;
        Assert.Equal("impinj-reader-llrp-1.0.1", extension.Id);
        Assert.Equal("reader-vendor", extension.MutualExclusionGroup);

        var validContext = new ReaderExtensionMatchContext(
            ManufacturerId: ImpinjReaderExtension.ManufacturerId,
            ModelId: 1,
            FirmwareVersion: "10.58.0",
            ProtocolVersion: LlrpProtocolVersion.Version101);

        Assert.True(extension.Matches(validContext));

        var wrongManufacturerContext = new ReaderExtensionMatchContext(
            ManufacturerId: 99999,
            ModelId: 1,
            FirmwareVersion: "10.58.0",
            ProtocolVersion: LlrpProtocolVersion.Version101);

        Assert.False(extension.Matches(wrongManufacturerContext));

        var wrongVersionContext = new ReaderExtensionMatchContext(
            ManufacturerId: ImpinjReaderExtension.ManufacturerId,
            ModelId: 1,
            FirmwareVersion: "10.58.0",
            ProtocolVersion: LlrpProtocolVersion.Version11);

        Assert.False(extension.Matches(wrongVersionContext));
    }

    [Fact]
    public async Task UseImpinj_ConfiguresReaderBuilder()
    {
        var builder = LlrpReader.CreateBuilder("192.0.2.1")
            .UseImpinj();

        await using var reader = builder.Build();
        Assert.NotNull(reader);
        Assert.NotNull(reader.Registry);
    }
}
