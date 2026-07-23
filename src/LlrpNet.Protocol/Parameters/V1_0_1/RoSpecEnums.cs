namespace LlrpNet.Protocol.Parameters.V1_0_1;

/// <summary>
/// Identifies the lifecycle state carried by an LLRP 1.0.1 ROSpec.
/// </summary>
public enum RoSpecState : byte
{
    /// <summary>The ROSpec is disabled.</summary>
    Disabled = 0,

    /// <summary>The ROSpec is enabled but not executing.</summary>
    Inactive = 1,

    /// <summary>The ROSpec is executing.</summary>
    Active = 2,
}

/// <summary>
/// Identifies the trigger that starts an LLRP 1.0.1 ROSpec.
/// </summary>
public enum RoSpecStartTriggerType : byte
{
    /// <summary>The ROSpec starts only through START_ROSPEC.</summary>
    Null = 0,

    /// <summary>The ROSpec starts immediately after it becomes enabled.</summary>
    Immediate = 1,

    /// <summary>The ROSpec uses a PeriodicTriggerValue parameter.</summary>
    Periodic = 2,

    /// <summary>The ROSpec uses a GPITriggerValue parameter.</summary>
    Gpi = 3,
}

/// <summary>
/// Identifies the trigger that stops an LLRP 1.0.1 ROSpec.
/// </summary>
public enum RoSpecStopTriggerType : byte
{
    /// <summary>The ROSpec has no explicit stop trigger.</summary>
    Null = 0,

    /// <summary>The ROSpec stops after its duration value.</summary>
    Duration = 1,

    /// <summary>The ROSpec uses a GPI trigger with a timeout.</summary>
    GpiWithTimeout = 2,
}

/// <summary>
/// Identifies the trigger that stops an LLRP 1.0.1 AISpec.
/// </summary>
public enum AiSpecStopTriggerType : byte
{
    /// <summary>The AISpec stops when its enclosing ROSpec finishes.</summary>
    Null = 0,

    /// <summary>The AISpec stops after its duration value.</summary>
    Duration = 1,

    /// <summary>The AISpec uses a GPI trigger with a timeout.</summary>
    GpiWithTimeout = 2,

    /// <summary>The AISpec uses a TagObservationTrigger parameter.</summary>
    TagObservation = 3,
}

/// <summary>
/// Identifies an air protocol used by an LLRP 1.0.1 inventory parameter specification.
/// </summary>
public enum AirProtocolId : byte
{
    /// <summary>No air protocol is specified.</summary>
    Unspecified = 0,

    /// <summary>EPCglobal Class 1 Generation 2.</summary>
    EpcGlobalClass1Gen2 = 1,
}

/// <summary>
/// Identifies the event that causes an LLRP 1.0.1 RO report to be emitted.
/// </summary>
public enum RoReportTriggerType : byte
{
    /// <summary>No automatic report trigger.</summary>
    None = 0,

    /// <summary>Report after N tags or at the end of an AISpec.</summary>
    UponNTagsOrEndOfAiSpec = 1,

    /// <summary>Report after N tags or at the end of a ROSpec.</summary>
    UponNTagsOrEndOfRoSpec = 2,
}
