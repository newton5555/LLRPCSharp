namespace LlrpSdk;

/// <summary>Describes the SDK-managed inventory resource state observed for this reader.</summary>
public enum ReaderResourceMode
{
    /// <summary>No SDK-managed inventory session is active.</summary>
    Idle,

    /// <summary>The SDK owns a persisted high-level ROSpec and AccessSpec, but inventory is stopped.</summary>
    HighLevelConfigured,

    /// <summary>The SDK exclusively owns resources for an active high-level inventory operation.</summary>
    HighLevelRunning,

    /// <summary>
    /// A high-level report session is attached to an ROSpec that was created outside the managed compiler.
    /// The SDK controls the session lifecycle but does not own the ROSpec definition.
    /// </summary>
    AttachedInventory,

    /// <summary>Raw protocol or a failed resource transition made resource state unknown.</summary>
    StateUnknown,
}
