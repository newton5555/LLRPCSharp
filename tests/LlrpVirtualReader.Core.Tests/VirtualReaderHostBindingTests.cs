using System.Net;
using System.Net.Sockets;
using LlrpVirtualReader;

namespace LlrpVirtualReader.Core.Tests;

public sealed class VirtualReaderHostBindingTests
{
    [Fact]
    public async Task Host_binds_the_requested_loopback_address_and_port()
    {
        int port = GetAvailablePort();
        await using var host = new VirtualReaderHost(new VirtualReaderHostOptions
        {
            ListenAddress = IPAddress.Loopback,
            Port = port,
        });

        host.Start();

        Assert.Equal(IPAddress.Loopback, host.ListenAddress);
        Assert.Equal(port, host.Port);
    }

    [Fact]
    public async Task Host_reports_created_running_and_stopped_lifecycle_states()
    {
        await using var host = new VirtualReaderHost(new VirtualReaderHostOptions { Port = 0 });
        var states = new List<VirtualReaderLifecycleState>();
        host.LifecycleChanged += (_, args) => states.Add(args.CurrentState);

        Assert.Equal(VirtualReaderLifecycleState.Created, host.State);
        await host.StartAsync();
        await host.StopAsync();

        Assert.Equal(VirtualReaderLifecycleState.Stopped, host.State);
        Assert.Equal(
            [
                VirtualReaderLifecycleState.Starting,
                VirtualReaderLifecycleState.Running,
                VirtualReaderLifecycleState.Stopping,
                VirtualReaderLifecycleState.Stopped,
            ],
            states);
    }

    [Fact]
    public async Task Host_fails_on_an_occupied_port_without_falling_back()
    {
        using var occupied = new TcpListener(IPAddress.Loopback, 0);
        occupied.Start();
        int port = ((IPEndPoint)occupied.LocalEndpoint).Port;

        await using var host = new VirtualReaderHost(new VirtualReaderHostOptions
        {
            ListenAddress = IPAddress.Loopback,
            Port = port,
        });

        Assert.Throws<SocketException>(() => host.Start());
        Assert.Equal(port, host.Port);
    }

    [Fact]
    public async Task Host_rejects_additional_clients_when_single_control_connection_is_configured()
    {
        await using var host = new VirtualReaderHost(
            new VirtualReaderHostOptions
            {
                Port = 0,
                ReaderOptions = new VirtualReaderOptions
                {
                    MaximumClientConnections = 1,
                    ConnectionLimitPolicy = VirtualReaderConnectionLimitPolicy.RejectAdditional,
                },
            });
        await host.StartAsync();

        using var first = new TcpClient();
        using var second = new TcpClient();
        await first.ConnectAsync(IPAddress.Loopback, host.Port);
        await second.ConnectAsync(IPAddress.Loopback, host.Port);
        await WaitUntilAsync(() => host.ConnectedClients.Count == 1);
        await Task.Delay(100);

        Assert.Single(host.ConnectedClients);
        await host.StopAsync();
        Assert.Empty(host.ConnectedClients);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private static int GetAvailablePort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }
}
