namespace LlrpNet.Protocol.Parameters;

/// <summary>
/// Identifies the wire header form used by an LLRP parameter.
/// </summary>
public enum LlrpParameterEncoding
{
    /// <summary>
    /// The one-octet type-value header. Its total length is defined by the registered parameter type.
    /// </summary>
    Tv,

    /// <summary>
    /// The four-octet type-length-value header.
    /// </summary>
    Tlv,
}
