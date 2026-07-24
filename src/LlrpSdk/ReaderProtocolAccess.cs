using LlrpNet.Protocol.Messages;

namespace LlrpSdk;

using LlrpNet.Core.Session;

internal sealed class ReaderProtocolAccess : IReaderProtocolAccess
{
    private readonly LlrpReader _reader;

    public ReaderProtocolAccess(LlrpReader reader)
    {
        _reader = reader;
    }

    public Task<TResponse> TransactAsync<TResponse>(
        ILlrpMessage request,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
        where TResponse : class, ILlrpMessage
    {
        return _reader.TransactFromRawProtocolAsync<TResponse>(request, timeout, cancellationToken);
    }

    public Task SendAsync<TMessage>(
        TMessage message,
        CancellationToken cancellationToken = default)
        where TMessage : ILlrpMessage
    {
        return _reader.SendFromRawProtocolAsync(message, cancellationToken);
    }

    public Task<ReadOnlyMemory<byte>> TransactRawAsync(
        ReadOnlyMemory<byte> requestFrame,
        LlrpResponseMatcher responseMatcher,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        return _reader.TransactRawAsync(
            requestFrame,
            responseMatcher,
            timeout,
            cancellationToken);
    }

    public Task SendRawAsync(
        ReadOnlyMemory<byte> frame,
        CancellationToken cancellationToken = default)
    {
        return _reader.SendRawAsync(frame, cancellationToken);
    }
}
