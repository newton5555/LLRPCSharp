using System.Collections.ObjectModel;

namespace LlrpNet.ProtocolGenerator.Generation;

/// <summary>
/// Contains the complete result of one protocol source-generation operation.
/// </summary>
public sealed class ProtocolGenerationResult
{
    /// <summary>
    /// Initializes a generation result.
    /// </summary>
    public ProtocolGenerationResult(
        IEnumerable<GeneratedSourceFile> sources,
        IEnumerable<ProtocolGenerationDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(diagnostics);

        GeneratedSourceFile[] sourceCopy = sources.ToArray();
        ProtocolGenerationDiagnostic[] diagnosticCopy = diagnostics.ToArray();
        if (sourceCopy.Any(static source => source is null))
        {
            throw new ArgumentException("A source collection cannot contain null entries.", nameof(sources));
        }

        if (diagnosticCopy.Any(static diagnostic => diagnostic is null))
        {
            throw new ArgumentException(
                "A diagnostic collection cannot contain null entries.",
                nameof(diagnostics));
        }

        Sources = new ReadOnlyCollection<GeneratedSourceFile>(sourceCopy);
        Diagnostics = new ReadOnlyCollection<ProtocolGenerationDiagnostic>(diagnosticCopy);
    }

    /// <summary>
    /// Gets generated sources ordered by their ordinal hint names.
    /// </summary>
    public IReadOnlyList<GeneratedSourceFile> Sources { get; }

    /// <summary>
    /// Gets validation and generation diagnostics in deterministic evaluation order.
    /// </summary>
    public IReadOnlyList<ProtocolGenerationDiagnostic> Diagnostics { get; }

    /// <summary>
    /// Gets a value indicating whether generation completed without errors.
    /// </summary>
    public bool Succeeded => Diagnostics.All(
        static diagnostic => diagnostic.Severity != ProtocolGenerationDiagnosticSeverity.Error);
}
