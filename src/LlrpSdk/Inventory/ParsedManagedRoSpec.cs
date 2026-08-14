using LlrpNet.Protocol.Parameters;

namespace LlrpSdk;

/// <summary>
/// Neutral reverse-compilation of one SDK-managed ROSpec: the standard inventory intent fields plus the custom
/// parameters that extension contributors consume, before the version-neutral assembly stage.
/// </summary>
internal sealed record ParsedManagedRoSpec(
    InventorySettings Settings,
    IReadOnlyList<ILlrpParameter> ReportCustomItems,
    IReadOnlyList<ILlrpParameter> CommandCustomItems,
    InventoryRuntimeState State);
