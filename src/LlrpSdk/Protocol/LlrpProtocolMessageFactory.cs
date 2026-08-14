using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Messages;
using V101Enumerations = LlrpNet.Protocol.Enumerations.V1_0_1;
using V101Messages = LlrpNet.Protocol.Messages.V1_0_1;
using V101Parameters = LlrpNet.Protocol.Parameters.V1_0_1;
using V11Enumerations = LlrpNet.Protocol.Enumerations.V1_1;
using V11Messages = LlrpNet.Protocol.Messages.V1_1;
using V11Parameters = LlrpNet.Protocol.Parameters.V1_1;
using V20Enumerations = LlrpNet.Protocol.Enumerations.V2_0;
using V20Messages = LlrpNet.Protocol.Messages.V2_0;
using V20Parameters = LlrpNet.Protocol.Parameters.V2_0;

namespace LlrpSdk;

/// <summary>
/// Version-aware construction and classification of the few standard protocol messages the reader lifecycle
/// answers itself (KEEPALIVE_ACK, CLOSE_CONNECTION_RESPONSE, ENABLE_EVENTS_AND_REPORTS) plus ERROR_MESSAGE
/// response classification. Construction follows the negotiated version; classification accepts either supported
/// wire version, preserving the facade's previous tolerant behavior.
/// </summary>
internal static class LlrpProtocolMessageFactory
{
    public static bool IsKeepalive(ILlrpMessage message) =>
        message is V101Messages.KEEPALIVE or V11Messages.KEEPALIVE or V20Messages.KEEPALIVE;

    public static bool IsCloseConnection(ILlrpMessage message) =>
        message is V101Messages.CLOSE_CONNECTION or V11Messages.CLOSE_CONNECTION or V20Messages.CLOSE_CONNECTION;

    public static ILlrpMessage CreateKeepaliveAck(LlrpProtocolVersion version, uint messageId) => version switch
    {
        LlrpProtocolVersion.Version101 => new V101Messages.KEEPALIVE_ACK(messageId),
        LlrpProtocolVersion.Version11 => new V11Messages.KEEPALIVE_ACK(messageId),
        LlrpProtocolVersion.Version20 => new V20Messages.KEEPALIVE_ACK(messageId),
        _ => throw new NotSupportedException(
            $"No KEEPALIVE_ACK encoder is available for LLRP {version}."),
    };

    public static ILlrpMessage CreateCloseConnectionResponse(LlrpProtocolVersion version, uint messageId) => version switch
    {
        LlrpProtocolVersion.Version101 => new V101Messages.CLOSE_CONNECTION_RESPONSE(
            messageId,
            new V101Parameters.LLRPStatus(V101Enumerations.StatusCode.M_Success, string.Empty, null, null)),
        LlrpProtocolVersion.Version11 => new V11Messages.CLOSE_CONNECTION_RESPONSE(
            messageId,
            new V11Parameters.LLRPStatus(V11Enumerations.StatusCode.M_Success, string.Empty, null, null)),
        LlrpProtocolVersion.Version20 => new V20Messages.CLOSE_CONNECTION_RESPONSE(
            messageId,
            new V20Parameters.LLRPStatus(V20Enumerations.StatusCode.M_Success, string.Empty, null, null)),
        _ => throw new NotSupportedException(
            $"No CLOSE_CONNECTION_RESPONSE encoder is available for LLRP {version}."),
    };

    public static ILlrpMessage CreateEnableEventsAndReports(LlrpProtocolVersion version, uint messageId) => version switch
    {
        LlrpProtocolVersion.Version101 => new V101Messages.ENABLE_EVENTS_AND_REPORTS(messageId),
        LlrpProtocolVersion.Version11 => new V11Messages.ENABLE_EVENTS_AND_REPORTS(messageId),
        LlrpProtocolVersion.Version20 => new V20Messages.ENABLE_EVENTS_AND_REPORTS(messageId),
        _ => throw new NotSupportedException(
            $"No ENABLE_EVENTS_AND_REPORTS encoder is available for LLRP {version}."),
    };

    public static bool TryCreateOperationException(
        string operation,
        ILlrpMessage response,
        out LlrpReaderOperationException? exception)
    {
        if (response is V101Messages.ERROR_MESSAGE v101Error)
        {
            exception = new LlrpReaderOperationException(
                operation,
                checked((ushort)v101Error.LLRPStatus.StatusCode),
                v101Error.LLRPStatus.ErrorDescription,
                v101Error.LLRPStatus,
                Enum.GetName(typeof(V101Enumerations.StatusCode), (long)v101Error.LLRPStatus.StatusCode));
            return true;
        }

        if (response is V11Messages.ERROR_MESSAGE v11Error)
        {
            exception = new LlrpReaderOperationException(
                operation,
                checked((ushort)v11Error.LLRPStatus.StatusCode),
                v11Error.LLRPStatus.ErrorDescription,
                v11Error.LLRPStatus,
                Enum.GetName(typeof(V11Enumerations.StatusCode), (long)v11Error.LLRPStatus.StatusCode));
            return true;
        }

        if (response is V20Messages.ERROR_MESSAGE v20Error)
        {
            exception = new LlrpReaderOperationException(
                operation,
                checked((ushort)v20Error.LLRPStatus.StatusCode),
                v20Error.LLRPStatus.ErrorDescription,
                v20Error.LLRPStatus,
                Enum.GetName(typeof(V20Enumerations.StatusCode), (long)v20Error.LLRPStatus.StatusCode));
            return true;
        }

        exception = null;
        return false;
    }
}
