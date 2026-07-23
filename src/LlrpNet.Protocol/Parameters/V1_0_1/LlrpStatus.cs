using System.Collections.ObjectModel;

namespace LlrpNet.Protocol.Parameters.V1_0_1;

/// <summary>
/// Reports the status of an LLRP 1.0.1 operation and preserves any nested error parameters.
/// </summary>
public sealed class LlrpStatus : ILlrpParameter
{
    /// <summary>
    /// The LLRP TLV parameter type.
    /// </summary>
    public const ushort ParameterType = 287;

    private readonly ReadOnlyCollection<ILlrpParameter> _errorParameters;

    /// <summary>
    /// Initializes a new instance of the <see cref="LlrpStatus"/> class.
    /// </summary>
    /// <param name="statusCode">The standard LLRP 1.0.1 status code.</param>
    /// <param name="errorDescription">The UTF-8 error description.</param>
    /// <param name="errorParameters">Optional FieldError and ParameterError parameters in wire order.</param>
    public LlrpStatus(
        LlrpStatusCode statusCode,
        string errorDescription = "",
        IEnumerable<ILlrpParameter>? errorParameters = null)
    {
        if (!IsDefined(statusCode))
        {
            throw new ArgumentOutOfRangeException(
                nameof(statusCode),
                statusCode,
                "The status code must be defined by LLRP 1.0.1.");
        }

        ArgumentNullException.ThrowIfNull(errorDescription);
        ILlrpParameter[] parameterArray = errorParameters?.ToArray() ?? [];
        if (parameterArray.Any(static parameter => parameter is null))
        {
            throw new ArgumentException("An error parameter collection cannot contain null entries.", nameof(errorParameters));
        }

        StatusCode = statusCode;
        ErrorDescription = errorDescription;
        _errorParameters = Array.AsReadOnly(parameterArray);
    }

    /// <summary>
    /// Gets the standard status code.
    /// </summary>
    public LlrpStatusCode StatusCode { get; }

    /// <summary>
    /// Gets the human-readable error description.
    /// </summary>
    public string ErrorDescription { get; }

    /// <summary>
    /// Gets nested FieldError and ParameterError parameters in their original wire order.
    /// </summary>
    public IReadOnlyList<ILlrpParameter> ErrorParameters => _errorParameters;

    internal static bool IsDefined(LlrpStatusCode value)
    {
        return value is LlrpStatusCode.MSuccess
            or LlrpStatusCode.MParameterError
            or LlrpStatusCode.MFieldError
            or LlrpStatusCode.MUnexpectedParameter
            or LlrpStatusCode.MMissingParameter
            or LlrpStatusCode.MDuplicateParameter
            or LlrpStatusCode.MOverflowParameter
            or LlrpStatusCode.MOverflowField
            or LlrpStatusCode.MUnknownParameter
            or LlrpStatusCode.MUnknownField
            or LlrpStatusCode.MUnsupportedMessage
            or LlrpStatusCode.MUnsupportedVersion
            or LlrpStatusCode.MUnsupportedParameter
            or LlrpStatusCode.PParameterError
            or LlrpStatusCode.PFieldError
            or LlrpStatusCode.PUnexpectedParameter
            or LlrpStatusCode.PMissingParameter
            or LlrpStatusCode.PDuplicateParameter
            or LlrpStatusCode.POverflowParameter
            or LlrpStatusCode.POverflowField
            or LlrpStatusCode.PUnknownParameter
            or LlrpStatusCode.PUnknownField
            or LlrpStatusCode.PUnsupportedParameter
            or LlrpStatusCode.AInvalid
            or LlrpStatusCode.AOutOfRange
            or LlrpStatusCode.RDeviceError;
    }
}
