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

    /// <summary>
    /// Gets or sets the local inventory-intent draft for the next managed inventory start.
    /// This is distinct from <see cref="LlrpReader.CurrentSettings"/>, which exists only while inventory runs.
    /// </summary>
    public ReaderSettings DesiredInventorySettings { get; set; } = new();

    public string? Host { get; set; }

    public int Port { get; set; } = 5084;

    public bool IsMonitoring { get; set; }

    public bool IsConnected => Reader?.IsConnected == true;
}
