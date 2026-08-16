using System.Net;
using System.Net.Sockets;
using LlrpNet.Core.Diagnostics;
using LlrpNet.Core.Protocol;
using LlrpNet.Core.Session;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LlrpNet.Core.Transport;

/// <summary>
/// Adapts an already accepted <see cref="TcpClient"/> to the shared LLRP transport contract.
/// </summary>
/// <remarks>
/// This is the server-side counterpart to <see cref="LlrpTcpTransport"/>. It intentionally implements
/// <see cref="ILlrpTransport"/> so an accepted connection can be owned by the existing
/// <see cref="LlrpNet.Core.Session.LlrpSession"/> without introducing a second framing or session model.
/// </remarks>
public sealed class LlrpAcceptedTcpTransport : ILlrpTransport
{
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly LlrpAcceptedTcpTransportOptions _options;
    private readonly ILogger<LlrpAcceptedTcpTransport> _logger;
    private readonly ILlrpFrameObserver _frameObserver;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly SemaphoreSlim _receiveLock = new(1, 1);
    private int _disposed;
    private int _connected = 1;

    /// <summary>Initializes a transport over an accepted TCP client.</summary>
    public LlrpAcceptedTcpTransport(
        TcpClient client,
        LlrpAcceptedTcpTransportOptions? options = null,
        ILoggerFactory? loggerFactory = null,
        ILlrpFrameObserver? frameObserver = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        _options = options ?? new LlrpAcceptedTcpTransportOptions();
        _options.Validate();
        _client = client;
        _client.NoDelay = true;
        _stream = client.GetStream();
        _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<LlrpAcceptedTcpTransport>();
        _frameObserver = frameObserver ?? NullLlrpFrameObserver.Instance;
        ConnectionId = Guid.NewGuid().ToString("N");
    }

    /// <inheritdoc />
    public string ConnectionId { get; }

    /// <summary>Gets the remote endpoint captured when the connection was accepted.</summary>
    public EndPoint? RemoteEndPoint => _client.Client.RemoteEndPoint;

    /// <inheritdoc />
    public bool IsConnected => Volatile.Read(ref _connected) != 0 && Volatile.Read(ref _disposed) == 0;

    /// <inheritdoc />
    public ValueTask ConnectAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask DisconnectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Close();
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public async ValueTask SendFrameAsync(
        ReadOnlyMemory<byte> frame,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisconnected();
        LlrpMessageHeader header = ValidateCompleteFrame(frame);
        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisconnected();
            await _stream.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
            await ObserveAsync(LlrpFrameDirection.Transmit, frame, cancellationToken).ConfigureAwait(false);
            LogFrame(LlrpFrameDirection.Transmit, header, frame.Span);
        }
        catch
        {
            Close();
            throw;
        }
        finally
        {
            _sendLock.Release();
        }
    }

    /// <summary>
    /// Sends an intentionally incomplete or otherwise raw wire fragment.
    /// This is reserved for transport-fault simulation; normal protocol traffic must use
    /// <see cref="SendFrameAsync"/> so the frame is validated and observed.
    /// </summary>
    public async ValueTask SendRawFrameAsync(
        ReadOnlyMemory<byte> frame,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisconnected();
        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisconnected();
            await _stream.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            Close();
            throw;
        }
        finally
        {
            _sendLock.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask<ReadOnlyMemory<byte>> ReceiveFrameAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisconnected();
        await _receiveLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisconnected();
            using CancellationTokenSource operationSource = CreateOperationTimeout(cancellationToken);
            byte[] headerBytes = GC.AllocateUninitializedArray<byte>(LlrpMessageHeader.EncodedLength);
            await ReadExactlyAsync(_stream, headerBytes, operationSource.Token).ConfigureAwait(false);
            LlrpMessageHeader header = LlrpMessageHeader.Decode(headerBytes);
            if (header.MessageLength > _options.MaximumFrameLength)
            {
                throw new LlrpProtocolException(
                    LlrpProtocolErrorCode.FrameTooLarge,
                    $"The encoded frame length {header.MessageLength} exceeds the configured limit {_options.MaximumFrameLength}.");
            }

            byte[] frame = GC.AllocateUninitializedArray<byte>(checked((int)header.MessageLength));
            headerBytes.CopyTo(frame, 0);
            if (frame.Length > headerBytes.Length)
            {
                using CancellationTokenSource assemblySource = CreateAssemblyTimeout(operationSource.Token);
                await ReadExactlyAsync(
                    _stream,
                    frame.AsMemory(headerBytes.Length),
                    assemblySource.Token).ConfigureAwait(false);
            }

            await ObserveAsync(LlrpFrameDirection.Receive, frame, cancellationToken).ConfigureAwait(false);
            LogFrame(LlrpFrameDirection.Receive, header, frame);
            return frame;
        }
        catch
        {
            Close();
            throw;
        }
        finally
        {
            _receiveLock.Release();
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            Close();
            _stream.Dispose();
            _client.Dispose();
        }

        return ValueTask.CompletedTask;
    }

    private CancellationTokenSource CreateOperationTimeout(CancellationToken cancellationToken)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (_options.IdleTimeout != Timeout.InfiniteTimeSpan)
        {
            source.CancelAfter(_options.IdleTimeout);
        }

        return source;
    }

    private CancellationTokenSource CreateAssemblyTimeout(CancellationToken cancellationToken)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (_options.FrameAssemblyTimeout != Timeout.InfiniteTimeSpan)
        {
            source.CancelAfter(_options.FrameAssemblyTimeout);
        }

        return source;
    }

    private async ValueTask ObserveAsync(
        LlrpFrameDirection direction,
        ReadOnlyMemory<byte> frame,
        CancellationToken cancellationToken)
    {
        try
        {
            await _frameObserver.ObserveAsync(
                new LlrpFrameObservation(direction, DateTimeOffset.UtcNow, ConnectionId, frame)
                {
                    ConnectionGeneration = 1,
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(
                exception,
                "LLRP frame observer failed for {Direction} frame on accepted transport {ConnectionId}",
                direction,
                ConnectionId);
        }
    }

    private void LogFrame(LlrpFrameDirection direction, LlrpMessageHeader header, ReadOnlySpan<byte> frame)
    {
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "{Direction} LLRP message {MessageType} id {MessageId}, version {Version}, length {FrameLength}, accepted transport {ConnectionId}",
                direction,
                header.MessageType,
                header.MessageId,
                header.Version,
                frame.Length,
                ConnectionId);
        }

        if (_options.LogFrameHex && _logger.IsEnabled(LogLevel.Trace))
        {
            _logger.LogTrace(
                "{Direction} LLRP frame on accepted transport {ConnectionId}: {FrameHex}",
                direction,
                ConnectionId,
                Convert.ToHexString(frame));
        }
    }

    private static LlrpMessageHeader ValidateCompleteFrame(ReadOnlyMemory<byte> frame)
    {
        LlrpMessageHeader header = LlrpMessageHeader.Decode(frame.Span);
        if (header.MessageLength != frame.Length)
        {
            throw new LlrpProtocolException(
                LlrpProtocolErrorCode.InvalidMessageLength,
                $"The encoded message length {header.MessageLength} does not match the supplied frame length {frame.Length}.");
        }

        return header;
    }

    private static async ValueTask ReadExactlyAsync(
        NetworkStream stream,
        Memory<byte> destination,
        CancellationToken cancellationToken)
    {
        int offset = 0;
        while (offset < destination.Length)
        {
            int bytesRead = await stream.ReadAsync(destination[offset..], cancellationToken).ConfigureAwait(false);
            if (bytesRead == 0)
            {
                throw new EndOfStreamException(
                    $"The LLRP TCP connection closed with {destination.Length - offset} expected octet(s) remaining.");
            }

            offset += bytesRead;
        }
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(LlrpAcceptedTcpTransport));
        }
    }

    private void ThrowIfDisconnected()
    {
        ThrowIfDisposed();
        if (Volatile.Read(ref _connected) == 0)
        {
            throw new LlrpSessionDisconnectedException(
                ConnectionId,
                $"LLRP accepted transport {ConnectionId} is disconnected.");
        }
    }

    private void Close()
    {
        if (Interlocked.Exchange(ref _connected, 0) == 0)
        {
            return;
        }

        try
        {
            _client.Close();
        }
        catch (SocketException)
        {
            // The connection is already being closed; the original failure is more useful to the caller.
        }
    }
}
