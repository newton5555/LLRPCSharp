using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Messages;
using LlrpNet.Protocol.Registry;
using LlrpDevice.Server;
using LlrpDevice.Virtual;
using LlrpDevice.Virtual.Hosting;
using LlrpSdk;
using V101Messages = LlrpNet.Protocol.Messages.V1_0_1;

namespace Interop.Tests;

public sealed class VirtualDeviceExtensionInteropTests
{
    [Fact]
    public async Task Registered_message_handler_runs_before_the_standard_profile()
    {
        var module = new KeepaliveModule();
        await using var host = new VirtualLlrpDeviceHost(
            new VirtualLlrpDeviceHostOptions
            {
                Server = new LlrpDeviceServerOptions
                {
                    Port = 0,
                    ProtocolModules = [module],
                },
            });
        await host.StartAsync();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using LlrpReader reader = LlrpReader.CreateBuilder("127.0.0.1")
            .WithPort(host.BoundPort)
            .WithConnectTimeout(TimeSpan.FromSeconds(2))
            .WithRequestTimeout(TimeSpan.FromSeconds(2))
            .WithProtocolVersionPolicy(LlrpProtocolVersionPolicy.Force101)
            .Build();
        await reader.ConnectAsync(timeout.Token);

        uint messageId = reader.Protocol.NextMessageId();
        var request = new V101Messages.KEEPALIVE(messageId);
        V101Messages.KEEPALIVE_ACK response = await reader.Protocol.TransactAsync<V101Messages.KEEPALIVE_ACK>(
            request,
            cancellationToken: timeout.Token);

        Assert.NotNull(response);
        Assert.Equal(messageId, response.MessageId);
        Assert.Equal(1, module.Calls);
    }

    private sealed class KeepaliveModule : ILlrpDeviceProtocolModule
    {
        public int Calls { get; private set; }

        public string Id => "test.keepalive";

        public IReadOnlySet<LlrpProtocolVersion> SupportedVersions =>
            new HashSet<LlrpProtocolVersion> { LlrpProtocolVersion.Version101 };

        public void RegisterCodecs(LlrpCodecRegistry registry)
        {
        }

        public void RegisterHandlers(LlrpDeviceHandlerRegistry registry) =>
            registry.Add(new KeepaliveHandler(this));

        private sealed class KeepaliveHandler : ILlrpDeviceMessageHandler
        {
            private readonly KeepaliveModule _owner;

            public KeepaliveHandler(KeepaliveModule owner)
            {
                _owner = owner;
            }

            public string Name => "test.keepalive-handler";

            public bool CanHandle(LlrpProtocolVersion version, ILlrpMessage message) =>
                version == LlrpProtocolVersion.Version101 && message is V101Messages.KEEPALIVE;

            public ValueTask<LlrpDeviceDispatchResult> HandleAsync(
                LlrpDeviceRequestContext context,
                ILlrpMessage message,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _owner.Calls++;
                V101Messages.KEEPALIVE request = (V101Messages.KEEPALIVE)message;
                return ValueTask.FromResult(
                    LlrpDeviceDispatchResult.FromResponse(new V101Messages.KEEPALIVE_ACK(request.MessageId)));
            }
        }
    }
}
