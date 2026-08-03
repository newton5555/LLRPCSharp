namespace LlrpNet.ProtocolModel.Definitions;

/// <summary>
/// Represents a versioned protocol delta relative to an externally selected baseline.
/// </summary>
public sealed class ProtocolDefinitionDelta
{
    /// <summary>Initializes a protocol delta.</summary>
    public ProtocolDefinitionDelta(ProtocolDefinition additions, ProtocolDefinition overrides)
    {
        ArgumentNullException.ThrowIfNull(additions);
        ArgumentNullException.ThrowIfNull(overrides);
        Additions = additions;
        Overrides = overrides;
    }

    /// <summary>Gets definitions that are new relative to the baseline.</summary>
    public ProtocolDefinition Additions { get; }

    /// <summary>Gets complete definitions that explicitly replace baseline definitions.</summary>
    public ProtocolDefinition Overrides { get; }

    /// <summary>Gets whether this delta replaces any baseline definition.</summary>
    public bool HasOverrides =>
        Overrides.Messages.Count != 0 ||
        Overrides.Parameters.Count != 0 ||
        Overrides.Enumerations.Count != 0 ||
        Overrides.Choices.Count != 0 ||
        Overrides.Vendors.Count != 0 ||
        Overrides.CustomMessages.Count != 0 ||
        Overrides.CustomParameters.Count != 0 ||
        Overrides.CustomEnumerations.Count != 0;
}
