using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Messages;
using LlrpNet.Protocol.Messages.V1_0_1;
using LlrpNet.Protocol.Parameters;
using LlrpNet.Protocol.Registry;
using LlrpNet.Protocol.Registry.V1_0_1;

namespace LlrpSdk;

/// <summary>LLRP 1.0.1 implementation of the SDK protocol-adapter boundary.</summary>
internal sealed class Llrp101ProtocolAdapter : ILlrpProtocolAdapter
{
    public LlrpProtocolVersion Version => LlrpProtocolVersion.Version101;

    public void RegisterStandardCodecs(LlrpCodecRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        Llrp101StandardModule.Register(registry);
    }

    public ILlrpParameter CompileInventory(ReaderSettings settings) => Llrp101InventoryCompiler.Compile(settings);

    public IReadOnlyList<TagReport> TranslateTagReports(ILlrpMessage message) =>
        message is RO_ACCESS_REPORT report ? Llrp101TagReportTranslator.Translate(report) : [];
}
