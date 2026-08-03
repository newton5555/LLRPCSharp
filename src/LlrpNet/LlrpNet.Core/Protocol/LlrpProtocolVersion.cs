namespace LlrpNet.Core.Protocol;

/// <summary>
/// Identifies the protocol version encoded in the three-bit LLRP message header field.
/// </summary>
public enum LlrpProtocolVersion : byte
{
    /// <summary>
    /// LLRP 1.0.1, encoded as header value <c>1</c>.
    /// </summary>
    Version101 = 1,

    /// <summary>
    /// LLRP 1.1, encoded as header value <c>2</c>.
    /// </summary>
    Version11 = 2,

    /// <summary>
    /// LLRP 2.0, encoded as header value <c>3</c>.
    /// </summary>
    Version20 = 3,
}

