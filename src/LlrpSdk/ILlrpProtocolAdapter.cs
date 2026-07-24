using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Messages;
using LlrpNet.Protocol.Parameters;
using LlrpNet.Protocol.Registry;

namespace LlrpSdk;

/// <summary>Maps one wire-protocol version to the version-independent SDK inventory boundary.</summary>
internal interface ILlrpProtocolAdapter
{
    public LlrpProtocolVersion Version { get; }

    public void RegisterStandardCodecs(LlrpCodecRegistry registry);

    public ILlrpParameter CompileInventory(ReaderSettings settings);

    public IReadOnlyList<TagReport> TranslateTagReports(ILlrpMessage message);
}
