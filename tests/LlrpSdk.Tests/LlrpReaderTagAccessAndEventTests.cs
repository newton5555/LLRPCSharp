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
            if (header.MessageType == V101.GET_READER_CONFIG.MessageType)
            {
                var response = new V101.GET_READER_CONFIG_RESPONSE(
                    header.MessageId,
                    new LLRPStatus(StatusCode.M_Success, "Success", null, null),
                    Identification: null,
                    AntennaPropertiesItems: [],
                    AntennaConfigurationItems: [],
                    ReaderEventNotificationSpec: null,
                    ROReportSpec: null,
                    AccessReportSpec: null,
                    LLRPConfigurationStateValue: null,
                    KeepaliveSpec: null,
                    GPIPortCurrentStateItems: [],
                    GPOWriteDataItems: [],
                    EventsAndReports: null,
                    CustomItems: []);
                transport.EnqueueFrame(Registry.EncodeMessage(LlrpProtocolVersion.Version101, response));
            }
            else if (header.MessageType == V101.GET_ROSPECS.MessageType)
            {
                transport.EnqueueFrame(Registry.EncodeMessage(LlrpProtocolVersion.Version101,
                    new V101.GET_ROSPECS_RESPONSE(header.MessageId, new LLRPStatus(StatusCode.M_Success, "Success", null, null), [])));
            }
            else if (header.MessageType == V101.GET_ACCESSSPECS.MessageType)
            {
                transport.EnqueueFrame(Registry.EncodeMessage(LlrpProtocolVersion.Version101,
                    new V101.GET_ACCESSSPECS_RESPONSE(header.MessageId, new LLRPStatus(StatusCode.M_Success, "Success", null, null), [])));
            }
            else if (header.MessageType == V101.SET_READER_CONFIG.MessageType)
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
        var antennaEventTcs = new TaskCompletionSource<AntennaChangedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        var keepaliveTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var warningTcs = new TaskCompletionSource<ReportBufferWarningEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        var overflowTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        reader.GpiChanged += (_, args) => gpiEventTcs.TrySetResult(args);
        reader.AntennaChanged += (_, args) => antennaEventTcs.TrySetResult(args);
        reader.KeepaliveReceived += (_, _) => keepaliveTcs.TrySetResult(true);
        reader.ReportBufferWarning += (_, args) => warningTcs.TrySetResult(args);
        reader.ReportBufferOverflow += (_, _) => overflowTcs.TrySetResult(true);

        await reader.ConnectAsync(timeout.Token);

        // Enqueue KEEPALIVE frame
        var keepaliveMsg = new V101.KEEPALIVE(100);
        transport.EnqueueFrame(Registry.EncodeMessage(LlrpProtocolVersion.Version101, keepaliveMsg));

        bool keepaliveReceived = await keepaliveTcs.Task.WaitAsync(timeout.Token);
        Assert.True(keepaliveReceived);

        // Enqueue READER_EVENT_NOTIFICATION frame with GPI, antenna, report-buffer warning and overflow.
        var eventData = new ReaderEventNotificationData(
            new Uptime(1000),
            null,
            new GPIEvent(2, true),
            null,
            new ReportBufferLevelWarningEvent(80),
            new ReportBufferOverflowErrorEvent(),
            null, null, null, new AntennaEvent(AntennaEventType.Antenna_Connected, 3), null, null, []);
        var notificationMsg = new V101.READER_EVENT_NOTIFICATION(101, eventData);
        transport.EnqueueFrame(Registry.EncodeMessage(LlrpProtocolVersion.Version101, notificationMsg));

        GpiChangedEventArgs gpiArgs = await gpiEventTcs.Task.WaitAsync(timeout.Token);
        Assert.Equal(2, gpiArgs.PortNumber);
        Assert.True(gpiArgs.State);

        AntennaChangedEventArgs antennaArgs = await antennaEventTcs.Task.WaitAsync(timeout.Token);
        Assert.Equal(3, antennaArgs.AntennaId);
        Assert.True(antennaArgs.IsConnected);

        ReportBufferWarningEventArgs warningArgs = await warningTcs.Task.WaitAsync(timeout.Token);
        Assert.Equal((byte)80, warningArgs.PercentageFull);

        bool overflowReceived = await overflowTcs.Task.WaitAsync(timeout.Token);
        Assert.True(overflowReceived);
    }

    [Fact]
    public async Task KeepaliveTimeout_IsOptInAndDoesNotDisconnectTheReader()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var transport = new ScriptedLlrpTransport();
        await using var reader = new LlrpReaderBuilder("scripted.local")
            .WithTransportFactory(_ => transport)
            .WithKeepaliveTimeout(TimeSpan.FromMilliseconds(75))
            .Build();
        var timedOut = new TaskCompletionSource<KeepaliveTimeoutEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        reader.KeepaliveTimedOut += (_, args) => timedOut.TrySetResult(args);

        await reader.ConnectAsync(timeout.Token);
        KeepaliveTimeoutEventArgs args = await timedOut.Task.WaitAsync(timeout.Token);

        Assert.Equal(TimeSpan.FromMilliseconds(75), args.Timeout);
        Assert.True(reader.IsConnected);
    }

    [Fact]
    public async Task ManagedRoSpecEndEvent_StopsCompatibilityInventoryWithoutSession()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var transport = new ScriptedLlrpTransport();
        ConfigureManagementSuccessResponses(transport);
        await using var reader = LlrpReaderLifecycleTests.CreateReader(transport);
        await reader.ConnectAsync(timeout.Token);
        await reader.StartAsync(new InventorySettings(), timeout.Token);

        var eventData = new ReaderEventNotificationData(
            new Uptime(1000), null, null,
            new ROSpecEvent(ROSpecEventType.End_Of_ROSpec, 14150, 0),
            null, null, null, null, null, null, null, null, []);
        transport.EnqueueFrame(Registry.EncodeMessage(
            LlrpProtocolVersion.Version101,
            new V101.READER_EVENT_NOTIFICATION(101, eventData)));

        await Task.Delay(50, timeout.Token);
        Assert.Equal(ReaderOperationState.Idle, reader.OperationState);
        Assert.Equal(ReaderResourceMode.HighLevelConfigured, reader.ResourceMode);
        Assert.NotNull(reader.CurrentInventorySettings);
    }

    [Fact]
    public async Task StartAsync_WithAttachedData_ManagesStandardReadAccessSpecLifecycle()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var transport = new ScriptedLlrpTransport();
        ConfigureManagementSuccessResponses(transport);

        await using var reader = LlrpReaderLifecycleTests.CreateReader(transport);
        await reader.ConnectAsync(timeout.Token);
        await reader.StartAsync(new InventorySettings
        {
            AttachedData = new AttachedDataOptions
            {
                Enabled = true,
                MemoryBank = 2,
                WordPointer = 0,
                WordCount = 6,
                AccessPassword = "00000000",
            },
        }, timeout.Token);
        await reader.StopAsync(timeout.Token);

        ILlrpMessage[] requests = transport.SentFrames
            .Select(frame => Registry.DecodeMessage(frame))
            .ToArray();
        var addAccessSpec = Assert.IsType<V101.ADD_ACCESSSPEC>(requests.Single(message => message is V101.ADD_ACCESSSPEC));
        var accessSpec = Assert.IsType<AccessSpec>(addAccessSpec.AccessSpec);
        Assert.Equal((uint)14150, accessSpec.ROSpecID);
        var command = Assert.IsType<AccessCommand>(accessSpec.AccessCommand);
        var read = Assert.IsType<C1G2Read>(Assert.Single(command.AccessCommandOpSpecItems));
        Assert.Equal((byte)2, read.MB);
        Assert.Equal((ushort)6, read.WordCount);

        uint accessSpecId = accessSpec.AccessSpecID;
        Assert.Contains(requests, message => message is V101.ENABLE_ACCESSSPEC enable && enable.AccessSpecID == accessSpecId);
        Assert.Contains(requests, message => message is V101.DISABLE_ACCESSSPEC disable && disable.AccessSpecID == accessSpecId);
        Assert.DoesNotContain(requests, message => message is V101.DELETE_ACCESSSPEC delete && delete.AccessSpecID == accessSpecId);
        Assert.Contains(requests, message => message is V101.START_ROSPEC start && start.ROSpecID == 14150);

        await reader.ClearManagedSettingsAsync(timeout.Token);
        requests = transport.SentFrames.Select(frame => Registry.DecodeMessage(frame)).ToArray();
        Assert.Contains(requests, message => message is V101.DELETE_ACCESSSPEC delete && delete.AccessSpecID == accessSpecId);
        Assert.Contains(requests, message => message is V101.DELETE_ROSPEC delete && delete.ROSpecID == 14150);
    }

    [Fact]
    public void WriteTagRequest_UsesBlockWriteOnlyWhenCapabilityAllowsIt()
    {
        var request = new WriteTagRequest
        {
            Selection = new TagSelection
            {
                BitLength = 1,
                Mask = new byte[] { 0 },
                Data = new byte[] { 0 },
            },
            WriteData = [0x1111, 0x2222],
        };

        var blockAccessSpec = Llrp101TagAccessCompiler.Compile(1, 14150, request, useBlockWrite: true);
        var standardAccessSpec = Llrp101TagAccessCompiler.Compile(2, 14150, request, useBlockWrite: false);

        Assert.IsType<C1G2BlockWrite>(Assert.Single(blockAccessSpec.AccessCommand.AccessCommandOpSpecItems));
        Assert.IsType<C1G2Write>(Assert.Single(standardAccessSpec.AccessCommand.AccessCommandOpSpecItems));
    }

    [Fact]
    public void TagReportTranslator_ProjectsEveryStandardC1G2OperationResult()
    {
        var report = new V101.RO_ACCESS_REPORT(
            1,
            TagReportDataItems:
            [
                CreateTagReportData(null,
                    new C1G2BlockWriteOpSpecResult(C1G2BlockWriteResultType.Success, 1, 2),
                    new C1G2LockOpSpecResult(C1G2LockResultType.Success, 2),
                    new C1G2KillOpSpecResult(C1G2KillResultType.No_Response_From_Tag, 3),
                    new C1G2BlockEraseOpSpecResult(C1G2BlockEraseResultType.Success, 4)),
            ],
            RFSurveyReportDataItems: [],
            CustomItems: []);

        TagReport tag = Assert.Single(Llrp101TagReportTranslator.Translate(report)).Report;
        IReadOnlyList<TagAccessOperationResult> results = tag.AccessOperationResults!;
        Assert.Collection(
            results,
            result => { Assert.True(result.Success); Assert.Equal((ushort)2, result.WordsWritten); },
            result => { Assert.True(result.Success); Assert.Null(result.WordsWritten); },
            result => { Assert.False(result.Success); Assert.Equal("No_Response_From_Tag", result.Error); },
            result => Assert.True(result.Success));
    }

    [Fact]
    public void TagAccessSequence_CompilesOperationsIntoOneAccessSpec()
    {
        var selection = new TagSelection
        {
            BitLength = 1,
            Mask = new byte[] { 0 },
            Data = new byte[] { 0 },
        };
        AccessSpec accessSpec = Llrp101TagAccessCompiler.CompileSequence(
            9,
            14150,
            [
                new ReadTagRequest { Selection = selection, MemoryBank = TagMemoryBank.Tid, WordCount = 2 },
                new WriteTagRequest { Selection = selection, MemoryBank = TagMemoryBank.User, WriteData = [0x1234] },
            ]);

        Assert.Collection(
            accessSpec.AccessCommand.AccessCommandOpSpecItems,
            item => Assert.Equal((ushort)1, Assert.IsType<C1G2Read>(item).OpSpecID),
            item => Assert.Equal((ushort)2, Assert.IsType<C1G2Write>(item).OpSpecID));
    }

    [Fact]
    public async Task ExecuteTagAccessSequenceAsync_ReusesStoppedManagedInventoryAndReturnsAllOperationResults()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var transport = new ScriptedLlrpTransport();
        uint? sequenceAccessSpecId = null;
        transport.OnSendAsync = (frame, _) =>
        {
            LlrpMessageHeader header = LlrpMessageHeader.Decode(frame.Span);
            var status = new LLRPStatus(StatusCode.M_Success, string.Empty, null, null);
            ILlrpMessage? response = header.MessageType switch
            {
                V101.ADD_ROSPEC.MessageType => new V101.ADD_ROSPEC_RESPONSE(header.MessageId, status),
                V101.ENABLE_ROSPEC.MessageType => new V101.ENABLE_ROSPEC_RESPONSE(header.MessageId, status),
                V101.START_ROSPEC.MessageType => new V101.START_ROSPEC_RESPONSE(header.MessageId, status),
                V101.DISABLE_ROSPEC.MessageType => new V101.DISABLE_ROSPEC_RESPONSE(header.MessageId, status),
                V101.STOP_ROSPEC.MessageType => new V101.STOP_ROSPEC_RESPONSE(header.MessageId, status),
                V101.DELETE_ROSPEC.MessageType => new V101.DELETE_ROSPEC_RESPONSE(header.MessageId, status),
                V101.ADD_ACCESSSPEC.MessageType => new V101.ADD_ACCESSSPEC_RESPONSE(header.MessageId, status),
                V101.ENABLE_ACCESSSPEC.MessageType => new V101.ENABLE_ACCESSSPEC_RESPONSE(header.MessageId, status),
                V101.DISABLE_ACCESSSPEC.MessageType => new V101.DISABLE_ACCESSSPEC_RESPONSE(header.MessageId, status),
                V101.DELETE_ACCESSSPEC.MessageType => new V101.DELETE_ACCESSSPEC_RESPONSE(header.MessageId, status),
                _ => null,
            };
            if (header.MessageType == V101.ADD_ACCESSSPEC.MessageType)
            {
                var add = Assert.IsType<V101.ADD_ACCESSSPEC>(Registry.DecodeMessage(frame.Span));
                sequenceAccessSpecId = add.AccessSpec.AccessSpecID;
            }
            if (response is not null)
            {
                transport.EnqueueFrame(Registry.EncodeMessage(LlrpProtocolVersion.Version101, response));
            }
            if (header.MessageType == V101.ENABLE_ACCESSSPEC.MessageType && sequenceAccessSpecId is uint accessSpecId)
            {
                var report = new V101.RO_ACCESS_REPORT(
                    99,
                    [CreateTagReportData(accessSpecId,
                        new C1G2ReadOpSpecResult(C1G2ReadResultType.Success, 1, [0x1234]),
                        new C1G2WriteOpSpecResult(C1G2WriteResultType.Success, 2, 1))],
                    [],
                    []);
                transport.EnqueueFrame(Registry.EncodeMessage(LlrpProtocolVersion.Version101, report));
            }
            return ValueTask.CompletedTask;
        };

        await using var reader = LlrpReaderLifecycleTests.CreateReader(transport);
        await reader.ConnectAsync(timeout.Token);
        var inventory = new InventorySettings { Session = 2, TagPopulationEstimate = 64 };
        await reader.StartAsync(inventory, timeout.Token);
        await reader.StopAsync(timeout.Token);

        TagAccessSequenceResult result = await reader.ExecuteTagAccessSequenceAsync(
            new TagAccessSequenceRequest
            {
                Operations =
                [
                    new ReadTagRequest { Selection = MatchAllSelection(), MemoryBank = TagMemoryBank.Tid, WordCount = 1 },
                    new WriteTagRequest { Selection = MatchAllSelection(), MemoryBank = TagMemoryBank.User, WriteData = [0x1234] },
                ],
            },
            cancellationToken: timeout.Token);

        Assert.Collection(
            result.Operations,
            operation => { Assert.Equal((ushort)1, operation.OpSpecID); Assert.Equal([0x1234], operation.ReadData); },
            operation => { Assert.Equal((ushort)2, operation.OpSpecID); Assert.Equal((ushort)1, operation.WordsWritten); });
        Assert.Equal(ReaderResourceMode.HighLevelConfigured, reader.ResourceMode);
        Assert.Same(inventory, reader.CurrentInventorySettings);
        ILlrpMessage[] requests = transport.SentFrames.Select(frame => Registry.DecodeMessage(frame)).ToArray();
        Assert.Single(requests.OfType<V101.ADD_ROSPEC>());
    }

    private static void ConfigureManagementSuccessResponses(ScriptedLlrpTransport transport)
    {
        transport.OnSendAsync = (frame, _) =>
        {
            LlrpMessageHeader header = LlrpMessageHeader.Decode(frame.Span);
            var status = new LLRPStatus(StatusCode.M_Success, string.Empty, null, null);
            ILlrpMessage? response = header.MessageType switch
            {
                V101.ADD_ROSPEC.MessageType => new V101.ADD_ROSPEC_RESPONSE(header.MessageId, status),
                V101.ENABLE_ROSPEC.MessageType => new V101.ENABLE_ROSPEC_RESPONSE(header.MessageId, status),
                V101.START_ROSPEC.MessageType => new V101.START_ROSPEC_RESPONSE(header.MessageId, status),
                V101.DISABLE_ROSPEC.MessageType => new V101.DISABLE_ROSPEC_RESPONSE(header.MessageId, status),
                V101.STOP_ROSPEC.MessageType => new V101.STOP_ROSPEC_RESPONSE(header.MessageId, status),
                V101.DELETE_ROSPEC.MessageType => new V101.DELETE_ROSPEC_RESPONSE(header.MessageId, status),
                V101.ADD_ACCESSSPEC.MessageType => new V101.ADD_ACCESSSPEC_RESPONSE(header.MessageId, status),
                V101.ENABLE_ACCESSSPEC.MessageType => new V101.ENABLE_ACCESSSPEC_RESPONSE(header.MessageId, status),
                V101.DISABLE_ACCESSSPEC.MessageType => new V101.DISABLE_ACCESSSPEC_RESPONSE(header.MessageId, status),
                V101.DELETE_ACCESSSPEC.MessageType => new V101.DELETE_ACCESSSPEC_RESPONSE(header.MessageId, status),
                _ => null,
            };
            if (response is not null)
            {
                transport.EnqueueFrame(Registry.EncodeMessage(LlrpProtocolVersion.Version101, response));
            }
            return ValueTask.CompletedTask;
        };
    }

    private static TagSelection MatchAllSelection() => new()
    {
        BitLength = 1,
        Mask = new byte[] { 0 },
        Data = new byte[] { 0 },
    };

    private static TagReportData CreateTagReportData(uint? accessSpecId, params ILlrpParameter[] results) => new(
        new EPC_96(new byte[] { 0xE2, 0x80, 0x11, 0x91, 0, 0, 0, 0, 0, 0, 0, 1 }),
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
        AirProtocolTagDataItems: [],
        AccessSpecID: accessSpecId is uint id ? new AccessSpecID(id) : null,
        AccessCommandOpSpecResultItems: results,
        CustomItems: []);

[Fact]
public void TagSelection_BitLengthZero_UsesEveryPackedBit()
{
    var selection = new TagSelection
    {
        BitLength = 0,
        Mask = new byte[] { 0xE2, 0x80 },
        Data = new byte[] { 0xE2, 0x80 },
    };
    AccessSpec accessSpec = Llrp101TagAccessCompiler.CompileSequence(
        9,
        14150,
        [new ReadTagRequest { Selection = selection, MemoryBank = TagMemoryBank.Tid, WordCount = 1 }]);

    C1G2TargetTag target = Assert.Single(
        Assert.IsType<C1G2TagSpec>(accessSpec.AccessCommand.AirProtocolTagSpec).C1G2TargetTagItems);
    Assert.Equal(16, target.TagMask.Count);
    Assert.Equal(16, target.TagData.Count);
}

[Fact]
public async Task TagReportsDropped_RaisesWhenConnectionReportBufferIsFull()
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    var transport = new ScriptedLlrpTransport();
    var options = new LlrpReaderOptionsBuilder("scripted.local")
        .WithTransportFactory(_ => transport)
        .WithIncomingMessageCapacity(2)
        .Build();
    await using var reader = new LlrpReader(options);
    var dropped = new TaskCompletionSource<TagReportOverflowEventArgs>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    reader.TagReportsDropped += (_, args) => dropped.TrySetResult(args);

    await reader.ConnectAsync(timeout.Token);

    for (int index = 0; index < 3; index++)
    {
        var report = new V101.RO_ACCESS_REPORT(
            (uint)(100 + index),
            TagReportDataItems:
            [
                CreateTagReportData(null),
            ],
            RFSurveyReportDataItems: [],
            CustomItems: []);
        transport.EnqueueFrame(Registry.EncodeMessage(LlrpProtocolVersion.Version101, report));
    }

    TagReportOverflowEventArgs args = await dropped.Task.WaitAsync(timeout.Token);
    Assert.Equal(2, args.BufferCapacity);
    Assert.Equal(1, args.TotalDropped);
}
}
