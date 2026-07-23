namespace LlrpSdk;

/// <summary>
/// Describes the high-level operation currently managed by one reader.
/// </summary>
public enum ReaderOperationState
{
    /// <summary>
    /// No managed reader operation is active.
    /// </summary>
    Idle,

    /// <summary>
    /// A managed inventory operation is starting.
    /// </summary>
    Starting,

    /// <summary>
    /// A managed inventory operation is active.
    /// </summary>
    Inventorying,

    /// <summary>
    /// A managed inventory operation is stopping.
    /// </summary>
    Stopping,

    /// <summary>
    /// A managed tag-access operation is active.
    /// </summary>
    Accessing,
}
