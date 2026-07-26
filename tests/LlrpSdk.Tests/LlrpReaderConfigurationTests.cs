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

        ReaderConfiguration config = await reader.QuerySettingsAsync(timeout.Token);

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

        await reader.ApplySettingsAsync(config, timeout.Token);
        Assert.True(setConfigSent);
    }

    private static LlrpReader CreateReader(ScriptedLlrpTransport transport)
    {
        return LlrpReader.CreateBuilder("scripted.local")
            .WithTransportFactory(_ => transport)
            .Build();
    }
}
