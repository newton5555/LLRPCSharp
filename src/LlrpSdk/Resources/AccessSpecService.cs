using LlrpNet.Core.Transactions;
using LlrpNet.Protocol.Parameters;

namespace LlrpSdk;

/// <summary>Dispatches protocol-aware AccessSpec resource operations through the negotiated adapter.</summary>
internal sealed class AccessSpecService : IAccessSpecService
{
    private readonly LlrpMessageIdGenerator messageIds;
    private readonly Func<ILlrpProtocolAdapter> protocolAdapter;
    private readonly LlrpReader reader;

    public AccessSpecService(
        LlrpReader reader,
        Func<ILlrpProtocolAdapter> protocolAdapter,
        LlrpMessageIdGenerator messageIds)
    {
        this.reader = reader ?? throw new ArgumentNullException(nameof(reader));
        this.protocolAdapter = protocolAdapter ?? throw new ArgumentNullException(nameof(protocolAdapter));
        this.messageIds = messageIds ?? throw new ArgumentNullException(nameof(messageIds));
    }

    public Task AddAsync(ILlrpParameter accessSpec, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(accessSpec);
        return reader.ExecuteManualResourceOperationAsync(
            () => protocolAdapter().AddAccessSpecAsync(reader, messageIds.Next(), accessSpec, cancellationToken), cancellationToken);
    }

    public Task DeleteAsync(uint accessSpecId, CancellationToken cancellationToken = default) =>
        reader.ExecuteManualResourceOperationAsync(
            () => protocolAdapter().DeleteAccessSpecAsync(reader, messageIds.Next(), accessSpecId, cancellationToken), cancellationToken);

    public Task EnableAsync(uint accessSpecId, CancellationToken cancellationToken = default) =>
        reader.ExecuteManualResourceOperationAsync(
            () => protocolAdapter().EnableAccessSpecAsync(reader, messageIds.Next(), accessSpecId, cancellationToken), cancellationToken);

    public Task DisableAsync(uint accessSpecId, CancellationToken cancellationToken = default) =>
        reader.ExecuteManualResourceOperationAsync(
            () => protocolAdapter().DisableAccessSpecAsync(reader, messageIds.Next(), accessSpecId, cancellationToken), cancellationToken);

    public Task<IReadOnlyList<ILlrpParameter>> GetAllAsync(CancellationToken cancellationToken = default) =>
        protocolAdapter().GetAccessSpecsAsync(reader, messageIds.Next(), cancellationToken);
}
