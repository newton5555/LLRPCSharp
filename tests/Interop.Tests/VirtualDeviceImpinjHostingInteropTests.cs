using LlrpDevice.Virtual.Hosting;
using LlrpNet.Core.Protocol;
using LlrpSdk;
using LlrpSdk.Extensions.Impinj;

namespace Interop.Tests;

public sealed class VirtualDeviceImpinjHostingInteropTests
{
    [Fact]
    public async Task Hosting_profile_composes_Impinj_R420_capabilities_and_extension_messages()
    {
        await using IVirtualDeviceHost host = VirtualLlrpDeviceHost.Create(
            new VirtualDeviceHostOptions
            {
                ProfileId = VirtualDeviceProfiles.ImpinjR420Id,
                Port = 0,
            });
        await host.StartAsync();

        await using LlrpReader reader = LlrpReader.CreateBuilder("127.0.0.1")
            .WithPort(host.BoundPort)
            .WithConnectTimeout(TimeSpan.FromSeconds(2))
            .WithRequestTimeout(TimeSpan.FromSeconds(2))
            .WithProtocolVersionPolicy(LlrpProtocolVersionPolicy.Force101)
            .UseImpinj()
            .Build();

        await reader.ConnectAsync();

        Assert.Equal((uint)25_882, reader.Identity!.ManufacturerId);
        Assert.Equal((uint)2_001_002, reader.Identity.ModelId);
        Assert.NotNull(reader.Extensions.Get<ImpinjReaderExtension>());
        Assert.NotNull(reader.Capabilities);
        Assert.Equal(87, reader.Capabilities.TxPowers.Count);
        Assert.Equal(42, reader.Capabilities.RxSensitivities.Count);

        await reader.DisconnectAsync();
        await host.StopAsync();
    }
}
