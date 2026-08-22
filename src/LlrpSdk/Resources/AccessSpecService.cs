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
        return AddCoreAsync(accessSpec, cancellationToken);
    }

    private async Task AddCoreAsync(ILlrpParameter accessSpec, CancellationToken cancellationToken)
    {
        await reader.ExecuteExpertResourceOperationAsync(
            () => protocolAdapter().AddAccessSpecAsync(reader, messageIds.Next(), accessSpec, cancellationToken), cancellationToken);
    }

    public Task DeleteAsync(uint accessSpecId, CancellationToken cancellationToken = default)
    {
        if (accessSpecId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(accessSpecId), "Zero selects all resources; use DeleteAllAsync(ResourceTakeoverPolicy.ReplaceAll) explicitly.");
        }

        return DeleteCoreAsync(accessSpecId, cancellationToken);
    }

    private async Task DeleteCoreAsync(uint accessSpecId, CancellationToken cancellationToken)
    {
        await reader.ExecuteExpertResourceOperationAsync(
            () => protocolAdapter().DeleteAccessSpecAsync(reader, messageIds.Next(), accessSpecId, cancellationToken), cancellationToken);
    }

    public Task DeleteAllAsync(ResourceTakeoverPolicy policy, CancellationToken cancellationToken = default) =>
        reader.DeleteAllAccessSpecsAsync(policy, cancellationToken);

    public Task EnableAsync(uint accessSpecId, CancellationToken cancellationToken = default) =>
        accessSpecId == 0
            ? throw new ArgumentOutOfRangeException(nameof(accessSpecId), "Zero selects all resources; use an explicit takeover policy.")
            :
        reader.ExecuteExpertResourceOperationAsync(
            () => protocolAdapter().EnableAccessSpecAsync(reader, messageIds.Next(), accessSpecId, cancellationToken), cancellationToken);

    public Task DisableAsync(uint accessSpecId, CancellationToken cancellationToken = default) =>
        accessSpecId == 0
            ? throw new ArgumentOutOfRangeException(nameof(accessSpecId), "Zero selects all resources; use an explicit takeover policy.")
            :
        reader.ExecuteExpertResourceOperationAsync(
            () => protocolAdapter().DisableAccessSpecAsync(reader, messageIds.Next(), accessSpecId, cancellationToken), cancellationToken);

    public Task<IReadOnlyList<ILlrpParameter>> GetAllAsync(CancellationToken cancellationToken = default) =>
        reader.ExecuteExpertResourceQueryAsync(
            () => protocolAdapter().GetAccessSpecsAsync(reader, messageIds.Next(), cancellationToken), cancellationToken);
}
