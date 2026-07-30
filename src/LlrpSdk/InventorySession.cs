using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace LlrpSdk;

/// <summary>Owns one SDK-managed inventory lifetime and its isolated tag-report stream.</summary>
public sealed class InventorySession : IAsyncDisposable
{
    private readonly LlrpReader reader;
    private readonly Channel<TagReport> reports = Channel.CreateUnbounded<TagReport>();

    internal InventorySession(
        LlrpReader reader,
        InventorySettings settings,
        uint roSpecId,
        uint? attachedDataAccessSpecId,
        InventoryRuntimeState initialState)
    {
        this.reader = reader;
        Settings = settings;
        RoSpecId = roSpecId;
        AttachedDataAccessSpecId = attachedDataAccessSpecId;
        State = initialState;
    }

    public InventorySettings Settings { get; }
    public InventoryRuntimeState State { get; private set; }
    internal uint RoSpecId { get; }
    internal uint? AttachedDataAccessSpecId { get; }

    public IAsyncEnumerable<TagReport> ReadReportsAsync(CancellationToken cancellationToken = default) =>
        reports.Reader.ReadAllAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken = default) => reader.StopInventorySessionAsync(this, cancellationToken);

    public ValueTask DisposeAsync() => new(StopAsync(CancellationToken.None));

    internal void Publish(TagReport report) => reports.Writer.TryWrite(report);

    internal void SetState(InventoryRuntimeState state)
    {
        State = state;
    }

    internal void Complete(InventoryRuntimeState state)
    {
        State = state;
        reports.Writer.TryComplete();
    }
}
