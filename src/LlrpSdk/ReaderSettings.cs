namespace LlrpSdk;

/// <summary>
/// Describes the version-independent intent for one managed inventory operation.
/// </summary>
/// <remarks>
/// The SDK compiles these settings into the protocol version selected for the reader. This type deliberately
/// contains no LLRP message or parameter types.
/// </remarks>
public sealed record ReaderSettings
{
    /// <summary>
    /// Gets the identifier reserved for the SDK-managed inventory ROSpec.
    /// </summary>
    /// <remarks>
    /// The value must be non-zero and must not conflict with a ROSpec managed through the advanced resource API.
    /// </remarks>
    public uint RoSpecId { get; init; } = 1;

    /// <summary>
    /// Gets the reader antenna identifiers to use. The default value <c>0</c> selects all reader antennas.
    /// </summary>
    public IReadOnlyList<ushort> AntennaIds { get; init; } = [0];

    /// <summary>
    /// Gets the priority assigned to the SDK-managed ROSpec.
    /// </summary>
    public byte Priority { get; init; }

    /// <summary>
    /// Gets the inventory parameter specification identifier inside the managed ROSpec.
    /// </summary>
    public ushort InventoryParameterSpecId { get; init; } = 1;

    /// <summary>
    /// Gets the number of observed tags that trigger one report. The default reports each observed tag.
    /// </summary>
    public ushort ReportEveryNTags { get; init; } = 1;
}
