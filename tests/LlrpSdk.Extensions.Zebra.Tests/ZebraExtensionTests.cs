using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Parameters;
using LlrpNet.Protocol.Zebra.Parameters.V1_0_1;
using LlrpSdk.Extensions;
using LlrpSdk.Extensions.Zebra;

namespace LlrpSdk.Extensions.Zebra.Tests;

public sealed class ZebraExtensionTests
{
    [Fact]
    public void SettingsContributor_RoundTripsConfiguration()
    {
        var extension = ZebraReaderExtension.Instance;
        var configuration = new ReaderConfiguration
        {
            Extensions = new Dictionary<string, object?>
            {
                [ZebraReaderConfiguration.ExtensionKey] = new ZebraReaderConfiguration
                {
                    RadioPowerState = true,
                    RadioTransmitDelay = 5,
                    AutonomousModeState = false,
                    SaveConfiguration = true,
                    SaveTagData = false,
                    SaveTagEventData = true,
                    EnableNxpSetAndResetQuietCommands = true,
                },
            },
        };

        IReadOnlyList<ILlrpParameter> applyParameters = extension.BuildApplyParameters(configuration);
        var queryContext = new ReaderSettingsContributionContext(configuration, applyParameters);
        var builder = new ReaderConfigurationExtensionBuilder();
        extension.ContributeQuery(queryContext, builder);

        ZebraReaderConfiguration roundTripped = Assert.IsType<ZebraReaderConfiguration>(
            configuration.Extensions[ZebraReaderConfiguration.ExtensionKey]);
        Assert.True(roundTripped.RadioPowerState);
        Assert.Equal((byte)5, roundTripped.RadioTransmitDelay);
        Assert.False(roundTripped.AutonomousModeState);
        Assert.True(roundTripped.SaveConfiguration);
        Assert.False(roundTripped.SaveTagData);
        Assert.True(roundTripped.SaveTagEventData);
        Assert.True(roundTripped.EnableNxpSetAndResetQuietCommands);
    }

    [Fact]
    public void InventoryContributor_EmitsAndRestoresReportSelector()
    {
        var extension = ZebraReaderExtension.Instance;
        var settings = new InventorySettings
        {
            Extensions = new Dictionary<string, object?>
            {
                [ZebraInventoryReportOptions.ExtensionKey] = new ZebraInventoryReportOptions
                {
                    IncludeZoneId = true,
                    IncludeZoneName = false,
                    IncludeAntennaPhysicalPortConfig = true,
                    IncludePhase = true,
                    IncludeGps = false,
                    IncludeMltReport = true,
                },
            },
        };

        var identity = new ReaderIdentity(ZebraReaderExtension.ManufacturerId, 96008, "3.32.37.0");
        var forwardContext = new InventoryContributionContext(settings, identity, null!, LlrpProtocolVersion.Version101);
        var forwardBuilder = new InventoryExtensionBuilder();
        extension.Contribute(forwardContext, forwardBuilder);
        Assert.NotEmpty(forwardBuilder.RoReportSpecCustomItems);

        var reverseContext = new InventorySettingsContributionContext(
            identity, null!, LlrpProtocolVersion.Version101,
            forwardBuilder.RoReportSpecCustomItems,
            C1G2InventoryCommandCustomItems: []);
        var reverseBuilder = new InventorySettingsExtensionBuilder();
        extension.ContributeQuery(reverseContext, reverseBuilder);

        var restored = Assert.IsType<ZebraInventoryReportOptions>(
            reverseBuilder.Build()[ZebraInventoryReportOptions.ExtensionKey]);
        Assert.True(restored.IncludeZoneId);
        Assert.False(restored.IncludeZoneName);
        Assert.True(restored.IncludeAntennaPhysicalPortConfig);
        Assert.True(restored.IncludePhase);
        Assert.False(restored.IncludeGps);
        Assert.True(restored.IncludeMltReport);
    }

    [Fact]
    public void ZebraDefaults_IncludeCapabilityResolvedStandardValuesAndTypedVendorDefaults()
    {
        var capabilities = new ReaderCapabilities(
            maxNumberOfAntennas: 4,
            canSetAntennaProperties: true,
            hasUtcClockCapability: true,
            generalDeviceParameters: [],
            rawResponse: new LlrpNet.Protocol.Messages.V1_0_1.ENABLE_EVENTS_AND_REPORTS(1),
            additionalParameters: [],
            txPowers: [new TxPowerEntry(10, 3000), new TxPowerEntry(2, 2500)],
            rxSensitivities: [new RxSensitivityEntry(1, 0)],
            rfModes:
            [
                new C1G2RfModeEntry(3, "DR", true, 2, "FM0", "DI", 120_000, 1_500, 25_000, 25_000, 0),
            ]);
        var context = new ReaderSettingsDefaultContext(
            new ReaderIdentity(ZebraReaderExtension.ManufacturerId, 96_008, "3.32.37.0"),
            capabilities,
            LlrpProtocolVersion.Version101);

        ReaderSettingsDefaults defaults = Assert.IsType<ReaderSettingsDefaults>(
            ZebraReaderExtension.Instance.GetDefaultSettings(context));
        ZebraReaderConfiguration configuration = Assert.IsType<ZebraReaderConfiguration>(
            defaults.Settings.Configuration.Extensions[ZebraReaderConfiguration.ExtensionKey]);
        InventorySettings inventory = Assert.IsType<InventorySettings>(defaults.Settings.Inventory);
        ZebraInventoryReportOptions report = Assert.IsType<ZebraInventoryReportOptions>(
            inventory.Extensions[ZebraInventoryReportOptions.ExtensionKey]);

        Assert.Equal("zebra.fx9600.llrp-1.0.1", defaults.ProfileId);
        Assert.True(configuration.RadioPowerState);
        Assert.Equal((byte)0, configuration.RadioTransmitDelay);
        Assert.False(configuration.AutonomousModeState);
        Assert.False(report.IncludePhase);

        string json = ReaderSettingsSerializer.SerializeToJson(defaults.Settings, [ZebraReaderExtension.Instance]);
        ReaderSettings restored = ReaderSettingsSerializer.DeserializeFromJson(json, [ZebraReaderExtension.Instance]);
        Assert.IsType<ZebraInventoryReportOptions>(
            restored.Inventory!.Extensions[ZebraInventoryReportOptions.ExtensionKey]);
    }

    [Fact]
    public void TagReportContributor_ProjectsPhaseGpsAndXpc()
    {
        var extension = ZebraReaderExtension.Instance;
        var report = new TagReport(
            ReadOnlyMemory<byte>.Empty, null, null, null, null, null, null, null, null, null, null);
        var context = new TagReportContributionContext(
            report,
            [
                new MotoTagPhase(-123),
                new MotoTagGPS(100, 200, 300),
                new MotoC1G2ExtendedPC(0xE201, 0x0001),
            ]);
        var builder = new TagReportExtensionBuilder();
        extension.Contribute(context, builder);

        TagReport enriched = report with { Extensions = builder.Build() };
        Assert.Equal((short)-123, enriched.GetPhase());
        Assert.Equal(new ZebraGpsCoordinates(100, 200, 300), enriched.GetGps());
        Assert.Equal(new ZebraExtendedPc(0xE201, 0x0001), enriched.GetExtendedPc());
    }

    [Fact]
    public void SerializationContributor_RoundTripsJson()
    {
        var extension = ZebraReaderExtension.Instance;
        var value = new ZebraReaderConfiguration
        {
            RadioPowerState = true,
            RadioTransmitDelay = 9,
            SaveTagData = true,
        };

        Assert.True(extension.CanHandle(ReaderSettingsExtensionScope.Configuration, ZebraReaderConfiguration.ExtensionKey, value));
        System.Text.Json.Nodes.JsonNode node = extension.Serialize(
            ReaderSettingsExtensionScope.Configuration, ZebraReaderConfiguration.ExtensionKey, value);
        object? restored = extension.Deserialize(
            ReaderSettingsExtensionScope.Configuration, ZebraReaderConfiguration.ExtensionKey, node);

        var roundTripped = Assert.IsType<ZebraReaderConfiguration>(restored);
        Assert.True(roundTripped.RadioPowerState);
        Assert.Equal((byte)9, roundTripped.RadioTransmitDelay);
        Assert.True(roundTripped.SaveTagData);
    }

    [Fact]
    public void UseZebra_WiresModuleAndExtension()
    {
        LlrpReaderOptions options = LlrpReader.CreateBuilder("zebra.local")
            .UseZebra()
            .BuildOptions();

        Assert.Contains(options.ProtocolModules, static module => module is ZebraProtocolModule);
        Assert.Contains(options.ReaderExtensions, static ext => ext is ZebraReaderExtension);
    }
}
