using LlrpNet.Core.Protocol;

namespace LlrpNet.Protocol.Registry.V1_0_1;

/// <summary>
/// Registers standard LLRP 1.0.1 parameter and message codecs.
/// </summary>
public static class Llrp101StandardModule
{
    /// <summary>
    /// Registers all standard LLRP 1.0.1 codecs.
    /// </summary>
    /// <param name="registry">The mutable codec registry to populate.</param>
    public static void Register(LlrpCodecRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        V1_0_1ProtocolModule.Register(registry);
    }
}
