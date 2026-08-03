namespace LlrpNet.Core.Transport;

/// <summary>
/// Provides framed, bidirectional LLRP transport without interpreting message-specific payloads.
/// </summary>
public interface ILlrpTransport : IAsyncDisposable
{
    /// <summary>
    /// Gets the identifier used to correlate transport logs and frame observations.
    /// </summary>
    public string ConnectionId { get; }

    /// <summary>
    /// Gets a value indicating whether the transport currently owns an open connection.
    /// </summary>
    public bool IsConnected { get; }

    /// <summary>
    /// Opens the underlying connection.
    /// </summary>
    /// <param name="cancellationToken">Cancels the connection attempt.</param>
    public ValueTask ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Closes the underlying connection. Calling this method repeatedly is safe.
    /// </summary>
    /// <param name="cancellationToken">Cancels waiting for the lifecycle lock.</param>
    public ValueTask DisconnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends exactly one complete LLRP frame.
    /// </summary>
    /// <param name="frame">The complete encoded message, including its ten-octet header.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <remarks>
    /// The caller must keep the memory underlying <paramref name="frame"/> unchanged until the returned operation
    /// completes. Implementations serialize concurrent sends so bytes from separate frames are never interleaved.
    /// If cancellation or an I/O failure can occur after part of a frame was written, an implementation may discard
    /// the connection to preserve LLRP framing.
    /// </remarks>
    public ValueTask SendFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken cancellationToken = default);

    /// <summary>
    /// Receives exactly one complete LLRP frame into owned memory.
    /// </summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>A complete frame whose memory remains valid after this call returns.</returns>
    /// <remarks>
    /// Only one receive operation may be active for a transport at a time. A concurrent receive throws
    /// <see cref="InvalidOperationException"/>. If cancellation, end of stream, I/O failure, or malformed framing
    /// occurs after bytes may have been consumed, an implementation may discard the connection rather than attempt
    /// to resume at an unknown frame boundary.
    /// </remarks>
    public ValueTask<ReadOnlyMemory<byte>> ReceiveFrameAsync(CancellationToken cancellationToken = default);
}
