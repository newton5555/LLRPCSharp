using System.Collections.ObjectModel;

namespace LlrpNet.Protocol.Parameters.V1_0_1;

/// <summary>
/// Defines when and how an LLRP 1.0.1 reader emits RO reports.
/// </summary>
public sealed class RoReportSpec : ILlrpParameter
{
    /// <summary>The LLRP TLV parameter type.</summary>
    public const ushort ParameterType = 237;

    private readonly ReadOnlyCollection<ILlrpParameter> _customParameters;

    /// <summary>
    /// Initializes an RO report specification.
    /// </summary>
    /// <param name="triggerType">The report trigger.</param>
    /// <param name="n">The trigger's tag-count threshold.</param>
    /// <param name="contentSelector">The required tag-report content selector.</param>
    /// <param name="customParameters">Optional trailing Custom parameters.</param>
    public RoReportSpec(
        RoReportTriggerType triggerType,
        ushort n,
        TagReportContentSelector contentSelector,
        IEnumerable<ILlrpParameter>? customParameters = null)
    {
        if (!IsDefined(triggerType))
        {
            throw new ArgumentOutOfRangeException(
                nameof(triggerType),
                triggerType,
                "The RO report trigger must be defined by LLRP 1.0.1.");
        }

        ArgumentNullException.ThrowIfNull(contentSelector);
        ILlrpParameter[] customArray = customParameters?.ToArray() ?? [];
        if (customArray.Any(static parameter => parameter is null))
        {
            throw new ArgumentException(
                "An ROReportSpec Custom collection cannot contain null entries.",
                nameof(customParameters));
        }

        TriggerType = triggerType;
        N = n;
        ContentSelector = contentSelector;
        _customParameters = Array.AsReadOnly(customArray);
    }

    /// <summary>Gets the report trigger.</summary>
    public RoReportTriggerType TriggerType { get; }

    /// <summary>Gets the trigger's tag-count threshold.</summary>
    public ushort N { get; }

    /// <summary>Gets the required tag-report content selector.</summary>
    public TagReportContentSelector ContentSelector { get; }

    /// <summary>Gets trailing Custom parameters in wire order.</summary>
    public IReadOnlyList<ILlrpParameter> CustomParameters => _customParameters;

    internal static bool IsDefined(RoReportTriggerType value)
    {
        return value is >= RoReportTriggerType.None and <= RoReportTriggerType.UponNTagsOrEndOfRoSpec;
    }
}
