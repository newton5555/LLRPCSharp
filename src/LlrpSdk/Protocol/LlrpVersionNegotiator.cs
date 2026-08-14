using LlrpNet.Core.Protocol;
using Microsoft.Extensions.Logging;
using V11Enumerations = LlrpNet.Protocol.Enumerations.V1_1;
using V11Messages = LlrpNet.Protocol.Messages.V1_1;

namespace LlrpSdk;

/// <summary>
/// Bootstraps the protocol version before the adapter boundary exists: probes the reader's highest supported
/// LLRP version with GET_SUPPORTED_VERSION and switches the reader adapter with SET_PROTOCOL_VERSION.
/// Auto selects the highest supported version (2.0 → 1.1 → 1.0.1); Force11/Force20 reject when unavailable.
/// This is the single deliberate pre-adapter version-aware component; the facade stays version-independent.
/// </summary>
internal static class LlrpVersionNegotiator
{
    public static async Task NegotiateAsync(LlrpReader reader, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);
        LlrpReaderOptions options = reader.Options;
        if (options.ProtocolVersionPolicy == LlrpProtocolVersionPolicy.Force101)
        {
            reader.Logger.LogDebug(
                "Reader {ConnectionId} is configured to use LLRP 1.0.1 without version negotiation.",
                reader.ConnectionId);
            return;
        }

        bool requireVersion = options.ProtocolVersionPolicy is
            LlrpProtocolVersionPolicy.Force11 or LlrpProtocolVersionPolicy.Force20;
        var getSupportedVersion = new V11Messages.GET_SUPPORTED_VERSION(reader.NextMessageId());
        V11Messages.GET_SUPPORTED_VERSION_RESPONSE supported;
        try
        {
            supported = await reader.TransactSessionAsync<V11Messages.GET_SUPPORTED_VERSION_RESPONSE>(
                getSupportedVersion,
                options.RequestTimeout,
                cancellationToken,
                MatchesGetSupportedVersionResponse,
                LlrpProtocolVersion.Version11).ConfigureAwait(false);
        }
        catch (LlrpReaderOperationException exception) when (exception.StatusCode == 110 && !requireVersion)
        {
            reader.Logger.LogDebug(
                "Reader {ConnectionId} rejected LLRP version negotiation; retaining LLRP 1.0.1.",
                reader.ConnectionId);
            return;
        }

        if (supported.LLRPStatus.StatusCode != V11Enumerations.StatusCode.M_Success)
        {
            throw new LlrpReaderOperationException(
                "GET_SUPPORTED_VERSION",
                checked((ushort)supported.LLRPStatus.StatusCode),
                supported.LLRPStatus.ErrorDescription,
                supported.LLRPStatus,
                Enum.GetName(typeof(V11Enumerations.StatusCode), (long)supported.LLRPStatus.StatusCode));
        }

        bool requireVersion20 = options.ProtocolVersionPolicy == LlrpProtocolVersionPolicy.Force20;
        LlrpProtocolVersion target = supported.SupportedVersion >= (byte)LlrpProtocolVersion.Version20
            ? LlrpProtocolVersion.Version20
            : supported.SupportedVersion >= (byte)LlrpProtocolVersion.Version11
                ? requireVersion20
                    ? throw new NotSupportedException(
                        $"Reader {reader.ConnectionId} supports LLRP through {supported.SupportedVersion}, but LLRP 2.0 was required.")
                    : LlrpProtocolVersion.Version11
                : LlrpProtocolVersion.Version101;
        if (target == LlrpProtocolVersion.Version101)
        {
            if (requireVersion)
            {
                throw new NotSupportedException(
                    $"Reader {reader.ConnectionId} supports LLRP through {supported.SupportedVersion}, but a newer version was required.");
            }

            reader.Logger.LogDebug(
                "Reader {ConnectionId} supports LLRP through {SupportedVersion}; retaining LLRP 1.0.1.",
                reader.ConnectionId,
                supported.SupportedVersion);
            return;
        }

        var setProtocolVersion = new V11Messages.SET_PROTOCOL_VERSION(
            reader.NextMessageId(),
            (byte)target);
        V11Messages.SET_PROTOCOL_VERSION_RESPONSE setResponse =
            await reader.TransactSessionAsync<V11Messages.SET_PROTOCOL_VERSION_RESPONSE>(
                setProtocolVersion,
                options.RequestTimeout,
                cancellationToken,
                MatchesSetProtocolVersionResponse,
                LlrpProtocolVersion.Version11).ConfigureAwait(false);
        if (setResponse.LLRPStatus.StatusCode != V11Enumerations.StatusCode.M_Success)
        {
            throw new LlrpReaderOperationException(
                "SET_PROTOCOL_VERSION",
                checked((ushort)setResponse.LLRPStatus.StatusCode),
                setResponse.LLRPStatus.ErrorDescription,
                setResponse.LLRPStatus,
                Enum.GetName(typeof(V11Enumerations.StatusCode), (long)setResponse.LLRPStatus.StatusCode));
        }

        reader.SelectProtocolAdapter(target);
        reader.Logger.LogDebug("Reader {ConnectionId} negotiated {Version}.", reader.ConnectionId, target);
    }

    private static bool MatchesGetSupportedVersionResponse(
        LlrpMessageHeader header,
        ReadOnlyMemory<byte> frame)
    {
        return header.MessageType is V11Messages.GET_SUPPORTED_VERSION_RESPONSE.MessageType or 100;
    }

    private static bool MatchesSetProtocolVersionResponse(
        LlrpMessageHeader header,
        ReadOnlyMemory<byte> frame)
    {
        return header.MessageType is V11Messages.SET_PROTOCOL_VERSION_RESPONSE.MessageType or 100;
    }
}
