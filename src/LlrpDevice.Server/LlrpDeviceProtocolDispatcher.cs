using LlrpNet.Core.Protocol;
using LlrpDevice.Abstractions;
using LlrpNet.Protocol.Messages;
using LlrpNet.Protocol.Registry;
using V101Enumerations = LlrpNet.Protocol.Enumerations.V1_0_1;
using V101Messages = LlrpNet.Protocol.Messages.V1_0_1;
using V101Parameters = LlrpNet.Protocol.Parameters.V1_0_1;
using V101Registry = LlrpNet.Protocol.Registry.V1_0_1;
using V11Enumerations = LlrpNet.Protocol.Enumerations.V1_1;
using V11Messages = LlrpNet.Protocol.Messages.V1_1;
using V11Parameters = LlrpNet.Protocol.Parameters.V1_1;
using V11Registry = LlrpNet.Protocol.Registry.V1_1;
using V20Enumerations = LlrpNet.Protocol.Enumerations.V2_0;
using V20Messages = LlrpNet.Protocol.Messages.V2_0;
using V20Parameters = LlrpNet.Protocol.Parameters.V2_0;
using V20Registry = LlrpNet.Protocol.Registry.V2_0;

namespace LlrpDevice.Server;

internal sealed class LlrpDeviceProtocolDispatcher
{
    private readonly LlrpDeviceServerState _state;
    private readonly LlrpCodecRegistry _registry;
    private readonly Dictionary<LlrpProtocolVersion, ILlrpDeviceVersionProfile> _profiles;
    private readonly IReadOnlyList<ILlrpDeviceMessageHandler> _handlers;

    public LlrpDeviceProtocolDispatcher(
        LlrpDeviceServerState state,
        LlrpCodecRegistry registry,
        IReadOnlyList<ILlrpDeviceProtocolModule> modules)
    {
        _state = state;
        _registry = registry;

        var standard101 = new LlrpStandard101Handler(state);
        var standard11 = new LlrpTranslatedStandardHandler(
            state,
            registry,
            LlrpProtocolVersion.Version11);
        var standard20 = new LlrpTranslatedStandardHandler(
            state,
            registry,
            LlrpProtocolVersion.Version20);
        _profiles = new Dictionary<LlrpProtocolVersion, ILlrpDeviceVersionProfile>
        {
            [LlrpProtocolVersion.Version101] = standard101,
            [LlrpProtocolVersion.Version11] = standard11,
            [LlrpProtocolVersion.Version20] = standard20,
        };

        var handlerRegistry = new LlrpDeviceHandlerRegistry();
        foreach (ILlrpDeviceProtocolModule module in modules)
        {
            module.RegisterHandlers(handlerRegistry);
        }

        _handlers = handlerRegistry.Handlers;
    }

    public static LlrpCodecRegistry CreateRegistry(
        IReadOnlyList<ILlrpDeviceProtocolModule> modules)
    {
        var registry = new LlrpCodecRegistry();
        V101Registry.Llrp101StandardModule.Register(registry);
        V11Registry.Llrp11StandardModule.Register(registry);
        V20Registry.Llrp20StandardModule.Register(registry);
        foreach (ILlrpDeviceProtocolModule module in modules)
        {
            module.RegisterCodecs(registry);
        }

        return registry;
    }

    public async ValueTask<LlrpDeviceDispatchResult> DispatchAsync(
        LlrpDeviceRequestContext context,
        ILlrpMessage message,
        CancellationToken cancellationToken)
    {
        if (TryHandleVersionNegotiation(context, message, out LlrpDeviceDispatchResult? negotiation))
        {
            return negotiation!;
        }

        if (message is RawCustomMessage &&
            _state.Options.UnknownVendorParameterBehavior == LlrpUnknownVendorParameterBehavior.PreserveAndIgnore)
        {
            return new LlrpDeviceDispatchResult(null, []);
        }

        if (!_profiles.TryGetValue(context.Version, out ILlrpDeviceVersionProfile? profile))
        {
            return new LlrpDeviceDispatchResult(
                CreateUnsupportedVersionError(context.Version, message.MessageId),
                []);
        }

        foreach (ILlrpDeviceMessageHandler handler in _handlers)
        {
            if (!handler.CanHandle(context.Version, message))
            {
                continue;
            }

            return await handler.HandleAsync(context, message, cancellationToken).ConfigureAwait(false);
        }

        if (profile.CanHandle(context.Version, message))
        {
            return await profile.HandleAsync(context, message, cancellationToken).ConfigureAwait(false);
        }

        return new LlrpDeviceDispatchResult(
            profile.CreateError(
                message.MessageId,
                (ushort)GetStatusCode(context.Version, "M_UnsupportedMessage"),
                $"The LLRP device does not implement message type {GetMessageType(message)}."),
            []);
    }

    public IReadOnlyList<ILlrpMessage> BuildInventoryReports(
        LlrpProtocolVersion version,
        uint roSpecId,
        int roundSequence)
    {
        return _profiles.TryGetValue(version, out ILlrpDeviceVersionProfile? profile)
            ? profile.BuildInventoryReports(roSpecId, roundSequence)
            : [];
    }

    public ILlrpMessage CreateKeepalive(LlrpProtocolVersion version, uint messageId) =>
        _profiles[version].CreateKeepalive(messageId);

    public ILlrpMessage CreateReaderEventNotification(LlrpProtocolVersion version, uint messageId) =>
        _profiles[version].CreateReaderEventNotification(messageId);

    public ILlrpMessage CreateReaderEventNotification(
        LlrpProtocolVersion version,
        uint messageId,
        LlrpDeviceEvent deviceEvent) =>
        _profiles[version].CreateReaderEventNotification(messageId, deviceEvent);

    public ILlrpMessage CreateCloseConnection(LlrpProtocolVersion version, uint messageId) =>
        _profiles[version].CreateCloseConnection(messageId);

    public ILlrpMessage TranslateFromCanonical(
        LlrpProtocolVersion version,
        ILlrpMessage message) => version == LlrpProtocolVersion.Version101
            ? message
            : _profiles[version] is LlrpTranslatedStandardHandler translated
                ? translated.TranslateFromCanonical(message)
                : throw new InvalidOperationException($"Protocol version {version} has no canonical translation profile.");

    public ILlrpMessage CreateError(
        LlrpProtocolVersion version,
        uint messageId,
        ushort statusCode,
        string description) =>
        _profiles.TryGetValue(version, out ILlrpDeviceVersionProfile? profile)
            ? profile.CreateError(messageId, statusCode, description)
            : CreateUnsupportedVersionError(version, messageId);

    private bool TryHandleVersionNegotiation(
        LlrpDeviceRequestContext context,
        ILlrpMessage message,
        out LlrpDeviceDispatchResult? result)
    {
        if (message is V11Messages.GET_SUPPORTED_VERSION request)
        {
            result = BuildGetSupportedVersionResponse(request.MessageId);
            return true;
        }

        if (message is V11Messages.SET_PROTOCOL_VERSION setProtocolVersion)
        {
            result = BuildSetProtocolVersionResponse(setProtocolVersion);
            return true;
        }

        if (message is V20Messages.GET_SUPPORTED_VERSION v20Request)
        {
            result = BuildGetSupportedVersionResponse(v20Request.MessageId, useVersion20: true);
            return true;
        }

        if (message is V20Messages.SET_PROTOCOL_VERSION v20SetProtocolVersion)
        {
            result = BuildSetProtocolVersionResponse(v20SetProtocolVersion);
            return true;
        }

        result = null;
        return false;
    }

    private LlrpDeviceDispatchResult BuildGetSupportedVersionResponse(
        uint messageId,
        bool useVersion20 = false)
    {
        LlrpProtocolVersion supported = _state.Options.ProtocolVersion;
        if (supported == LlrpProtocolVersion.Version101)
        {
            return new LlrpDeviceDispatchResult(
                CreateV11Error(messageId, V11Enumerations.StatusCode.M_UnsupportedVersion,
                    "This LLRP device supports LLRP 1.0.1 only."),
                [],
                ResponseVersion: LlrpProtocolVersion.Version11);
        }

        if (useVersion20)
        {
            return new LlrpDeviceDispatchResult(
                new V20Messages.GET_SUPPORTED_VERSION_RESPONSE(
                    messageId,
                    new V20Parameters.LLRPStatus(V20Enumerations.StatusCode.M_Success, string.Empty, null, null),
                    (byte)supported,
                    (byte)supported),
                [],
                ResponseVersion: LlrpProtocolVersion.Version20);
        }

        return new LlrpDeviceDispatchResult(
            new V11Messages.GET_SUPPORTED_VERSION_RESPONSE(
                messageId,
                new V11Parameters.LLRPStatus(V11Enumerations.StatusCode.M_Success, string.Empty, null, null),
                (byte)supported,
                (byte)supported),
            [],
            ResponseVersion: LlrpProtocolVersion.Version11);
    }

    private LlrpDeviceDispatchResult BuildSetProtocolVersionResponse(V11Messages.SET_PROTOCOL_VERSION request)
    {
        LlrpProtocolVersion requested = (LlrpProtocolVersion)request.ProtocolVersion;
        bool supported = requested is LlrpProtocolVersion.Version101 or LlrpProtocolVersion.Version11
            ? _state.Options.ProtocolVersion >= requested
            : _state.Options.ProtocolVersion == LlrpProtocolVersion.Version20;
        if (!supported)
        {
            return new LlrpDeviceDispatchResult(
                CreateV11Error(request.MessageId, V11Enumerations.StatusCode.M_UnsupportedVersion,
                    $"LLRP version {request.ProtocolVersion} is not supported by this LLRP device."),
                [],
                ResponseVersion: LlrpProtocolVersion.Version11);
        }

        return new LlrpDeviceDispatchResult(
            new V11Messages.SET_PROTOCOL_VERSION_RESPONSE(
                request.MessageId,
                new V11Parameters.LLRPStatus(V11Enumerations.StatusCode.M_Success, string.Empty, null, null)),
            [],
            ResponseVersion: LlrpProtocolVersion.Version11,
            NextProtocolVersion: requested);
    }

    private LlrpDeviceDispatchResult BuildSetProtocolVersionResponse(V20Messages.SET_PROTOCOL_VERSION request)
    {
        LlrpProtocolVersion requested = (LlrpProtocolVersion)request.ProtocolVersion;
        if (_state.Options.ProtocolVersion != LlrpProtocolVersion.Version20 || requested != LlrpProtocolVersion.Version20)
        {
            return new LlrpDeviceDispatchResult(
                new V20Messages.ERROR_MESSAGE(
                    request.MessageId,
                    new V20Parameters.LLRPStatus(V20Enumerations.StatusCode.M_UnsupportedVersion,
                        "The requested LLRP version is not supported by this LLRP device.", null, null)),
                [],
                ResponseVersion: LlrpProtocolVersion.Version20);
        }

        return new LlrpDeviceDispatchResult(
            new V20Messages.SET_PROTOCOL_VERSION_RESPONSE(
                request.MessageId,
                new V20Parameters.LLRPStatus(V20Enumerations.StatusCode.M_Success, string.Empty, null, null)),
            [],
            ResponseVersion: LlrpProtocolVersion.Version20,
            NextProtocolVersion: requested);
    }

    private ILlrpMessage CreateUnsupportedVersionError(LlrpProtocolVersion version, uint messageId) =>
        version switch
        {
            LlrpProtocolVersion.Version101 => _profiles[version].CreateError(
                messageId,
                (ushort)V101Enumerations.StatusCode.M_UnsupportedVersion,
                "The LLRP protocol version is not supported by this LLRP device."),
            LlrpProtocolVersion.Version11 => CreateV11Error(
                messageId,
                V11Enumerations.StatusCode.M_UnsupportedVersion,
                "The LLRP protocol version is not supported by this LLRP device."),
            _ => new V20Messages.ERROR_MESSAGE(
                messageId,
                new V20Parameters.LLRPStatus(V20Enumerations.StatusCode.M_UnsupportedVersion,
                    "The LLRP protocol version is not supported by this LLRP device.", null, null)),
        };

    private static ushort GetMessageType(ILlrpMessage message) =>
        message switch
        {
            UnknownMessage unknown => unknown.MessageType,
            RawCustomMessage => RawCustomMessage.CustomMessageType,
            _ => 0,
        };

    private static long GetStatusCode(LlrpProtocolVersion version, string name) => version switch
    {
        LlrpProtocolVersion.Version101 => (long)V101Enumerations.StatusCode.M_UnsupportedMessage,
        LlrpProtocolVersion.Version11 => (long)V11Enumerations.StatusCode.M_UnsupportedMessage,
        _ => (long)V20Enumerations.StatusCode.M_UnsupportedMessage,
    };

    private static V11Messages.ERROR_MESSAGE CreateV11Error(
        uint messageId,
        V11Enumerations.StatusCode statusCode,
        string description) =>
        new(
            messageId,
            new V11Parameters.LLRPStatus(statusCode, description, null, null));
}

/// <summary>
/// Translates the shared standard wire model through the LlrpNet registry for LLRP 1.1 or 2.0.
/// </summary>
internal sealed class LlrpTranslatedStandardHandler : ILlrpDeviceVersionProfile
{
    private readonly LlrpStandard101Handler _inner;
    private readonly LlrpCodecRegistry _registry;

    public LlrpTranslatedStandardHandler(
        LlrpDeviceServerState state,
        LlrpCodecRegistry registry,
        LlrpProtocolVersion version)
    {
        if (version is not (LlrpProtocolVersion.Version11 or LlrpProtocolVersion.Version20))
        {
            throw new ArgumentOutOfRangeException(nameof(version));
        }

        _inner = new LlrpStandard101Handler(state);
        _registry = registry;
        Version = version;
    }

    public string Name => $"standard-llrp-{Version}";

    public LlrpProtocolVersion Version { get; }

    public bool CanHandle(LlrpProtocolVersion version, ILlrpMessage message) =>
        version == Version && message is not V11Messages.GET_SUPPORTED_VERSION &&
        message is not V11Messages.SET_PROTOCOL_VERSION &&
        message is not V20Messages.GET_SUPPORTED_VERSION &&
        message is not V20Messages.SET_PROTOCOL_VERSION;

    public async ValueTask<LlrpDeviceDispatchResult> HandleAsync(
        LlrpDeviceRequestContext context,
        ILlrpMessage message,
        CancellationToken cancellationToken)
    {
        try
        {
            ILlrpMessage translatedRequest = TranslateTo101(message);
            var innerContext = new LlrpDeviceRequestContext(
                context.Server,
                context.Device,
                context.ConnectionId,
                LlrpProtocolVersion.Version101,
                message.MessageId);
            LlrpDeviceDispatchResult result = await _inner
                .HandleAsync(innerContext, translatedRequest, cancellationToken)
                .ConfigureAwait(false);
            return new LlrpDeviceDispatchResult(
                result.Response is null ? null : TranslateFrom101(result.Response),
                result.AdditionalMessages.Select(TranslateFrom101).ToArray(),
                result.CloseConnection,
                ResponseVersion: Version,
                NextProtocolVersion: result.NextProtocolVersion);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or LlrpNet.Core.Protocol.LlrpProtocolException)
        {
            return new LlrpDeviceDispatchResult(
                CreateError(message.MessageId, (ushort)GetUnsupportedMessageStatus(), exception.Message),
                [],
                ResponseVersion: Version);
        }
    }

    public ILlrpMessage CreateError(uint messageId, ushort statusCode, string description) => Version switch
    {
        LlrpProtocolVersion.Version11 => new V11Messages.ERROR_MESSAGE(
            messageId,
            new V11Parameters.LLRPStatus((V11Enumerations.StatusCode)statusCode, description, null, null)),
        LlrpProtocolVersion.Version20 => new V20Messages.ERROR_MESSAGE(
            messageId,
            new V20Parameters.LLRPStatus((V20Enumerations.StatusCode)statusCode, description, null, null)),
        _ => throw new InvalidOperationException(),
    };

    public ILlrpMessage CreateKeepalive(uint messageId) => Version switch
    {
        LlrpProtocolVersion.Version11 => new V11Messages.KEEPALIVE(messageId),
        LlrpProtocolVersion.Version20 => new V20Messages.KEEPALIVE(messageId),
        _ => throw new InvalidOperationException(),
    };

    public ILlrpMessage CreateReaderEventNotification(uint messageId, LlrpDeviceEvent? deviceEvent = null) =>
        TranslateFrom101(_inner.CreateReaderEventNotification(messageId, deviceEvent));

    public ILlrpMessage CreateCloseConnection(uint messageId) =>
        TranslateFrom101(_inner.CreateCloseConnection(messageId));

    public IReadOnlyList<ILlrpMessage> BuildInventoryReports(uint roSpecId, int roundSequence) =>
        _inner.BuildInventoryReports(roSpecId, roundSequence).Select(TranslateFrom101).ToArray();

    internal ILlrpMessage TranslateFromCanonical(ILlrpMessage message) => TranslateFrom101(message);

    private ILlrpMessage TranslateTo101(ILlrpMessage message)
    {
        byte[] frame = _registry.EncodeMessage(Version, message);
        return DecodeWithVersion(frame, LlrpProtocolVersion.Version101);
    }

    private ILlrpMessage TranslateFrom101(ILlrpMessage message)
    {
        byte[] frame = _registry.EncodeMessage(LlrpProtocolVersion.Version101, message);
        return DecodeWithVersion(frame, Version);
    }

    private ILlrpMessage DecodeWithVersion(ReadOnlySpan<byte> frame, LlrpProtocolVersion targetVersion)
    {
        byte[] translated = frame.ToArray();
        LlrpMessageHeader header = LlrpMessageHeader.Decode(translated);
        (header with { Version = targetVersion }).Encode(translated);
        return _registry.DecodeMessage(translated);
    }

    private ushort GetUnsupportedMessageStatus() => Version switch
    {
        LlrpProtocolVersion.Version11 => (ushort)V11Enumerations.StatusCode.M_UnsupportedMessage,
        _ => (ushort)V20Enumerations.StatusCode.M_UnsupportedMessage,
    };
}
