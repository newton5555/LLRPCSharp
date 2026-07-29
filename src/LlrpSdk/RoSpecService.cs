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
        return reader.ExecuteManualResourceOperationAsync(
            () => protocolAdapter().AddRoSpecAsync(reader, messageIds.Next(), roSpec, cancellationToken), cancellationToken);
    }

    public Task AddDefaultAsync(InventorySettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return AddAsync(reader.CompileDefaultInventoryRoSpec(settings), cancellationToken);
    }

    public Task DeleteAsync(uint roSpecId, CancellationToken cancellationToken = default) =>
        reader.ExecuteManualResourceOperationAsync(
            () => protocolAdapter().DeleteRoSpecAsync(reader, messageIds.Next(), roSpecId, cancellationToken), cancellationToken);

    public Task EnableAsync(uint roSpecId, CancellationToken cancellationToken = default) =>
        reader.ExecuteManualResourceOperationAsync(
            () => protocolAdapter().EnableRoSpecAsync(reader, messageIds.Next(), roSpecId, cancellationToken), cancellationToken);

    public Task DisableAsync(uint roSpecId, CancellationToken cancellationToken = default) =>
        reader.ExecuteManualResourceOperationAsync(
            () => protocolAdapter().DisableRoSpecAsync(reader, messageIds.Next(), roSpecId, cancellationToken), cancellationToken);

    public Task StartAsync(uint roSpecId, CancellationToken cancellationToken = default) =>
        reader.ExecuteManualResourceOperationAsync(
            () => protocolAdapter().StartRoSpecAsync(reader, messageIds.Next(), roSpecId, cancellationToken), cancellationToken);

    public Task StopAsync(uint roSpecId, CancellationToken cancellationToken = default) =>
        reader.ExecuteManualResourceOperationAsync(
            () => protocolAdapter().StopRoSpecAsync(reader, messageIds.Next(), roSpecId, cancellationToken), cancellationToken);

    public Task<IReadOnlyList<ILlrpParameter>> GetAllAsync(CancellationToken cancellationToken = default) =>
        protocolAdapter().GetRoSpecsAsync(reader, messageIds.Next(), cancellationToken);
}
