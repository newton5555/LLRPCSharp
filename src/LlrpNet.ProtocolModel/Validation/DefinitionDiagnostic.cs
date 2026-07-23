namespace LlrpNet.ProtocolModel.Validation;

/// <summary>
/// Describes one deterministic protocol-model validation result.
/// </summary>
/// <param name="Code">A stable machine-readable diagnostic code.</param>
/// <param name="Severity">The diagnostic severity.</param>
/// <param name="Location">The logical definition or member location.</param>
/// <param name="Message">A human-readable explanation.</param>
public sealed record DefinitionDiagnostic(
    string Code,
    DefinitionDiagnosticSeverity Severity,
    string Location,
    string Message);

/// <summary>
/// Defines protocol-model diagnostic severities.
/// </summary>
public enum DefinitionDiagnosticSeverity
{
    /// <summary>
    /// The definition is usable but deserves attention.
    /// </summary>
    Warning,

    /// <summary>
    /// Safe code generation is not possible.
    /// </summary>
    Error,
}
