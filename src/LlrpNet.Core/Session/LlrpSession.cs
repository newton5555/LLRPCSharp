using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using LlrpNet.Core.Protocol;
using LlrpNet.Core.Transactions;
using LlrpNet.Core.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LlrpNet.Core.Session;

/// <summary>
/// Coordinates complete raw LLRP frames over a transport and correlates transactions by message identifier.
/// </summary>
/// <remarks>
/// This version-independent session interprets only the common LLRP header. Matched response frames complete
/// transactions; all other complete frames are published through <see cref="UnsolicitedFrames"/> without
/// changing their bytes. The session owns and disposes the supplied transport.
/// </remarks>
public sealed class LlrpSession : IAsyncDisposable
{
    private static readonly Task<LlrpSessionTermination> NoConnectionCompletion =
        Task.FromResult(LlrpSessionTermination.Requested);
    private static readonly TimeSpan MaximumTimerTimeout =
        TimeSpan.FromMilliseconds(uint.MaxValue - 1d);
    private readonly ILogger<LlrpSession> _logger;
    private readonly LlrpSessionOptions _options;
    private readonly PendingTransactionManager<ReadOnlyMemory<byte>> _transactions;
    private readonly ILlrpTransport _transport;
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private readonly Channel<ReadOnlyMemory<byte>> _unsolicitedFrames;
    private CancellationTokenSource? _receiveCancellation;
    private TaskCompletionSource<LlrpSessionTermination>? _connectionCompletion;
    private Task? _receiveLoop;
    private int _connected;
    private int _disposed;
    private long _connectionGeneration;
    private long _discardedUnsolicitedFrameCount;
    private long _droppedUnsolicitedFrameCount;

    /// <summary>
    /// Initializes a new instance of the <see cref="LlrpSession"/> class.
    /// </summary>
    /// <param name="transport">The framed transport owned by this session.</param>
    /// <param name="options">Optional transaction settings.</param>
    /// <param name="loggerFactory">The optional application logging factory.</param>
    public LlrpSession(
        ILlrpTransport transport,
        LlrpSessionOptions? options = null,
        ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(transport);

        _transport = transport;
        _options = options ?? new LlrpSessionOptions();
        _options.Validate();
        _transactions = new PendingTransactionManager<ReadOnlyMemory<byte>>(
            _options.MessageIdReuseQuarantine);
        _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<LlrpSession>();
        _unsolicitedFrames = Channel.CreateBounded<ReadOnlyMemory<byte>>(new BoundedChannelOptions(
            _options.UnsolicitedFrameCapacity)
        {
            AllowSynchronousContinuations = false,
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = true,
        });
    }

    /// <summary>
    /// Gets the identifier supplied by the underlying transport.
    /// </summary>
    public string ConnectionId => _transport.ConnectionId;

    /// <summary>
    /// Gets the monotonically increasing session generation assigned to the latest successful connection.
    /// </summary>
    public long ConnectionGeneration => Interlocked.Read(ref _connectionGeneration);

    /// <summary>
    /// Gets a value indicating whether the session has an active receive loop over a connected transport.
    /// </summary>
    public bool IsConnected =>
        Volatile.Read(ref _connected) != 0 && _transport.IsConnected;

    /// <summary>
    /// Gets the completion of the current or most recently connected session generation.
    /// </summary>
    /// <remarks>
    /// A successful <see cref="ConnectAsync"/> replaces this task. It completes with
    /// <see cref="LlrpSessionTermination.WasRequested"/> set for an explicit disconnect/disposal, or with
    /// <see cref="LlrpSessionTermination.Error"/> populated when the receive loop fails. The task itself never
    /// faults, so observing connection health cannot create an unobserved task exception.
    /// </remarks>
    public Task<LlrpSessionTermination> ConnectionCompletion =>
        Volatile.Read(ref _connectionCompletion)?.Task ?? NoConnectionCompletion;

    /// <summary>
    /// Gets the stream of complete received frames that did not match a pending transaction.
    /// </summary>
    /// <remarks>
    /// The channel is bounded by <see cref="LlrpSessionOptions.UnsolicitedFrameCapacity"/> so a stalled consumer
    /// cannot grow memory without limit. Applications should continuously consume this channel while connected and
    /// select an explicit overflow policy when dropping notifications is acceptable.
    /// </remarks>
    public ChannelReader<ReadOnlyMemory<byte>> UnsolicitedFrames => _unsolicitedFrames.Reader;

    /// <summary>
    /// Gets the total number of newly received unsolicited frames dropped by the configured overflow policy.
    /// </summary>
    public long DroppedUnsolicitedFrameCount =>
        Interlocked.Read(ref _droppedUnsolicitedFrameCount);

    /// <summary>
    /// Gets the number of old-generation unsolicited frames discarded before reconnecting.
    /// </summary>
    public long DiscardedUnsolicitedFrameCount =>
        Interlocked.Read(ref _discardedUnsolicitedFrameCount);

    /// <summary>
    /// Opens the transport and starts the single receive loop. Repeated calls while connected are safe.
    /// </summary>
    /// <param name="cancellationToken">Cancels the connection attempt or waiting for a lifecycle operation.</param>
    public async ValueTask ConnectAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (Volatile.Read(ref _connected) != 0 &&
                _transport.IsConnected &&
                _receiveLoop is { IsCompleted: false })
            {
                return;
            }

            if (Volatile.Read(ref _connected) != 0)
            {
                await DisconnectCoreAsync(
                    new LlrpSessionDisconnectedException(
                        ConnectionId,
                        $"LLRP session {ConnectionId} discarded an unhealthy connection generation before reconnecting."))
                    .ConfigureAwait(false);
            }

            DiscardQueuedFramesBeforeReconnect();

            await _transport.ConnectAsync(cancellationToken).ConfigureAwait(false);
            var receiveCancellation = new CancellationTokenSource();
            var connectionCompletion = new TaskCompletionSource<LlrpSessionTermination>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _receiveCancellation = receiveCancellation;
            Volatile.Write(ref _connectionCompletion, connectionCompletion);
            Volatile.Write(ref _connected, 1);
            long connectionGeneration = Interlocked.Increment(ref _connectionGeneration);
            _receiveLoop = ReceiveLoopAsync(receiveCancellation, connectionCompletion);

            _logger.LogInformation(
                "Started LLRP session generation {ConnectionGeneration} receive loop on transport {ConnectionId}",
                connectionGeneration,
                ConnectionId);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    /// <summary>
    /// Stops receiving, fails every pending transaction, and closes the transport.
    /// </summary>
    /// <param name="cancellationToken">Cancels only waiting to enter the lifecycle operation.</param>
    public async ValueTask DisconnectAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            await DisconnectCoreAsync(
                new LlrpSessionDisconnectedException(
                    ConnectionId,
                    $"LLRP session {ConnectionId} was disconnected."))
                .ConfigureAwait(false);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    /// <summary>
    /// Sends one complete frame without registering a transaction.
    /// </summary>
    /// <param name="frame">The exact common header and complete message payload.</param>
    /// <param name="cancellationToken">Cancels waiting for the connection lease or the transport write.</param>
    public async ValueTask SendFrameAsync(
        ReadOnlyMemory<byte> frame,
        CancellationToken cancellationToken = default)
    {
        ValidateCompleteFrame(frame);
        ThrowIfDisposed();

        CancellationToken connectionToken;
        CancellationTokenSource sendCancellation;
        await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            EnsureConnected();
            CancellationTokenSource connectionCancellation = _receiveCancellation
                ?? throw new InvalidOperationException("The LLRP session has no active connection generation.");
            connectionToken = connectionCancellation.Token;
            sendCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                connectionToken);
        }
        finally
        {
            _lifecycleLock.Release();
        }

        using (sendCancellation)
        {
            try
            {
                await _transport.SendFrameAsync(frame, sendCancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException exception) when (
                !cancellationToken.IsCancellationRequested)
            {
                ThrowIfDisposed();
                throw CreateDisconnectedException(exception);
            }
        }
    }

    /// <summary>
    /// Sends one complete request frame and asynchronously returns the first frame with the same message identifier.
    /// </summary>
    /// <param name="requestFrame">The exact common header and complete request payload.</param>
    /// <param name="timeout">
    /// The response timeout, or <see langword="null"/> to use <see cref="LlrpSessionOptions.DefaultRequestTimeout"/>.
    /// </param>
    /// <param name="cancellationToken">Cancels the write and the pending transaction.</param>
    /// <returns>The exact complete response frame received from the transport.</returns>
    /// <exception cref="ArgumentException">The request has message identifier zero.</exception>
    /// <exception cref="InvalidOperationException">The session is disconnected or the identifier is already pending.</exception>
    /// <exception cref="TimeoutException">No matching frame arrived before the timeout.</exception>
    public async Task<ReadOnlyMemory<byte>> TransactAsync(
        ReadOnlyMemory<byte> requestFrame,
        LlrpResponseMatcher responseMatcher,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(responseMatcher);
        LlrpMessageHeader header = ValidateCompleteFrame(requestFrame);
        if (header.MessageId == 0)
        {
            throw new ArgumentException(
                "A transactional LLRP request must use a non-zero message identifier.",
                nameof(requestFrame));
        }

        TimeSpan effectiveTimeout = timeout ?? _options.DefaultRequestTimeout;
        ValidateTimeout(effectiveTimeout, nameof(timeout));
        ThrowIfDisposed();

        PendingTransactionManager<ReadOnlyMemory<byte>>.PendingTransactionRegistration registration;
        CancellationToken connectionToken;
        CancellationTokenSource sendCancellation;
        await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            EnsureConnected();
            CancellationTokenSource connectionCancellation = _receiveCancellation
                ?? throw new InvalidOperationException("The LLRP session has no active connection generation.");
            connectionToken = connectionCancellation.Token;
            registration = _transactions.RegisterDeferred(
                header.MessageId,
                frame => MatchesResponse(responseMatcher, frame),
                cancellationToken);
            try
            {
                sendCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    connectionToken);
            }
            catch
            {
                registration.Abandon();
                throw;
            }
        }
        finally
        {
            _lifecycleLock.Release();
        }

        using (sendCancellation)
        {
            if (!registration.ResponseTask.IsCompleted)
            {
                try
                {
                    await _transport
                        .SendFrameAsync(requestFrame, sendCancellation.Token)
                        .ConfigureAwait(false);
                    registration.ArmTimeout(effectiveTimeout);
                }
                catch (OperationCanceledException exception) when (
                    !cancellationToken.IsCancellationRequested)
                {
                    Exception failure = Volatile.Read(ref _disposed) != 0
                        ? new ObjectDisposedException(nameof(LlrpSession))
                        : CreateDisconnectedException(exception);
                    registration.TryFail(failure);
                }
                catch (Exception exception)
                {
                    registration.TryFail(exception);
                }
            }
        }

        return await registration.ResponseTask.ConfigureAwait(false);
    }

    /// <summary>
    /// Asynchronously enumerates complete frames that did not match a pending transaction.
    /// </summary>
    /// <param name="cancellationToken">Cancels this enumeration without affecting the session.</param>
    /// <returns>An asynchronous sequence backed by <see cref="UnsolicitedFrames"/>.</returns>
    public IAsyncEnumerable<ReadOnlyMemory<byte>> ReadUnsolicitedFramesAsync(
        CancellationToken cancellationToken = default)
    {
        return _unsolicitedFrames.Reader.ReadAllAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _lifecycleLock.WaitAsync().ConfigureAwait(false);
        Exception? firstFailure = null;
        try
        {
            CancellationTokenSource? receiveCancellation = _receiveCancellation;
            TaskCompletionSource<LlrpSessionTermination>? connectionCompletion = _connectionCompletion;
            Task? receiveLoop = _receiveLoop;
            _receiveCancellation = null;
            _receiveLoop = null;
            Volatile.Write(ref _connected, 0);

            CancelWithoutThrowing(receiveCancellation);
            _transactions.Dispose();
            _unsolicitedFrames.Writer.TryComplete();

            try
            {
                await _transport.DisconnectAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                firstFailure = exception;
            }

            if (receiveLoop is not null)
            {
                try
                {
                    await receiveLoop.ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    firstFailure ??= exception;
                }
            }

            receiveCancellation?.Dispose();
            connectionCompletion?.TrySetResult(LlrpSessionTermination.Requested);

            try
            {
                await _transport.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                firstFailure ??= exception;
            }
        }
        finally
        {
            _lifecycleLock.Release();
        }

        if (firstFailure is not null)
        {
            ExceptionDispatchInfo.Capture(firstFailure).Throw();
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

    private static void ValidateTimeout(TimeSpan timeout, string parameterName)
    {
        if ((timeout < TimeSpan.Zero || timeout > MaximumTimerTimeout) &&
            timeout != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                timeout,
                $"The transaction timeout must be non-negative, no greater than {MaximumTimerTimeout}, or Timeout.InfiniteTimeSpan.");
        }
    }

    private async Task ReceiveLoopAsync(
        CancellationTokenSource receiveCancellation,
        TaskCompletionSource<LlrpSessionTermination> connectionCompletion)
    {
        CancellationToken cancellationToken = receiveCancellation.Token;
        LlrpSessionDisconnectedException? sessionFailure = null;

        try
        {
            while (true)
            {
                ReadOnlyMemory<byte> frame = await _transport
                    .ReceiveFrameAsync(cancellationToken)
                    .ConfigureAwait(false);
                LlrpMessageHeader header = ValidateCompleteFrame(frame);

                if (_transactions.TryComplete(header.MessageId, frame))
                {
                    if (_logger.IsEnabled(LogLevel.Debug))
                    {
                        _logger.LogDebug(
                            "Matched LLRP response type {MessageType} id {MessageId} on transport {ConnectionId}",
                            header.MessageType,
                            header.MessageId,
                            ConnectionId);
                    }
                }
                else
                {
                    PublishUnsolicitedFrame(header, frame);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // An explicit disconnect or disposal owns lifecycle cleanup.
        }
        catch (Exception exception) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug(
                exception,
                "LLRP receive loop stopped during session shutdown on transport {ConnectionId}",
                ConnectionId);
        }
        catch (Exception exception)
        {
            sessionFailure = new LlrpSessionDisconnectedException(
                ConnectionId,
                $"LLRP session {ConnectionId} stopped because its receive loop failed.",
                exception);
            connectionCompletion.TrySetResult(
                LlrpSessionTermination.Unexpected(sessionFailure));
            _logger.LogError(
                exception,
                "LLRP receive loop failed on transport {ConnectionId}",
                ConnectionId);
        }

        if (sessionFailure is not null)
        {
            await HandleReceiveFailureAsync(
                receiveCancellation,
                sessionFailure).ConfigureAwait(false);
        }
    }

    private async Task HandleReceiveFailureAsync(
        CancellationTokenSource receiveCancellation,
        LlrpSessionDisconnectedException sessionFailure)
    {
        try
        {
            await _lifecycleLock
                .WaitAsync(receiveCancellation.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (receiveCancellation.IsCancellationRequested)
        {
            return;
        }

        try
        {
            if (!ReferenceEquals(_receiveCancellation, receiveCancellation))
            {
                return;
            }

            _receiveCancellation = null;
            _receiveLoop = null;
            Volatile.Write(ref _connected, 0);
            _transactions.FailAll(sessionFailure);

            try
            {
                await _transport.DisconnectAsync().ConfigureAwait(false);
            }
            catch (Exception disconnectFailure)
            {
                _logger.LogWarning(
                    disconnectFailure,
                    "Failed to close transport {ConnectionId} after the LLRP receive loop stopped",
                    ConnectionId);
            }

            receiveCancellation.Dispose();
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    private async ValueTask DisconnectCoreAsync(Exception transactionFailure)
    {
        CancellationTokenSource? receiveCancellation = _receiveCancellation;
        TaskCompletionSource<LlrpSessionTermination>? connectionCompletion = _connectionCompletion;
        Task? receiveLoop = _receiveLoop;
        _receiveCancellation = null;
        _receiveLoop = null;
        Volatile.Write(ref _connected, 0);

        CancelWithoutThrowing(receiveCancellation);
        _transactions.FailAll(transactionFailure);

        Exception? disconnectFailure = null;
        try
        {
            await _transport.DisconnectAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            disconnectFailure = exception;
        }

        if (receiveLoop is not null)
        {
            try
            {
                await receiveLoop.ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                disconnectFailure ??= exception;
            }
        }

        receiveCancellation?.Dispose();
        connectionCompletion?.TrySetResult(LlrpSessionTermination.Requested);
        _logger.LogInformation("Stopped LLRP session on transport {ConnectionId}", ConnectionId);

        if (disconnectFailure is not null)
        {
            ExceptionDispatchInfo.Capture(disconnectFailure).Throw();
        }
    }

    private bool MatchesResponse(
        LlrpResponseMatcher responseMatcher,
        ReadOnlyMemory<byte> frame)
    {
        LlrpMessageHeader header = ValidateCompleteFrame(frame);
        try
        {
            return responseMatcher(header, frame);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "LLRP response matcher threw for message type {MessageType} id {MessageId} on transport {ConnectionId}; the frame remains unsolicited",
                header.MessageType,
                header.MessageId,
                ConnectionId);
            return false;
        }
    }

    private void PublishUnsolicitedFrame(
        LlrpMessageHeader header,
        ReadOnlyMemory<byte> frame)
    {
        if (_unsolicitedFrames.Writer.TryWrite(frame))
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(
                    "Published unsolicited LLRP message type {MessageType} id {MessageId} on transport {ConnectionId}",
                    header.MessageType,
                    header.MessageId,
                    ConnectionId);
            }

            return;
        }

        if (_options.UnsolicitedFrameOverflowPolicy ==
            LlrpUnsolicitedFrameOverflowPolicy.FaultConnection)
        {
            throw new LlrpSessionBackpressureException(
                ConnectionId,
                _options.UnsolicitedFrameCapacity);
        }

        long droppedCount = Interlocked.Increment(ref _droppedUnsolicitedFrameCount);
        if (droppedCount == 1 || (droppedCount & (droppedCount - 1)) == 0)
        {
            _logger.LogWarning(
                "Dropped unsolicited LLRP message type {MessageType} id {MessageId} on transport {ConnectionId} because the bounded queue of {QueueCapacity} frames is full; total dropped {DroppedCount}",
                header.MessageType,
                header.MessageId,
                ConnectionId,
                _options.UnsolicitedFrameCapacity,
                droppedCount);
        }
    }

    private void DiscardQueuedFramesBeforeReconnect()
    {
        if (ConnectionGeneration == 0)
        {
            return;
        }

        _transactions.ClearRetiredIdentifiers();

        long discarded = 0;
        while (_unsolicitedFrames.Reader.TryRead(out _))
        {
            discarded++;
        }

        if (discarded == 0)
        {
            return;
        }

        long totalDiscarded = Interlocked.Add(
            ref _discardedUnsolicitedFrameCount,
            discarded);
        _logger.LogWarning(
            "Discarded {DiscardedCount} queued unsolicited LLRP frames from an old session generation on transport {ConnectionId}; total discarded on reconnect {TotalDiscardedCount}",
            discarded,
            ConnectionId,
            totalDiscarded);
    }

    private LlrpSessionDisconnectedException CreateDisconnectedException(Exception? innerException = null)
    {
        const string Message = "The LLRP session connection generation ended before the frame was sent.";
        return innerException is null
            ? new LlrpSessionDisconnectedException(ConnectionId, Message)
            : new LlrpSessionDisconnectedException(ConnectionId, Message, innerException);
    }

    private void CancelWithoutThrowing(CancellationTokenSource? cancellationSource)
    {
        if (cancellationSource is null)
        {
            return;
        }

        try
        {
            cancellationSource.Cancel();
        }
        catch (AggregateException exception)
        {
            _logger.LogWarning(
                exception,
                "One or more cancellation callbacks failed while stopping LLRP session {ConnectionId}; cleanup continues",
                ConnectionId);
        }
        catch (ObjectDisposedException)
        {
            // A concurrent receive-failure path already owns and disposed this generation.
        }
    }

    private void EnsureConnected()
    {
        if (Volatile.Read(ref _connected) == 0 || !_transport.IsConnected)
        {
            throw new InvalidOperationException("The LLRP session is not connected.");
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
    }
}
