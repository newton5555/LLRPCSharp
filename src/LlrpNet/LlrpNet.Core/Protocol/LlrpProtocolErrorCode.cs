namespace LlrpNet.Core.Protocol;

/// <summary>
/// Categorizes protocol errors detected before a message-specific codec runs.
/// </summary>
public enum LlrpProtocolErrorCode
{
    /// <summary>
    /// The input ended before the required structure was complete.
    /// </summary>
    TruncatedData,

    /// <summary>
    /// Reserved header bits were not zero.
    /// </summary>
    InvalidReservedBits,

    /// <summary>
    /// The encoded protocol version is not supported by this SDK.
    /// </summary>
    UnsupportedVersion,

    /// <summary>
    /// The encoded message type does not fit in the ten-bit message type field.
    /// </summary>
    InvalidMessageType,

    /// <summary>
    /// The encoded message length is smaller than the fixed message header.
    /// </summary>
    InvalidMessageLength,

    /// <summary>
    /// A parameter header uses the wrong TV/TLV marker or reserved bits.
    /// </summary>
    InvalidParameterEncoding,

    /// <summary>
    /// The encoded parameter type is outside the range representable by its header form.
    /// </summary>
    InvalidParameterType,

    /// <summary>
    /// A TLV parameter length is smaller than its fixed header.
    /// </summary>
    InvalidParameterLength,

    /// <summary>
    /// The frame exceeds the configured defensive frame-size limit.
    /// </summary>
    FrameTooLarge,
}
