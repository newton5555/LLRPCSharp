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
    public LlrpReader? Reader { get; set; }

    public DelegateFrameObserver? FrameObserver { get; set; }

    public CancellationTokenSource? InventoryCancellation { get; set; }

    public Task? InventoryPumpTask { get; set; }

    public string? Host { get; set; }

    public int Port { get; set; } = 5084;

    public bool IsMonitoring { get; set; }

    public bool IsMonitoringTable { get; set; }

    public Action<CapturedFrame>? MonitorFrameCallback { get; set; }

    public bool IsConnected => Reader?.IsConnected == true;
}
