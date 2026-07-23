namespace LlrpNet.Core.Diagnostics;

/// <summary>
/// Identifies the direction in which an LLRP frame crosses the transport boundary.
/// </summary>
public enum LlrpFrameDirection
{
    /// <summary>
    /// The frame is being transmitted to the reader.
    /// </summary>
    Transmit,

    /// <summary>
    /// The frame was received from the reader.
    /// </summary>
    Receive,
}
