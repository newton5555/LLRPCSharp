using System.Collections.Concurrent;
using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Messages;
using LlrpNet.Protocol.Messages.V1_0_1;
using LlrpNet.Protocol.Parameters;
using LlrpNet.Protocol.Parameters.V1_0_1;
using LlrpNet.Protocol.Registry;
using LlrpNet.Protocol.Registry.V1_0_1;
using LlrpSdk.Tests.Support;

namespace LlrpSdk.Tests;

public sealed class LlrpRoSpecServiceTests
{
    [Fact]
    public async Task Operations_MapToTypedMessagesWithUniqueNonzeroIds()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var returnedRoSpecs = new ILlrpParameter[]
        {
            RoSpec([0x01]),
            RoSpec([0x02, 0x03]),
        };
        var transport = new ScriptedLlrpTransport();
        ConfigureSuccessResponses(transport, returnedRoSpecs);
        await using LlrpReader reader = CreateReader(transport);
        await reader.ConnectAsync(timeout.Token);
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
                    >= AddRoSpec.MessageType and <= GetRoSpecs.MessageType)
            .Select(frame => registry.DecodeMessage(frame))
            .ToArray();

        Assert.Collection(
            requests,
            request =>
            {
                var add = Assert.IsType<AddRoSpec>(request);
                UnknownParameter parameter = Assert.IsType<UnknownParameter>(add.RoSpec);
                Assert.Equal(new byte[] { 0xCA, 0xFE }, parameter.Data.ToArray());
            },
            request => Assert.Equal(10U, Assert.IsType<DeleteRoSpec>(request).RoSpecId),
            request => Assert.Equal(20U, Assert.IsType<EnableRoSpec>(request).RoSpecId),
            request => Assert.Equal(30U, Assert.IsType<DisableRoSpec>(request).RoSpecId),
            request => Assert.Equal(40U, Assert.IsType<StartRoSpec>(request).RoSpecId),
            request => Assert.Equal(50U, Assert.IsType<StopRoSpec>(request).RoSpecId),
            request => Assert.IsType<GetRoSpecs>(request));
        Assert.All(requests, static request => Assert.NotEqual(0U, request.MessageId));
        Assert.Equal(requests.Length, requests.Select(static request => request.MessageId).Distinct().Count());

        Assert.Equal(2, actualRoSpecs.Count);
        Assert.Equal(
            new byte[] { 0x01 },
            Assert.IsType<UnknownParameter>(actualRoSpecs[0]).Data.ToArray());
        Assert.Equal(
            new byte[] { 0x02, 0x03 },
            Assert.IsType<UnknownParameter>(actualRoSpecs[1]).Data.ToArray());
        ICollection<ILlrpParameter> immutable =
            Assert.IsAssignableFrom<ICollection<ILlrpParameter>>(actualRoSpecs);
        Assert.True(immutable.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => immutable.Add(RoSpec([])));
    }

    [Fact]
    public async Task EveryNonSuccessStatusThrowsOperationExceptionWithExactStatus()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var failureStatus = new LlrpStatus(
            LlrpStatusCode.MParameterError,
            "reader rejected ROSpec operation");
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
            Assert.Equal(LlrpStatusCode.MParameterError, exception.StatusCode);
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
            if (header.MessageType == DeleteRoSpec.MessageType)
            {
                transport.EnqueueFrame(LlrpTestFrames.ErrorMessageFrame(
                    header.MessageId,
                    new LlrpStatus(
                        LlrpStatusCode.MUnsupportedMessage,
                        "DELETE_ROSPEC is unavailable")));
            }

            return ValueTask.CompletedTask;
        };
        await using LlrpReader reader = CreateReader(transport);
        await reader.ConnectAsync(timeout.Token);

        LlrpReaderOperationException exception =
            await Assert.ThrowsAsync<LlrpReaderOperationException>(() =>
                reader.RoSpecs.DeleteAsync(7, timeout.Token));

        Assert.Equal(nameof(DeleteRoSpec), exception.Operation);
        Assert.Equal(LlrpStatusCode.MUnsupportedMessage, exception.StatusCode);
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
        DeleteRoSpec[] requests = transport.SentFrames
            .Where(static frame =>
                LlrpMessageHeader.Decode(frame).MessageType == DeleteRoSpec.MessageType)
            .Select(frame => Assert.IsType<DeleteRoSpec>(registry.DecodeMessage(frame)))
            .ToArray();
        Assert.Equal(32, requests.Length);
        Assert.Equal(32, requests.Select(static request => request.MessageId).Distinct().Count());
        Assert.All(requests, static request => Assert.NotEqual(0U, request.MessageId));
        Assert.Equal(
            Enumerable.Range(1, 32).Select(static id => (uint)id).Order(),
            requests.Select(static request => request.RoSpecId).Order());
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
            GetReaderCapabilities.MessageType,
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
            static frame => LlrpMessageHeader.Decode(frame).MessageType == GetRoSpecs.MessageType);

        uint messageId = LlrpMessageHeader.Decode(capabilityRequest).MessageId;
        transport.EnqueueFrame(LlrpTestFrames.CapabilitiesResponse(messageId));
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
            if (header.MessageType == GetRoSpecs.MessageType)
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
        Assert.Equal(
            new byte[] { 0x01 },
            Assert.IsType<UnknownParameter>(Assert.Single(first)).Data.ToArray());
        Assert.Equal(
            new byte[] { 0x02 },
            Assert.IsType<UnknownParameter>(Assert.Single(second)).Data.ToArray());
    }

    private static void ConfigureSuccessResponses(
        ScriptedLlrpTransport transport,
        IEnumerable<ILlrpParameter>? returnedRoSpecs = null)
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
        LlrpStatus status)
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
        LlrpStatus? status,
        IEnumerable<ILlrpParameter>? returnedRoSpecs)
    {
        ushort? responseType = requestHeader.MessageType switch
        {
            AddRoSpec.MessageType => AddRoSpecResponse.MessageType,
            DeleteRoSpec.MessageType => DeleteRoSpecResponse.MessageType,
            EnableRoSpec.MessageType => EnableRoSpecResponse.MessageType,
            DisableRoSpec.MessageType => DisableRoSpecResponse.MessageType,
            StartRoSpec.MessageType => StartRoSpecResponse.MessageType,
            StopRoSpec.MessageType => StopRoSpecResponse.MessageType,
            _ => null,
        };
        if (responseType is ushort actualResponseType)
        {
            transport.EnqueueFrame(LlrpTestFrames.RoSpecStatusResponse(
                actualResponseType,
                requestHeader.MessageId,
                status));
        }
        else if (requestHeader.MessageType == GetRoSpecs.MessageType)
        {
            transport.EnqueueFrame(LlrpTestFrames.GetRoSpecsResponseFrame(
                requestHeader.MessageId,
                status,
                returnedRoSpecs));
        }
    }

    private static UnknownParameter RoSpec(ReadOnlySpan<byte> data)
    {
        return new UnknownParameter(
            LlrpProtocolVersion.Version101,
            parameterType: 177,
            data);
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
