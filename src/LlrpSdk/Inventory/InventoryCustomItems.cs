using LlrpNet.Protocol.Enumerations.V1_0_1;
using LlrpNet.Protocol.Messages.V1_0_1;
using LlrpNet.Protocol.Parameters;
using LlrpNet.Protocol.Parameters.V1_0_1;

namespace LlrpSdk;

/// <summary>Vendor parameters contributed to supported locations in one SDK-managed inventory ROSpec.</summary>
internal sealed record InventoryCustomItems(
    IReadOnlyList<ILlrpParameter> RoReportSpec,
    IReadOnlyList<ILlrpParameter> C1G2InventoryCommand);
