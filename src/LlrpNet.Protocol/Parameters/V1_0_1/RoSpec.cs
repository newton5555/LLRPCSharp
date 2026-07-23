using System.Collections.ObjectModel;

namespace LlrpNet.Protocol.Parameters.V1_0_1;

/// <summary>
/// Defines one LLRP 1.0.1 reader operation specification.
/// </summary>
public sealed class RoSpec : ILlrpParameter
{
    /// <summary>The LLRP TLV parameter type.</summary>
    public const ushort ParameterType = 177;

    private readonly ReadOnlyCollection<ILlrpParameter> _specParameters;

    /// <summary>
    /// Initializes a ROSpec.
    /// </summary>
    /// <param name="roSpecId">The client-assigned ROSpec identifier.</param>
    /// <param name="priority">The ROSpec priority.</param>
    /// <param name="currentState">The current ROSpec lifecycle state.</param>
    /// <param name="boundarySpec">The required start and stop boundary.</param>
    /// <param name="specParameters">One or more AISpec, RFSurveySpec, or Custom choice parameters.</param>
    /// <param name="reportSpec">An optional report specification.</param>
    public RoSpec(
        uint roSpecId,
        byte priority,
        RoSpecState currentState,
        RoBoundarySpec boundarySpec,
        IEnumerable<ILlrpParameter> specParameters,
        RoReportSpec? reportSpec = null)
    {
        if (!IsDefined(currentState))
        {
            throw new ArgumentOutOfRangeException(
                nameof(currentState),
                currentState,
                "The ROSpec state must be defined by LLRP 1.0.1.");
        }

        ArgumentNullException.ThrowIfNull(boundarySpec);
        ArgumentNullException.ThrowIfNull(specParameters);
        ILlrpParameter[] parameterArray = specParameters.ToArray();
        if (parameterArray.Length == 0)
        {
            throw new ArgumentException(
                "A ROSpec requires at least one SpecParameter choice.",
                nameof(specParameters));
        }

        if (parameterArray.Any(static parameter => parameter is null))
        {
            throw new ArgumentException(
                "A ROSpec SpecParameter collection cannot contain null entries.",
                nameof(specParameters));
        }

        RoSpecId = roSpecId;
        Priority = priority;
        CurrentState = currentState;
        BoundarySpec = boundarySpec;
        _specParameters = Array.AsReadOnly(parameterArray);
        ReportSpec = reportSpec;
    }

    /// <summary>Gets the client-assigned ROSpec identifier.</summary>
    public uint RoSpecId { get; }

    /// <summary>Gets the ROSpec priority.</summary>
    public byte Priority { get; }

    /// <summary>Gets the current ROSpec lifecycle state.</summary>
    public RoSpecState CurrentState { get; }

    /// <summary>Gets the required start and stop boundary.</summary>
    public RoBoundarySpec BoundarySpec { get; }

    /// <summary>Gets AISpec, RFSurveySpec, or Custom choice parameters in wire order.</summary>
    public IReadOnlyList<ILlrpParameter> SpecParameters => _specParameters;

    /// <summary>Gets the optional report specification.</summary>
    public RoReportSpec? ReportSpec { get; }

    internal static bool IsDefined(RoSpecState value)
    {
        return value is >= RoSpecState.Disabled and <= RoSpecState.Active;
    }
}
