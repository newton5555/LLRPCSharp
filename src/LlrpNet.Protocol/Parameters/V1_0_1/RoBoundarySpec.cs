namespace LlrpNet.Protocol.Parameters.V1_0_1;

/// <summary>
/// Defines the start and stop boundary for one LLRP 1.0.1 ROSpec.
/// </summary>
public sealed class RoBoundarySpec : ILlrpParameter
{
    /// <summary>The LLRP TLV parameter type.</summary>
    public const ushort ParameterType = 178;

    /// <summary>
    /// Initializes a ROSpec boundary.
    /// </summary>
    /// <param name="startTrigger">The required start trigger.</param>
    /// <param name="stopTrigger">The required stop trigger.</param>
    public RoBoundarySpec(
        RoSpecStartTrigger startTrigger,
        RoSpecStopTrigger stopTrigger)
    {
        StartTrigger = startTrigger ?? throw new ArgumentNullException(nameof(startTrigger));
        StopTrigger = stopTrigger ?? throw new ArgumentNullException(nameof(stopTrigger));
    }

    /// <summary>Gets the required start trigger.</summary>
    public RoSpecStartTrigger StartTrigger { get; }

    /// <summary>Gets the required stop trigger.</summary>
    public RoSpecStopTrigger StopTrigger { get; }
}
