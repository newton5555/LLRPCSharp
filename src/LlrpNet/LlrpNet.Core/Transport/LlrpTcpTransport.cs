using System.Net.Sockets;
using LlrpNet.Core.Diagnostics;
using LlrpNet.Core.Protocol;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LlrpNet.Core.Transport;

/// <summary>
/// Implements framed LLRP communication over a TCP client connection.
/// </summary>
public sealed class LlrpTcpTransport : ILlrpTransport
{
    private readonly LlrpTcpTransportOptions _options;
    private readonly ILogger<LlrpTcpTransport> _logger;
    private readonly ILlrpFrameObserver _frameObserver;
    private readonly object _connectionSync = new();
    private readonly CancellationTokenSource _lifetimeSource = new();
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private ConnectionState? _connection;
    private long _nextConnectionGeneration;
    private int _disposed;
    private int _receiveInProgress;

    /// <summary>
    /// Initializes a new instance of the <see cref="LlrpTcpTransport"/> class.
    /// </summary>
    /// <param name="options">The endpoint and diagnostic settings.</param>
    /// <param name="loggerFactory">The optional application logging factory.</param>
    /// <param name="frameObserver">The optional best-effort observer for exact complete wire frames.</param>
    public LlrpTcpTransport(
        LlrpTcpTransportOptions options,
        ILoggerFactory? loggerFactory = null,
        ILlrpFrameObserver? frameObserver = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        _options = options;
        _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<LlrpTcpTransport>();
        _frameObserver = frameObserver ?? NullLlrpFrameObserver.Instance;
        ConnectionId = Guid.NewGuid().ToString("N");
    }

    /// <inheritdoc />
    public string ConnectionId { get; }

    /// <inheritdoc />
    public bool IsConnected => Volatile.Read(ref _connection) is not null;

    /// <inheritdoc />
    public async ValueTask ConnectAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await WaitForLifecycleLockAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (IsConnected)
            {
                return;
            }

            _logger.LogInformation(
                "Connecting LLRP transport {ConnectionId} to {Host}:{Port}",
                ConnectionId,
                _options.Host,
                _options.Port);

            TcpClient? client = new();
            try
            {
                using var connectSource = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    _lifetimeSource.Token);
                if (_options.ConnectTimeout != Timeout.InfiniteTimeSpan)
                {
                    connectSource.CancelAfter(_options.ConnectTimeout);
                }

                try
                {
                    await client.ConnectAsync(_options.Host, _options.Port, connectSource.Token).ConfigureAwait(false);
                    client.NoDelay = true;
                }
                catch (OperationCanceledException exception)
                {
                    if (Volatile.Read(ref _disposed) != 0)
                    {
                        throw new ObjectDisposedException(nameof(LlrpTcpTransport));
                    }

                    if (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }

                    throw new TimeoutException(
                        $"Connecting to LLRP reader {_options.Host}:{_options.Port} exceeded {_options.ConnectTimeout}.",
                        exception);
                }

                NetworkStream stream = client.GetStream();
                long generation = Interlocked.Increment(ref _nextConnectionGeneration);
                var connection = new ConnectionState(generation, client, stream);
                client = null;

                bool published;
                lock (_connectionSync)
                {
                    published = Volatile.Read(ref _disposed) == 0;
                    if (published)
                    {
                        Volatile.Write(ref _connection, connection);
                    }
                }

                if (!published)
                {
                    connection.Close();
                    throw new ObjectDisposedException(nameof(LlrpTcpTransport));
                }

                _logger.LogInformation(
                    "Connected LLRP transport {ConnectionId} generation {ConnectionGeneration} to {Host}:{Port}",
                    ConnectionId,
                    generation,
                    _options.Host,
                    _options.Port);
            }
            catch (Exception exception)
            {
                LogConnectFailure(exception, cancellationToken);
                throw;
            }
            finally
            {
                client?.Dispose();
            }
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisconnectAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await WaitForLifecycleLockAsync(cancellationToken).ConfigureAwait(false);

        ConnectionState? connection;
        try
        {
            ThrowIfDisposed();
            connection = DetachCurrentConnection();
            connection?.Close();
        }
        finally
        {
            _lifecycleLock.Release();
        }

        if (connection is not null)
        {
            _logger.LogInformation(
                "Disconnected LLRP transport {ConnectionId} generation {ConnectionGeneration}",
                ConnectionId,
                connection.Generation);
        }
    }

    /// <inheritdoc />
    public async ValueTask SendFrameAsync(
        ReadOnlyMemory<byte> frame,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        LlrpMessageHeader header = ValidateCompleteFrame(frame);

        using ConnectionLease lease = AcquireConnectedConnection();
        ConnectionState connection = lease.Connection;
        await WaitForSendLockAsync(connection, cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            connection.CancellationToken.ThrowIfCancellationRequested();
            EnsureCurrentConnection(connection);

            using (var operationSource = CancellationTokenSource.CreateLinkedTokenSource(
                       cancellationToken,
                       _lifetimeSource.Token,
                       connection.CancellationToken))
            {
                try
                {
                    await connection.Stream.WriteAsync(frame, operationSource.Token).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    bool faultedCurrentConnection = FaultConnection(connection);
                    LogSendFailure(
                        exception,
                        header,
                        connection,
                        faultedCurrentConnection,
                        cancellationToken);
                    throw;
                }
            }

            // Keep frame diagnostics within the send gate so transmit observations preserve wire order.
            // Once the write completes, cancellation must not turn a committed send into an apparent failure.
            LogFrame(LlrpFrameDirection.Transmit, header, frame.Span, connection.Generation);
            await ObserveFrameAsync(
                LlrpFrameDirection.Transmit,
                frame,
                connection.Generation).ConfigureAwait(false);
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
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.CompareExchange(ref _receiveInProgress, 1, 0) != 0)
        {
            throw new InvalidOperationException("Only one concurrent LLRP frame receive is supported per transport.");
        }

        try
        {
            using ConnectionLease lease = AcquireConnectedConnection();
            ConnectionState connection = lease.Connection;
            LlrpMessageHeader header;
            byte[] frame;

            using (var operationSource = CancellationTokenSource.CreateLinkedTokenSource(
                       cancellationToken,
                       _lifetimeSource.Token,
                       connection.CancellationToken))
            {
                try
                {
                    byte[] headerBytes = GC.AllocateUninitializedArray<byte>(LlrpMessageHeader.EncodedLength);
                    await ReadExactlyAsync(
                        connection.Stream,
                        headerBytes.AsMemory(0, 1),
                        operationSource.Token).ConfigureAwait(false);

                    using var assemblyTimeoutSource = new CancellationTokenSource();
                    if (_options.FrameAssemblyTimeout != Timeout.InfiniteTimeSpan)
                    {
                        assemblyTimeoutSource.CancelAfter(_options.FrameAssemblyTimeout);
                    }

                    using var assemblySource = CancellationTokenSource.CreateLinkedTokenSource(
                        operationSource.Token,
                        assemblyTimeoutSource.Token);

                    try
                    {
                        await ReadExactlyAsync(
                            connection.Stream,
                            headerBytes.AsMemory(1),
                            assemblySource.Token).ConfigureAwait(false);
                        header = LlrpMessageHeader.Decode(headerBytes);

                        if (header.MessageLength > _options.MaximumFrameLength)
                        {
                            throw new LlrpProtocolException(
                                LlrpProtocolErrorCode.FrameTooLarge,
                                $"The encoded frame length {header.MessageLength} exceeds the configured limit {_options.MaximumFrameLength}.");
                        }

                        int frameLength = checked((int)header.MessageLength);
                        frame = GC.AllocateUninitializedArray<byte>(frameLength);
                        headerBytes.CopyTo(frame, 0);
                        if (frameLength > LlrpMessageHeader.EncodedLength)
                        {
                            await ReadExactlyAsync(
                                connection.Stream,
                                frame.AsMemory(LlrpMessageHeader.EncodedLength),
                                assemblySource.Token).ConfigureAwait(false);
                        }
                    }
                    catch (OperationCanceledException exception) when (
                        assemblyTimeoutSource.IsCancellationRequested &&
                        !operationSource.IsCancellationRequested)
                    {
                        throw new TimeoutException(
                            $"Assembling an LLRP frame exceeded {_options.FrameAssemblyTimeout} after its first octet arrived.",
                            exception);
                    }
                }
                catch (Exception exception)
                {
                    bool faultedCurrentConnection = FaultConnection(connection);
                    LogReceiveFailure(
                        exception,
                        connection,
                        faultedCurrentConnection,
                        cancellationToken);
                    throw;
                }
            }

            // A complete frame has been consumed. Observation is diagnostic and must not change read success.
            LogFrame(LlrpFrameDirection.Receive, header, frame, connection.Generation);
            await ObserveFrameAsync(
                LlrpFrameDirection.Receive,
                frame,
                connection.Generation).ConfigureAwait(false);
            return frame;
        }
        finally
        {
            Volatile.Write(ref _receiveInProgress, 0);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        CancelWithoutThrowing(_lifetimeSource);
        await _lifecycleLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);

        ConnectionState? connection;
        try
        {
            connection = DetachCurrentConnection();
            connection?.Close();
        }
        finally
        {
            _lifecycleLock.Release();
        }

        if (connection is not null)
        {
            _logger.LogInformation(
                "Disposed LLRP transport {ConnectionId} generation {ConnectionGeneration}",
                ConnectionId,
                connection.Generation);
        }

        // These synchronization objects intentionally remain undisposed. Operations that were already in flight
        // can still be unwinding after the socket is closed; disposing a semaphore here could make their Release
        // calls mask the real I/O outcome or strand a waiter. They own no native resource unless their wait handle
        // is explicitly requested, which this type never does.
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

    private static void CancelWithoutThrowing(CancellationTokenSource source)
    {
        try
        {
            source.Cancel();
        }
        catch (AggregateException)
        {
            // Cancellation is followed by closing the socket, so callback failures cannot prevent shutdown.
        }
        catch (ObjectDisposedException)
        {
            // Closing is idempotent under concurrent fault and lifecycle paths.
        }
    }

    private LlrpMessageHeader ValidateCompleteFrame(ReadOnlyMemory<byte> frame)
    {
        LlrpMessageHeader header = LlrpMessageHeader.Decode(frame.Span);
        if (header.MessageLength != frame.Length)
        {
            throw new LlrpProtocolException(
                LlrpProtocolErrorCode.InvalidMessageLength,
                $"The encoded message length {header.MessageLength} does not match the supplied frame length {frame.Length}.");
        }

        if (header.MessageLength > _options.MaximumFrameLength)
        {
            throw new LlrpProtocolException(
                LlrpProtocolErrorCode.FrameTooLarge,
                $"The frame length {header.MessageLength} exceeds the configured limit {_options.MaximumFrameLength}.");
        }

        return header;
    }

    private async ValueTask WaitForLifecycleLockAsync(CancellationToken cancellationToken)
    {
        using var waitSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetimeSource.Token);
        try
        {
            await _lifecycleLock.WaitAsync(waitSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(LlrpTcpTransport));
        }
    }

    private async ValueTask WaitForSendLockAsync(
        ConnectionState connection,
        CancellationToken cancellationToken)
    {
        using var waitSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetimeSource.Token,
            connection.CancellationToken);
        try
        {
            await _sendLock.WaitAsync(waitSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(LlrpTcpTransport));
        }
    }

    private void EnsureCurrentConnection(ConnectionState expectedConnection)
    {
        lock (_connectionSync)
        {
            ThrowIfDisposed();
            if (!ReferenceEquals(_connection, expectedConnection))
            {
                throw new OperationCanceledException(
                    $"LLRP transport connection generation {expectedConnection.Generation} is no longer current.",
                    expectedConnection.CancellationToken);
            }
        }
    }

    private ConnectionLease AcquireConnectedConnection()
    {
        lock (_connectionSync)
        {
            ThrowIfDisposed();
            ConnectionState connection = _connection
                ?? throw new InvalidOperationException("The LLRP transport is not connected.");
            return connection.Acquire();
        }
    }

    private ConnectionState? DetachCurrentConnection()
    {
        lock (_connectionSync)
        {
            ConnectionState? connection = _connection;
            Volatile.Write(ref _connection, null);
            return connection;
        }
    }

    private bool FaultConnection(ConnectionState connection)
    {
        bool detached;
        lock (_connectionSync)
        {
            detached = ReferenceEquals(_connection, connection);
            if (detached)
            {
                Volatile.Write(ref _connection, null);
            }
        }

        if (detached)
        {
            connection.Close();
        }

        return detached;
    }

    private void LogFrame(
        LlrpFrameDirection direction,
        LlrpMessageHeader header,
        ReadOnlySpan<byte> frame,
        long connectionGeneration)
    {
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "{Direction} LLRP message {MessageType} id {MessageId}, version {Version}, length {FrameLength}, transport {ConnectionId}, generation {ConnectionGeneration}",
                direction,
                header.MessageType,
                header.MessageId,
                header.Version,
                frame.Length,
                ConnectionId,
                connectionGeneration);
        }

        if (_options.LogFrameHex && _logger.IsEnabled(LogLevel.Trace))
        {
            _logger.LogTrace(
                "{Direction} LLRP frame on transport {ConnectionId}, generation {ConnectionGeneration}: {FrameHex}",
                direction,
                ConnectionId,
                connectionGeneration,
                Convert.ToHexString(frame));
        }
    }

    private async ValueTask ObserveFrameAsync(
        LlrpFrameDirection direction,
        ReadOnlyMemory<byte> frame,
        long connectionGeneration)
    {
        try
        {
            await _frameObserver.ObserveAsync(
                new LlrpFrameObservation(direction, DateTimeOffset.UtcNow, ConnectionId, frame)
                {
                    ConnectionGeneration = connectionGeneration,
                },
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "LLRP frame observer failed for {Direction} frame on transport {ConnectionId}; network I/O remains successful",
                direction,
                ConnectionId);
        }
    }

    private void LogConnectFailure(Exception exception, CancellationToken callerToken)
    {
        if (exception is OperationCanceledException or ObjectDisposedException || callerToken.IsCancellationRequested)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(
                    exception,
                    "LLRP connection attempt ended by cancellation for transport {ConnectionId}",
                    ConnectionId);
            }

            return;
        }

        if (exception is TimeoutException)
        {
            _logger.LogWarning(
                exception,
                "LLRP connection attempt timed out for transport {ConnectionId} to {Host}:{Port}",
                ConnectionId,
                _options.Host,
                _options.Port);
            return;
        }

        _logger.LogError(
            exception,
            "Failed to connect LLRP transport {ConnectionId} to {Host}:{Port}",
            ConnectionId,
            _options.Host,
            _options.Port);
    }

    private void LogSendFailure(
        Exception exception,
        LlrpMessageHeader header,
        ConnectionState connection,
        bool faultedCurrentConnection,
        CancellationToken callerToken)
    {
        if (exception is OperationCanceledException or ObjectDisposedException
            || callerToken.IsCancellationRequested
            || Volatile.Read(ref _disposed) != 0
            || !faultedCurrentConnection)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(
                    exception,
                    "LLRP send ended by cancellation or connection closure for message {MessageType} id {MessageId} on transport {ConnectionId}, generation {ConnectionGeneration}",
                    header.MessageType,
                    header.MessageId,
                    ConnectionId,
                    connection.Generation);
            }

            return;
        }

        _logger.LogError(
            exception,
            "Failed to send LLRP message {MessageType} id {MessageId} on transport {ConnectionId}, generation {ConnectionGeneration}; the connection was discarded",
            header.MessageType,
            header.MessageId,
            ConnectionId,
            connection.Generation);
    }

    private void LogReceiveFailure(
        Exception exception,
        ConnectionState connection,
        bool faultedCurrentConnection,
        CancellationToken callerToken)
    {
        if (exception is OperationCanceledException or ObjectDisposedException
            || callerToken.IsCancellationRequested
            || Volatile.Read(ref _disposed) != 0
            || !faultedCurrentConnection)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(
                    exception,
                    "LLRP receive ended by cancellation or connection closure on transport {ConnectionId}, generation {ConnectionGeneration}",
                    ConnectionId,
                    connection.Generation);
            }

            return;
        }

        if (exception is LlrpProtocolException)
        {
            _logger.LogWarning(
                exception,
                "Invalid LLRP frame received on transport {ConnectionId}, generation {ConnectionGeneration}; the connection was discarded",
                ConnectionId,
                connection.Generation);
            return;
        }

        _logger.LogError(
            exception,
            "Failed to receive an LLRP frame on transport {ConnectionId}, generation {ConnectionGeneration}; the connection was discarded",
            ConnectionId,
            connection.Generation);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
    }

    private sealed class ConnectionState
    {
        private readonly CancellationTokenSource _cancellationSource = new();
        private int _closed;
        private int _referenceCount = 1;

        public ConnectionState(long generation, TcpClient client, NetworkStream stream)
        {
            Generation = generation;
            Client = client;
            Stream = stream;
        }

        public long Generation { get; }

        public TcpClient Client { get; }

        public NetworkStream Stream { get; }

        public CancellationToken CancellationToken => _cancellationSource.Token;

        public ConnectionLease Acquire()
        {
            Interlocked.Increment(ref _referenceCount);
            return new ConnectionLease(this);
        }

        public void Close()
        {
            if (Interlocked.Exchange(ref _closed, 1) != 0)
            {
                return;
            }

            CancelWithoutThrowing(_cancellationSource);
            try
            {
                Stream.Dispose();
            }
            catch (Exception)
            {
                // Cancellation has already signaled operations. TcpClient disposal below is the final close path.
            }

            try
            {
                Client.Dispose();
            }
            catch (Exception)
            {
                // Socket disposal is best effort and must not mask the operation that faulted the connection.
            }

            Release();
        }

        public void Release()
        {
            if (Interlocked.Decrement(ref _referenceCount) == 0)
            {
                _cancellationSource.Dispose();
            }
        }
    }

    private sealed class ConnectionLease : IDisposable
    {
        private ConnectionState? _connection;

        public ConnectionLease(ConnectionState connection)
        {
            _connection = connection;
        }

        public ConnectionState Connection => _connection
            ?? throw new ObjectDisposedException(nameof(ConnectionLease));

        public void Dispose()
        {
            ConnectionState? connection = Interlocked.Exchange(ref _connection, null);
            connection?.Release();
        }
    }
}
