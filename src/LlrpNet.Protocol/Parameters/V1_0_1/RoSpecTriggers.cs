namespace LlrpNet.Protocol.Parameters.V1_0_1;

/// <summary>
/// Defines the start condition for one LLRP 1.0.1 ROSpec.
/// </summary>
public sealed class RoSpecStartTrigger : ILlrpParameter
{
    /// <summary>The LLRP TLV parameter type.</summary>
    public const ushort ParameterType = 179;

    /// <summary>
    /// Initializes a ROSpec start trigger.
    /// </summary>
    /// <param name="triggerType">The start trigger type.</param>
    /// <param name="periodicTriggerValue">An optional PeriodicTriggerValue parameter (type 180).</param>
    /// <param name="gpiTriggerValue">An optional GPITriggerValue parameter (type 181).</param>
    public RoSpecStartTrigger(
        RoSpecStartTriggerType triggerType,
        ILlrpParameter? periodicTriggerValue = null,
        ILlrpParameter? gpiTriggerValue = null)
    {
        if (!IsDefined(triggerType))
        {
            throw new ArgumentOutOfRangeException(
                nameof(triggerType),
                triggerType,
                "The ROSpec start trigger type must be defined by LLRP 1.0.1.");
        }

        TriggerType = triggerType;
        PeriodicTriggerValue = periodicTriggerValue;
        GpiTriggerValue = gpiTriggerValue;
    }

    /// <summary>Gets the start trigger type.</summary>
    public RoSpecStartTriggerType TriggerType { get; }

    /// <summary>Gets the optional PeriodicTriggerValue parameter.</summary>
    public ILlrpParameter? PeriodicTriggerValue { get; }

    /// <summary>Gets the optional GPITriggerValue parameter.</summary>
    public ILlrpParameter? GpiTriggerValue { get; }

    internal static bool IsDefined(RoSpecStartTriggerType value)
    {
        return value is >= RoSpecStartTriggerType.Null and <= RoSpecStartTriggerType.Gpi;
    }
}

/// <summary>
/// Defines the stop condition for one LLRP 1.0.1 ROSpec.
/// </summary>
public sealed class RoSpecStopTrigger : ILlrpParameter
{
    /// <summary>The LLRP TLV parameter type.</summary>
    public const ushort ParameterType = 182;

    /// <summary>
    /// Initializes a ROSpec stop trigger.
    /// </summary>
    /// <param name="triggerType">The stop trigger type.</param>
    /// <param name="durationTriggerValue">The duration trigger value in milliseconds.</param>
    /// <param name="gpiTriggerValue">An optional GPITriggerValue parameter (type 181).</param>
    public RoSpecStopTrigger(
        RoSpecStopTriggerType triggerType,
        uint durationTriggerValue,
        ILlrpParameter? gpiTriggerValue = null)
    {
        if (!IsDefined(triggerType))
        {
            throw new ArgumentOutOfRangeException(
                nameof(triggerType),
                triggerType,
                "The ROSpec stop trigger type must be defined by LLRP 1.0.1.");
        }

        TriggerType = triggerType;
        DurationTriggerValue = durationTriggerValue;
        GpiTriggerValue = gpiTriggerValue;
    }

    /// <summary>Gets the stop trigger type.</summary>
    public RoSpecStopTriggerType TriggerType { get; }

    /// <summary>Gets the duration trigger value in milliseconds.</summary>
    public uint DurationTriggerValue { get; }

    /// <summary>Gets the optional GPITriggerValue parameter.</summary>
    public ILlrpParameter? GpiTriggerValue { get; }

    internal static bool IsDefined(RoSpecStopTriggerType value)
    {
        return value is >= RoSpecStopTriggerType.Null and <= RoSpecStopTriggerType.GpiWithTimeout;
    }
}
