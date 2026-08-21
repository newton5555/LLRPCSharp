namespace LlrpSdk;

/// <summary>Describes the freshness of the reader resource snapshot held by the SDK.</summary>
public enum ReaderObservedState
{
    /// <summary>No resource snapshot has been captured for the current connection.</summary>
    Unknown,

    /// <summary>A lower-level operation may have changed resources since the last snapshot.</summary>
    Stale,

    /// <summary>The SDK has a successful snapshot or has completed a known resource transition.</summary>
    Synchronized,
}
