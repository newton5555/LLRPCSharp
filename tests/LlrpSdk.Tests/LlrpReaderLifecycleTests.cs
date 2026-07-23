using System.Collections.Concurrent;
using LlrpNet.Core.Protocol;
using LlrpNet.Core.Session;
using LlrpSdk.Tests.Support;

namespace LlrpSdk.Tests;

public sealed class LlrpReaderLifecycleTests
{
    [Fact]
    public async Task ConcurrentConnectAndDisconnect_AreIdempotentAndPublishState()
    {
        var transport = new ScriptedLlrpTransport();
        var reader = CreateReader(transport);
        var transitions = new ConcurrentQueue<ReaderConnectionState>();
        reader.ConnectionChanged += (_, args) => transitions.Enqueue(args.CurrentState);

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => reader.ConnectAsync()));

        Assert.True(reader.IsConnected);
        Assert.Equal(ReaderConnectionState.Ready, reader.ConnectionState);
        Assert.Equal(ReaderOperationState.Idle, reader.OperationState);
        Assert.Equal(LlrpProtocolVersion.Version101, reader.NegotiatedVersion);
        Assert.NotNull(reader.Identity);
        Assert.NotNull(reader.Capabilities);
        Assert.Equal(1, transport.ConnectCallCount);

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => reader.DisconnectAsync()));

        Assert.False(reader.IsConnected);
        Assert.Equal(ReaderConnectionState.Disconnected, reader.ConnectionState);
        Assert.Null(reader.Identity);
        Assert.Null(reader.Capabilities);
        Assert.Equal(1, transport.DisconnectCount);
        Assert.Equal(
            [
                ReaderConnectionState.Connecting,
                ReaderConnectionState.Negotiating,
                ReaderConnectionState.Initializing,
                ReaderConnectionState.Ready,
                ReaderConnectionState.Disconnecting,
                ReaderConnectionState.Disconnected,
            ],
            transitions.ToArray());

        await reader.DisposeAsync();
        await reader.DisposeAsync();
        Assert.Equal(1, transport.DisposeCount);
    }

    [Fact]
    public async Task ReceiveFailure_TransitionsToFaultedAndCanReconnect()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var transport = new ScriptedLlrpTransport();
        await using var reader = CreateReader(transport);
        var transitions = new System.Collections.Concurrent.ConcurrentQueue<ReaderConnectionState>();
        var faulted = new TaskCompletionSource<ReaderConnectionChangedEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var error = new TaskCompletionSource<ReaderErrorEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        reader.ConnectionChanged += (_, args) =>
        {
            transitions.Enqueue(args.CurrentState);
            if (args.CurrentState == ReaderConnectionState.Faulted)
            {
                faulted.TrySetResult(args);
            }
        };
        reader.ErrorOccurred += (_, args) => error.TrySetResult(args);

        await reader.ConnectAsync(timeout.Token);
        transport.EnqueueFailure(new IOException("simulated receive failure"));

        ReaderConnectionChangedEventArgs transition = await faulted.Task.WaitAsync(timeout.Token);
        ReaderErrorEventArgs observedError = await error.Task.WaitAsync(timeout.Token);
        Assert.Equal(ReaderConnectionState.Ready, transition.PreviousState);
        Assert.NotNull(transition.Error);
        Assert.Equal(ReaderConnectionState.Faulted, observedError.ConnectionState);
        LlrpSessionDisconnectedException sessionError =
            Assert.IsType<LlrpSessionDisconnectedException>(observedError.Error);
        Assert.IsType<IOException>(sessionError.InnerException);
        Assert.False(reader.IsConnected);
        Assert.Null(reader.Identity);
        Assert.Null(reader.Capabilities);

        await reader.ReconnectAsync(timeout.Token);
        Assert.True(reader.IsConnected);
        Assert.Equal(2, transport.ConnectCallCount);
        Assert.Contains(ReaderConnectionState.Reconnecting, transitions);
    }

    [Fact]
    public async Task DisposeDuringConnect_CompletesWithoutLeakingTheTransport()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var transport = new ScriptedLlrpTransport
        {
            ConnectGate = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously),
        };
        var reader = CreateReader(transport);

        Task connectTask = reader.ConnectAsync(timeout.Token);
        await transport.ConnectEntered.Task.WaitAsync(timeout.Token);
        Task disposeTask = reader.DisposeAsync().AsTask();
        transport.ConnectGate.SetResult(true);

        await Assert.ThrowsAsync<ObjectDisposedException>(() => connectTask);
        await disposeTask.WaitAsync(timeout.Token);
        await reader.DisposeAsync();

        Assert.Equal(ReaderConnectionState.Disconnected, reader.ConnectionState);
        Assert.False(reader.IsConnected);
        Assert.Equal(1, transport.DisposeCount);
    }

    private static LlrpReader CreateReader(ScriptedLlrpTransport transport)
    {
        LlrpReaderOptions options = new LlrpReaderOptionsBuilder("scripted.local")
            .WithTransportFactory(_ => transport)
            .Build();
        return new LlrpReader(options);
    }
}
