namespace LlrpSdk;

/// <summary>
/// Version-independent projection of one reader event notification produced by a version-specific projector.
/// The facade publishes each projection through the matching public event without knowing wire types.
/// </summary>
internal abstract record ReaderEventProjection;

internal sealed record ManagedRoSpecEventProjection(
    uint? RoSpecId,
    InventoryRuntimeState? State) : ReaderEventProjection;

internal sealed record GpiChangedEventProjection(
    ushort PortNumber,
    bool State) : ReaderEventProjection;

internal sealed record AntennaChangedEventProjection(
    ushort AntennaId,
    bool IsConnected) : ReaderEventProjection;

internal sealed record ReportBufferOverflowEventProjection : ReaderEventProjection;

internal sealed record ReportBufferWarningEventProjection(
    byte PercentageFull) : ReaderEventProjection;

internal sealed record ReaderExceptionEventProjection(
    string Message,
    uint? RoSpecId,
    ushort? SpecIndex,
    ushort? InventoryParameterSpecId,
    ushort? AntennaId,
    uint? AccessSpecId,
    ushort? OpSpecId) : ReaderEventProjection;
