using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;
using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Enumerations.V1_0_1;
using LlrpNet.Protocol.Messages.V1_0_1;
using LlrpNet.Protocol.Parameters;
using LlrpNet.Protocol.Parameters.V1_0_1;
using LlrpSdk.Tests.Support;
using LlrpNet.Protocol.Registry;
using LlrpNet.Protocol.Registry.V1_0_1;
using LlrpSdk.Extensions.Seuic;
using V11Parameters = LlrpNet.Protocol.Parameters.V1_1;

namespace LlrpSdk.Tests;

public sealed class LlrpReaderConfigurationTests
{
    [Fact]
    public void SettingsBuilders_CreateAndEditCanonicalRecords()
    {
        ReaderSettings created = ReaderSettings.Create(settings => settings
            .Inventory(inventory => inventory
                .Antennas(1, 2)
                .Session(2)
                .Population(64)
                .ReportEveryTag()
                .ReadTid(words: 6)));

        Assert.Equal(new ushort[] { 1, 2 }, created.Inventory!.AntennaIds);
        Assert.Equal((byte)2, created.Inventory.Session);
        Assert.Equal((ushort)64, created.Inventory.TagPopulationEstimate);
        Assert.True(created.Inventory.AttachedData.Enabled);
        Assert.Equal((ushort)TagMemoryBank.Tid, created.Inventory.AttachedData.MemoryBank);

        ReaderSettings edited = created.Edit(settings => settings.Inventory(inventory => inventory.BatchAfterStop()));

        Assert.NotSame(created, edited);
        Assert.Equal(created.Configuration, edited.Configuration);
        Assert.Equal(created.Inventory.AntennaIds, edited.Inventory!.AntennaIds);
        Assert.Equal((ushort)0, edited.Inventory.ReportEveryNTags);
        Assert.Equal(InventoryReportTrigger.UponNTagsOrEndOfRoSpec, edited.Inventory.Report.Trigger);
    }

    [Fact]
    public async Task ValidateSettings_ReturnsStructuredDiagnosticsWithoutWritingReaderState()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var transport = new ScriptedLlrpTransport();
        bool settingsWriteSent = false;
        transport.OnSendAsync = (frame, _) =>
        {
            ushort messageType = LlrpMessageHeader.Decode(frame.Span).MessageType;
            settingsWriteSent |= messageType == SET_READER_CONFIG.MessageType ||
                messageType == DELETE_ROSPEC.MessageType ||
                messageType == DELETE_ACCESSSPEC.MessageType;
            return ValueTask.CompletedTask;
        };

        await using LlrpReader reader = CreateReader(transport);
        await reader.ConnectAsync(timeout.Token);
        var settings = new ReaderSettings
        {
            Configuration = new ReaderConfiguration
            {
                Keepalive = new KeepaliveConfiguration { TriggerType = KeepaliveTriggerType.Periodic },
            },
            Inventory = new InventorySettings
            {
                AntennaIds = [0, 1],
                Session = 4,
                TagPopulationEstimate = 0,
            },
        };

        SettingsValidationResult result = await reader.ValidateSettingsAsync(settings, timeout.Token);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, static item => item.Path == "configuration.keepalive.intervalMs");
        Assert.Contains(result.Diagnostics, static item => item.Path == "inventory.antennaIds");
        Assert.Contains(result.Diagnostics, static item => item.Path == "inventory.session");
        Assert.Contains(result.Diagnostics, static item => item.Path == "inventory.tagPopulationEstimate");
        Assert.False(settingsWriteSent);
    }

    [Fact]
    public async Task ApplySettings_ValidatesBeforeMutatingReaderResources()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var transport = new ScriptedLlrpTransport();
        bool mutationSent = false;
        transport.OnSendAsync = (frame, _) =>
        {
            ushort messageType = LlrpMessageHeader.Decode(frame.Span).MessageType;
            mutationSent |= messageType == SET_READER_CONFIG.MessageType ||
                messageType == DELETE_ROSPEC.MessageType ||
                messageType == DELETE_ACCESSSPEC.MessageType;
            return ValueTask.CompletedTask;
        };

        await using LlrpReader reader = CreateReader(transport);
        await reader.ConnectAsync(timeout.Token);
        var settings = new ReaderSettings
        {
            Inventory = new InventorySettings
            {
                Session = 4,
            },
        };

        SettingsValidationException error = await Assert.ThrowsAsync<SettingsValidationException>(
            () => reader.ApplySettingsAsync(settings, timeout.Token));

        Assert.Contains(error.Diagnostics, static item => item.Code == "SET-INV-005");
        Assert.False(mutationSent);
    }

    [Fact]
    public void ReaderIdentity_TrimsProtocolStringPadding()
    {
        var identity = new ReaderIdentity(1, 2, "1.0.0\0\0");

        Assert.Equal("1.0.0", identity.FirmwareVersion);
    }

    [Fact]
    public async Task QuerySettings_SendsGetReaderConfigAndReturnsConfiguration()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var transport = new ScriptedLlrpTransport();
        
        await using LlrpReader reader = CreateReader(transport);
        await reader.ConnectAsync(timeout.Token);

        // Enqueue response for GET_READER_CONFIG
        transport.OnSendAsync = (frame, ct) =>
        {
            LlrpMessageHeader header = LlrpMessageHeader.Decode(frame.Span);
            var registry = new LlrpCodecRegistry();
            Llrp101StandardModule.Register(registry);
            if (header.MessageType == GET_READER_CONFIG.MessageType)
            {
                var keepaliveSpec = new KeepaliveSpec(global::LlrpNet.Protocol.Enumerations.V1_0_1.KeepaliveTriggerType.Periodic, 10000);
                var antennaProps = new List<AntennaProperties>
                {
                    new(AntennaConnected: true, AntennaID: 1, AntennaGain: 15)
                };
                var antennaConfigs = new List<AntennaConfiguration>
                {
                    new(
                        AntennaID: 1,
                        new RFReceiver(80),
                        new RFTransmitter(HopTableID: 0, ChannelIndex: 2, TransmitPower: 60),
                        []
                    )
                };
                var gpoStates = new List<GPOWriteData>
                {
                    new(GPOPortNumber: 2, GPOData: true)
                };
                var gpiStates = new List<GPIPortCurrentState>
                {
                    new(GPIPortNum: 3, Config: true, GPIPortState.High)
                };

                var eventStates = new List<EventNotificationState>
                {
                    new(NotificationEventType.GPI_Event, true),
                    new(NotificationEventType.ROSpec_Event, false)
                };
                var eventSpec = new ReaderEventNotificationSpec(eventStates);

                var response = new GET_READER_CONFIG_RESPONSE(
                    header.MessageId,
                    new LLRPStatus(StatusCode.M_Success, string.Empty, null, null),
                    Identification: null,
                    AntennaPropertiesItems: antennaProps,
                    AntennaConfigurationItems: antennaConfigs,
                    ReaderEventNotificationSpec: eventSpec,
                    ROReportSpec: null,
                    AccessReportSpec: null,
                    LLRPConfigurationStateValue: null,
                    KeepaliveSpec: keepaliveSpec,
                    GPIPortCurrentStateItems: gpiStates,
                    GPOWriteDataItems: gpoStates,
                    EventsAndReports: null,
                    CustomItems: []
                );

                byte[] encoded = registry.EncodeMessage(LlrpProtocolVersion.Version101, response);
                transport.EnqueueFrame(encoded);
            }
            else if (header.MessageType == GET_ROSPECS.MessageType)
            {
                transport.EnqueueFrame(registry.EncodeMessage(LlrpProtocolVersion.Version101,
                    new GET_ROSPECS_RESPONSE(header.MessageId, new LLRPStatus(StatusCode.M_Success, string.Empty, null, null), [])));
            }
            else if (header.MessageType == GET_ACCESSSPECS.MessageType)
            {
                transport.EnqueueFrame(registry.EncodeMessage(LlrpProtocolVersion.Version101,
                    new GET_ACCESSSPECS_RESPONSE(header.MessageId, new LLRPStatus(StatusCode.M_Success, string.Empty, null, null), [])));
            }
            return ValueTask.CompletedTask;
        };

        ReaderConfiguration config = (await reader.QuerySettingsAsync(timeout.Token)).Settings.Configuration;

        Assert.NotNull(config);
        Assert.Equal(KeepaliveTriggerType.Periodic, config.Keepalive.TriggerType);
        Assert.Equal(10000U, config.Keepalive.IntervalMs);

        Assert.Single(config.Antennas);
        var ant = config.Antennas[0];
        Assert.Equal((ushort)1, ant.AntennaId);
        Assert.True(ant.IsConnected);
        Assert.Equal((short)15, ant.Gain);
        Assert.Equal((ushort)60, ant.TransmitPowerIndex);
        Assert.Equal((ushort)80, ant.ReceiverSensitivityIndex);
        Assert.Equal((ushort)2, ant.ChannelIndex);

        Assert.Single(config.Gpos);
        Assert.Equal((ushort)2, config.Gpos[0].GpoPortNumber);
        Assert.True(config.Gpos[0].GpoData);

        Assert.Single(config.Gpis);
        Assert.Equal((ushort)3, config.Gpis[0].GpiPortNumber);
        Assert.True(config.Gpis[0].Configured);
        Assert.Equal(GpiState.High, config.Gpis[0].State);

        Assert.True(config.Events.GpiEventEnabled);
        Assert.False(config.Events.RoSpecEventEnabled);
    }

    [Fact]
    public async Task ApplySettings_SendsSetReaderConfig()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var transport = new ScriptedLlrpTransport();

        await using LlrpReader reader = CreateReader(transport);
        await reader.ConnectAsync(timeout.Token);

        bool setConfigSent = false;
        transport.OnSendAsync = (frame, ct) =>
        {
            LlrpMessageHeader header = LlrpMessageHeader.Decode(frame.Span);
            if (header.MessageType == SET_READER_CONFIG.MessageType)
            {
                setConfigSent = true;
                
                var registry = new LlrpCodecRegistry();
                Llrp101StandardModule.Register(registry);
                var setConfigMsg = (SET_READER_CONFIG)registry.DecodeMessage(frame.Span);

                Assert.False(setConfigMsg.ResetToFactoryDefault);
                Assert.NotNull(setConfigMsg.KeepaliveSpec);
                Assert.Equal(global::LlrpNet.Protocol.Enumerations.V1_0_1.KeepaliveTriggerType.Periodic, setConfigMsg.KeepaliveSpec.KeepaliveTriggerType);
                Assert.Equal(5000U, setConfigMsg.KeepaliveSpec.PeriodicTriggerValue);

                Assert.Single(setConfigMsg.AntennaConfigurationItems);
                var antConfig = setConfigMsg.AntennaConfigurationItems[0];
                Assert.Equal((ushort)1, antConfig.AntennaID);
                Assert.NotNull(antConfig.RFTransmitter);
                Assert.Equal((ushort)30, antConfig.RFTransmitter.TransmitPower);
                Assert.Equal((ushort)4, antConfig.RFTransmitter.ChannelIndex);

                Assert.Single(setConfigMsg.GPOWriteDataItems);
                Assert.Equal((ushort)2, setConfigMsg.GPOWriteDataItems[0].GPOPortNumber);
                Assert.True(setConfigMsg.GPOWriteDataItems[0].GPOData);

                var response = new SET_READER_CONFIG_RESPONSE(
                    header.MessageId,
                    new LLRPStatus(StatusCode.M_Success, string.Empty, null, null)
                );
                byte[] encoded = registry.EncodeMessage(LlrpProtocolVersion.Version101, response);
                transport.EnqueueFrame(encoded);
            }
            return ValueTask.CompletedTask;
        };

        var config = new ReaderConfiguration
        {
            Keepalive = new KeepaliveConfiguration { TriggerType = KeepaliveTriggerType.Periodic, IntervalMs = 5000 },
            Antennas = [new AntennaConfigurationSettings { AntennaId = 1, TransmitPowerIndex = 30, ChannelIndex = 4 }],
            Gpos = [new GpoConfiguration { GpoPortNumber = 2, GpoData = true }]
        };

        await reader.ApplySettingsAsync(new ReaderSettings { Configuration = config }, timeout.Token);
        Assert.True(setConfigSent);
        Assert.True(reader.IsManagedStateSynchronized);
    }

    [Fact]
    public void ReaderSettings_C1G2SingulationCompiler_IncludesSingulationAndRFControl()
    {
        var settings = new InventorySettings
        {
            Session = 2,
            TagPopulationEstimate = 128,
            ModeIndex = 1,
            AttachedData = new AttachedDataOptions
            {
                Enabled = true,
                MemoryBank = 2,
                WordCount = 6
            }
        };

        ROSpec roSpec = Llrp101InventoryCompiler.Compile(settings, []);
        Assert.NotNull(roSpec);
        Assert.Single(roSpec.SpecParameterItems);
        var aiSpec = Assert.IsType<AISpec>(roSpec.SpecParameterItems[0]);
        Assert.Single(aiSpec.InventoryParameterSpecItems);
        InventoryParameterSpec invSpec = aiSpec.InventoryParameterSpecItems[0];
        Assert.Single(invSpec.AntennaConfigurationItems);
        AntennaConfiguration antConfig = invSpec.AntennaConfigurationItems[0];
        Assert.Single(antConfig.AirProtocolInventoryCommandSettingsItems);
        var invCmd = Assert.IsType<C1G2InventoryCommand>(antConfig.AirProtocolInventoryCommandSettingsItems[0]);

        Assert.NotNull(invCmd.C1G2SingulationControl);
        Assert.Equal((byte)2, invCmd.C1G2SingulationControl.Session);
        Assert.Equal((ushort)128, invCmd.C1G2SingulationControl.TagPopulation);

        Assert.NotNull(invCmd.C1G2RFControl);
        Assert.Equal((ushort)1, invCmd.C1G2RFControl.ModeIndex);
    }

    [Fact]
    public void ReaderSettingsDefaults_SerializesProfileProvenanceAndSettings()
    {
        var defaults = new ReaderSettingsDefaults
        {
            ProfileId = "llrp.generic",
            Source = ReaderSettingsDefaultSource.Generic,
            Notes = ["Portable baseline."],
            Settings = new ReaderSettings { Inventory = new InventorySettings { AntennaIds = [1] } }
        };

        string json = ReaderSettingsSerializer.SerializeDefaultsToJson(defaults);
        ReaderSettingsDefaults restored = ReaderSettingsSerializer.DeserializeDefaultsFromJson(json);

        Assert.Contains("readerSettingsDefaults", json, StringComparison.Ordinal);
        Assert.Equal(defaults.ProfileId, restored.ProfileId);
        Assert.Equal(defaults.Source, restored.Source);
        Assert.Equal(defaults.Notes, restored.Notes);
        Assert.Equal([(ushort)1], restored.Settings.Inventory!.AntennaIds);
    }

    [Fact]
    public void ReaderSettingsSerializer_DoesNotTreatDefaultsDocumentAsApplicableSettings()
    {
        string json = ReaderSettingsSerializer.SerializeDefaultsToJson(ReaderSettingsDefaults.CreateGeneric());

        JsonException exception = Assert.Throws<JsonException>(() => ReaderSettingsSerializer.DeserializeFromJson(json));

        Assert.Contains("loaded into a draft", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SeuicDefaults_ExposeAndRoundTripCapabilityResolvedStandardInventorySettings()
    {
        var capabilities = new ReaderCapabilities(
            maxNumberOfAntennas: 4,
            canSetAntennaProperties: true,
            hasUtcClockCapability: false,
            generalDeviceParameters: [],
            rawResponse: new ENABLE_EVENTS_AND_REPORTS(1),
            additionalParameters: [],
            txPowers: [new TxPowerEntry(2, 2500), new TxPowerEntry(8, 3000)],
            rxSensitivities: [new RxSensitivityEntry(1, -7000)]);
        var context = new ReaderSettingsDefaultContext(
            new ReaderIdentity(SeuicReaderExtension.ManufacturerId, SeuicReaderExtension.Uf40ModelId, "1.0"),
            capabilities,
            LlrpProtocolVersion.Version101);

        ReaderSettingsDefaults defaults = Assert.IsType<ReaderSettingsDefaults>(SeuicReaderExtension.Instance.GetDefaultSettings(context));
        InventorySettings inventory = defaults.Settings.Inventory!;
        InventoryAntennaConfiguration profile = Assert.Single(inventory.AntennaConfigurations, configuration => configuration.AntennaId == 1);

        Assert.Equal("seuic.uf40.llrp-1.0.1", defaults.ProfileId);
        Assert.Equal([(ushort)1, 2, 3, 4], inventory.AntennaIds);
        Assert.Equal((ushort)8, profile.TransmitPowerIndex);
        Assert.Equal((ushort)1, profile.ReceiverSensitivityIndex);
        Assert.Empty(inventory.Extensions);

        string json = ReaderSettingsSerializer.SerializeDefaultsToJson(defaults);
        ReaderSettingsDefaults restored = ReaderSettingsSerializer.DeserializeDefaultsFromJson(json);
        InventoryAntennaConfiguration restoredProfile = Assert.Single(restored.Settings.Inventory!.AntennaConfigurations, configuration => configuration.AntennaId == 1);
        Assert.Equal(profile, restoredProfile);
    }

    [Fact]
    public void ReaderSettings_DefaultStartTrigger_CompilesToNull()
    {
        ROSpec roSpec = Llrp101InventoryCompiler.Compile(new InventorySettings(), []);

        Assert.Equal(ROSpecStartTriggerType.Null, roSpec.ROBoundarySpec.ROSpecStartTrigger.ROSpecStartTriggerType);
    }

    [Fact]
    public void ReaderSettings_ExplicitAntennaConfigurations_CompilePerAntennaAiSpec()
    {
        var settings = new InventorySettings
        {
            AntennaIds = [1, 2, 3, 4],
            AntennaConfigurations = [
                new() { AntennaId = 1, ReceiverSensitivityIndex = 1, TransmitPowerIndex = 8, HopTableId = 1, ChannelIndex = 1 },
                new() { AntennaId = 2, ReceiverSensitivityIndex = 1, TransmitPowerIndex = 8, HopTableId = 1, ChannelIndex = 1 },
                new() { AntennaId = 3, ReceiverSensitivityIndex = 1, TransmitPowerIndex = 8, HopTableId = 1, ChannelIndex = 1 },
                new() { AntennaId = 4, ReceiverSensitivityIndex = 1, TransmitPowerIndex = 8, HopTableId = 1, ChannelIndex = 1 }
            ]
        };

        ROSpec roSpec = Llrp101InventoryCompiler.Compile(settings, []);

        var aiSpec = Assert.IsType<AISpec>(Assert.Single(roSpec.SpecParameterItems));
        Assert.Equal([1, 2, 3, 4], aiSpec.AntennaIDs);
        InventoryParameterSpec inventory = Assert.Single(aiSpec.InventoryParameterSpecItems);
        Assert.Collection(
            inventory.AntennaConfigurationItems,
            antenna => AssertExplicitLegacyDefaults(antenna, 1),
            antenna => AssertExplicitLegacyDefaults(antenna, 2),
            antenna => AssertExplicitLegacyDefaults(antenna, 3),
            antenna => AssertExplicitLegacyDefaults(antenna, 4));
    }

    [Fact]
    public void ReaderSettings_TriggerCompiler_EmitsPeriodicStartAndDurationStop()
    {
        var settings = new InventorySettings
        {
            StartTrigger = new InventoryStartTrigger
            {
                Type = InventoryStartTriggerType.Periodic,
                OffsetMilliseconds = 100,
                PeriodMilliseconds = 5_000,
                StartAtUtc = DateTimeOffset.UnixEpoch.AddHours(1),
            },
            StopTrigger = new InventoryStopTrigger
            {
                Type = InventoryStopTriggerType.Duration,
                DurationMilliseconds = 30_000,
            },
        };

        ROSpec roSpec = Llrp101InventoryCompiler.Compile(settings, []);
        Assert.Equal(ROSpecStartTriggerType.Periodic, roSpec.ROBoundarySpec.ROSpecStartTrigger.ROSpecStartTriggerType);
        Assert.Equal((uint)100, roSpec.ROBoundarySpec.ROSpecStartTrigger.PeriodicTriggerValue!.Offset);
        Assert.Equal((uint)5_000, roSpec.ROBoundarySpec.ROSpecStartTrigger.PeriodicTriggerValue.Period);
        Assert.Equal((ulong)3_600_000_000, roSpec.ROBoundarySpec.ROSpecStartTrigger.PeriodicTriggerValue.UTCTimestamp!.Microseconds);
        Assert.Equal(ROSpecStopTriggerType.Duration, roSpec.ROBoundarySpec.ROSpecStopTrigger.ROSpecStopTriggerType);
        Assert.Equal((uint)30_000, roSpec.ROBoundarySpec.ROSpecStopTrigger.DurationTriggerValue);
    }

    private static void AssertExplicitLegacyDefaults(AntennaConfiguration antenna, ushort antennaId)
    {
        Assert.Equal(antennaId, antenna.AntennaID);
        Assert.Equal((ushort)1, antenna.RFReceiver!.ReceiverSensitivity);
        Assert.Equal((ushort)1, antenna.RFTransmitter!.HopTableID);
        Assert.Equal((ushort)1, antenna.RFTransmitter.ChannelIndex);
        Assert.Equal((ushort)8, antenna.RFTransmitter.TransmitPower);

        var command = Assert.IsType<C1G2InventoryCommand>(Assert.Single(antenna.AirProtocolInventoryCommandSettingsItems));
        Assert.Equal((ushort)0, command.C1G2RFControl!.ModeIndex);
        Assert.Equal((ushort)0, command.C1G2RFControl.Tari);
        Assert.Equal((byte)0, command.C1G2SingulationControl!.Session);
        Assert.Equal((ushort)32, command.C1G2SingulationControl.TagPopulation);
    }

    [Fact]
    public void ReaderSettings_StateAwareSingulation_RequiresCapabilityAndCompilesTarget()
    {
        var settings = new InventorySettings
        {
            StateAwareSingulation = new InventoryStateAwareSingulation
            {
                Target = InventoryTarget.StateB,
                SelectedFlag = InventorySelectedFlag.Clear,
            },
        };

        Assert.Throws<NotSupportedException>(() => Llrp101InventoryCompiler.Compile(settings, []));

        ROSpec roSpec = Llrp101InventoryCompiler.Compile(settings, [], supportsStateAwareSingulation: true);
        var aiSpec = Assert.IsType<AISpec>(Assert.Single(roSpec.SpecParameterItems));
        var inventory = Assert.IsType<InventoryParameterSpec>(Assert.Single(aiSpec.InventoryParameterSpecItems));
        var antenna = Assert.IsType<AntennaConfiguration>(Assert.Single(inventory.AntennaConfigurationItems));
        var command = Assert.IsType<C1G2InventoryCommand>(Assert.Single(antenna.AirProtocolInventoryCommandSettingsItems));
        Assert.True(command.TagInventoryStateAware);
        Assert.Equal(C1G2TagInventoryStateAwareI.State_B, command.C1G2SingulationControl!.C1G2TagInventoryStateAwareSingulationAction!.I);
        Assert.Equal(C1G2TagInventoryStateAwareS.Not_SL, command.C1G2SingulationControl.C1G2TagInventoryStateAwareSingulationAction.S);
    }

    [Fact]
    public void ReaderSettings_StateAwareSingulation_AllIsLlrp11Only()
    {
        var settings = new InventorySettings
        {
            StateAwareSingulation = new InventoryStateAwareSingulation
            {
                Target = InventoryTarget.StateB,
                SelectedFlag = InventorySelectedFlag.All,
            },
        };

        Assert.Throws<NotSupportedException>(() => Llrp101InventoryCompiler.Compile(
            settings, [], supportsStateAwareSingulation: true));

        V11Parameters.ROSpec roSpec = Llrp11InventoryCompiler.Compile(
            settings, [], supportsStateAwareSingulation: true);
        var aiSpec = Assert.IsType<V11Parameters.AISpec>(Assert.Single(roSpec.SpecParameterItems));
        var inventory = Assert.IsType<V11Parameters.InventoryParameterSpec>(Assert.Single(aiSpec.InventoryParameterSpecItems));
        var antenna = Assert.IsType<V11Parameters.AntennaConfiguration>(Assert.Single(inventory.AntennaConfigurationItems));
        var command = Assert.IsType<V11Parameters.C1G2InventoryCommand>(Assert.Single(antenna.AirProtocolInventoryCommandSettingsItems));

        Assert.Equal("State_B", command.C1G2SingulationControl!.C1G2TagInventoryStateAwareSingulationAction!.I.ToString());
        Assert.True(command.C1G2SingulationControl.C1G2TagInventoryStateAwareSingulationAction.SAll);
    }

    [Fact]
    public void ReaderSettings_BatchAfterStop_UsesEndOfRoSpecWithZeroReportInterval()
    {
        var settings = new InventorySettings
        {
            ReportEveryNTags = 0,
            Report = new InventoryReportSettings
            {
                Trigger = InventoryReportTrigger.UponNTagsOrEndOfRoSpec,
            },
        };

        ROSpec v101 = Llrp101InventoryCompiler.Compile(settings, []);
        V11Parameters.ROSpec v11 = Llrp11InventoryCompiler.Compile(settings, []);

        Assert.Equal((ushort)0, v101.ROReportSpec!.N);
        Assert.Equal(ROReportTriggerType.Upon_N_Tags_Or_End_Of_ROSpec, v101.ROReportSpec.ROReportTrigger);
        Assert.Equal((ushort)0, v11.ROReportSpec!.N);
        Assert.Equal("Upon_N_Tags_Or_End_Of_ROSpec", v11.ROReportSpec.ROReportTrigger.ToString());

        settings = settings with { Report = new InventoryReportSettings { Trigger = InventoryReportTrigger.UponNTagsOrEndOfAiSpec } };
        Assert.Throws<ArgumentOutOfRangeException>(() => Llrp101InventoryCompiler.Compile(settings, []));
        Assert.Throws<ArgumentOutOfRangeException>(() => Llrp11InventoryCompiler.Compile(settings, []));
    }

    [Fact]
    public async Task QuerySettings_ParsesPersistedLlrp11ManagedInventory()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var transport = new ScriptedLlrpTransport();
        await using LlrpReader reader = CreateReader(transport);
        await reader.ConnectAsync(timeout.Token);

        var expected = new InventorySettings
        {
            AntennaIds = [1],
            AntennaConfigurations =
            [
                new InventoryAntennaConfiguration
                {
                    AntennaId = 1,
                    ReceiverSensitivityIndex = 2,
                    TransmitPowerIndex = 3,
                    HopTableId = 4,
                    ChannelIndex = 5,
                },
            ],
            Filters =
            [
                new InventorySelectFilter
                {
                    MemoryBank = 1,
                    BitPointer = 32,
                    Mask = new byte[] { 0xE2, 0x80 },
                    BitLength = 9,
                    StateAwareAction = new InventoryStateAwareFilterAction
                    {
                        Target = InventoryFilterTarget.Session2,
                        Action = InventoryFilterAction.NoOperationAndAssertSelectedOrStateA,
                    },
                },
            ],
            StateAwareSingulation = new InventoryStateAwareSingulation
            {
                Target = InventoryTarget.StateB,
                SelectedFlag = InventorySelectedFlag.All,
            },
            StartTrigger = new InventoryStartTrigger
            {
                Type = InventoryStartTriggerType.Gpi,
                GpiPortNumber = 2,
                GpiState = true,
                TimeoutMilliseconds = 300,
            },
            StopTrigger = new InventoryStopTrigger
            {
                Type = InventoryStopTriggerType.Duration,
                DurationMilliseconds = 400,
            },
            ReportEveryNTags = 6,
            Report = new InventoryReportSettings
            {
                Trigger = InventoryReportTrigger.UponNTagsOrEndOfRoSpec,
                IncludeCrc = true,
                IncludePcBits = true,
            },
        };
        V11Parameters.ROSpec roSpec = Llrp11InventoryCompiler.Compile(
            expected, [], supportsStateAwareSingulation: true) with
        {
            CurrentState = LlrpNet.Protocol.Enumerations.V1_1.ROSpecState.Active,
        };
        MethodInfo parse = typeof(LlrpReader).GetMethod(
            "ParseManagedInventory",
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: [typeof(ILlrpParameter), typeof(IReadOnlyList<ILlrpParameter>)],
            modifiers: null)!;

        var snapshot = Assert.IsType<InventorySnapshot>(parse.Invoke(reader, [roSpec, Array.Empty<ILlrpParameter>()]));

        Assert.Equal(InventoryRuntimeState.Running, snapshot.State);
        Assert.Equal(InventorySelectedFlag.All, snapshot.Settings.StateAwareSingulation!.SelectedFlag);
        Assert.Equal(InventoryTarget.StateB, snapshot.Settings.StateAwareSingulation.Target);
        Assert.Equal(expected.StartTrigger, snapshot.Settings.StartTrigger);
        Assert.Equal(expected.StopTrigger, snapshot.Settings.StopTrigger);
        Assert.Equal(expected.ReportEveryNTags, snapshot.Settings.ReportEveryNTags);
        Assert.Equal(expected.Report, snapshot.Settings.Report);
        Assert.Equal(expected.AntennaConfigurations, snapshot.Settings.AntennaConfigurations);
        InventorySelectFilter filter = Assert.Single(snapshot.Settings.Filters);
        Assert.Equal((ushort)1, filter.MemoryBank);
        Assert.Equal((ushort)32, filter.BitPointer);
        Assert.Equal((ushort)9, filter.BitLength);
        Assert.Equal(new byte[] { 0xE2, 0x80 }, filter.Mask.ToArray());
        Assert.Equal(InventoryFilterTarget.Session2, filter.StateAwareAction!.Target);
        Assert.Equal(InventoryFilterAction.NoOperationAndAssertSelectedOrStateA, filter.StateAwareAction.Action);
    }

    [Fact]
    public void ReaderSettings_FilterCompiler_PreservesNonByteAlignedAndStateAwareFilters()
    {
        var settings = new InventorySettings
        {
            Filters =
            [
                new InventorySelectFilter
                {
                    MemoryBank = 1,
                    BitPointer = 32,
                    Mask = new byte[] { 0b_1010_0000 },
                    BitLength = 4,
                    StateAwareAction = new InventoryStateAwareFilterAction
                    {
                        Target = InventoryFilterTarget.Session1,
                        Action = InventoryFilterAction.NoOperationAndAssertSelectedOrStateA,
                    },
                }
            ],
            StateAwareSingulation = new InventoryStateAwareSingulation
            {
                Target = InventoryTarget.StateA,
                SelectedFlag = InventorySelectedFlag.Set,
            },
        };

        ROSpec roSpec = Llrp101InventoryCompiler.Compile(settings, [], supportsStateAwareSingulation: true);
        var aiSpec = Assert.IsType<AISpec>(Assert.Single(roSpec.SpecParameterItems));
        var inventory = Assert.IsType<InventoryParameterSpec>(Assert.Single(aiSpec.InventoryParameterSpecItems));
        var antenna = Assert.IsType<AntennaConfiguration>(Assert.Single(inventory.AntennaConfigurationItems));
        var command = Assert.IsType<C1G2InventoryCommand>(Assert.Single(antenna.AirProtocolInventoryCommandSettingsItems));
        Assert.True(command.TagInventoryStateAware);
        C1G2Filter filter = Assert.Single(command.C1G2FilterItems);

        Assert.Equal(4, filter.C1G2TagInventoryMask.TagMask.Count);
        Assert.Equal(C1G2StateAwareTarget.Inventoried_State_For_Session_S1, filter.C1G2TagInventoryStateAwareFilterAction!.Target);
        Assert.Equal(C1G2StateAwareAction.Noop_AssertSLOrA, filter.C1G2TagInventoryStateAwareFilterAction.Action);
        Assert.Null(filter.C1G2TagInventoryStateUnawareFilterAction);
    }

    [Fact]
    public void ReaderSettings_StateAwareFilters_RequireSingulationAndCapability()
    {
        var settings = new InventorySettings
        {
            Filters =
            [
                new InventorySelectFilter
                {
                    MemoryBank = 1,
                    BitPointer = 32,
                    Mask = new byte[] { 0xE2 },
                    StateAwareAction = new InventoryStateAwareFilterAction(),
                },
            ],
        };

        Assert.Throws<ArgumentException>(() => Llrp101InventoryCompiler.Compile(settings, []));

        settings = settings with
        {
            StateAwareSingulation = new InventoryStateAwareSingulation(),
        };
        Assert.Throws<NotSupportedException>(() => Llrp101InventoryCompiler.Compile(settings, []));
        Assert.NotNull(Llrp101InventoryCompiler.Compile(settings, [], supportsStateAwareSingulation: true));
    }

    private static LlrpReader CreateReader(ScriptedLlrpTransport transport)
    {
        return LlrpReader.CreateBuilder("scripted.local")
            .WithTransportFactory(_ => transport)
            .Build();
    }
}
