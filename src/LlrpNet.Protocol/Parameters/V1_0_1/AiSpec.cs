using System.Collections.ObjectModel;

namespace LlrpNet.Protocol.Parameters.V1_0_1;

/// <summary>
/// Defines antenna inventory operations for an LLRP 1.0.1 ROSpec.
/// </summary>
public sealed class AiSpec : ILlrpParameter
{
    /// <summary>The LLRP TLV parameter type.</summary>
    public const ushort ParameterType = 183;

    private readonly ReadOnlyCollection<ushort> _antennaIds;
    private readonly ReadOnlyCollection<InventoryParameterSpec> _inventoryParameterSpecs;
    private readonly ReadOnlyCollection<ILlrpParameter> _customParameters;

    /// <summary>
    /// Initializes an AISpec.
    /// </summary>
    /// <param name="antennaIds">Antenna identifiers; zero selects all antennas.</param>
    /// <param name="stopTrigger">The required AISpec stop trigger.</param>
    /// <param name="inventoryParameterSpecs">One or more inventory parameter specifications.</param>
    /// <param name="customParameters">Optional trailing Custom parameters.</param>
    public AiSpec(
        IEnumerable<ushort> antennaIds,
        AiSpecStopTrigger stopTrigger,
        IEnumerable<InventoryParameterSpec> inventoryParameterSpecs,
        IEnumerable<ILlrpParameter>? customParameters = null)
    {
        ArgumentNullException.ThrowIfNull(antennaIds);
        ArgumentNullException.ThrowIfNull(stopTrigger);
        ArgumentNullException.ThrowIfNull(inventoryParameterSpecs);
        ushort[] antennaArray = antennaIds.ToArray();
        InventoryParameterSpec[] inventoryArray = inventoryParameterSpecs.ToArray();
        if (inventoryArray.Length == 0)
        {
            throw new ArgumentException(
                "An AISpec requires at least one InventoryParameterSpec.",
                nameof(inventoryParameterSpecs));
        }

        if (inventoryArray.Any(static parameter => parameter is null))
        {
            throw new ArgumentException(
                "An AISpec inventory collection cannot contain null entries.",
                nameof(inventoryParameterSpecs));
        }

        ILlrpParameter[] customArray = customParameters?.ToArray() ?? [];
        if (customArray.Any(static parameter => parameter is null))
        {
            throw new ArgumentException(
                "An AISpec Custom collection cannot contain null entries.",
                nameof(customParameters));
        }

        _antennaIds = Array.AsReadOnly(antennaArray);
        StopTrigger = stopTrigger;
        _inventoryParameterSpecs = Array.AsReadOnly(inventoryArray);
        _customParameters = Array.AsReadOnly(customArray);
    }

    /// <summary>Gets antenna identifiers in wire order.</summary>
    public IReadOnlyList<ushort> AntennaIds => _antennaIds;

    /// <summary>Gets the required AISpec stop trigger.</summary>
    public AiSpecStopTrigger StopTrigger { get; }

    /// <summary>Gets inventory parameter specifications in wire order.</summary>
    public IReadOnlyList<InventoryParameterSpec> InventoryParameterSpecs => _inventoryParameterSpecs;

    /// <summary>Gets trailing Custom parameters in wire order.</summary>
    public IReadOnlyList<ILlrpParameter> CustomParameters => _customParameters;
}

/// <summary>
/// Defines the stop condition for one LLRP 1.0.1 AISpec.
/// </summary>
public sealed class AiSpecStopTrigger : ILlrpParameter
{
    /// <summary>The LLRP TLV parameter type.</summary>
    public const ushort ParameterType = 184;

    /// <summary>
    /// Initializes an AISpec stop trigger.
    /// </summary>
    /// <param name="triggerType">The stop trigger type.</param>
    /// <param name="durationTrigger">The duration trigger in milliseconds.</param>
    /// <param name="gpiTriggerValue">An optional GPITriggerValue parameter (type 181).</param>
    /// <param name="tagObservationTrigger">An optional TagObservationTrigger parameter (type 185).</param>
    public AiSpecStopTrigger(
        AiSpecStopTriggerType triggerType,
        uint durationTrigger,
        ILlrpParameter? gpiTriggerValue = null,
        ILlrpParameter? tagObservationTrigger = null)
    {
        if (!IsDefined(triggerType))
        {
            throw new ArgumentOutOfRangeException(
                nameof(triggerType),
                triggerType,
                "The AISpec stop trigger type must be defined by LLRP 1.0.1.");
        }

        TriggerType = triggerType;
        DurationTrigger = durationTrigger;
        GpiTriggerValue = gpiTriggerValue;
        TagObservationTrigger = tagObservationTrigger;
    }

    /// <summary>Gets the AISpec stop trigger type.</summary>
    public AiSpecStopTriggerType TriggerType { get; }

    /// <summary>Gets the duration trigger in milliseconds.</summary>
    public uint DurationTrigger { get; }

    /// <summary>Gets the optional GPITriggerValue parameter.</summary>
    public ILlrpParameter? GpiTriggerValue { get; }

    /// <summary>Gets the optional TagObservationTrigger parameter.</summary>
    public ILlrpParameter? TagObservationTrigger { get; }

    internal static bool IsDefined(AiSpecStopTriggerType value)
    {
        return value is >= AiSpecStopTriggerType.Null and <= AiSpecStopTriggerType.TagObservation;
    }
}
