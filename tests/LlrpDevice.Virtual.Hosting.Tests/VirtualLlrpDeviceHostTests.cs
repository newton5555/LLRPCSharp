using System.Net;
using LlrpDevice.Server;
using LlrpDevice.Virtual.Hosting;

namespace LlrpDevice.Virtual.Hosting.Tests;

public sealed class VirtualLlrpDeviceHostTests
{
    [Fact]
    public async Task Host_starts_and_stops_one_device_through_the_public_interface()
    {
        await using IVirtualLlrpDeviceHost host = new VirtualLlrpDeviceHost(
            new VirtualLlrpDeviceHostOptions
            {
                Server = new LlrpDeviceServerOptions
                {
                    ListenAddress = IPAddress.Loopback,
                    Port = 0,
                },
            });

        var transitions = new List<VirtualLlrpDeviceHostState>();
        host.LifecycleChanged += (_, args) => transitions.Add(args.CurrentState);

        await host.StartAsync();

        Assert.Equal(VirtualLlrpDeviceHostState.Running, host.State);
        Assert.InRange(host.BoundPort, 1, ushort.MaxValue);
        Assert.Equal("Virtual Reader", host.Device.Identity.Name);

        await host.StopAsync();

        Assert.Equal(VirtualLlrpDeviceHostState.Stopped, host.State);
        Assert.Contains(VirtualLlrpDeviceHostState.Running, transitions);
        Assert.Contains(VirtualLlrpDeviceHostState.Stopped, transitions);
    }

    [Fact]
    public async Task Restart_keeps_one_device_instance_and_its_mutable_tag_state()
    {
        await using var host = new VirtualLlrpDeviceHost(
            new VirtualLlrpDeviceHostOptions
            {
                Server = new LlrpDeviceServerOptions { Port = 0 },
            });

        await host.StartAsync();
        object deviceBeforeRestart = host.VirtualDevice;

        await host.RestartAsync();

        Assert.Equal(VirtualLlrpDeviceHostState.Running, host.State);
        Assert.Same(deviceBeforeRestart, host.VirtualDevice);
        Assert.InRange(host.BoundPort, 1, ushort.MaxValue);
    }

    [Fact]
    public async Task Host_rejects_operations_after_disposal()
    {
        var host = new VirtualLlrpDeviceHost(
            new VirtualLlrpDeviceHostOptions
            {
                Server = new LlrpDeviceServerOptions { Port = 0 },
            });

        await host.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => host.StartAsync());
    }

    [Fact]
    public async Task Hosting_options_configure_profile_and_initial_tags_without_low_level_options()
    {
        await using IVirtualDeviceHost host = VirtualLlrpDeviceHost.Create(
            new VirtualDeviceHostOptions
            {
                ProfileId = VirtualDeviceProfiles.Standard101Id,
                Port = 0,
                Name = "Configured Reader",
                Inventory = new VirtualInventoryOptions
                {
                    SourceId = "test-tags",
                    Tags =
                    [
                        new VirtualInventoryTag
                        {
                            ElectronicProductCode = Convert.FromHexString("300833B2DDD9014000000001"),
                        },
                    ],
                },
            });

        Assert.Equal("Configured Reader", host.Definition.Name);
        Assert.Equal("test-tags", host.Definition.Inventory.SourceId);
        await host.StartAsync();
        Assert.Equal(VirtualLlrpDeviceHostState.Running, host.State);
        await host.StopAsync();
    }

    [Fact]
    public void Hosting_exposes_the_captured_impinj_profile()
    {
        VirtualDeviceProfileInfo profile = VirtualDeviceProfiles.Get("impinj.r420.llrp-1.0.1");

        Assert.Equal("Impinj R420 (Virtual)", profile.Name);
        Assert.Equal((uint)25_882, profile.ManufacturerId);
        Assert.Equal((uint)2_001_002, profile.ModelId);
        Assert.Equal((ushort)4, profile.MaxNumberOfAntennas);
    }
}
