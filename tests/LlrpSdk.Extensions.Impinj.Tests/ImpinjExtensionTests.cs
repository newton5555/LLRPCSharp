using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Registry;
using LlrpSdk.Extensions;
using LlrpSdk.Extensions.Impinj.Enumerations.V1_0_1;
using LlrpSdk.Extensions.Impinj.Messages.V1_0_1;
using LlrpSdk.Extensions.Impinj.Parameters.V1_0_1;

namespace LlrpSdk.Extensions.Impinj.Tests;

public sealed class ImpinjExtensionTests
{
    [Fact]
    public void InventoryCapabilities_R420Firmware641_AcceptsTagReportContentSelector()
    {
        var context = new ReaderExtensionMatchContext(
            ManufacturerId: ImpinjReaderExtension.ManufacturerId,
            ModelId: 2_001_002,
            FirmwareVersion: "6.4.1.240",
            ProtocolVersion: LlrpProtocolVersion.Version101);

        ImpinjInventoryCapabilities capabilities = ImpinjInventoryCapabilityCatalog.Get(context);

        Assert.True(capabilities.SupportsTagReportContentSelector);
        Assert.True(capabilities.SupportsSerializedTid);
        Assert.True(capabilities.SupportsRfPhaseAngle);
        Assert.True(capabilities.SupportsPeakRssi);
        Assert.Equal(
            "SDK verification confirmed Serialized TID, RF Phase Angle, and Peak RSSI report fields.",
            capabilities.Reason);
    }

    [Fact]
    public void InventoryCapabilities_UnknownImpinjReader_DoesNotAssumeVendorReportExtensions()
    {
        var context = new ReaderExtensionMatchContext(
            ManufacturerId: ImpinjReaderExtension.ManufacturerId,
            ModelId: 999_999,
            FirmwareVersion: "99.0.0",
            ProtocolVersion: LlrpProtocolVersion.Version101);

        ImpinjInventoryCapabilities capabilities = ImpinjInventoryCapabilityCatalog.Get(context);

        Assert.False(capabilities.SupportsTagReportContentSelector);
        Assert.False(capabilities.SupportsSerializedTid);
        Assert.False(capabilities.SupportsRfPhaseAngle);
        Assert.False(capabilities.SupportsPeakRssi);
        Assert.Equal("No verified inventory capability profile matches this reader.", capabilities.Reason);
    }

    [Fact]
    public void InventoryReportConfigurator_R420Firmware641_EmitsPeakRssiSelector()
    {
        var context = new ReaderExtensionMatchContext(
            ManufacturerId: ImpinjReaderExtension.ManufacturerId,
            ModelId: 2_001_002,
            FirmwareVersion: "6.4.1.240",
            ProtocolVersion: LlrpProtocolVersion.Version101);
        var options = new ImpinjInventoryReportOptions { IncludePeakRssi = true };

        IReadOnlyList<LlrpNet.Protocol.Parameters.ILlrpParameter> items =
            ImpinjInventoryReportConfigurator.BuildCustomItems(context, options);

        var selector = Assert.IsType<ImpinjTagReportContentSelector>(Assert.Single(items));
        Assert.Null(selector.ImpinjEnableSerializedTID);
        Assert.Null(selector.ImpinjEnableRFPhaseAngle);
        Assert.NotNull(selector.ImpinjEnablePeakRSSI);
        Assert.Equal(ImpinjPeakRSSIMode.Enabled, selector.ImpinjEnablePeakRSSI.PeakRSSIMode);
    }

    [Fact]
    public void InventoryReportConfigurator_R420Firmware641_EmitsAllVerifiedReportSelectors()
    {
        var context = new ReaderExtensionMatchContext(
            ManufacturerId: ImpinjReaderExtension.ManufacturerId,
            ModelId: 2_001_002,
            FirmwareVersion: "6.4.1.240",
            ProtocolVersion: LlrpProtocolVersion.Version101);
        var options = new ImpinjInventoryReportOptions
        {
            IncludeSerializedTid = true,
            IncludeRfPhaseAngle = true,
            IncludePeakRssi = true,
        };

        IReadOnlyList<LlrpNet.Protocol.Parameters.ILlrpParameter> items =
            ImpinjInventoryReportConfigurator.BuildCustomItems(context, options);

        var selector = Assert.IsType<ImpinjTagReportContentSelector>(Assert.Single(items));
        Assert.Equal(ImpinjSerializedTIDMode.Enabled, selector.ImpinjEnableSerializedTID?.SerializedTIDMode);
        Assert.Equal(ImpinjRFPhaseAngleMode.Enabled, selector.ImpinjEnableRFPhaseAngle?.RFPhaseAngleMode);
        Assert.Equal(ImpinjPeakRSSIMode.Enabled, selector.ImpinjEnablePeakRSSI?.PeakRSSIMode);
    }

    [Fact]
    public void InventoryReportConfigurator_EmptyOptions_DoesNotAddVendorParameter()
    {
        var context = new ReaderExtensionMatchContext(
            ManufacturerId: ImpinjReaderExtension.ManufacturerId,
            ModelId: 2_001_002,
            FirmwareVersion: "6.4.1.240",
            ProtocolVersion: LlrpProtocolVersion.Version101);

        IReadOnlyList<LlrpNet.Protocol.Parameters.ILlrpParameter> items =
            ImpinjInventoryReportConfigurator.BuildCustomItems(context, new ImpinjInventoryReportOptions());

        Assert.Empty(items);
    }

    [Fact]
    public void ImpinjReaderExtension_RegistersAsInventoryContributor()
    {
        Assert.IsAssignableFrom<IInventoryContributor>(ImpinjReaderExtension.Instance);
    }

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
