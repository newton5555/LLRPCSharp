namespace LlrpNet.Core.Session;

/// <summary>
/// Selects how a session reacts when its bounded unsolicited-frame queue is full.
/// </summary>
public enum LlrpUnsolicitedFrameOverflowPolicy
{
    /// <summary>
    /// Faults the connection so an application never loses a reader-initiated message silently.
    /// </summary>
    FaultConnection = 0,

    /// <summary>
    /// Drops the newly received unsolicited frame, increments the drop counter, and keeps the connection alive.
    /// </summary>
    DropNewest = 1,
}
