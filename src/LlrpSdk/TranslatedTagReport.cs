using LlrpNet.Protocol.Parameters;

namespace LlrpSdk;

/// <summary>Internal adapter output that retains decoded vendor parameters for active contributors.</summary>
internal sealed record TranslatedTagReport(
    TagReport Report,
    IReadOnlyList<ILlrpParameter> CustomItems);
