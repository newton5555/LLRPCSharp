using LlrpCli.Terminal;
using LlrpSdk;

namespace LlrpCli.Commands;

/// <summary>
/// Owns the mutable state of one interactive Live Shell session.
/// </summary>
/// <remarks>
/// Lifecycle operations remain in <see cref="LiveCommand"/>. This type only
/// groups state so handlers do not depend on fields spread across the command host.
/// </remarks>
internal sealed class LiveSessionContext
{
    private readonly object monitorStateLock = new();
    private readonly List<CapturedFrame> deferredFrames = [];

    public LlrpReader? Reader { get; set; }

    public DelegateFrameObserver? FrameObserver { get; set; }

    public CancellationTokenSource? InventoryCancellation { get; set; }

    public Task? InventoryPumpTask { get; set; }

    public InventorySession? InventorySession { get; set; }

    /// <summary>
    /// Gets or sets the application's complete high-level intent draft. The CLI owns this value;
    /// the reader owns device facts and the deployed high-level resource state.
    /// </summary>
    public ReaderSettings DesiredSettings { get; set; } = new()
    {
        Inventory = new InventorySettings()
    };

    public string? Host { get; set; }

    public int Port { get; set; } = 5084;

    /// <summary>Serializes terminal frame rendering outside Spectre's Live display.</summary>
    public object FrameRenderLock { get; } = new();

    public bool IsConnected => Reader?.IsConnected == true;

    public void BeginMonitor(LiveMonitorMode mode)
    {
        lock (monitorStateLock)
        {
            ActiveMonitorMode = mode;
        }
    }

    public IReadOnlyList<CapturedFrame> EndMonitor()
    {
        lock (monitorStateLock)
        {
            ActiveMonitorMode = null;
            CapturedFrame[] frames = deferredFrames.ToArray();
            deferredFrames.Clear();
            return frames;
        }
    }

    public bool IsRawFrameMonitorActive()
    {
        lock (monitorStateLock)
        {
            return ActiveMonitorMode == LiveMonitorMode.Frames;
        }
    }

    public bool TryDeferFrameDuringLiveMonitor(CapturedFrame frame)
    {
        lock (monitorStateLock)
        {
            if (ActiveMonitorMode != LiveMonitorMode.Live)
            {
                return false;
            }

            deferredFrames.Add(frame);
            return true;
        }
    }

    private LiveMonitorMode? ActiveMonitorMode { get; set; }
}
