using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using LlrpNet.Core.Protocol;
using LlrpNet.Core.Session;
using LlrpNet.Core.Transport;
using Microsoft.Extensions.Logging;

namespace LlrpNet.Core.Tests.Session;

public sealed class LlrpSessionTests
{
    [Fact]
    public async Task ConnectionCompletion_ReportsUnexpectedReceiveFailureWithoutFaultingTask()
    {
        using var timeoutSource = CreateTestTimeout();
        await using var transport = new TestTransport();
        await using var session = new LlrpSession(transport);
        await session.ConnectAsync(timeoutSource.Token);
        Task<LlrpSessionTermination> completion = session.ConnectionCompletion;
        var receiveFailure = new IOException("peer closed");

        transport.QueueReceiveFailure(receiveFailure);
        LlrpSessionTermination termination = await completion.WaitAsync(timeoutSource.Token);

        Assert.False(termination.WasRequested);
        LlrpSessionDisconnectedException error =
            Assert.IsType<LlrpSessionDisconnectedException>(termination.Error);
        Assert.Same(receiveFailure, error.InnerException);
        Assert.True(completion.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task ConnectionCompletion_IsReplacedOnReconnectAndExplicitDisconnectIsRequested()
    {
        using var timeoutSource = CreateTestTimeout();
        await using var transport = new TestTransport();
        await using var session = new LlrpSession(transport);
        await session.ConnectAsync(timeoutSource.Token);
        Task<LlrpSessionTermination> first = session.ConnectionCompletion;

        await session.DisconnectAsync(timeoutSource.Token);
        LlrpSessionTermination firstTermination = await first.WaitAsync(timeoutSource.Token);
        await session.ConnectAsync(timeoutSource.Token);
        Task<LlrpSessionTermination> second = session.ConnectionCompletion;

        Assert.True(firstTermination.WasRequested);
        Assert.Null(firstTermination.Error);
        Assert.NotSame(first, second);
        Assert.False(second.IsCompleted);

        await session.DisconnectAsync(timeoutSource.Token);
        Assert.True((await second.WaitAsync(timeoutSource.Token)).WasRequested);
    }

    [Fact]
    public async Task ConnectAsync_DiscardsQueuedFramesFromPreviousGeneration()
    {
        using var timeoutSource = CreateTestTimeout();
        await using var transport = new TestTransport();
        await using var session = new LlrpSession(transport);
        await session.ConnectAsync(timeoutSource.Token);
        transport.QueueReceivedFrame(CreateFrame(messageType: 61, messageId: 1, payload: []));
        while (!session.UnsolicitedFrames.TryPeek(out _))
        {
            await Task.Delay(1, timeoutSource.Token);
        }

        await session.DisconnectAsync(timeoutSource.Token);
        await session.ConnectAsync(timeoutSource.Token);

        Assert.False(session.UnsolicitedFrames.TryPeek(out _));
        Assert.Equal(1, session.DiscardedUnsolicitedFrameCount);
        Assert.Equal(2, session.ConnectionGeneration);
    }

    [Fact]
    public async Task TransactAsync_CorrelatesConcurrentResponsesByMessageId()
    {
        using var timeoutSource = CreateTestTimeout();
        await using var transport = new TestTransport();
        await using var session = new LlrpSession(transport);
        await session.ConnectAsync(timeoutSource.Token);
        byte[] firstRequest = CreateFrame(messageType: 1, messageId: 101, payload: [0x01]);
        byte[] secondRequest = CreateFrame(messageType: 20, messageId: 202, payload: [0x02]);

        Task<ReadOnlyMemory<byte>> firstTask = session.TransactAsync(
            firstRequest,
            MatchAnyResponse,
            cancellationToken: timeoutSource.Token);
        Task<ReadOnlyMemory<byte>> secondTask = session.TransactAsync(
            secondRequest,
            MatchAnyResponse,
            cancellationToken: timeoutSource.Token);
        await transport.ReadSentFrameAsync(timeoutSource.Token);
        await transport.ReadSentFrameAsync(timeoutSource.Token);

        byte[] secondResponse = CreateFrame(messageType: 30, messageId: 202, payload: [0xA2]);
        byte[] firstResponse = CreateFrame(messageType: 11, messageId: 101, payload: [0xA1]);
        transport.QueueReceivedFrame(secondResponse);
        transport.QueueReceivedFrame(firstResponse);

        Assert.Equal(firstResponse, (await firstTask).ToArray());
        Assert.Equal(secondResponse, (await secondTask).ToArray());
    }

    [Fact]
    public async Task TransactAsync_SameIdentifierWrongType_RemainsUnsolicited()
    {
        using var timeoutSource = CreateTestTimeout();
        await using var transport = new TestTransport();
        await using var session = new LlrpSession(transport);
        await session.ConnectAsync(timeoutSource.Token);
        byte[] request = CreateFrame(messageType: 1, messageId: 7, payload: []);
        Task<ReadOnlyMemory<byte>> transaction = session.TransactAsync(
            request,
            static (header, _) => header.MessageType is 11 or 100,
            cancellationToken: timeoutSource.Token);
        await transport.ReadSentFrameAsync(timeoutSource.Token);

        byte[] keepalive = CreateFrame(messageType: 62, messageId: 7, payload: []);
        transport.QueueReceivedFrame(keepalive);
        ReadOnlyMemory<byte> unsolicited = await session.UnsolicitedFrames
            .ReadAsync(timeoutSource.Token);

        Assert.Equal(keepalive, unsolicited.ToArray());
        Assert.False(transaction.IsCompleted);

        byte[] response = CreateFrame(messageType: 11, messageId: 7, payload: [0x01]);
        transport.QueueReceivedFrame(response);
        Assert.Equal(response, (await transaction).ToArray());
    }

    [Fact]
    public async Task ReceiveLoop_PublishesUnmatchedFramesWithoutChangingBytes()
    {
        using var timeoutSource = CreateTestTimeout();
        await using var transport = new TestTransport();
        await using var session = new LlrpSession(transport);
        await session.ConnectAsync(timeoutSource.Token);
        byte[] zeroIdFrame = CreateFrame(messageType: 63, messageId: 0, payload: [0x10, 0x20]);
        byte[] unmatchedFrame = CreateFrame(messageType: 61, messageId: 999, payload: [0x30]);

        transport.QueueReceivedFrame(zeroIdFrame);
        transport.QueueReceivedFrame(unmatchedFrame);

        ReadOnlyMemory<byte> first = await session.UnsolicitedFrames.ReadAsync(timeoutSource.Token);
        ReadOnlyMemory<byte> second = await session.UnsolicitedFrames.ReadAsync(timeoutSource.Token);
        Assert.Equal(zeroIdFrame, first.ToArray());
        Assert.Equal(unmatchedFrame, second.ToArray());
    }

    [Fact]
    public async Task UnsolicitedQueue_WhenFull_FaultsConnectionByDefault()
    {
        using var timeoutSource = CreateTestTimeout();
        await using var transport = new TestTransport();
        await using var session = new LlrpSession(
            transport,
            new LlrpSessionOptions { UnsolicitedFrameCapacity = 1 });
        await session.ConnectAsync(timeoutSource.Token);
        Task<LlrpSessionTermination> completion = session.ConnectionCompletion;

        transport.QueueReceivedFrame(CreateFrame(messageType: 61, messageId: 1, payload: []));
        transport.QueueReceivedFrame(CreateFrame(messageType: 62, messageId: 2, payload: []));

        LlrpSessionTermination termination = await completion.WaitAsync(timeoutSource.Token);
        LlrpSessionDisconnectedException sessionError =
            Assert.IsType<LlrpSessionDisconnectedException>(termination.Error);
        Assert.IsType<LlrpSessionBackpressureException>(sessionError.InnerException);
        Assert.False(session.IsConnected);
        Assert.Equal(0, session.DroppedUnsolicitedFrameCount);
    }

    [Fact]
    public async Task UnsolicitedQueue_DropNewest_KeepsConnectionAndCountsDrops()
    {
        using var timeoutSource = CreateTestTimeout();
        await using var transport = new TestTransport();
        await using var session = new LlrpSession(
            transport,
            new LlrpSessionOptions
            {
                UnsolicitedFrameCapacity = 1,
                UnsolicitedFrameOverflowPolicy =
                    LlrpUnsolicitedFrameOverflowPolicy.DropNewest,
            });
        await session.ConnectAsync(timeoutSource.Token);
        byte[] retained = CreateFrame(messageType: 61, messageId: 1, payload: []);

        transport.QueueReceivedFrame(retained);
        transport.QueueReceivedFrame(CreateFrame(messageType: 62, messageId: 2, payload: []));
        while (session.DroppedUnsolicitedFrameCount == 0)
        {
            await Task.Delay(1, timeoutSource.Token);
        }

        Assert.Equal(1, session.DroppedUnsolicitedFrameCount);
        Assert.True(session.IsConnected);
        Assert.Equal(
            retained,
            (await session.UnsolicitedFrames.ReadAsync(timeoutSource.Token)).ToArray());
    }

    [Fact]
    public async Task TransactAsync_WhenCanceled_PublishesLateResponseAsUnsolicited()
    {
        using var timeoutSource = CreateTestTimeout();
        using var requestCancellation = new CancellationTokenSource();
        await using var transport = new TestTransport();
        await using var session = new LlrpSession(transport);
        await session.ConnectAsync(timeoutSource.Token);
        byte[] request = CreateFrame(messageType: 1, messageId: 31, payload: []);
        Task<ReadOnlyMemory<byte>> transaction = session.TransactAsync(
            request,
            MatchAnyResponse,
            cancellationToken: requestCancellation.Token);
        await transport.ReadSentFrameAsync(timeoutSource.Token);

        requestCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await transaction);

        byte[] lateResponse = CreateFrame(messageType: 11, messageId: 31, payload: [0x42]);
        transport.QueueReceivedFrame(lateResponse);
        ReadOnlyMemory<byte> unsolicited = await session.UnsolicitedFrames.ReadAsync(timeoutSource.Token);
        Assert.Equal(lateResponse, unsolicited.ToArray());
    }

    [Fact]
    public async Task TransactAsync_WhenTimedOut_PublishesLateResponseAsUnsolicited()
    {
        using var timeoutSource = CreateTestTimeout();
        await using var transport = new TestTransport();
        await using var session = new LlrpSession(transport);
        await session.ConnectAsync(timeoutSource.Token);
        byte[] request = CreateFrame(messageType: 1, messageId: 41, payload: []);
        Task<ReadOnlyMemory<byte>> transaction = session.TransactAsync(
            request,
            MatchAnyResponse,
            timeout: TimeSpan.FromMilliseconds(50),
            cancellationToken: timeoutSource.Token);
        await transport.ReadSentFrameAsync(timeoutSource.Token);

        await Assert.ThrowsAsync<TimeoutException>(async () => await transaction);

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await session.TransactAsync(
                request,
                MatchAnyResponse,
                cancellationToken: timeoutSource.Token));

        byte[] lateResponse = CreateFrame(messageType: 11, messageId: 41, payload: [0x43]);
        transport.QueueReceivedFrame(lateResponse);
        ReadOnlyMemory<byte> unsolicited = await session.UnsolicitedFrames.ReadAsync(timeoutSource.Token);
        Assert.Equal(lateResponse, unsolicited.ToArray());
    }

    [Fact]
    public async Task TransactAsync_ResponseTimeoutStartsAfterSendCompletes()
    {
        using var timeoutSource = CreateTestTimeout();
        await using var transport = new TestTransport();
        await using var session = new LlrpSession(transport);
        await session.ConnectAsync(timeoutSource.Token);
        transport.BlockSends();

        Task<ReadOnlyMemory<byte>> transaction = session.TransactAsync(
            CreateFrame(messageType: 1, messageId: 45, payload: []),
            MatchAnyResponse,
            timeout: TimeSpan.FromMilliseconds(25),
            cancellationToken: timeoutSource.Token);
        await transport.WaitForBlockedSendAsync(timeoutSource.Token);
        await Task.Delay(75, timeoutSource.Token);

        Assert.False(transaction.IsCompleted);

        transport.ReleaseBlockedSends();
        await transport.ReadSentFrameAsync(timeoutSource.Token);
        await Assert.ThrowsAsync<TimeoutException>(async () => await transaction);
    }

    [Fact]
    public async Task ReceiveFailure_FailsEveryPendingTransactionAndClosesTransport()
    {
        using var timeoutSource = CreateTestTimeout();
        await using var transport = new TestTransport();
        await using var session = new LlrpSession(transport);
        await session.ConnectAsync(timeoutSource.Token);
        Task<ReadOnlyMemory<byte>> first = session.TransactAsync(
            CreateFrame(messageType: 1, messageId: 51, payload: []),
            MatchAnyResponse,
            cancellationToken: timeoutSource.Token);
        Task<ReadOnlyMemory<byte>> second = session.TransactAsync(
            CreateFrame(messageType: 20, messageId: 52, payload: []),
            MatchAnyResponse,
            cancellationToken: timeoutSource.Token);
        await transport.ReadSentFrameAsync(timeoutSource.Token);
        await transport.ReadSentFrameAsync(timeoutSource.Token);

        var receiveFailure = new IOException("peer closed");
        transport.QueueReceiveFailure(receiveFailure);

        LlrpSessionDisconnectedException firstException =
            await Assert.ThrowsAsync<LlrpSessionDisconnectedException>(async () => await first);
        LlrpSessionDisconnectedException secondException =
            await Assert.ThrowsAsync<LlrpSessionDisconnectedException>(async () => await second);
        Assert.Same(receiveFailure, firstException.InnerException);
        Assert.Same(receiveFailure, secondException.InnerException);
        Assert.False(session.IsConnected);
        Assert.False(transport.IsConnected);
    }

    [Fact]
    public async Task DisconnectAsync_FailsPendingTransactionAndSupportsReconnect()
    {
        using var timeoutSource = CreateTestTimeout();
        await using var transport = new TestTransport();
        await using var session = new LlrpSession(transport);
        await session.ConnectAsync(timeoutSource.Token);
        byte[] request = CreateFrame(messageType: 1, messageId: 61, payload: []);
        Task<ReadOnlyMemory<byte>> interrupted = session.TransactAsync(
            request,
            MatchAnyResponse,
            cancellationToken: timeoutSource.Token);
        await transport.ReadSentFrameAsync(timeoutSource.Token);

        await session.DisconnectAsync(timeoutSource.Token);
        await Assert.ThrowsAsync<LlrpSessionDisconnectedException>(async () => await interrupted);
        Assert.False(session.IsConnected);

        await session.ConnectAsync(timeoutSource.Token);
        Task<ReadOnlyMemory<byte>> retried = session.TransactAsync(
            request,
            MatchAnyResponse,
            cancellationToken: timeoutSource.Token);
        await transport.ReadSentFrameAsync(timeoutSource.Token);
        byte[] response = CreateFrame(messageType: 11, messageId: 61, payload: [0x61]);
        transport.QueueReceivedFrame(response);

        Assert.Equal(response, (await retried).ToArray());
        Assert.True(session.IsConnected);
    }

    [Fact]
    public async Task DisconnectAsync_ConcurrentReceiveFailure_DoesNotDeadlockOrLeakCancellation()
    {
        using var timeoutSource = CreateTestTimeout();
        await using var transport = new TestTransport();
        using var loggerFactory = new BlockingErrorLoggerFactory();
        await using var session = new LlrpSession(transport, loggerFactory: loggerFactory);
        await session.ConnectAsync(timeoutSource.Token);
        Task<ReadOnlyMemory<byte>> transaction = session.TransactAsync(
            CreateFrame(messageType: 1, messageId: 62, payload: []),
            MatchAnyResponse,
            cancellationToken: timeoutSource.Token);
        await transport.ReadSentFrameAsync(timeoutSource.Token);

        transport.QueueReceiveFailure(new IOException("concurrent peer close"));
        await loggerFactory.WaitForErrorAsync(timeoutSource.Token);
        Task disconnect = session.DisconnectAsync(timeoutSource.Token).AsTask();
        loggerFactory.ReleaseError();

        await disconnect.WaitAsync(timeoutSource.Token);
        await Assert.ThrowsAsync<LlrpSessionDisconnectedException>(async () => await transaction);
        Assert.False(session.IsConnected);
    }

    [Fact]
    public async Task ConnectAsync_WhenPreviousTransportAlreadyFaulted_ReplacesGeneration()
    {
        using var timeoutSource = CreateTestTimeout();
        await using var transport = new TestTransport();
        using var loggerFactory = new BlockingErrorLoggerFactory();
        await using var session = new LlrpSession(transport, loggerFactory: loggerFactory);
        await session.ConnectAsync(timeoutSource.Token);
        Task<LlrpSessionTermination> firstCompletion = session.ConnectionCompletion;

        transport.QueueReceiveFailure(new IOException("generation failed"));
        LlrpSessionTermination firstTermination = await firstCompletion.WaitAsync(timeoutSource.Token);
        await loggerFactory.WaitForErrorAsync(timeoutSource.Token);
        Task reconnect = session.ConnectAsync(timeoutSource.Token).AsTask();
        loggerFactory.ReleaseError();

        await reconnect.WaitAsync(timeoutSource.Token);
        Assert.False(firstTermination.WasRequested);
        Assert.NotNull(firstTermination.Error);
        Assert.True(session.IsConnected);
        Assert.Equal(2, transport.ConnectCount);
        Assert.NotSame(firstCompletion, session.ConnectionCompletion);
    }

    [Fact]
    public async Task TransactAsync_WhenSendFails_ReleasesMessageIdentifierForRetry()
    {
        using var timeoutSource = CreateTestTimeout();
        await using var transport = new TestTransport();
        await using var session = new LlrpSession(transport);
        await session.ConnectAsync(timeoutSource.Token);
        byte[] request = CreateFrame(messageType: 1, messageId: 71, payload: []);
        var sendFailure = new IOException("send failed");
        transport.NextSendFailure = sendFailure;

        IOException actual = await Assert.ThrowsAsync<IOException>(
            async () => await session.TransactAsync(
                request,
                MatchAnyResponse,
                cancellationToken: timeoutSource.Token));
        Assert.Same(sendFailure, actual);

        Task<ReadOnlyMemory<byte>> retry = session.TransactAsync(
            request,
            MatchAnyResponse,
            cancellationToken: timeoutSource.Token);
        await transport.ReadSentFrameAsync(timeoutSource.Token);
        byte[] response = CreateFrame(messageType: 11, messageId: 71, payload: [0x71]);
        transport.QueueReceivedFrame(response);
        Assert.Equal(response, (await retry).ToArray());
    }

    [Fact]
    public async Task DisposeAsync_FailsPendingTransactionCompletesChannelAndDisposesTransport()
    {
        using var timeoutSource = CreateTestTimeout();
        await using var transport = new TestTransport();
        var session = new LlrpSession(transport);
        await session.ConnectAsync(timeoutSource.Token);
        Task<ReadOnlyMemory<byte>> transaction = session.TransactAsync(
            CreateFrame(messageType: 1, messageId: 81, payload: []),
            MatchAnyResponse,
            cancellationToken: timeoutSource.Token);
        await transport.ReadSentFrameAsync(timeoutSource.Token);

        await session.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await transaction);
        await session.UnsolicitedFrames.Completion.WaitAsync(timeoutSource.Token);
        Assert.True(transport.IsDisposed);
        Assert.False(session.IsConnected);
        await session.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(
            async () => await session.ConnectAsync(timeoutSource.Token));
    }

    [Fact]
    public async Task DisposeAsync_ConcurrentReceiveFailure_DoesNotDeadlockOrLeakCancellation()
    {
        using var timeoutSource = CreateTestTimeout();
        await using var transport = new TestTransport();
        using var loggerFactory = new BlockingErrorLoggerFactory();
        var session = new LlrpSession(transport, loggerFactory: loggerFactory);
        await session.ConnectAsync(timeoutSource.Token);
        Task<ReadOnlyMemory<byte>> transaction = session.TransactAsync(
            CreateFrame(messageType: 1, messageId: 82, payload: []),
            MatchAnyResponse,
            cancellationToken: timeoutSource.Token);
        await transport.ReadSentFrameAsync(timeoutSource.Token);

        transport.QueueReceiveFailure(new IOException("concurrent peer close"));
        await loggerFactory.WaitForErrorAsync(timeoutSource.Token);
        Task dispose = session.DisposeAsync().AsTask();
        loggerFactory.ReleaseError();

        await dispose.WaitAsync(timeoutSource.Token);
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await transaction);
        Assert.True(transport.IsDisposed);
    }

    [Fact]
    public async Task DisposeAsync_DoesNotWaitForBlockedExternalSend()
    {
        using var timeoutSource = CreateTestTimeout();
        await using var transport = new TestTransport();
        var session = new LlrpSession(transport);
        await session.ConnectAsync(timeoutSource.Token);
        transport.BlockSends();
        Task send = session.SendFrameAsync(
            CreateFrame(messageType: 72, messageId: 1, payload: []),
            timeoutSource.Token).AsTask();
        await transport.WaitForBlockedSendAsync(timeoutSource.Token);

        await session.DisposeAsync().AsTask().WaitAsync(timeoutSource.Token);

        Assert.True(transport.IsDisposed);
        transport.ReleaseBlockedSends();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => send);
    }

    [Fact]
    public async Task TransactAsync_RejectsZeroIdentifierAndMismatchedLengthBeforeSending()
    {
        using var timeoutSource = CreateTestTimeout();
        await using var transport = new TestTransport();
        await using var session = new LlrpSession(transport);
        await session.ConnectAsync(timeoutSource.Token);
        byte[] zeroIdentifier = CreateFrame(messageType: 1, messageId: 0, payload: []);
        byte[] invalidLength = CreateFrame(messageType: 1, messageId: 91, payload: []);
        invalidLength[5]++;

        await Assert.ThrowsAsync<ArgumentException>(
            async () => await session.TransactAsync(
                zeroIdentifier,
                MatchAnyResponse,
                cancellationToken: timeoutSource.Token));
        LlrpProtocolException exception = await Assert.ThrowsAsync<LlrpProtocolException>(
            async () => await session.TransactAsync(
                invalidLength,
                MatchAnyResponse,
                cancellationToken: timeoutSource.Token));

        Assert.Equal(LlrpProtocolErrorCode.InvalidMessageLength, exception.ErrorCode);
        Assert.Empty(transport.SentFrames);
    }

    [Fact]
    public async Task SendFrameAsync_SendsZeroIdentifierFrameWithoutRegisteringTransaction()
    {
        using var timeoutSource = CreateTestTimeout();
        await using var transport = new TestTransport();
        await using var session = new LlrpSession(transport);
        await session.ConnectAsync(timeoutSource.Token);
        byte[] frame = CreateFrame(messageType: 72, messageId: 0, payload: []);

        await session.SendFrameAsync(frame, timeoutSource.Token);

        ReadOnlyMemory<byte> sent = await transport.ReadSentFrameAsync(timeoutSource.Token);
        Assert.Equal(frame, sent.ToArray());
    }

    private static CancellationTokenSource CreateTestTimeout()
    {
        return new CancellationTokenSource(TimeSpan.FromSeconds(10));
    }

    private static bool MatchAnyResponse(
        LlrpMessageHeader header,
        ReadOnlyMemory<byte> frame)
    {
        return true;
    }

    private static byte[] CreateFrame(ushort messageType, uint messageId, byte[] payload)
    {
        var frame = new byte[LlrpMessageHeader.EncodedLength + payload.Length];
        new LlrpMessageHeader(
            LlrpProtocolVersion.Version101,
            messageType,
            (uint)frame.Length,
            messageId).Encode(frame);
        payload.CopyTo(frame, LlrpMessageHeader.EncodedLength);
        return frame;
    }

    private sealed class BlockingErrorLoggerFactory : ILoggerFactory, ILogger
    {
        private readonly TaskCompletionSource<bool> _errorEntered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _errorRelease =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void AddProvider(ILoggerProvider provider)
        {
            ArgumentNullException.ThrowIfNull(provider);
        }

        public ILogger CreateLogger(string categoryName)
        {
            return this;
        }

        public void Dispose()
        {
            _errorRelease.TrySetResult(true);
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel != LogLevel.Error)
            {
                return;
            }

            _errorEntered.TrySetResult(true);
            _errorRelease.Task.GetAwaiter().GetResult();
        }

        public Task WaitForErrorAsync(CancellationToken cancellationToken)
        {
            return _errorEntered.Task.WaitAsync(cancellationToken);
        }

        public void ReleaseError()
        {
            _errorRelease.TrySetResult(true);
        }
    }

    private sealed class TestTransport : ILlrpTransport
    {
        private readonly Channel<ReceiveOutcome> _received = Channel.CreateUnbounded<ReceiveOutcome>();
        private readonly Channel<ReadOnlyMemory<byte>> _sent =
            Channel.CreateUnbounded<ReadOnlyMemory<byte>>();
        private int _connected;
        private int _connectCount;
        private int _disposed;
        private Exception? _nextSendFailure;
        private TaskCompletionSource<bool>? _blockedSendEntered;
        private TaskCompletionSource<bool>? _blockedSendRelease;

        public string ConnectionId { get; } = "test-connection";

        public bool IsConnected => Volatile.Read(ref _connected) != 0;

        public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

        public int ConnectCount => Volatile.Read(ref _connectCount);

        public ConcurrentQueue<ReadOnlyMemory<byte>> SentFrames { get; } = new();

        public Exception? NextSendFailure
        {
            get => Volatile.Read(ref _nextSendFailure);
            set => Volatile.Write(ref _nextSendFailure, value);
        }

        public ValueTask ConnectAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            Volatile.Write(ref _connected, 1);
            Interlocked.Increment(ref _connectCount);
            return ValueTask.CompletedTask;
        }

        public ValueTask DisconnectAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Volatile.Write(ref _connected, 0);
            return ValueTask.CompletedTask;
        }

        public async ValueTask SendFrameAsync(
            ReadOnlyMemory<byte> frame,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsConnected)
            {
                throw new InvalidOperationException("The test transport is disconnected.");
            }

            Exception? failure = Interlocked.Exchange(ref _nextSendFailure, null);
            if (failure is not null)
            {
                ExceptionDispatchInfo.Capture(failure).Throw();
            }

            TaskCompletionSource<bool>? blockedSendEntered =
                Volatile.Read(ref _blockedSendEntered);
            TaskCompletionSource<bool>? blockedSendRelease =
                Volatile.Read(ref _blockedSendRelease);
            if (blockedSendEntered is not null && blockedSendRelease is not null)
            {
                blockedSendEntered.TrySetResult(true);
                await blockedSendRelease.Task.ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                ObjectDisposedException.ThrowIf(IsDisposed, this);
                if (!IsConnected)
                {
                    throw new InvalidOperationException("The test transport is disconnected.");
                }
            }

            ReadOnlyMemory<byte> ownedFrame = frame.ToArray();
            SentFrames.Enqueue(ownedFrame);
            _sent.Writer.TryWrite(ownedFrame);
        }

        public async ValueTask<ReadOnlyMemory<byte>> ReceiveFrameAsync(
            CancellationToken cancellationToken = default)
        {
            if (!IsConnected)
            {
                throw new InvalidOperationException("The test transport is disconnected.");
            }

            ReceiveOutcome outcome = await _received.Reader
                .ReadAsync(cancellationToken)
                .ConfigureAwait(false);
            if (outcome.Failure is not null)
            {
                Volatile.Write(ref _connected, 0);
                ExceptionDispatchInfo.Capture(outcome.Failure).Throw();
            }

            return outcome.Frame;
        }

        public ValueTask DisposeAsync()
        {
            Volatile.Write(ref _disposed, 1);
            Volatile.Write(ref _connected, 0);
            _received.Writer.TryComplete();
            _sent.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }

        public void QueueReceivedFrame(ReadOnlyMemory<byte> frame)
        {
            _received.Writer.TryWrite(new ReceiveOutcome(frame.ToArray(), Failure: null));
        }

        public void QueueReceiveFailure(Exception exception)
        {
            _received.Writer.TryWrite(new ReceiveOutcome(default, exception));
        }

        public ValueTask<ReadOnlyMemory<byte>> ReadSentFrameAsync(CancellationToken cancellationToken)
        {
            return _sent.Reader.ReadAsync(cancellationToken);
        }

        public void BlockSends()
        {
            _blockedSendEntered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _blockedSendRelease = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public Task WaitForBlockedSendAsync(CancellationToken cancellationToken)
        {
            TaskCompletionSource<bool> entered = _blockedSendEntered
                ?? throw new InvalidOperationException("Sends are not blocked.");
            return entered.Task.WaitAsync(cancellationToken);
        }

        public void ReleaseBlockedSends()
        {
            _blockedSendRelease?.TrySetResult(true);
        }

        private readonly record struct ReceiveOutcome(
            ReadOnlyMemory<byte> Frame,
            Exception? Failure);
    }
}
