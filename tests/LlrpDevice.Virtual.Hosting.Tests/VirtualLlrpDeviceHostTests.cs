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
}
