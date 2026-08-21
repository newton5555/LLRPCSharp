namespace LlrpSdk;

/// <summary>
/// Controls which reader resources a managed deployment may replace.
/// </summary>
public enum ResourceTakeoverPolicy
{
    /// <summary>
    /// Replace only SDK-reserved resources and preserve resources owned by other clients.
    /// </summary>
    PreserveForeign,

    /// <summary>
    /// Explicitly delete all standard ROSpec and AccessSpec resources before deployment.
    /// </summary>
    ReplaceAll,
}
