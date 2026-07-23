using System.Collections.Concurrent;
using System.Threading.Channels;
using LlrpNet.Core.Protocol;
using LlrpNet.Core.Transport;
using LlrpNet.Protocol.Messages.V1_0_1;

namespace LlrpSdk.Tests.Support;

internal sealed class ScriptedLlrpTransport : ILlrpTransport
{
    private readonly Channel<ReceiveItem> _incoming = Channel.CreateUnbounded<ReceiveItem>();
    private readonly Channel<byte[]> _sentFrames = Channel.CreateUnbounded<byte[]>();
    private readonly ConcurrentQueue<byte[]> _sentFrameSnapshot = new();
    private int _connectCallCount;
    private int _connected;
    private int _disconnectCount;
    private int _disposeCount;

    public string ConnectionId { get; } = "scripted-connection";

    public bool IsConnected => Volatile.Read(ref _connected) != 0;

    public int ConnectCallCount => Volatile.Read(ref _connectCallCount);

    public int DisconnectCount => Volatile.Read(ref _disconnectCount);

    public int DisposeCount => Volatile.Read(ref _disposeCount);

    public TaskCompletionSource<bool> ConnectEntered { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource<bool>? ConnectGate { get; set; }

    public Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask>? OnSendAsync { get; set; }

    public bool AutoRespondToCapabilities { get; set; } = true;

    public Func<uint, byte[]?>? CapabilityResponseFactory { get; set; }

    public IReadOnlyCollection<byte[]> SentFrames => _sentFrameSnapshot.ToArray();

    public async ValueTask ConnectAsync(CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _connectCallCount);
        ConnectEntered.TrySetResult(true);

        TaskCompletionSource<bool>? gate = ConnectGate;
        if (gate is not null)
        {
            await gate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        Volatile.Write(ref _connected, 1);
    }

    public ValueTask DisconnectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.Exchange(ref _connected, 0) != 0)
        {
            Interlocked.Increment(ref _disconnectCount);
        }

        return ValueTask.CompletedTask;
    }

    public async ValueTask SendFrameAsync(
        ReadOnlyMemory<byte> frame,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsConnected)
        {
            throw new InvalidOperationException("The scripted transport is disconnected.");
        }

        byte[] copy = frame.ToArray();
        _sentFrameSnapshot.Enqueue(copy);
        await _sentFrames.Writer.WriteAsync(copy, cancellationToken).ConfigureAwait(false);

        LlrpMessageHeader header = LlrpMessageHeader.Decode(copy);
        if (AutoRespondToCapabilities && header.MessageType == GetReaderCapabilities.MessageType)
        {
            byte[]? response = CapabilityResponseFactory is null
                ? LlrpTestFrames.CapabilitiesResponse(header.MessageId)
                : CapabilityResponseFactory(header.MessageId);
            if (response is not null)
            {
                EnqueueFrame(response);
            }
        }

        Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask>? handler = OnSendAsync;
        if (handler is not null)
        {
            await handler(copy, cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask<ReadOnlyMemory<byte>> ReceiveFrameAsync(
        CancellationToken cancellationToken = default)
    {
        ReceiveItem item = await _incoming.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (item.Error is Exception error)
        {
            Volatile.Write(ref _connected, 0);
            throw error;
        }

        return item.Frame;
    }

    public void EnqueueFrame(ReadOnlyMemory<byte> frame)
    {
        if (!_incoming.Writer.TryWrite(new ReceiveItem(frame.ToArray(), Error: null)))
        {
            throw new InvalidOperationException("The scripted receive queue is closed.");
        }
    }

    public void EnqueueFailure(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        if (!_incoming.Writer.TryWrite(new ReceiveItem(ReadOnlyMemory<byte>.Empty, error)))
        {
            throw new InvalidOperationException("The scripted receive queue is closed.");
        }
    }

    public async Task<byte[]> ReadSentFrameAsync(CancellationToken cancellationToken = default)
    {
        return await _sentFrames.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<byte[]> ReadSentFrameAsync(
        ushort messageType,
        CancellationToken cancellationToken = default)
    {
        while (true)
        {
            byte[] frame = await ReadSentFrameAsync(cancellationToken).ConfigureAwait(false);
            if (LlrpMessageHeader.Decode(frame).MessageType == messageType)
            {
                return frame;
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeCount, 1) == 0)
        {
            if (Interlocked.Exchange(ref _connected, 0) != 0)
            {
                Interlocked.Increment(ref _disconnectCount);
            }

            _incoming.Writer.TryComplete();
            _sentFrames.Writer.TryComplete();
        }

        return ValueTask.CompletedTask;
    }

    private sealed record ReceiveItem(ReadOnlyMemory<byte> Frame, Exception? Error);
}
