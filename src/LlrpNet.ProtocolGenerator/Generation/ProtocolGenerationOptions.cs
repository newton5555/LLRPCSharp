namespace LlrpNet.ProtocolGenerator.Generation;

/// <summary>
/// Configures the namespace layout used by generated protocol model sources.
/// </summary>
public sealed record ProtocolGenerationOptions
{
    /// <summary>
    /// Gets the root namespace placed before the Messages, Parameters, Enumerations, and Choices segments.
    /// </summary>
    public string RootNamespace { get; init; } = "LlrpNet.Protocol.Generated";

    /// <summary>
    /// Gets the final namespace segment that identifies the generated protocol version.
    /// </summary>
    public string VersionNamespace { get; init; } = "V1_0_1";

    /// <summary>
    /// Gets a value indicating whether binary codecs, their shared runtime support, and a registry module are emitted.
    /// </summary>
    public bool GenerateCodecs { get; init; }

    /// <summary>
    /// Gets the numeric LLRP version value accepted and registered by generated codecs.
    /// </summary>
    public byte ProtocolVersionValue { get; init; } = 1;
}
