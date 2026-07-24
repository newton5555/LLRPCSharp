namespace LlrpSdk;

/// <summary>
/// Represents timestamps reported by a reader for one tag observation.
/// </summary>
/// <remarks>
/// Readers may report UTC time, reader uptime, or both. Values remain in protocol microseconds so the SDK does not
/// assume that a device clock is synchronized or impose an epoch conversion policy.
/// </remarks>
public sealed record TagTimestamp(
    ulong? UtcMicroseconds,
    ulong? UptimeMicroseconds)
{
    /// <summary>
    /// Gets a value indicating whether neither timestamp source was reported.
    /// </summary>
    public bool IsEmpty => UtcMicroseconds is null && UptimeMicroseconds is null;
}
