using LlrpNet.Protocol.Registry;

namespace LlrpSdk.Extensions;

/// <summary>
/// Registers a cohesive standard, vendor, or customer protocol extension before a reader connects.
/// </summary>
/// <remarks>
/// Protocol modules are intentionally separate from reader-feature extensions. A module must be registered before
/// connection because the reader's initial messages can already contain custom parameters.
/// </remarks>
public interface ILlrpProtocolModule
{
    /// <summary>Gets a stable, globally unique module identifier.</summary>
    public string Id { get; }

    /// <summary>Registers message and parameter codecs with the supplied registry.</summary>
    /// <param name="registry">The versioned codec registry owned by the reader.</param>
    /// <remarks>Conflicting wire identities must be allowed to fail; a module must not replace existing registrations.</remarks>
    public void Register(LlrpCodecRegistry registry);
}
