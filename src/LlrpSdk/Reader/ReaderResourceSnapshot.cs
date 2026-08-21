using LlrpNet.Protocol.Parameters;

namespace LlrpSdk;

/// <summary>Immutable observation of standard ROSpec and AccessSpec resources on a reader.</summary>
public sealed record ReaderResourceSnapshot(
    IReadOnlyList<ILlrpParameter> RoSpecs,
    IReadOnlyList<ILlrpParameter> AccessSpecs,
    bool HasManagedInventory,
    bool HasForeignResources,
    DateTimeOffset CapturedAtUtc)
{
    /// <summary>Gets an empty snapshot suitable for callers that need a stable collection instance.</summary>
    public static ReaderResourceSnapshot Empty { get; } = new(
        Array.Empty<ILlrpParameter>(),
        Array.Empty<ILlrpParameter>(),
        HasManagedInventory: false,
        HasForeignResources: false,
        DateTimeOffset.MinValue);
}
