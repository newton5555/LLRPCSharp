namespace LlrpSdk;

/// <summary>Describes who currently owns ROSpec and AccessSpec lifecycle for this reader.</summary>
public enum ReaderResourceMode
{
    /// <summary>No SDK high-level or manual resource session is active.</summary>
    Idle,

    /// <summary>The SDK exclusively owns the resources created for a high-level inventory operation.</summary>
    HighLevelExclusive,

    /// <summary>The application explicitly owns resource-level ROSpec and AccessSpec operations.</summary>
    ManualResources,

    /// <summary>Raw protocol or a failed resource transition made resource state unknown.</summary>
    StateUnknown,
}
