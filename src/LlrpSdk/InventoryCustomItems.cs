using LlrpNet.Protocol.Parameters;

namespace LlrpSdk;

/// <summary>Vendor parameters contributed to supported locations in one SDK-managed inventory ROSpec.</summary>
internal sealed record InventoryCustomItems(
    IReadOnlyList<ILlrpParameter> RoReportSpec,
    IReadOnlyList<ILlrpParameter> C1G2InventoryCommand);
