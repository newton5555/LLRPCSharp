using LlrpNet.Core.Protocol;
using LlrpNet.Protocol;
using LlrpNet.Protocol.Choices.V1_0_1;
using LlrpNet.Protocol.Enumerations.V1_0_1;
using LlrpNet.Protocol.Messages;
using LlrpNet.Protocol.Parameters;
using LlrpNet.Protocol.Parameters.V1_0_1;
using LlrpNet.Protocol.Registry;
using LlrpSdk.Tests.Support;
using V101 = LlrpNet.Protocol.Messages.V1_0_1;

namespace LlrpSdk.Tests;

public sealed class LlrpReaderTagAccessAndEventTests
{
    private static readonly LlrpCodecRegistry Registry;

    static LlrpReaderTagAccessAndEventTests()
    {
        Registry = new LlrpCodecRegistry();
        new Llrp101ProtocolAdapter().RegisterStandardCodecs(Registry);
    }

    [Fact]
    public async Task GetTagReportsAsync_PollsTagReportsFromReader()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var transport = new ScriptedLlrpTransport();

        byte[] epc = [0xE2, 0x80, 0x11, 0x91, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01];
        var airProtocolData = new List<IAirProtocolTagData>();
        var customItems = new List<ILlrpParameter>();
        var opSpecResults = new List<ILlrpParameter>();
        var tagReportData = new TagReportData(
            new EPC_96(epc),
            ROSpecID: null,
            SpecIndex: null,
            InventoryParameterSpecID: null,
            AntennaID: null,
            PeakRSSI: null,
            ChannelIndex: null,
            FirstSeenTimestampUTC: null,
            FirstSeenTimestampUptime: null,
            LastSeenTimestampUTC: null,
            LastSeenTimestampUptime: null,
            TagSeenCount: null,
            AirProtocolTagDataItems: airProtocolData,
            AccessSpecID: null,
            AccessCommandOpSpecResultItems: opSpecResults,
            CustomItems: customItems);

        transport.OnSendAsync = (copy, _) =>
        {
            LlrpMessageHeader header = LlrpMessageHeader.Decode(copy.Span);
            if (header.MessageType == V101.GET_REPORT.MessageType)
            {
                var reportMsg = new V101.RO_ACCESS_REPORT(header.MessageId, TagReportDataItems: [tagReportData], RFSurveyReportDataItems: [], CustomItems: []);
                transport.EnqueueFrame(Registry.EncodeMessage(LlrpProtocolVersion.Version101, reportMsg));
            }
            return ValueTask.CompletedTask;
        };

        await using var reader = LlrpReaderLifecycleTests.CreateReader(transport);
        await reader.ConnectAsync(timeout.Token);

        IReadOnlyList<TagReport> reports = await reader.GetTagReportsAsync(timeout.Token);

        Assert.Single(reports);
        Assert.Equal("E28011910000000000000001", Convert.ToHexString(reports[0].ElectronicProductCode.Span));
    }

    [Fact]
    public async Task SetGpoAsync_SendsConfigurationToReader()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var transport = new ScriptedLlrpTransport();

        transport.OnSendAsync = (copy, _) =>
        {
            LlrpMessageHeader header = LlrpMessageHeader.Decode(copy.Span);
            if (header.MessageType == V101.SET_READER_CONFIG.MessageType)
            {
                var configResponse = new V101.SET_READER_CONFIG_RESPONSE(header.MessageId, new LLRPStatus(StatusCode.M_Success, "Success", null, null));
                transport.EnqueueFrame(Registry.EncodeMessage(LlrpProtocolVersion.Version101, configResponse));
            }
            return ValueTask.CompletedTask;
        };

        await using var reader = LlrpReaderLifecycleTests.CreateReader(transport);
        await reader.ConnectAsync(timeout.Token);

        await reader.SetGpoAsync(portNumber: 1, state: true, timeout.Token);

        byte[] sentFrame = transport.SentFrames.Last();
        ILlrpMessage sentMsg = Registry.DecodeMessage(sentFrame);
        Assert.IsType<V101.SET_READER_CONFIG>(sentMsg);
    }

    [Fact]
    public async Task UnsolicitedEvents_TriggersGpiAndKeepaliveAndBufferOverflow()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var transport = new ScriptedLlrpTransport();

        await using var reader = LlrpReaderLifecycleTests.CreateReader(transport);

        var gpiEventTcs = new TaskCompletionSource<GpiChangedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        var keepaliveTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var overflowTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        reader.GpiChanged += (_, args) => gpiEventTcs.TrySetResult(args);
        reader.KeepaliveReceived += (_, _) => keepaliveTcs.TrySetResult(true);
        reader.ReportBufferOverflow += (_, _) => overflowTcs.TrySetResult(true);

        await reader.ConnectAsync(timeout.Token);

        // Enqueue KEEPALIVE frame
        var keepaliveMsg = new V101.KEEPALIVE(100);
        transport.EnqueueFrame(Registry.EncodeMessage(LlrpProtocolVersion.Version101, keepaliveMsg));

        bool keepaliveReceived = await keepaliveTcs.Task.WaitAsync(timeout.Token);
        Assert.True(keepaliveReceived);

        // Enqueue READER_EVENT_NOTIFICATION frame with GPIEvent and ReportBufferOverflow
        var eventData = new ReaderEventNotificationData(
            new Uptime(1000),
            null,
            new GPIEvent(2, true),
            null,
            null,
            new ReportBufferOverflowErrorEvent(),
            null, null, null, null, null, null, []);
        var notificationMsg = new V101.READER_EVENT_NOTIFICATION(101, eventData);
        transport.EnqueueFrame(Registry.EncodeMessage(LlrpProtocolVersion.Version101, notificationMsg));

        GpiChangedEventArgs gpiArgs = await gpiEventTcs.Task.WaitAsync(timeout.Token);
        Assert.Equal(2, gpiArgs.PortNumber);
        Assert.True(gpiArgs.State);

        bool overflowReceived = await overflowTcs.Task.WaitAsync(timeout.Token);
        Assert.True(overflowReceived);
    }
}
