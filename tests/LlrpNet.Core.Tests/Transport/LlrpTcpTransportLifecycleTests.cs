using System.Net;
using System.Net.Sockets;
using LlrpNet.Core.Diagnostics;
using LlrpNet.Core.Protocol;
using LlrpNet.Core.Transport;

namespace LlrpNet.Core.Tests.Transport;

public sealed class LlrpTcpTransportLifecycleTests
{
    [Fact]
    public async Task ReceiveFrameAsync_WhenCancelledAfterPartialHeader_FaultsConnectionAndCanReconnect()
    {
        using var timeoutSource = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        CancellationToken timeoutToken = timeoutSource.Token;
        using var listener = StartListener(out int port);
        await using var transport = CreateTransport(port);
        Task<TcpClient> firstAcceptTask = listener.AcceptTcpClientAsync();
        await transport.ConnectAsync(timeoutToken);
        using TcpClient firstServer = await firstAcceptTask.WaitAsync(timeoutToken);
        using var receiveCancellation = CancellationTokenSource.CreateLinkedTokenSource(timeoutToken);

        Task<ReadOnlyMemory<byte>> receiveTask = transport.ReceiveFrameAsync(receiveCancellation.Token).AsTask();
        await firstServer.GetStream().WriteAsync(new byte[] { 0x04, 0x01, 0x00 }, timeoutToken);
        await Task.Delay(25, timeoutToken);
        receiveCancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => receiveTask);
        Assert.False(transport.IsConnected);

        Task<TcpClient> secondAcceptTask = listener.AcceptTcpClientAsync();
        await transport.ConnectAsync(timeoutToken);
        using TcpClient secondServer = await secondAcceptTask.WaitAsync(timeoutToken);
        byte[] expected = CreateFrame(messageType: 62, messageId: 2, payload: []);
        await secondServer.GetStream().WriteAsync(expected, timeoutToken);

        ReadOnlyMemory<byte> actual = await transport.ReceiveFrameAsync(timeoutToken);

        Assert.True(transport.IsConnected);
        Assert.Equal(expected, actual.ToArray());
    }

    [Fact]
    public async Task ReceiveFrameAsync_WhenAlreadyCancelled_DoesNotFaultConnection()
    {
        using var timeoutSource = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        CancellationToken timeoutToken = timeoutSource.Token;
        using var listener = StartListener(out int port);
        await using var transport = CreateTransport(port);
        Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();
        await transport.ConnectAsync(timeoutToken);
        using TcpClient server = await acceptTask.WaitAsync(timeoutToken);
        using var receiveCancellation = new CancellationTokenSource();
        receiveCancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => transport.ReceiveFrameAsync(receiveCancellation.Token).AsTask());

        Assert.True(transport.IsConnected);
    }

    [Fact]
    public async Task ReceiveFrameAsync_WhenCancelledAfterPartialBody_FaultsConnection()
    {
        using var timeoutSource = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        CancellationToken timeoutToken = timeoutSource.Token;
        using var listener = StartListener(out int port);
        await using var transport = CreateTransport(port);
        Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();
        await transport.ConnectAsync(timeoutToken);
        using TcpClient server = await acceptTask.WaitAsync(timeoutToken);
        using var receiveCancellation = CancellationTokenSource.CreateLinkedTokenSource(timeoutToken);
        byte[] frame = CreateFrame(messageType: 1, messageId: 3, payload: [0x10, 0x20, 0x30, 0x40]);

        Task<ReadOnlyMemory<byte>> receiveTask = transport.ReceiveFrameAsync(receiveCancellation.Token).AsTask();
        await server.GetStream().WriteAsync(
            frame.AsMemory(0, LlrpMessageHeader.EncodedLength + 2),
            timeoutToken);
        await Task.Delay(25, timeoutToken);
        receiveCancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => receiveTask);
        Assert.False(transport.IsConnected);
    }

    [Fact]
    public async Task ReceiveFrameAsync_WhenPartialHeaderStalls_TimesOutAndFaultsConnection()
    {
        using var timeoutSource = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        CancellationToken cancellationToken = timeoutSource.Token;
        using var listener = StartListener(out int port);
        await using var transport = CreateTransport(
            port,
            frameAssemblyTimeout: TimeSpan.FromMilliseconds(100));
        Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();
        await transport.ConnectAsync(cancellationToken);
        using TcpClient server = await acceptTask.WaitAsync(cancellationToken);

        Task<ReadOnlyMemory<byte>> receive = transport.ReceiveFrameAsync(cancellationToken).AsTask();
        await server.GetStream().WriteAsync(new byte[] { 0x04, 0x01, 0x00 }, cancellationToken);

        await Assert.ThrowsAsync<TimeoutException>(() => receive.WaitAsync(cancellationToken));
        Assert.False(transport.IsConnected);
    }

    [Fact]
    public async Task ReceiveFrameAsync_WhenPartialBodyStalls_TimesOutAndFaultsConnection()
    {
        using var timeoutSource = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        CancellationToken cancellationToken = timeoutSource.Token;
        using var listener = StartListener(out int port);
        await using var transport = CreateTransport(
            port,
            frameAssemblyTimeout: TimeSpan.FromMilliseconds(100));
        Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();
        await transport.ConnectAsync(cancellationToken);
        using TcpClient server = await acceptTask.WaitAsync(cancellationToken);
        byte[] frame = CreateFrame(messageType: 1, messageId: 5, payload: [0x10, 0x20, 0x30, 0x40]);

        Task<ReadOnlyMemory<byte>> receive = transport.ReceiveFrameAsync(cancellationToken).AsTask();
        await server.GetStream().WriteAsync(
            frame.AsMemory(0, LlrpMessageHeader.EncodedLength + 2),
            cancellationToken);

        await Assert.ThrowsAsync<TimeoutException>(() => receive.WaitAsync(cancellationToken));
        Assert.False(transport.IsConnected);
    }

    [Fact]
    public async Task ReceiveFrameAsync_WhenFrameExceedsLimit_FaultsConnection()
    {
        using var timeoutSource = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        CancellationToken cancellationToken = timeoutSource.Token;
        using var listener = StartListener(out int port);
        await using var transport = CreateTransport(port, maximumFrameLength: LlrpMessageHeader.EncodedLength);
        Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();
        await transport.ConnectAsync(cancellationToken);
        using TcpClient server = await acceptTask.WaitAsync(cancellationToken);
        byte[] frame = CreateFrame(messageType: 1, messageId: 4, payload: [0x00]);
        await server.GetStream().WriteAsync(frame, cancellationToken);

        LlrpProtocolException exception = await Assert.ThrowsAsync<LlrpProtocolException>(
            () => transport.ReceiveFrameAsync(cancellationToken).AsTask());

        Assert.Equal(LlrpProtocolErrorCode.FrameTooLarge, exception.ErrorCode);
        Assert.False(transport.IsConnected);
    }

    [Fact]
    public async Task ReceiveFrameAsync_WhenPeerClosesDuringHeader_FaultsConnection()
    {
        using var timeoutSource = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        CancellationToken cancellationToken = timeoutSource.Token;
        using var listener = StartListener(out int port);
        await using var transport = CreateTransport(port);
        Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();
        await transport.ConnectAsync(cancellationToken);
        using TcpClient server = await acceptTask.WaitAsync(cancellationToken);
        await server.GetStream().WriteAsync(new byte[] { 0x04, 0x01, 0x00 }, cancellationToken);
        server.Client.Shutdown(SocketShutdown.Send);

        await Assert.ThrowsAsync<EndOfStreamException>(
            () => transport.ReceiveFrameAsync(cancellationToken).AsTask());

        Assert.False(transport.IsConnected);
    }

    [Fact]
    public async Task QueuedSend_FromOldGeneration_DoesNotCrossIntoReconnectedTransport()
    {
        using var timeoutSource = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        CancellationToken cancellationToken = timeoutSource.Token;
        using var listener = StartListener(out int port);
        var observer = new BlockingFirstTransmitObserver();
        await using var transport = CreateTransport(port, observer);
        Task<TcpClient> firstAcceptTask = listener.AcceptTcpClientAsync();
        await transport.ConnectAsync(cancellationToken);
        using TcpClient firstServer = await firstAcceptTask.WaitAsync(cancellationToken);
        byte[] firstFrame = CreateFrame(messageType: 62, messageId: 1, payload: []);
        byte[] secondFrame = CreateFrame(messageType: 62, messageId: 2, payload: []);

        Task firstSend = transport.SendFrameAsync(firstFrame, cancellationToken).AsTask();
        await observer.FirstObservationEntered.Task.WaitAsync(cancellationToken);
        var firstActual = new byte[firstFrame.Length];
        await firstServer.GetStream().ReadExactlyAsync(firstActual, cancellationToken);
        Task secondSend = transport.SendFrameAsync(secondFrame, cancellationToken).AsTask();
        Assert.False(secondSend.IsCompleted);

        await transport.DisconnectAsync(cancellationToken);
        Task<TcpClient> secondAcceptTask = listener.AcceptTcpClientAsync();
        await transport.ConnectAsync(cancellationToken);
        using TcpClient secondServer = await secondAcceptTask.WaitAsync(cancellationToken);

        observer.ReleaseFirstObservation();
        await firstSend.WaitAsync(cancellationToken);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => secondSend.WaitAsync(cancellationToken));

        Assert.Equal(firstFrame, firstActual);
        Assert.Equal([1U], observer.ObservedMessageIds);
        Assert.Single(observer.ObservedConnectionGenerations);
        Assert.False(observer.FirstObservationTokenCanBeCanceled);
        Assert.True(transport.IsConnected);
    }

    [Fact]
    public async Task DisposeAsync_WithActiveAndQueuedSends_DoesNotStrandOrMaskCommittedSend()
    {
        using var timeoutSource = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        CancellationToken cancellationToken = timeoutSource.Token;
        using var listener = StartListener(out int port);
        var observer = new BlockingFirstTransmitObserver();
        await using var transport = CreateTransport(port, observer);
        Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();
        await transport.ConnectAsync(cancellationToken);
        using TcpClient server = await acceptTask.WaitAsync(cancellationToken);
        byte[] firstFrame = CreateFrame(messageType: 62, messageId: 1, payload: []);
        byte[] secondFrame = CreateFrame(messageType: 62, messageId: 2, payload: []);
        Task firstSend = transport.SendFrameAsync(firstFrame, cancellationToken).AsTask();
        await observer.FirstObservationEntered.Task.WaitAsync(cancellationToken);
        Task secondSend = transport.SendFrameAsync(secondFrame, cancellationToken).AsTask();

        try
        {
            await transport.DisposeAsync().AsTask().WaitAsync(cancellationToken);
        }
        finally
        {
            observer.ReleaseFirstObservation();
        }

        await firstSend.WaitAsync(cancellationToken);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => secondSend.WaitAsync(cancellationToken));
        Assert.False(observer.FirstObservationTokenCanBeCanceled);
        Assert.False(transport.IsConnected);
    }

    [Fact]
    public async Task DisposeAsync_WithBlockedReceive_CancelsReadWithoutHanging()
    {
        using var timeoutSource = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        CancellationToken cancellationToken = timeoutSource.Token;
        using var listener = StartListener(out int port);
        await using var transport = CreateTransport(port);
        Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();
        await transport.ConnectAsync(cancellationToken);
        using TcpClient server = await acceptTask.WaitAsync(cancellationToken);
        Task<ReadOnlyMemory<byte>> receiveTask = transport.ReceiveFrameAsync(cancellationToken).AsTask();

        await transport.DisposeAsync().AsTask().WaitAsync(cancellationToken);
        Exception? receiveException = await Record.ExceptionAsync(
            () => receiveTask.WaitAsync(cancellationToken));

        Assert.NotNull(receiveException);
        Assert.False(transport.IsConnected);
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => transport.ConnectAsync(cancellationToken).AsTask());
    }

    [Fact]
    public async Task DisposeAsync_RacingConnect_DoesNotHang()
    {
        using var timeoutSource = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        CancellationToken cancellationToken = timeoutSource.Token;
        using var listener = StartListener(out int port);
        await using var transport = CreateTransport(port);

        Task connectTask = transport.ConnectAsync(cancellationToken).AsTask();
        Task disposeTask = transport.DisposeAsync().AsTask();

        await disposeTask.WaitAsync(cancellationToken);
        Exception? connectException = await Record.ExceptionAsync(
            () => connectTask.WaitAsync(cancellationToken));

        Assert.True(connectException is null or ObjectDisposedException);
        Assert.False(transport.IsConnected);
    }

    private static TcpListener StartListener(out int port)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        port = ((IPEndPoint)listener.LocalEndpoint).Port;
        return listener;
    }

    private static LlrpTcpTransport CreateTransport(
        int port,
        ILlrpFrameObserver? observer = null,
        uint maximumFrameLength = 1024,
        TimeSpan? frameAssemblyTimeout = null)
    {
        return new LlrpTcpTransport(
            new LlrpTcpTransportOptions
            {
                Host = IPAddress.Loopback.ToString(),
                Port = port,
                ConnectTimeout = TimeSpan.FromSeconds(5),
                FrameAssemblyTimeout = frameAssemblyTimeout ?? TimeSpan.FromSeconds(5),
                MaximumFrameLength = maximumFrameLength,
            },
            frameObserver: observer);
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

    private sealed class BlockingFirstTransmitObserver : ILlrpFrameObserver
    {
        private readonly object _sync = new();
        private readonly List<long> _observedConnectionGenerations = [];
        private readonly List<uint> _observedMessageIds = [];
        private readonly TaskCompletionSource _releaseFirst =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource FirstObservationEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool FirstObservationTokenCanBeCanceled { get; private set; }

        public IReadOnlyList<uint> ObservedMessageIds
        {
            get
            {
                lock (_sync)
                {
                    return [.. _observedMessageIds];
                }
            }
        }

        public IReadOnlyList<long> ObservedConnectionGenerations
        {
            get
            {
                lock (_sync)
                {
                    return [.. _observedConnectionGenerations];
                }
            }
        }

        public async ValueTask ObserveAsync(
            LlrpFrameObservation observation,
            CancellationToken cancellationToken = default)
        {
            LlrpMessageHeader header = LlrpMessageHeader.Decode(observation.FrameBytes.Span);
            lock (_sync)
            {
                _observedMessageIds.Add(header.MessageId);
                _observedConnectionGenerations.Add(observation.ConnectionGeneration);
            }

            if (header.MessageId == 1)
            {
                FirstObservationTokenCanBeCanceled = cancellationToken.CanBeCanceled;
                FirstObservationEntered.TrySetResult();
                await _releaseFirst.Task.ConfigureAwait(false);
            }
        }

        public void ReleaseFirstObservation()
        {
            _releaseFirst.TrySetResult();
        }
    }
}
