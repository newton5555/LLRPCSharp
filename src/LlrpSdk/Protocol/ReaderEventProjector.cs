using LlrpNet.Protocol.Messages;
using V101Messages = LlrpNet.Protocol.Messages.V1_0_1;
using V11Messages = LlrpNet.Protocol.Messages.V1_1;
using V20Messages = LlrpNet.Protocol.Messages.V2_0;

namespace LlrpSdk;

/// <summary>
/// Dispatches one decoded message to the projector of its wire version. This is the single version dispatch
/// point for event projection; the facade stays version-independent while preserving the previous behavior of
/// accepting event notifications encoded in either supported version.
/// </summary>
internal static class ReaderEventProjector
{
    public static IReadOnlyList<ReaderEventProjection> Project(ILlrpMessage message)
    {
        if (message is V101Messages.READER_EVENT_NOTIFICATION v101)
        {
            return Llrp101EventProjector.Project(v101);
        }

        if (message is V11Messages.READER_EVENT_NOTIFICATION v11)
        {
            return Llrp11EventProjector.Project(v11);
        }

        if (message is V20Messages.READER_EVENT_NOTIFICATION v20)
        {
            return Llrp20EventProjector.Project(v20);
        }

        return [];
    }
}
