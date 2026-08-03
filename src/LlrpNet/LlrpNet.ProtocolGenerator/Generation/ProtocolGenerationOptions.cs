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
    /// Gets the root namespace of definitions supplied through the validation dependency context.
    /// When omitted, dependencies are assumed to be generated into <see cref="RootNamespace"/>.
    /// </summary>
    public string? DependencyRootNamespace { get; init; }

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

    /// <summary>
    /// Gets an optional registry module class name. When omitted, the version-derived standard module name is used.
    /// </summary>
    public string? RegistryModuleName { get; init; }
}
