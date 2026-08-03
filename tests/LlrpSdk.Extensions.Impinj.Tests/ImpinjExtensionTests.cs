using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Registry;
using LlrpSdk;
using LlrpSdk.Extensions;
using LlrpNet.Protocol.Impinj.Enumerations.V1_0_1;
using LlrpNet.Protocol.Impinj.Messages.V1_0_1;
using LlrpNet.Protocol.Impinj.Parameters.V1_0_1;

namespace LlrpSdk.Extensions.Impinj.Tests;

public sealed class ImpinjExtensionTests
{
    [Fact]
    public void InventoryBuilder_CreatesTypedImpinjExtensionsWithoutMagicKeys()
    {
        InventorySettings settings = InventorySettings.Create(inventory => inventory
            .Antennas(1, 2)
            .Session(2)
            .Impinj(impinj => impinj
                .IncludeSerializedTid()
                .IncludeRfPhaseAngle()
                .IncludePeakRssi()
                .EnableTagPopulationEstimation()));

        var report = Assert.IsType<ImpinjInventoryReportOptions>(
            settings.Extensions[ImpinjInventoryReportOptions.ExtensionKey]);
        var control = Assert.IsType<ImpinjInventoryControlOptions>(
            settings.Extensions[ImpinjInventoryControlOptions.ExtensionKey]);
        Assert.True(report.IncludeSerializedTid);
        Assert.True(report.IncludeRfPhaseAngle);
        Assert.True(report.IncludePeakRssi);
        Assert.True(control.EnableTagPopulationEstimation);

        InventorySettings edited = settings.Edit(inventory => inventory
            .Impinj(impinj => impinj.IncludeTxPower()));
        var editedReport = Assert.IsType<ImpinjInventoryReportOptions>(
            edited.Extensions[ImpinjInventoryReportOptions.ExtensionKey]);
        Assert.True(editedReport.IncludeSerializedTid);
        Assert.True(editedReport.IncludeTxPower);
    }

    [Fact]
    public void ReaderSettingsSerializer_RoundTripsVersionedTypedImpinjExtensions()
    {
        var settings = new ReaderSettings
        {
            Configuration = new ReaderConfiguration
            {
                Extensions = new Dictionary<string, object?>
                {
                    [ImpinjReaderConfiguration.ExtensionKey] = new ImpinjReaderConfiguration
                    {
                        InventorySearchMode = ImpinjInventorySearchType.Single_Target,
                        GpiDebounce = [new ImpinjGpiDebounceSetting(1, 250)],
                    },
                    [ImpinjReaderFacts.ExtensionKey] = new ImpinjReaderFacts
                    {
                        TemperatureCelsius = 35,
                    },
                },
            },
            Inventory = new InventorySettings
            {
                Extensions = new Dictionary<string, object?>
                {
                    [ImpinjInventoryReportOptions.ExtensionKey] = new ImpinjInventoryReportOptions
                    {
                        IncludeSerializedTid = true,
                        IncludePeakRssi = true,
                    },
                },
            },
        };

        string json = ReaderSettingsSerializer.SerializeToJson(settings, [ImpinjReaderExtension.Instance]);
        ReaderSettings restored = ReaderSettingsSerializer.DeserializeFromJson(json, [ImpinjReaderExtension.Instance]);

        Assert.Contains("\"schemaVersion\": 1", json, StringComparison.Ordinal);
        Assert.IsType<ImpinjReaderConfiguration>(restored.Configuration.Extensions[ImpinjReaderConfiguration.ExtensionKey]);
        var facts = Assert.IsType<ImpinjReaderFacts>(restored.Configuration.Extensions[ImpinjReaderFacts.ExtensionKey]);
        Assert.Equal((short)35, facts.TemperatureCelsius);
        var reports = Assert.IsType<ImpinjInventoryReportOptions>(restored.Inventory!.Extensions[ImpinjInventoryReportOptions.ExtensionKey]);
        Assert.True(reports.IncludeSerializedTid);
        Assert.True(reports.IncludePeakRssi);
    }

    [Fact]
    public void ReaderConfiguration_CompilesTypedImpinjExtensionParameters()
    {
        var configuration = new ReaderConfiguration
        {
            Extensions = new Dictionary<string, object?>
            {
                [ImpinjReaderConfiguration.ExtensionKey] = new ImpinjReaderConfiguration
                {
                    InventorySearchMode = ImpinjInventorySearchType.Single_Target,
                    FixedFrequency = new ImpinjFixedFrequencySettings(
                        ImpinjFixedFrequencyMode.Channel_List, [1, 4]),
                    GpiDebounce = [new ImpinjGpiDebounceSetting(1, 250)],
                    LinkMonitor = new ImpinjLinkMonitorSettings(true, 3),
                    AccessSpec = new ImpinjAccessSpecSettings(4, 2, ImpinjAccessSpecOrderingMode.FIFO),
                },
            },
        };

        IReadOnlyList<LlrpNet.Protocol.Parameters.ILlrpParameter> parameters =
            ImpinjReaderExtension.Instance.BuildApplyParameters(configuration);

        Assert.Contains(parameters, static item => item is ImpinjInventorySearchMode);
        Assert.Contains(parameters, static item => item is ImpinjFixedFrequencyList list &&
            list.ChannelList.Count == 2 && list.ChannelList[0] == 1 && list.ChannelList[1] == 4);
        Assert.Contains(parameters, static item => item is ImpinjGPIDebounceConfiguration debounce && debounce.GPIDebounceTimerMSec == 250);
        Assert.Contains(parameters, static item => item is ImpinjLinkMonitorConfiguration monitor && monitor.LinkMonitorMode == ImpinjLinkMonitorMode.Enabled);
        Assert.Contains(parameters, static item => item is ImpinjAccessSpecConfiguration);
    }

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
        Assert.False(capabilities.SupportsTagPopulationEstimation);
        Assert.False(capabilities.SupportsXpcWords);
        Assert.Equal(
            "SDK verification confirmed Serialized TID, RF Phase Angle, and Peak RSSI report fields; tag population estimation was rejected by this firmware.",
            capabilities.Reason);
    }

    [Fact]
    public void InventoryReportOptions_R420RejectsUnverifiedReportField()
    {
        var context = new ReaderExtensionMatchContext(
            ManufacturerId: ImpinjReaderExtension.ManufacturerId,
            ModelId: 2_001_002,
            FirmwareVersion: "6.4.1.240",
            ProtocolVersion: LlrpProtocolVersion.Version101);

        Assert.Throws<NotSupportedException>(() =>
            ImpinjInventoryReportConfigurator.BuildCustomItems(context, new ImpinjInventoryReportOptions
            {
                IncludeXpcWords = true,
            }));
    }

    [Fact]
    public void InventoryControlOptions_R420RejectsUnverifiedPopulationEstimation()
    {
        var context = new ReaderExtensionMatchContext(
            ManufacturerId: ImpinjReaderExtension.ManufacturerId,
            ModelId: 2_001_002,
            FirmwareVersion: "6.4.1.240",
            ProtocolVersion: LlrpProtocolVersion.Version101);

        Assert.Throws<NotSupportedException>(() =>
            ImpinjInventoryControlConfigurator.BuildCustomItems(context, new ImpinjInventoryControlOptions
            {
                EnableTagPopulationEstimation = true,
            }));
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
    public void InventoryReportConfigurator_CompilesAndRestoresOptimizedReads()
    {
        var context = new ReaderExtensionMatchContext(
            ImpinjReaderExtension.ManufacturerId, 999_999, "99.0.0", LlrpProtocolVersion.Version101);
        var options = new ImpinjInventoryReportOptions
        {
            IncludeOptimizedRead = true,
            OptimizedReads = [new ImpinjOptimizedReadOperation(7, TagMemoryBank.User, 4, 3, 0x11223344)],
            AllowUnverifiedFields = true,
        };

        var selector = Assert.IsType<ImpinjTagReportContentSelector>(
            Assert.Single(ImpinjInventoryReportConfigurator.BuildCustomItems(context, options)));
        var read = Assert.Single(selector.ImpinjEnableOptimizedRead!.C1G2ReadItems);
        Assert.Equal((ushort)7, read.OpSpecID);
        Assert.Equal((byte)3, read.MB);
        Assert.Equal((ushort)4, read.WordPointer);
        Assert.Equal((ushort)3, read.WordCount);
        Assert.Equal(0x11223344u, read.AccessPassword);

        var identity = new ReaderIdentity(ImpinjReaderExtension.ManufacturerId, 999_999, "99.0.0");
        var extensions = new InventorySettingsExtensionBuilder();
        ImpinjReaderExtension.Instance.ContributeQuery(
            new InventorySettingsContributionContext(identity, null!, LlrpProtocolVersion.Version101,
                [selector], []), extensions);
        var restored = Assert.IsType<ImpinjInventoryReportOptions>(
            extensions.Build()[ImpinjInventoryReportOptions.ExtensionKey]);
        Assert.True(restored.AllowUnverifiedFields);
        Assert.Equal(options.OptimizedReads, restored.OptimizedReads);
    }

    [Fact]
    public void TagReportContributor_ProjectsKnownImpinjFields()
    {
        var report = new TagReport(new byte[] { 0x30, 0x00 }, 14150, 1, 1, 2, -45, 3, null, null, 1, null);
        var context = new TagReportContributionContext(report,
        [
            new ImpinjGPSCoordinates(31_230_000, 121_470_000, []),
            new ImpinjRFDopplerFrequency(-32, []),
            new ImpinjTxPower(20, []),
            new ImpinjXPCWords([0x1234, 0x5678], []),
            new ImpinjCRHandle(42, []),
            new ImpinjID([true, false, true], []),
            new ImpinjEnhancedIntegraReport(ImpinjEnhancedIntegraResultType.No_Parity_Error, 8, []),
            new ImpinjEndpointICVerificationReport(1, 9, []),
        ]);
        var builder = new TagReportExtensionBuilder();

        ImpinjReaderExtension.Instance.Contribute(context, builder);

        var values = builder.Build();
        var gps = Assert.IsType<ImpinjGpsCoordinates>(values["impinj.gpsCoordinates"]);
        Assert.Equal(31.23, gps.LatitudeDegrees, 5);
        Assert.Equal(-2d, Assert.IsType<ImpinjRfDopplerFrequency>(values["impinj.rfDopplerFrequency"]).Hertz);
        Assert.Equal((ushort)20, values["impinj.txPower"]);
        Assert.Equal((uint)42, values["impinj.crHandle"]);
        Assert.Equal("A", Assert.IsType<ImpinjBitVector>(values["impinj.id"]).Hex);
        Assert.IsType<ImpinjEnhancedIntegraResult>(values["impinj.enhancedIntegra"]);
        Assert.IsType<ImpinjEndpointIcVerification>(values["impinj.endpointIcVerification"]);
    }

    [Fact]
    public void ImpinjReaderExtension_RegistersAsInventoryContributor()
    {
        Assert.IsAssignableFrom<IInventoryContributor>(ImpinjReaderExtension.Instance);
        Assert.IsAssignableFrom<IInventorySettingsContributor>(ImpinjReaderExtension.Instance);
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
