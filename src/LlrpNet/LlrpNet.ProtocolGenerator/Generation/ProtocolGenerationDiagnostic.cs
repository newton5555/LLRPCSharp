namespace LlrpNet.ProtocolGenerator.Generation;

/// <summary>
/// Describes one deterministic protocol source-generation diagnostic.
/// </summary>
/// <param name="Code">A stable machine-readable diagnostic code.</param>
/// <param name="Severity">The diagnostic severity.</param>
/// <param name="Location">The logical option or definition location.</param>
/// <param name="Message">A human-readable explanation.</param>
public sealed record ProtocolGenerationDiagnostic(
    string Code,
    ProtocolGenerationDiagnosticSeverity Severity,
    string Location,
    string Message);

/// <summary>
/// Defines source-generation diagnostic severities.
/// </summary>
public enum ProtocolGenerationDiagnosticSeverity
{
    /// <summary>
    /// Generation can continue, but the definition deserves attention.
    /// </summary>
    Warning,

    /// <summary>
    /// Safe source generation is not possible.
    /// </summary>
    Error,
}
