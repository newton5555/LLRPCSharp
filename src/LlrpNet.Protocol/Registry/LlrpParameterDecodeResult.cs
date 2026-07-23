using LlrpNet.Protocol.Parameters;

namespace LlrpNet.Protocol.Registry;

/// <summary>
/// Contains one decoded parameter and the number of wire octets it consumed.
/// </summary>
/// <param name="Parameter">The decoded parameter.</param>
/// <param name="BytesConsumed">The complete TV or TLV length, including its header.</param>
public readonly record struct LlrpParameterDecodeResult(
    ILlrpParameter Parameter,
    int BytesConsumed);
