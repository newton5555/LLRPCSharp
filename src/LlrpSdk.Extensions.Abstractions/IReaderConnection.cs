using System;
using System.Threading;
using System.Threading.Tasks;
using LlrpNet.Protocol.Messages;

namespace LlrpSdk.Extensions;

/// <summary>
/// Provides a minimal interface for reader extensions to interact with the LLRP connection.
/// </summary>
public interface IReaderConnection
{
    /// <summary>
    /// Encodes and sends a typed request, then decodes and correlates its response type.
    /// </summary>
    public Task<TResponse> TransactAsync<TResponse>(
        ILlrpMessage request,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
        where TResponse : class, ILlrpMessage;

    /// <summary>
    /// Generates the next unique message identifier.
    /// </summary>
    public uint NextMessageId();
}
