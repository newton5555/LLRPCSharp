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

    private static int GetAvailablePort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }
}
