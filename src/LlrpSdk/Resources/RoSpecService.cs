using LlrpNet.Core.Transactions;
using LlrpNet.Protocol.Parameters;

namespace LlrpSdk;

/// <summary>Dispatches protocol-aware ROSpec resource operations through the negotiated adapter.</summary>
internal sealed class RoSpecService : IRoSpecService
{
    private readonly LlrpMessageIdGenerator messageIds;
    private readonly Func<ILlrpProtocolAdapter> protocolAdapter;
    private readonly LlrpReader reader;

    public RoSpecService(
        LlrpReader reader,
        Func<ILlrpProtocolAdapter> protocolAdapter,
        LlrpMessageIdGenerator messageIds)
    {
        this.reader = reader ?? throw new ArgumentNullException(nameof(reader));
        this.protocolAdapter = protocolAdapter ?? throw new ArgumentNullException(nameof(protocolAdapter));
        this.messageIds = messageIds ?? throw new ArgumentNullException(nameof(messageIds));
    }

    public Task AddAsync(ILlrpParameter roSpec, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(roSpec);
        return AddCoreAsync(roSpec, cancellationToken);
    }

    private async Task AddCoreAsync(ILlrpParameter roSpec, CancellationToken cancellationToken)
    {
        await reader.ExecuteExpertResourceOperationAsync(
            () => protocolAdapter().AddRoSpecAsync(reader, messageIds.Next(), roSpec, cancellationToken), cancellationToken);
    }

    public Task AddDefaultAsync(uint roSpecId, InventorySettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (roSpecId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(roSpecId), "The ROSpec identifier must be non-zero.");
        }
        return AddAsync(reader.CompileDefaultInventoryRoSpec(settings, roSpecId), cancellationToken);
    }

    public Task DeleteAsync(uint roSpecId, CancellationToken cancellationToken = default)
    {
        if (roSpecId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(roSpecId), "Zero selects all resources; use DeleteAllAsync(ResourceTakeoverPolicy.ReplaceAll) explicitly.");
        }

        return DeleteCoreAsync(roSpecId, cancellationToken);
    }

    private async Task DeleteCoreAsync(uint roSpecId, CancellationToken cancellationToken)
    {
        await reader.ExecuteExpertResourceOperationAsync(
            () => protocolAdapter().DeleteRoSpecAsync(reader, messageIds.Next(), roSpecId, cancellationToken), cancellationToken);
    }

    public Task DeleteAllAsync(ResourceTakeoverPolicy policy, CancellationToken cancellationToken = default) =>
        reader.DeleteAllRoSpecsAsync(policy, cancellationToken);

    public Task EnableAsync(uint roSpecId, CancellationToken cancellationToken = default) =>
        roSpecId == 0
            ? throw new ArgumentOutOfRangeException(nameof(roSpecId), "Zero selects all resources; use an explicit takeover policy.")
            :
        reader.ExecuteExpertResourceOperationAsync(
            () => protocolAdapter().EnableRoSpecAsync(reader, messageIds.Next(), roSpecId, cancellationToken), cancellationToken);

    public Task DisableAsync(uint roSpecId, CancellationToken cancellationToken = default) =>
        roSpecId == 0
            ? throw new ArgumentOutOfRangeException(nameof(roSpecId), "Zero selects all resources; use an explicit takeover policy.")
            :
        reader.ExecuteExpertResourceOperationAsync(
            () => protocolAdapter().DisableRoSpecAsync(reader, messageIds.Next(), roSpecId, cancellationToken), cancellationToken);

    public Task StartAsync(uint roSpecId, CancellationToken cancellationToken = default) =>
        reader.ExecuteExpertResourceOperationAsync(
            () => protocolAdapter().StartRoSpecAsync(reader, messageIds.Next(), roSpecId, cancellationToken), cancellationToken);

    public Task StopAsync(uint roSpecId, CancellationToken cancellationToken = default) =>
        reader.ExecuteExpertResourceOperationAsync(
            () => protocolAdapter().StopRoSpecAsync(reader, messageIds.Next(), roSpecId, cancellationToken), cancellationToken);

    public Task<IReadOnlyList<ILlrpParameter>> GetAllAsync(CancellationToken cancellationToken = default) =>
        reader.ExecuteExpertResourceQueryAsync(
            () => protocolAdapter().GetRoSpecsAsync(reader, messageIds.Next(), cancellationToken), cancellationToken);
}
