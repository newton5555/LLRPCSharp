namespace LlrpDevice.Abstractions;

/// <summary>
/// Device-side behavior contract consumed by the generic LLRP Server.
/// </summary>
/// <remarks>
/// This contract deliberately contains no LLRP wire types. A virtual device and a future
/// real RFID-module adapter implement the same behavior surface; the Server owns TCP,
/// protocol versions, ROSpec/AccessSpec resources, and report composition.
/// </remarks>
public interface ILlrpDevice : IAsyncDisposable
{
    public LlrpDeviceIdentity Identity { get; }

    public LlrpDeviceCapabilities Capabilities { get; }

    public LlrpDeviceConfiguration Configuration { get; }

    public event EventHandler<LlrpDeviceEvent>? EventRaised;

    public ValueTask<LlrpDeviceOperationResult> ApplyConfigurationAsync(
        LlrpDeviceConfigurationUpdate update,
        CancellationToken cancellationToken = default);

    public ValueTask<IInventoryExecution> StartInventoryAsync(
        LlrpInventoryPlan plan,
        CancellationToken cancellationToken = default);

    public ValueTask<IReadOnlyList<LlrpTagAccessResult>> ExecuteTagAccessAsync(
        LlrpTagAccessRequest request,
        CancellationToken cancellationToken = default);
}
