using System;
using System.Collections.Generic;
using System.Linq;
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

namespace LlrpSdk.Tests;

public sealed class LlrpReaderConfigurationTests
{
    [Fact]
    public void ReaderIdentity_TrimsProtocolStringPadding()
    {
        var identity = new ReaderIdentity(1, 2, "1.0.0\0\0");

        Assert.Equal("1.0.0", identity.FirmwareVersion);
    }

    [Fact]
    public async Task QueryConfiguration_SendsGetReaderConfigAndReturnsConfiguration()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var transport = new ScriptedLlrpTransport();
        
        await using LlrpReader reader = CreateReader(transport);
        await reader.ConnectAsync(timeout.Token);

        // Enqueue response for GET_READER_CONFIG
        transport.OnSendAsync = (frame, ct) =>
        {
            LlrpMessageHeader header = LlrpMessageHeader.Decode(frame.Span);
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

                var registry = new LlrpCodecRegistry();
                Llrp101StandardModule.Register(registry);
                byte[] encoded = registry.EncodeMessage(LlrpProtocolVersion.Version101, response);
                transport.EnqueueFrame(encoded);
            }
            return ValueTask.CompletedTask;
        };

        ReaderConfiguration config = await reader.QueryConfigurationAsync(timeout.Token);

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
    public async Task ApplyConfiguration_SendsSetReaderConfig()
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

        await reader.ApplyConfigurationAsync(config, timeout.Token);
        Assert.True(setConfigSent);
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
    public void ReaderSettings_DefaultStartTrigger_CompilesToNull()
    {
        ROSpec roSpec = Llrp101InventoryCompiler.Compile(new InventorySettings(), []);

        Assert.Equal(ROSpecStartTriggerType.Null, roSpec.ROBoundarySpec.ROSpecStartTrigger.ROSpecStartTriggerType);
    }

    [Fact]
    public void ReaderSettings_SeuicCompatibilityDefaults_CompileExplicitPerAntennaAiSpec()
    {
        var defaults = new InventoryCompilationDefaults(
            AntennaIds: [1, 2, 3, 4],
            ReceiverSensitivityIndex: 1,
            TransmitPowerIndex: 8,
            HopTableId: 1,
            ChannelIndex: 1);

        ROSpec roSpec = Llrp101InventoryCompiler.CompileWithDefaults(
            new InventorySettings(),
            [],
            supportsStateAwareSingulation: false,
            compilationDefaults: defaults);

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

    private static LlrpReader CreateReader(ScriptedLlrpTransport transport)
    {
        return LlrpReader.CreateBuilder("scripted.local")
            .WithTransportFactory(_ => transport)
            .Build();
    }
}
