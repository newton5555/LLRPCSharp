namespace LlrpSdk;

/// <summary>
/// Immutable resource and graph limits reported by a reader's LLRP capabilities.
/// </summary>
/// <remarks>
/// A <see langword="null"/> value means that the reader did not return the corresponding capability
/// parameter (or that the capability response did not contain <c>LLRPCapabilities</c>). A reported zero
/// is preserved and means that the reader explicitly does not support the resource.
/// </remarks>
public sealed record ReaderResourceLimits
{
    /// <summary>Gets the maximum number of ROSpec resources, or <see langword="null"/> when unknown.</summary>
    public uint? MaxNumROSpecs { get; init; }

    /// <summary>Gets the maximum number of Spec entries in one ROSpec, or <see langword="null"/> when unknown.</summary>
    public uint? MaxNumSpecsPerROSpec { get; init; }

    /// <summary>Gets the maximum number of inventory parameter specs in one AISpec, or <see langword="null"/> when unknown.</summary>
    public uint? MaxNumInventoryParameterSpecsPerAISpec { get; init; }

    /// <summary>Gets the maximum number of AccessSpec resources, or <see langword="null"/> when unknown.</summary>
    public uint? MaxNumAccessSpecs { get; init; }

    /// <summary>Gets the maximum number of OpSpecs in one AccessSpec, or <see langword="null"/> when unknown.</summary>
    public uint? MaxNumOpSpecsPerAccessSpec { get; init; }

    /// <summary>Gets the maximum number of supported priority levels, or <see langword="null"/> when unknown.</summary>
    public uint? MaxNumPriorityLevelsSupported { get; init; }

    /// <summary>Gets the C1G2 maximum number of select filters per query, or <see langword="null"/> when unknown.</summary>
    public uint? MaxNumSelectFiltersPerQuery { get; init; }

    /// <summary>Gets a model with every limit unknown.</summary>
    public static ReaderResourceLimits Unknown { get; } = new();

    /// <summary>Creates a capability limit model while preserving zero values as explicit limits.</summary>
    internal static ReaderResourceLimits FromLlrp(
        byte? maxNumPriorityLevelsSupported,
        uint? maxNumRoSpecs,
        uint? maxNumSpecsPerRoSpec,
        uint? maxNumInventoryParameterSpecsPerAiSpec,
        uint? maxNumAccessSpecs,
        uint? maxNumOpSpecsPerAccessSpec,
        ushort? maxNumSelectFiltersPerQuery) => new()
    {
        MaxNumPriorityLevelsSupported = maxNumPriorityLevelsSupported,
        MaxNumROSpecs = maxNumRoSpecs,
        MaxNumSpecsPerROSpec = maxNumSpecsPerRoSpec,
        MaxNumInventoryParameterSpecsPerAISpec = maxNumInventoryParameterSpecsPerAiSpec,
        MaxNumAccessSpecs = maxNumAccessSpecs,
        MaxNumOpSpecsPerAccessSpec = maxNumOpSpecsPerAccessSpec,
        MaxNumSelectFiltersPerQuery = maxNumSelectFiltersPerQuery,
    };
}
