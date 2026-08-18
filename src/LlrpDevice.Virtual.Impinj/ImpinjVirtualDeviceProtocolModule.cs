using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Messages;
using LlrpNet.Protocol.Parameters.V1_0_1;
using LlrpNet.Protocol.Registry;
using LlrpDevice.Server;
using ImpinjMessages = LlrpNet.Protocol.Impinj.Messages.V1_0_1;
using ImpinjRegistry = LlrpNet.Protocol.Impinj.Registry.V1_0_1;

namespace LlrpDevice.Virtual.Impinj;

/// <summary>Device-side Impinj LLRP support for the virtual R420 profile.</summary>
public sealed class ImpinjVirtualDeviceProtocolModule : ILlrpDeviceProtocolModule
{
    public static ImpinjVirtualDeviceProtocolModule Instance { get; } = new();

    private ImpinjVirtualDeviceProtocolModule()
    {
    }

    public string Id => "impinj-virtual-llrp-1.0.1";

    public IReadOnlySet<LlrpProtocolVersion> SupportedVersions { get; } =
        new HashSet<LlrpProtocolVersion> { LlrpProtocolVersion.Version101 };

    public void RegisterCodecs(LlrpCodecRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ImpinjRegistry.ImpinjProtocolModule.Register(registry);
    }

    public void RegisterHandlers(LlrpDeviceHandlerRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        registry.Add(new ImpinjMessageHandler());
    }

    private sealed class ImpinjMessageHandler : ILlrpDeviceMessageHandler
    {
        public string Name => "impinj-virtual-control";

        public bool CanHandle(LlrpProtocolVersion version, ILlrpMessage message) =>
            version == LlrpProtocolVersion.Version101 &&
            message is ImpinjMessages.IMPINJ_ENABLE_EXTENSIONS or ImpinjMessages.IMPINJ_SAVE_SETTINGS;

        public ValueTask<LlrpDeviceDispatchResult> HandleAsync(
            LlrpDeviceRequestContext context,
            ILlrpMessage message,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ILlrpMessage response = message switch
            {
                ImpinjMessages.IMPINJ_ENABLE_EXTENSIONS request =>
                    new ImpinjMessages.IMPINJ_ENABLE_EXTENSIONS_RESPONSE(
                        request.MessageId,
                        new LLRPStatus(
                            LlrpNet.Protocol.Enumerations.V1_0_1.StatusCode.M_Success,
                            string.Empty,
                            null,
                            null),
                        []),
                ImpinjMessages.IMPINJ_SAVE_SETTINGS request =>
                    new ImpinjMessages.IMPINJ_SAVE_SETTINGS_RESPONSE(
                        request.MessageId,
                        new LLRPStatus(
                            LlrpNet.Protocol.Enumerations.V1_0_1.StatusCode.M_Success,
                            string.Empty,
                            null,
                            null),
                        []),
                _ => throw new InvalidOperationException("Unsupported Impinj virtual-device message."),
            };

            return ValueTask.FromResult(LlrpDeviceDispatchResult.FromResponse(response));
        }
    }
}
