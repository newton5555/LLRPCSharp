namespace LlrpNet.ProtocolGenerator.Generation;

/// <summary>
/// Represents one generated C# source file.
/// </summary>
/// <param name="HintName">A stable, relative hint name for the source.</param>
/// <param name="Kind">The kind of protocol symbol emitted by the source.</param>
/// <param name="SourceText">The complete UTF-8-compatible C# source text.</param>
public sealed record GeneratedSourceFile(
    string HintName,
    GeneratedSourceKind Kind,
    string SourceText);

/// <summary>
/// Identifies the protocol symbol emitted by a generated source file.
/// </summary>
public enum GeneratedSourceKind
{
    /// <summary>An enumeration definition.</summary>
    Enumeration,

    /// <summary>A choice marker interface.</summary>
    Choice,

    /// <summary>An LLRP message model.</summary>
    Message,

    /// <summary>An LLRP parameter model.</summary>
    Parameter,

    /// <summary>Shared binary-codec runtime support.</summary>
    CodecRuntime,

    /// <summary>A strongly typed message or parameter codec.</summary>
    Codec,

    /// <summary>A module that registers generated codecs.</summary>
    RegistryModule,
}
