namespace LlrpSdk;

/// <summary>Describes who currently owns ROSpec and AccessSpec lifecycle for this reader.</summary>
public enum ReaderResourceMode
{
    /// <summary>No SDK high-level or manual resource session is active.</summary>
    Idle,

    /// <summary>The SDK owns a persisted high-level ROSpec and AccessSpec, but inventory is stopped.</summary>
    HighLevelConfigured,

    /// <summary>The SDK exclusively owns resources for an active high-level inventory operation.</summary>
    HighLevelRunning,

    /// <summary>Compatibility name for <see cref="HighLevelRunning"/>.</summary>
    HighLevelExclusive = HighLevelRunning,

    /// <summary>The application explicitly owns resource-level ROSpec and AccessSpec operations.</summary>
    ManualResources,

    /// <summary>Raw protocol or a failed resource transition made resource state unknown.</summary>
    StateUnknown,
}
