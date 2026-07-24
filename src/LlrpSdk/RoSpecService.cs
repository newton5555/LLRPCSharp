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
        return protocolAdapter().AddRoSpecAsync(reader, messageIds.Next(), roSpec, cancellationToken);
    }

    public Task DeleteAsync(uint roSpecId, CancellationToken cancellationToken = default) =>
        protocolAdapter().DeleteRoSpecAsync(reader, messageIds.Next(), roSpecId, cancellationToken);

    public Task EnableAsync(uint roSpecId, CancellationToken cancellationToken = default) =>
        protocolAdapter().EnableRoSpecAsync(reader, messageIds.Next(), roSpecId, cancellationToken);

    public Task DisableAsync(uint roSpecId, CancellationToken cancellationToken = default) =>
        protocolAdapter().DisableRoSpecAsync(reader, messageIds.Next(), roSpecId, cancellationToken);

    public Task StartAsync(uint roSpecId, CancellationToken cancellationToken = default) =>
        protocolAdapter().StartRoSpecAsync(reader, messageIds.Next(), roSpecId, cancellationToken);

    public Task StopAsync(uint roSpecId, CancellationToken cancellationToken = default) =>
        protocolAdapter().StopRoSpecAsync(reader, messageIds.Next(), roSpecId, cancellationToken);

    public Task<IReadOnlyList<ILlrpParameter>> GetAllAsync(CancellationToken cancellationToken = default) =>
        protocolAdapter().GetRoSpecsAsync(reader, messageIds.Next(), cancellationToken);
}
