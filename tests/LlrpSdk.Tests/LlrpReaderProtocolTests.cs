using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Messages;
using LlrpNet.Protocol.Messages.V1_0_1;
using LlrpSdk.Tests.Support;

namespace LlrpSdk.Tests;

public sealed class LlrpReaderProtocolTests
{
    [Fact]
    public async Task TypedAndRawTransactions_CanRunConcurrently()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var transport = new ScriptedLlrpTransport();
        transport.OnSendAsync = (frame, _) =>
        {
            LlrpMessageHeader header = LlrpMessageHeader.Decode(frame.Span);
            if (header.MessageType == Keepalive.MessageType)
            {
                transport.EnqueueFrame(LlrpTestFrames.EmptyMessage(
                    KeepaliveAck.MessageType,
                    header.MessageId));
            }

            return ValueTask.CompletedTask;
        };
        await using var reader = CreateReader(transport);
        await reader.ConnectAsync(timeout.Token);

        Task<KeepaliveAck>[] typed = Enumerable.Range(1, 16)
            .Select(id => reader.Protocol.TransactAsync<KeepaliveAck>(
                new Keepalive((uint)id),
                cancellationToken: timeout.Token))
            .ToArray();
        Task<ReadOnlyMemory<byte>>[] raw = Enumerable.Range(101, 16)
            .Select(id => reader.Protocol.TransactRawAsync(
                LlrpTestFrames.EmptyMessage(Keepalive.MessageType, (uint)id),
                static (header, _) => header.MessageType == KeepaliveAck.MessageType,
                cancellationToken: timeout.Token))
            .ToArray();

        await Task.WhenAll(typed.Cast<Task>().Concat(raw));

        Assert.Equal(
            Enumerable.Range(1, 16).Select(static id => (uint)id),
            typed.Select(static task => task.Result.MessageId));
        Assert.Equal(
            Enumerable.Range(101, 16).Select(static id => (uint)id),
            raw.Select(static task => LlrpMessageHeader.Decode(task.Result.Span).MessageId));
    }

    [Fact]
    public async Task TypedTransaction_LeavesUnexpectedResponseUnsolicitedAndTimesOut()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var transport = new ScriptedLlrpTransport();
        transport.OnSendAsync = (frame, _) =>
        {
            LlrpMessageHeader header = LlrpMessageHeader.Decode(frame.Span);
            if (header.MessageType == Keepalive.MessageType)
            {
                transport.EnqueueFrame(LlrpTestFrames.EmptyMessage(
                    KeepaliveAck.MessageType,
                    header.MessageId));
            }

            return ValueTask.CompletedTask;
        };
        await using var reader = CreateReader(transport);
        await reader.ConnectAsync(timeout.Token);
        await using IAsyncEnumerator<ILlrpMessage> messages = reader
            .ReadMessagesAsync(timeout.Token)
            .GetAsyncEnumerator(timeout.Token);

        Task<Keepalive> transaction = reader.Protocol.TransactAsync<Keepalive>(
            new Keepalive(900),
            timeout: TimeSpan.FromMilliseconds(100),
            cancellationToken: timeout.Token);

        Assert.True(await messages.MoveNextAsync());
        KeepaliveAck response = Assert.IsType<KeepaliveAck>(messages.Current);
        Assert.Equal(900U, response.MessageId);
        await Assert.ThrowsAsync<TimeoutException>(() => transaction);
    }

    [Fact]
    public void UnexpectedResponseException_ValidatesBeforeBuildingItsMessage()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new LlrpUnexpectedResponseException(
                typeof(Keepalive),
                typeof(KeepaliveAck),
                null!));
    }

    [Fact]
    public async Task Keepalive_IsAcknowledgedWithSameIdAndPublished()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var transport = new ScriptedLlrpTransport();
        await using var reader = CreateReader(transport);
        await reader.ConnectAsync(timeout.Token);
        await using IAsyncEnumerator<ILlrpMessage> messages = reader
            .ReadMessagesAsync(timeout.Token)
            .GetAsyncEnumerator(timeout.Token);

        transport.EnqueueFrame(LlrpTestFrames.EmptyMessage(Keepalive.MessageType, 0x12345678));

        Assert.True(await messages.MoveNextAsync());
        Keepalive keepalive = Assert.IsType<Keepalive>(messages.Current);
        byte[] acknowledgement = await transport.ReadSentFrameAsync(
            KeepaliveAck.MessageType,
            timeout.Token);
        LlrpMessageHeader header = LlrpMessageHeader.Decode(acknowledgement);
        Assert.Equal(0x12345678U, keepalive.MessageId);
        Assert.Equal(KeepaliveAck.MessageType, header.MessageType);
        Assert.Equal(keepalive.MessageId, header.MessageId);
    }

    [Fact]
    public async Task IncomingMessageQueue_WhenApplicationDoesNotConsume_FaultsReaderExplicitly()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var transport = new ScriptedLlrpTransport();
        var failure = new TaskCompletionSource<Exception>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using LlrpReader reader = LlrpReader.CreateBuilder("scripted.local")
            .WithIncomingMessageCapacity(1)
            .WithTransportFactory(_ => transport)
            .Build();
        reader.ErrorOccurred += (_, args) => failure.TrySetResult(args.Error);
        await reader.ConnectAsync(timeout.Token);

        transport.EnqueueFrame(LlrpTestFrames.EmptyMessage(KeepaliveAck.MessageType, 1));
        transport.EnqueueFrame(LlrpTestFrames.EmptyMessage(KeepaliveAck.MessageType, 2));

        Exception actual = await failure.Task.WaitAsync(timeout.Token);
        Assert.IsType<LlrpReaderBackpressureException>(actual);
        Assert.Equal(ReaderConnectionState.Faulted, reader.ConnectionState);
        Assert.False(reader.IsConnected);
    }

    [Fact]
    public async Task TypedAndRawSend_PreserveTheirCompleteFrames()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var transport = new ScriptedLlrpTransport();
        await using var reader = CreateReader(transport);
        await reader.ConnectAsync(timeout.Token);

        await reader.Protocol.SendAsync(new KeepaliveAck(41), timeout.Token);
        byte[] raw = LlrpTestFrames.EmptyMessage(KeepaliveAck.MessageType, 42);
        await reader.Protocol.SendRawAsync(raw, timeout.Token);

        byte[][] sentMessages = transport.SentFrames
            .Where(static frame =>
                LlrpMessageHeader.Decode(frame).MessageType == KeepaliveAck.MessageType)
            .ToArray();
        Assert.Equal(2, sentMessages.Length);
        LlrpMessageHeader[] headers = sentMessages
            .Select(static frame => LlrpMessageHeader.Decode(frame))
            .ToArray();
        Assert.Equal([41U, 42U], headers.Select(static header => header.MessageId));
        Assert.Equal(raw, sentMessages[^1]);
    }

    private static LlrpReader CreateReader(ScriptedLlrpTransport transport)
    {
        LlrpReaderOptions options = new LlrpReaderOptionsBuilder("scripted.local")
            .WithRequestTimeout(TimeSpan.FromSeconds(3))
            .WithTransportFactory(_ => transport)
            .Build();
        return new LlrpReader(options);
    }
}
