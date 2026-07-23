using LlrpNet.Protocol.Messages;

namespace LlrpSdk;

using LlrpNet.Core.Session;

/// <summary>
/// Exposes typed and exact-frame protocol operations for one connected reader.
/// </summary>
public interface IReaderProtocolAccess
{
    /// <summary>
    /// Encodes and sends a typed request, then decodes and validates its correlated response type.
    /// </summary>
    /// <typeparam name="TResponse">The exact expected decoded response CLR type.</typeparam>
    /// <param name="request">The typed request with a non-zero message identifier.</param>
    /// <param name="timeout">An optional response timeout; <see langword="null"/> uses reader options.</param>
    /// <param name="cancellationToken">Cancels the send or pending transaction.</param>
    /// <returns>The decoded typed response.</returns>
    public Task<TResponse> TransactAsync<TResponse>(
        ILlrpMessage request,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
        where TResponse : class, ILlrpMessage;

    /// <summary>
    /// Encodes and sends a typed message without registering a response transaction.
    /// </summary>
    /// <typeparam name="TMessage">The exact typed message CLR type.</typeparam>
    /// <param name="message">The typed message.</param>
    /// <param name="cancellationToken">Cancels the send.</param>
    /// <returns>A task representing the send.</returns>
    public Task SendAsync<TMessage>(
        TMessage message,
        CancellationToken cancellationToken = default)
        where TMessage : ILlrpMessage;

    /// <summary>
    /// Sends one exact complete frame and returns the exact complete frame with the same message identifier.
    /// </summary>
    /// <param name="requestFrame">The complete raw request frame.</param>
    /// <param name="responseMatcher">
    /// A required non-throwing predicate that distinguishes the expected response from reader-initiated messages
    /// that happen to use the same message identifier.
    /// </param>
    /// <param name="timeout">An optional response timeout; <see langword="null"/> uses reader options.</param>
    /// <param name="cancellationToken">Cancels the send or pending transaction.</param>
    /// <returns>The exact correlated response frame.</returns>
    public Task<ReadOnlyMemory<byte>> TransactRawAsync(
        ReadOnlyMemory<byte> requestFrame,
        LlrpResponseMatcher responseMatcher,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends one exact complete frame without registering a response transaction.
    /// </summary>
    /// <param name="frame">The complete raw frame.</param>
    /// <param name="cancellationToken">Cancels the send.</param>
    /// <returns>A task representing the send.</returns>
    public Task SendRawAsync(
        ReadOnlyMemory<byte> frame,
        CancellationToken cancellationToken = default);
}
