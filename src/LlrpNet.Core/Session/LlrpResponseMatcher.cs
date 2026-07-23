using LlrpNet.Core.Protocol;

namespace LlrpNet.Core.Session;

/// <summary>
/// Determines whether a received frame is a valid response for one pending request.
/// </summary>
/// <param name="header">The validated common header of the received frame.</param>
/// <param name="frame">The exact complete received frame.</param>
/// <returns><see langword="true"/> only when the frame may complete the transaction.</returns>
/// <remarks>
/// The session invokes this delegate on its receive loop. Implementations must be fast, non-blocking, and must not
/// throw. A frame with the same message identifier that is rejected by this matcher remains reader-initiated and is
/// published through the unsolicited-frame stream.
/// </remarks>
public delegate bool LlrpResponseMatcher(
    LlrpMessageHeader header,
    ReadOnlyMemory<byte> frame);
