using LlrpNet.Protocol.Parameters;

namespace LlrpSdk;

/// <summary>Internal Adapter output retaining decoded configuration custom parameters for active contributors.</summary>
internal sealed record TranslatedReaderConfiguration(
    ReaderConfiguration Configuration,
    IReadOnlyList<ILlrpParameter> CustomItems);
