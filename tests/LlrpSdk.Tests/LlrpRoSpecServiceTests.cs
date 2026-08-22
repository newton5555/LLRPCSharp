using System.Collections.Concurrent;
using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Enumerations.V1_0_1;
using LlrpNet.Protocol.Messages;
using LlrpNet.Protocol.Messages.V1_0_1;
using LlrpNet.Protocol.Parameters;
using LlrpNet.Protocol.Parameters.V1_0_1;
using LlrpNet.Protocol.Registry;
using LlrpNet.Protocol.Registry.V1_0_1;
using LlrpSdk.Tests.Support;
using V101Messages = LlrpNet.Protocol.Messages.V1_0_1;

namespace LlrpSdk.Tests;

public sealed class LlrpRoSpecServiceTests
{
    [Fact]
    public async Task Operations_MapToTypedMessagesWithUniqueNonzeroIds()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var returnedRoSpecs = new ROSpec[]
        {
            RoSpec([0x01]),
            RoSpec([0x02, 0x03]),
        };
        var transport = new ScriptedLlrpTransport();
        ConfigureSuccessResponses(transport, returnedRoSpecs);
        await using LlrpReader reader = CreateReader(transport);
        await reader.ConnectAsync(timeout.Token);
        Assert.Equal(ReaderResourceMode.Idle, reader.ResourceMode);
        var addedRoSpec = RoSpec([0xCA, 0xFE]);

        await reader.RoSpecs.AddAsync(addedRoSpec, timeout.Token);
        await reader.RoSpecs.DeleteAsync(10, timeout.Token);
        await reader.RoSpecs.EnableAsync(20, timeout.Token);
        await reader.RoSpecs.DisableAsync(30, timeout.Token);
        await reader.RoSpecs.StartAsync(40, timeout.Token);
        await reader.RoSpecs.StopAsync(50, timeout.Token);
        IReadOnlyList<ILlrpParameter> actualRoSpecs = await reader.RoSpecs.GetAllAsync(timeout.Token);

        LlrpCodecRegistry registry = CreateRegistry();
        ILlrpMessage[] requests = transport.SentFrames
            .Where(static frame =>
                LlrpMessageHeader.Decode(frame).MessageType is
                    >= V101Messages.ADD_ROSPEC.MessageType and <= V101Messages.GET_ROSPECS.MessageType)
            .Select(frame => registry.DecodeMessage(frame))
            .ToArray();

        Assert.Collection(
            requests,
            request =>
            {
                var add = Assert.IsType<V101Messages.ADD_ROSPEC>(request);
                Assert.Equal(0xCAFEU, add.ROSpec.ROSpecID);
            },
            request => Assert.Equal(10U, Assert.IsType<V101Messages.DELETE_ROSPEC>(request).ROSpecID),
            request => Assert.Equal(20U, Assert.IsType<V101Messages.ENABLE_ROSPEC>(request).ROSpecID),
            request => Assert.Equal(30U, Assert.IsType<V101Messages.DISABLE_ROSPEC>(request).ROSpecID),
            request => Assert.Equal(40U, Assert.IsType<V101Messages.START_ROSPEC>(request).ROSpecID),
            request => Assert.Equal(50U, Assert.IsType<V101Messages.STOP_ROSPEC>(request).ROSpecID),
            request => Assert.IsType<V101Messages.GET_ROSPECS>(request));
        Assert.All(requests, static request => Assert.NotEqual(0U, request.MessageId));
        Assert.Equal(requests.Length, requests.Select(static request => request.MessageId).Distinct().Count());

        Assert.Equal(2, actualRoSpecs.Count);
        Assert.Equal(1U, Assert.IsType<ROSpec>(actualRoSpecs[0]).ROSpecID);
        Assert.Equal(0x0203U, Assert.IsType<ROSpec>(actualRoSpecs[1]).ROSpecID);
        ICollection<ILlrpParameter> immutable =
            Assert.IsAssignableFrom<ICollection<ILlrpParameter>>(actualRoSpecs);
        Assert.True(immutable.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => immutable.Add(RoSpec([])));
    }

    [Fact]
    public async Task AddDefault_AllowsManagedRecognitionIdAsAnExpertWrite()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var transport = new ScriptedLlrpTransport();
        ConfigureSuccessResponses(transport);
        await using LlrpReader reader = CreateReader(transport);
        await reader.ConnectAsync(timeout.Token);

        await reader.RoSpecs.AddDefaultAsync(
            LlrpReader.ManagedInventoryRoSpecId,
            new InventorySettings(),
            timeout.Token);

        LlrpCodecRegistry registry = CreateRegistry();
        V101Messages.ADD_ROSPEC request = transport.SentFrames
            .Where(static frame => LlrpMessageHeader.Decode(frame).MessageType == V101Messages.ADD_ROSPEC.MessageType)
            .Select(frame => Assert.IsType<V101Messages.ADD_ROSPEC>(registry.DecodeMessage(frame)))
            .Single();
        Assert.Equal(LlrpReader.ManagedInventoryRoSpecId, request.ROSpec.ROSpecID);
        Assert.Equal(ReaderObservedState.Stale, reader.ObservedState);
    }

    [Fact]
    public async Task EveryNonSuccessStatusThrowsOperationExceptionWithExactStatus()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var failureStatus = new LLRPStatus(
            StatusCode.M_ParameterError,
            "reader rejected ROSpec operation",
            null,
            null);
        var transport = new ScriptedLlrpTransport();
        ConfigureStatusResponses(transport, failureStatus);
        await using LlrpReader reader = CreateReader(transport);
        await reader.ConnectAsync(timeout.Token);
        (string Operation, Func<Task> Invoke)[] cases =
        [
            ("ADD_ROSPEC", () => reader.RoSpecs.AddAsync(RoSpec([]), timeout.Token)),
            ("DELETE_ROSPEC", () => reader.RoSpecs.DeleteAsync(1, timeout.Token)),
            ("ENABLE_ROSPEC", () => reader.RoSpecs.EnableAsync(1, timeout.Token)),
            ("DISABLE_ROSPEC", () => reader.RoSpecs.DisableAsync(1, timeout.Token)),
            ("START_ROSPEC", () => reader.RoSpecs.StartAsync(1, timeout.Token)),
            ("STOP_ROSPEC", () => reader.RoSpecs.StopAsync(1, timeout.Token)),
            ("GET_ROSPECS", () => reader.RoSpecs.GetAllAsync(timeout.Token)),
        ];

        foreach ((string operation, Func<Task> invoke) in cases)
        {
            LlrpReaderOperationException exception =
                await Assert.ThrowsAsync<LlrpReaderOperationException>(invoke);

            Assert.Equal(operation, exception.Operation);
            Assert.Equal((ushort)StatusCode.M_ParameterError, exception.StatusCode);
            Assert.Equal("reader rejected ROSpec operation", exception.ErrorDescription);
        }
    }

    [Fact]
    public async Task ErrorMessage_IsConvertedToReaderOperationException()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var transport = new ScriptedLlrpTransport();
        transport.OnSendAsync = (frame, _) =>
        {
            LlrpMessageHeader header = LlrpMessageHeader.Decode(frame.Span);
            if (header.MessageType == V101Messages.DELETE_ROSPEC.MessageType)
            {
                transport.EnqueueFrame(LlrpTestFrames.ErrorMessageFrame(
                    header.MessageId,
                    new LLRPStatus(
                        StatusCode.M_UnsupportedMessage,
                        "DELETE_ROSPEC is unavailable",
                        null,
                        null)));
            }

            return ValueTask.CompletedTask;
        };
        await using LlrpReader reader = CreateReader(transport);
        await reader.ConnectAsync(timeout.Token);
        LlrpReaderOperationException exception =
            await Assert.ThrowsAsync<LlrpReaderOperationException>(() =>
                reader.RoSpecs.DeleteAsync(7, timeout.Token));

        Assert.Equal("DELETE_ROSPEC", exception.Operation);
        Assert.Equal((ushort)StatusCode.M_UnsupportedMessage, exception.StatusCode);
        Assert.Equal("DELETE_ROSPEC is unavailable", exception.ErrorDescription);
    }

    [Fact]
    public async Task Add_RejectsNullAndNonRoSpecBeforeSending()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var transport = new ScriptedLlrpTransport();
        ConfigureSuccessResponses(transport);
        await using LlrpReader reader = CreateReader(transport);
        await reader.ConnectAsync(timeout.Token);
        int sentBefore = transport.SentFrames.Count;
        var wrongType = new UnknownParameter(
            LlrpProtocolVersion.Version101,
            parameterType: 178,
            []);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => reader.RoSpecs.AddAsync(null!, timeout.Token));
        ArgumentException exception = await Assert.ThrowsAsync<ArgumentException>(
            () => reader.RoSpecs.AddAsync(wrongType, timeout.Token));

        Assert.Contains("177", exception.Message, StringComparison.Ordinal);
        Assert.Equal(sentBefore, transport.SentFrames.Count);
    }

    [Fact]
    public async Task ConcurrentOperations_CorrelateDistinctTransactions()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var transport = new ScriptedLlrpTransport();
        ConfigureSuccessResponses(transport);
        await using LlrpReader reader = CreateReader(transport);
        await reader.ConnectAsync(timeout.Token);
        Task[] operations = Enumerable.Range(1, 32)
            .Select(id => reader.RoSpecs.DeleteAsync((uint)id, timeout.Token))
            .ToArray();
        await Task.WhenAll(operations);

        LlrpCodecRegistry registry = CreateRegistry();
        V101Messages.DELETE_ROSPEC[] requests = transport.SentFrames
            .Where(static frame =>
                LlrpMessageHeader.Decode(frame).MessageType == V101Messages.DELETE_ROSPEC.MessageType)
            .Select(frame => Assert.IsType<V101Messages.DELETE_ROSPEC>(registry.DecodeMessage(frame)))
            .ToArray();
        Assert.Equal(32, requests.Length);
        Assert.Equal(32, requests.Select(static request => request.MessageId).Distinct().Count());
        Assert.All(requests, static request => Assert.NotEqual(0U, request.MessageId));
        Assert.Equal(
            Enumerable.Range(1, 32).Select(static id => (uint)id).Order(),
            requests.Select(static request => request.ROSpecID).Order());
    }

    [Fact]
    public async Task ServiceOperations_AreRejectedWhileDisconnected()
    {
        var transport = new ScriptedLlrpTransport();
        await using LlrpReader reader = CreateReader(transport);
        var validRoSpec = RoSpec([]);
        Func<Task>[] operations =
        [
            () => reader.RoSpecs.AddAsync(validRoSpec),
            () => reader.RoSpecs.DeleteAsync(1),
            () => reader.RoSpecs.EnableAsync(1),
            () => reader.RoSpecs.DisableAsync(1),
            () => reader.RoSpecs.StartAsync(1),
            () => reader.RoSpecs.StopAsync(1),
            () => reader.RoSpecs.GetAllAsync(),
        ];

        foreach (Func<Task> operation in operations)
        {
            InvalidOperationException exception =
                await Assert.ThrowsAsync<InvalidOperationException>(operation);

            Assert.Contains(
                nameof(ReaderConnectionState.Disconnected),
                exception.Message,
                StringComparison.Ordinal);
        }

        Assert.Empty(transport.SentFrames);
    }

    [Fact]
    public async Task ServiceOperation_IsRejectedUntilInitializationCompletes()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var transport = new ScriptedLlrpTransport
        {
            CapabilityResponseFactory = _ => null,
        };
        await using LlrpReader reader = CreateReader(transport);

        Task connectTask = reader.ConnectAsync(timeout.Token);
        byte[] capabilityRequest = await transport.ReadSentFrameAsync(
            V101Messages.GET_READER_CAPABILITIES.MessageType,
            timeout.Token);
        Assert.Equal(ReaderConnectionState.Initializing, reader.ConnectionState);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => reader.RoSpecs.GetAllAsync(timeout.Token));

        Assert.Contains(
            nameof(ReaderConnectionState.Initializing),
            exception.Message,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            transport.SentFrames,
            static frame => LlrpMessageHeader.Decode(frame).MessageType == V101Messages.GET_ROSPECS.MessageType);

        uint messageId = LlrpMessageHeader.Decode(capabilityRequest).MessageId;
        transport.EnqueueFrame(LlrpTestFrames.CapabilitiesResponse(messageId));

        byte[] allRequest = await transport.ReadSentFrameAsync(
            V101Messages.GET_READER_CAPABILITIES.MessageType,
            timeout.Token);
        uint allMsgId = LlrpMessageHeader.Decode(allRequest).MessageId;
        transport.EnqueueFrame(LlrpTestFrames.CapabilitiesResponse(allMsgId));

        await connectTask;
    }

    [Fact]
    public async Task GetAll_DoesNotCacheDeviceState()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        int getCount = 0;
        var transport = new ScriptedLlrpTransport();
        transport.OnSendAsync = (frame, _) =>
        {
            LlrpMessageHeader header = LlrpMessageHeader.Decode(frame.Span);
            if (header.MessageType == V101Messages.GET_ROSPECS.MessageType)
            {
                int current = Interlocked.Increment(ref getCount);
                transport.EnqueueFrame(LlrpTestFrames.GetRoSpecsResponseFrame(
                    header.MessageId,
                    roSpecs: [RoSpec([(byte)current])]));
            }

            return ValueTask.CompletedTask;
        };
        await using LlrpReader reader = CreateReader(transport);
        await reader.ConnectAsync(timeout.Token);

        IReadOnlyList<ILlrpParameter> first = await reader.RoSpecs.GetAllAsync(timeout.Token);
        IReadOnlyList<ILlrpParameter> second = await reader.RoSpecs.GetAllAsync(timeout.Token);

        Assert.Equal(2, getCount);
        Assert.Equal(1U, Assert.IsType<ROSpec>(Assert.Single(first)).ROSpecID);
        Assert.Equal(2U, Assert.IsType<ROSpec>(Assert.Single(second)).ROSpecID);
    }

    [Fact]
    public async Task RawAndExpertResourceOperations_AreSerializedByOneOperationBoundary()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var transport = new ScriptedLlrpTransport();
        int activeOperations = 0;
        int maximumActiveOperations = 0;

        await using LlrpReader reader = CreateReader(transport);
        await reader.ConnectAsync(timeout.Token);
        transport.OnSendAsync = async (frame, cancellationToken) =>
        {
            int active = Interlocked.Increment(ref activeOperations);
            int observedMaximum;
            do
            {
                observedMaximum = Volatile.Read(ref maximumActiveOperations);
                if (active <= observedMaximum)
                {
                    break;
                }
            }
            while (Interlocked.CompareExchange(
                       ref maximumActiveOperations,
                       active,
                       observedMaximum) != observedMaximum);

            try
            {
                LlrpMessageHeader header = LlrpMessageHeader.Decode(frame.Span);
                if (header.MessageType == V101Messages.GET_ROSPECS.MessageType)
                {
                    transport.EnqueueFrame(LlrpTestFrames.GetRoSpecsResponseFrame(header.MessageId));
                }
                else if (header.MessageType == V101Messages.KEEPALIVE.MessageType)
                {
                    transport.EnqueueFrame(LlrpTestFrames.EmptyMessage(
                        V101Messages.KEEPALIVE_ACK.MessageType,
                        header.MessageId));
                }

                await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
            }
            finally
            {
                Interlocked.Decrement(ref activeOperations);
            }
        };

        Task<IReadOnlyList<ILlrpParameter>> expertQuery = reader.RoSpecs.GetAllAsync(timeout.Token);
        Task<V101Messages.KEEPALIVE_ACK> rawOperation = reader.Protocol.TransactAsync<V101Messages.KEEPALIVE_ACK>(
            new V101Messages.KEEPALIVE(reader.Protocol.NextMessageId()),
            cancellationToken: timeout.Token);

        await Task.WhenAll(expertQuery, rawOperation);
        Assert.Equal(1, Volatile.Read(ref maximumActiveOperations));
    }

    [Fact]
    public async Task RawAndHighLevelOperations_AreSerializedByOneOperationBoundary()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var transport = new ScriptedLlrpTransport();
        var registry = CreateRegistry();
        int activeOperations = 0;
        int maximumActiveOperations = 0;

        await using LlrpReader reader = CreateReader(transport);
        await reader.ConnectAsync(timeout.Token);
        transport.OnSendAsync = async (frame, cancellationToken) =>
        {
            int active = Interlocked.Increment(ref activeOperations);
            int observedMaximum;
            do
            {
                observedMaximum = Volatile.Read(ref maximumActiveOperations);
                if (active <= observedMaximum)
                {
                    break;
                }
            }
            while (Interlocked.CompareExchange(
                       ref maximumActiveOperations,
                       active,
                       observedMaximum) != observedMaximum);

            try
            {
                LlrpMessageHeader header = LlrpMessageHeader.Decode(frame.Span);
                LLRPStatus status = new(StatusCode.M_Success, string.Empty, null, null);
                switch (header.MessageType)
                {
                    case V101Messages.ADD_ROSPEC.MessageType:
                        transport.EnqueueFrame(LlrpTestFrames.RoSpecStatusResponse(
                            V101Messages.ADD_ROSPEC_RESPONSE.MessageType,
                            header.MessageId));
                        break;
                    case V101Messages.DELETE_ROSPEC.MessageType:
                        transport.EnqueueFrame(LlrpTestFrames.RoSpecStatusResponse(
                            V101Messages.DELETE_ROSPEC_RESPONSE.MessageType,
                            header.MessageId));
                        break;
                    case V101Messages.ENABLE_ROSPEC.MessageType:
                        transport.EnqueueFrame(LlrpTestFrames.RoSpecStatusResponse(
                            V101Messages.ENABLE_ROSPEC_RESPONSE.MessageType,
                            header.MessageId));
                        break;
                    case V101Messages.DISABLE_ROSPEC.MessageType:
                        transport.EnqueueFrame(LlrpTestFrames.RoSpecStatusResponse(
                            V101Messages.DISABLE_ROSPEC_RESPONSE.MessageType,
                            header.MessageId));
                        break;
                    case V101Messages.START_ROSPEC.MessageType:
                        transport.EnqueueFrame(LlrpTestFrames.RoSpecStatusResponse(
                            V101Messages.START_ROSPEC_RESPONSE.MessageType,
                            header.MessageId));
                        break;
                    case V101Messages.STOP_ROSPEC.MessageType:
                        transport.EnqueueFrame(LlrpTestFrames.RoSpecStatusResponse(
                            V101Messages.STOP_ROSPEC_RESPONSE.MessageType,
                            header.MessageId));
                        break;
                    case V101Messages.DELETE_ACCESSSPEC.MessageType:
                        transport.EnqueueFrame(registry.EncodeMessage(
                            LlrpProtocolVersion.Version101,
                            new V101Messages.DELETE_ACCESSSPEC_RESPONSE(header.MessageId, status)));
                        break;
                    case V101Messages.KEEPALIVE.MessageType:
                        transport.EnqueueFrame(LlrpTestFrames.EmptyMessage(
                            V101Messages.KEEPALIVE_ACK.MessageType,
                            header.MessageId));
                        break;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
            }
            finally
            {
                Interlocked.Decrement(ref activeOperations);
            }
        };

        Task<InventorySession> highLevelOperation = reader.StartInventoryAsync(
            new InventorySettings(),
            timeout.Token);
        Task<V101Messages.KEEPALIVE_ACK> rawOperation = reader.Protocol.TransactAsync<V101Messages.KEEPALIVE_ACK>(
            new V101Messages.KEEPALIVE(reader.Protocol.NextMessageId()),
            cancellationToken: timeout.Token);

        InventorySession session = await highLevelOperation;
        await rawOperation;
        await using (session)
        {
            await session.StopAsync(timeout.Token);
        }

        Assert.Equal(1, Volatile.Read(ref maximumActiveOperations));
    }

    [Fact]
    public async Task ExpertWriteFailure_MarksResourceObservationStale()
    {
        var transport = new ScriptedLlrpTransport();
        await using LlrpReader reader = CreateReader(transport);
        using var connectionTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await reader.ConnectAsync(connectionTimeout.Token);

        transport.OnSendAsync = (_, _) => ValueTask.CompletedTask;
        using var operationTimeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            reader.RoSpecs.DeleteAsync(7, operationTimeout.Token));

        Assert.Equal(ReaderObservedState.Stale, reader.ObservedState);
        Assert.False(reader.IsManagedStateSynchronized);
    }

    private static void ConfigureSuccessResponses(
        ScriptedLlrpTransport transport,
        IEnumerable<ROSpec>? returnedRoSpecs = null)
    {
        transport.OnSendAsync = (frame, _) =>
        {
            LlrpMessageHeader header = LlrpMessageHeader.Decode(frame.Span);
            EnqueueResponse(transport, header, status: null, returnedRoSpecs);
            return ValueTask.CompletedTask;
        };
    }

    private static void ConfigureStatusResponses(
        ScriptedLlrpTransport transport,
        LLRPStatus status)
    {
        transport.OnSendAsync = (frame, _) =>
        {
            LlrpMessageHeader header = LlrpMessageHeader.Decode(frame.Span);
            EnqueueResponse(transport, header, status, returnedRoSpecs: null);
            return ValueTask.CompletedTask;
        };
    }

    private static void EnqueueResponse(
        ScriptedLlrpTransport transport,
        LlrpMessageHeader requestHeader,
        LLRPStatus? status,
        IEnumerable<ROSpec>? returnedRoSpecs)
    {
        ushort? responseType = requestHeader.MessageType switch
        {
            V101Messages.ADD_ROSPEC.MessageType => V101Messages.ADD_ROSPEC_RESPONSE.MessageType,
            V101Messages.DELETE_ROSPEC.MessageType => V101Messages.DELETE_ROSPEC_RESPONSE.MessageType,
            V101Messages.ENABLE_ROSPEC.MessageType => V101Messages.ENABLE_ROSPEC_RESPONSE.MessageType,
            V101Messages.DISABLE_ROSPEC.MessageType => V101Messages.DISABLE_ROSPEC_RESPONSE.MessageType,
            V101Messages.START_ROSPEC.MessageType => V101Messages.START_ROSPEC_RESPONSE.MessageType,
            V101Messages.STOP_ROSPEC.MessageType => V101Messages.STOP_ROSPEC_RESPONSE.MessageType,
            _ => null,
        };
        if (responseType is ushort actualResponseType)
        {
            transport.EnqueueFrame(LlrpTestFrames.RoSpecStatusResponse(
                actualResponseType,
                requestHeader.MessageId,
                status));
        }
        else if (requestHeader.MessageType == V101Messages.GET_ROSPECS.MessageType)
        {
            transport.EnqueueFrame(LlrpTestFrames.GetRoSpecsResponseFrame(
                requestHeader.MessageId,
                status,
                returnedRoSpecs));
        }
    }

    private static ROSpec RoSpec(ReadOnlySpan<byte> data)
    {
        uint id = data.IsEmpty ? 1U : data.ToArray().Aggregate(0U, static (value, octet) => (value << 8) | octet);
        return new ROSpec(
            id,
            Priority: 0,
            ROSpecState.Disabled,
            new ROBoundarySpec(
                new ROSpecStartTrigger(ROSpecStartTriggerType.Immediate, null, null),
                new ROSpecStopTrigger(ROSpecStopTriggerType.Null, 0, null)),
            [new RFSurveySpec(
                AntennaID: 1,
                StartFrequency: 0,
                EndFrequency: 0,
                new RFSurveySpecStopTrigger(RFSurveySpecStopTriggerType.Null, 0, 0),
                [])],
            ROReportSpec: null);
    }

    private static LlrpCodecRegistry CreateRegistry()
    {
        var registry = new LlrpCodecRegistry();
        Llrp101StandardModule.Register(registry);
        return registry;
    }

    private static LlrpReader CreateReader(ScriptedLlrpTransport transport)
    {
        return LlrpReader.CreateBuilder("scripted.local")
            .WithRequestTimeout(TimeSpan.FromSeconds(3))
            .WithTransportFactory(_ => transport)
            .Build();
    }
}
