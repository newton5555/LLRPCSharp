namespace LlrpNet.Core.Protocol;

/// <summary>
/// Represents malformed or unsupported data encountered in the LLRP wire protocol.
/// </summary>
public sealed class LlrpProtocolException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LlrpProtocolException"/> class.
    /// </summary>
    /// <param name="errorCode">The machine-readable error category.</param>
    /// <param name="message">A diagnostic description of the failure.</param>
    public LlrpProtocolException(LlrpProtocolErrorCode errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    /// <summary>
    /// Gets the machine-readable error category.
    /// </summary>
    public LlrpProtocolErrorCode ErrorCode { get; }
}

