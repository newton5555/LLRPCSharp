using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace LlrpSdk;

/// <summary>Owns one SDK-managed inventory lifetime and its isolated tag-report stream.</summary>
/// <remarks>
/// The first report consumer claims the inventory report outlet. Once this session is read, the reader-level
/// <c>TagsReported</c> and <c>ReadTagReportsAsync</c> outlets are mutually exclusive until the inventory stops.
/// The bounded stream can drop reports when the consumer is slower than the reader; inspect
/// <see cref="DroppedReportCount"/> when loss matters.
/// </remarks>
public sealed class InventorySession : IAsyncDisposable
{
    private readonly LlrpReader reader;
    private readonly Channel<TagReport> reports;
    private readonly int reportCapacity;
    private long droppedReportCount;

    internal InventorySession(
        LlrpReader reader,
        InventorySettings settings,
        uint roSpecId,
        uint? attachedDataAccessSpecId,
        InventoryRuntimeState initialState,
        int reportCapacity)
    {
        this.reader = reader;
        this.reportCapacity = reportCapacity;
        reports = Channel.CreateBounded<TagReport>(new BoundedChannelOptions(reportCapacity)
        {
            AllowSynchronousContinuations = false,
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = true,
        });
        Settings = settings;
        RoSpecId = roSpecId;
        AttachedDataAccessSpecId = attachedDataAccessSpecId;
        State = initialState;
    }

    public InventorySettings Settings { get; }
    public InventoryRuntimeState State { get; private set; }
    public long DroppedReportCount => Interlocked.Read(ref droppedReportCount);
    internal bool IsCompleted { get; private set; }
    internal uint RoSpecId { get; }
    internal uint? AttachedDataAccessSpecId { get; }

    public IAsyncEnumerable<TagReport> ReadReportsAsync(CancellationToken cancellationToken = default) =>
        reader.ReadInventorySessionReports(this, cancellationToken);

    internal IAsyncEnumerable<TagReport> ReadReportsCore(CancellationToken cancellationToken) =>
        ReadReportsCoreAsync(cancellationToken);

    private async IAsyncEnumerable<TagReport> ReadReportsCoreAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        try
        {
            await foreach (TagReport report in reports.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return report;
            }
        }
        finally
        {
            reader.ReleaseSessionTagReportOwnership(this);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken = default) => reader.StopInventorySessionAsync(this, cancellationToken);

    public ValueTask DisposeAsync() => new(StopAsync(CancellationToken.None));

    internal void Publish(TagReport report)
    {
        if (reports.Reader.Count >= reportCapacity)
        {
            Interlocked.Increment(ref droppedReportCount);
        }

        reports.Writer.TryWrite(report);
    }

    internal void DiscardPendingReports()
    {
        while (reports.Reader.TryRead(out _)) { }
    }

    internal void SetState(InventoryRuntimeState state)
    {
        State = state;
    }

    internal void Complete(InventoryRuntimeState state)
    {
        State = state;
        IsCompleted = true;
        reader.ReleaseSessionTagReportOwnership(this);
        reports.Writer.TryComplete();
    }
}
