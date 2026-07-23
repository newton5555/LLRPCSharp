namespace LlrpNet.Protocol.Parameters.V1_0_1;

/// <summary>
/// Identifies the status of an LLRP 1.0.1 operation.
/// </summary>
public enum LlrpStatusCode : ushort
{
    /// <summary>The message was processed successfully (M_Success).</summary>
    MSuccess = 0,

    /// <summary>The message contains a parameter error (M_ParameterError).</summary>
    MParameterError = 100,

    /// <summary>The message contains a field error (M_FieldError).</summary>
    MFieldError = 101,

    /// <summary>The message contains an unexpected parameter (M_UnexpectedParameter).</summary>
    MUnexpectedParameter = 102,

    /// <summary>The message is missing a required parameter (M_MissingParameter).</summary>
    MMissingParameter = 103,

    /// <summary>The message contains a duplicate parameter (M_DuplicateParameter).</summary>
    MDuplicateParameter = 104,

    /// <summary>The message contains too many repeated parameters (M_OverflowParameter).</summary>
    MOverflowParameter = 105,

    /// <summary>The message contains a field value that overflows its representation (M_OverflowField).</summary>
    MOverflowField = 106,

    /// <summary>The message contains an unknown parameter (M_UnknownParameter).</summary>
    MUnknownParameter = 107,

    /// <summary>The message contains an unknown field (M_UnknownField).</summary>
    MUnknownField = 108,

    /// <summary>The message type is unsupported (M_UnsupportedMessage).</summary>
    MUnsupportedMessage = 109,

    /// <summary>The protocol version is unsupported (M_UnsupportedVersion).</summary>
    MUnsupportedVersion = 110,

    /// <summary>The message contains an unsupported parameter (M_UnsupportedParameter).</summary>
    MUnsupportedParameter = 111,

    /// <summary>A parameter contains a nested parameter error (P_ParameterError).</summary>
    PParameterError = 200,

    /// <summary>A parameter contains a field error (P_FieldError).</summary>
    PFieldError = 201,

    /// <summary>A parameter contains an unexpected nested parameter (P_UnexpectedParameter).</summary>
    PUnexpectedParameter = 202,

    /// <summary>A parameter is missing a required nested parameter (P_MissingParameter).</summary>
    PMissingParameter = 203,

    /// <summary>A parameter contains a duplicate nested parameter (P_DuplicateParameter).</summary>
    PDuplicateParameter = 204,

    /// <summary>A parameter contains too many repeated nested parameters (P_OverflowParameter).</summary>
    POverflowParameter = 205,

    /// <summary>A parameter field value overflows its representation (P_OverflowField).</summary>
    POverflowField = 206,

    /// <summary>A parameter contains an unknown nested parameter (P_UnknownParameter).</summary>
    PUnknownParameter = 207,

    /// <summary>A parameter contains an unknown field (P_UnknownField).</summary>
    PUnknownField = 208,

    /// <summary>A parameter contains an unsupported nested parameter (P_UnsupportedParameter).</summary>
    PUnsupportedParameter = 209,

    /// <summary>An access operation is invalid (A_Invalid).</summary>
    AInvalid = 300,

    /// <summary>An access-operation value is outside its valid range (A_OutOfRange).</summary>
    AOutOfRange = 301,

    /// <summary>The reader device reported an internal error (R_DeviceError).</summary>
    RDeviceError = 401,
}
