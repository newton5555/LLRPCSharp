using System.Net;
using System.Net.Sockets;
using LlrpNet.Core.Diagnostics;
using LlrpNet.Core.Protocol;
using LlrpNet.Core.Transport;

namespace LlrpNet.Core.Tests.Transport;

public sealed class LlrpTcpTransportTests
{
    [Fact]
    public async Task SendFrameAsync_SendsExactBytesAndObservesTransmitFrame()
    {
        using var timeoutSource = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        CancellationToken cancellationToken = timeoutSource.Token;
        using var listener = StartListener(out int port);
        var observer = new RecordingObserver();
        await using var transport = CreateTransport(port, observer);
        Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();

        await transport.ConnectAsync(cancellationToken);
        using TcpClient server = await acceptTask;
        byte[] expected = CreateFrame(messageType: 62, messageId: 7, payload: []);

        await transport.SendFrameAsync(expected, cancellationToken);
        var actual = new byte[expected.Length];
        await server.GetStream().ReadExactlyAsync(actual, cancellationToken);

        Assert.Equal(expected, actual);
        LlrpFrameObservation observation = Assert.Single(observer.Observations);
        Assert.Equal(LlrpFrameDirection.Transmit, observation.Direction);
        Assert.Equal(transport.ConnectionId, observation.ConnectionId);
        Assert.True(observation.ConnectionGeneration > 0);
        Assert.Equal(expected, observation.FrameBytes.ToArray());
    }

    [Fact]
    public async Task ReceiveFrameAsync_ReassemblesFragmentedTcpReadsAndObservesReceiveFrame()
    {
        using var timeoutSource = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        CancellationToken cancellationToken = timeoutSource.Token;
        using var listener = StartListener(out int port);
        var observer = new RecordingObserver();
        await using var transport = CreateTransport(port, observer);
        Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();
        await transport.ConnectAsync(cancellationToken);
        using TcpClient server = await acceptTask;
        byte[] expected = CreateFrame(messageType: 1, messageId: 0x11223344, payload: [0x00]);

        ValueTask<ReadOnlyMemory<byte>> receiveTask = transport.ReceiveFrameAsync(cancellationToken);
        await server.GetStream().WriteAsync(expected.AsMemory(0, 3), cancellationToken);
        await Task.Delay(10, cancellationToken);
        await server.GetStream().WriteAsync(expected.AsMemory(3), cancellationToken);

        ReadOnlyMemory<byte> actual = await receiveTask;
        Assert.Equal(expected, actual.ToArray());
        LlrpFrameObservation observation = Assert.Single(observer.Observations);
        Assert.Equal(LlrpFrameDirection.Receive, observation.Direction);
        Assert.True(observation.ConnectionGeneration > 0);
        Assert.Equal(expected, observation.FrameBytes.ToArray());
    }

    [Fact]
    public async Task ReceiveFrameAsync_ReturnsCoalescedFramesOneAtATime()
    {
        using var timeoutSource = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        CancellationToken cancellationToken = timeoutSource.Token;
        using var listener = StartListener(out int port);
        await using var transport = CreateTransport(port);
        Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();
        await transport.ConnectAsync(cancellationToken);
        using TcpClient server = await acceptTask;
        byte[] first = CreateFrame(messageType: 62, messageId: 1, payload: []);
        byte[] second = CreateFrame(messageType: 72, messageId: 1, payload: []);
        byte[] combined = [.. first, .. second];
        await server.GetStream().WriteAsync(combined, cancellationToken);

        ReadOnlyMemory<byte> firstActual = await transport.ReceiveFrameAsync(cancellationToken);
        ReadOnlyMemory<byte> secondActual = await transport.ReceiveFrameAsync(cancellationToken);

        Assert.Equal(first, firstActual.ToArray());
        Assert.Equal(second, secondActual.ToArray());
    }

    [Fact]
    public async Task SendFrameAsync_WhenObserverFails_DoesNotReportNetworkSendAsFailed()
    {
        using var timeoutSource = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        CancellationToken cancellationToken = timeoutSource.Token;
        using var listener = StartListener(out int port);
        await using var transport = CreateTransport(port, new ThrowingObserver());
        Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();
        await transport.ConnectAsync(cancellationToken);
        using TcpClient server = await acceptTask;
        byte[] expected = CreateFrame(messageType: 62, messageId: 9, payload: []);

        await transport.SendFrameAsync(expected, cancellationToken);
        var actual = new byte[expected.Length];
        await server.GetStream().ReadExactlyAsync(actual, cancellationToken);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task ReceiveFrameAsync_WhenObserverFails_DoesNotReportNetworkReadAsFailed()
    {
        using var timeoutSource = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        CancellationToken cancellationToken = timeoutSource.Token;
        using var listener = StartListener(out int port);
        await using var transport = CreateTransport(port, new ThrowingObserver());
        Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();
        await transport.ConnectAsync(cancellationToken);
        using TcpClient server = await acceptTask;
        byte[] expected = CreateFrame(messageType: 1, messageId: 10, payload: [0x00]);
        await server.GetStream().WriteAsync(expected, cancellationToken);

        ReadOnlyMemory<byte> actual = await transport.ReceiveFrameAsync(cancellationToken);

        Assert.Equal(expected, actual.ToArray());
        Assert.True(transport.IsConnected);
    }

    [Fact]
    public async Task ReceiveFrameAsync_WhenPeerClosesMidFrame_ThrowsEndOfStreamException()
    {
        using var timeoutSource = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        CancellationToken cancellationToken = timeoutSource.Token;
        using var listener = StartListener(out int port);
        await using var transport = CreateTransport(port);
        Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();
        await transport.ConnectAsync(cancellationToken);
        using TcpClient server = await acceptTask;
        byte[] frame = CreateFrame(messageType: 1, messageId: 1, payload: [0x00]);
        await server.GetStream().WriteAsync(frame.AsMemory(0, 5), cancellationToken);
        server.Client.Shutdown(SocketShutdown.Send);

        await Assert.ThrowsAsync<EndOfStreamException>(
            async () => await transport.ReceiveFrameAsync(cancellationToken));
        Assert.False(transport.IsConnected);
    }

    [Fact]
    public async Task SendFrameAsync_RejectsLengthThatDoesNotMatchSuppliedBytes()
    {
        await using var transport = new LlrpTcpTransport(new LlrpTcpTransportOptions
        {
            Host = IPAddress.Loopback.ToString(),
        });
        byte[] frame = CreateFrame(messageType: 62, messageId: 1, payload: []);
        frame[5] = 11;

        LlrpProtocolException exception = await Assert.ThrowsAsync<LlrpProtocolException>(
            async () => await transport.SendFrameAsync(frame));

        Assert.Equal(LlrpProtocolErrorCode.InvalidMessageLength, exception.ErrorCode);
    }

    private static TcpListener StartListener(out int port)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        port = ((IPEndPoint)listener.LocalEndpoint).Port;
        return listener;
    }

    private static LlrpTcpTransport CreateTransport(int port, ILlrpFrameObserver? observer = null)
    {
        return new LlrpTcpTransport(
            new LlrpTcpTransportOptions
            {
                Host = IPAddress.Loopback.ToString(),
                Port = port,
                ConnectTimeout = TimeSpan.FromSeconds(5),
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

    private sealed class RecordingObserver : ILlrpFrameObserver
    {
        public List<LlrpFrameObservation> Observations { get; } = [];

        public ValueTask ObserveAsync(
            LlrpFrameObservation observation,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Observations.Add(observation with { FrameBytes = observation.FrameBytes.ToArray() });
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingObserver : ILlrpFrameObserver
    {
        public ValueTask ObserveAsync(
            LlrpFrameObservation observation,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("observer failure");
        }
    }
}
