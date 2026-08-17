using System.Net;
using System.Net.Sockets;
using LlrpNet.Core.Protocol;
using LlrpSdk;
using LlrpVirtualDevice.Cli;

namespace LlrpVirtualDevice.Cli.Tests;

public sealed class VirtualDeviceCliApplicationTests
{
    [Fact]
    public async Task Help_describes_single_device_lifecycle_and_no_manager()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = await new VirtualDeviceCliApplication().RunAsync(
            ["--help"],
            output,
            error);

        Assert.Equal(0, exitCode);
        Assert.Contains("one virtual LLRP device", output.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("live", output.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("validate", output.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no multi-device manager", output.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(error.ToString());
    }

    [Fact]
    public async Task Run_help_is_a_successful_subcommand_help_path()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = await new VirtualDeviceCliApplication().RunAsync(
            ["run", "--help"],
            output,
            error);

        Assert.Equal(0, exitCode);
        Assert.Contains("--rf-scenario", output.ToString(), StringComparison.Ordinal);
        Assert.Empty(error.ToString());
    }

    [Fact]
    public async Task Default_invocation_enters_the_interactive_shell()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        using var input = new StringReader("help\nexit\n");

        int exitCode = await new VirtualDeviceCliApplication().RunAsync(
            [],
            output,
            error,
            input);

        Assert.Equal(0, exitCode);
        Assert.Contains("LLRP Virtual Device Shell", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("server create", output.ToString(), StringComparison.Ordinal);
        Assert.Empty(error.ToString());
    }

    [Fact]
    public async Task Validate_loads_one_device_configuration_without_binding()
    {
        string path = Path.Combine(Path.GetTempPath(), $"llrpcsharp-virtual-device-{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(
                path,
                """
                {
                  "schemaVersion": 1,
                  "presetId": "llrp.standard11.basic",
                  "name": "Standalone virtual device",
                  "listenAddress": "127.0.0.1",
                  "port": 5097,
                  "tags": [
                    { "epc": "E28011710000020D056E9BEE", "userMemory": [1, 2, 3, 4] }
                  ]
                }
                """);

            using var output = new StringWriter();
            using var error = new StringWriter();
            int exitCode = await new VirtualDeviceCliApplication().RunAsync(
                ["validate", "--config", path],
                output,
                error);

            Assert.Equal(0, exitCode);
            Assert.Contains("one virtual device", output.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("LLRP 1.1", output.ToString(), StringComparison.Ordinal);
            Assert.Empty(error.ToString());
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task Run_starts_one_device_and_stops_when_cancelled()
    {
        int port = GetFreePort();
        using var output = new StringWriter();
        using var error = new StringWriter();
        using var cancellation = new CancellationTokenSource();
        Task<int> run = new VirtualDeviceCliApplication().RunAsync(
            ["run", "--port", port.ToString()],
            output,
            error,
            cancellation.Token);

        await WaitForOutputAsync(output, "listening on", cancellation.Token);
        cancellation.Cancel();

        int exitCode = await run;

        Assert.Equal(0, exitCode);
        Assert.Contains("Virtual LLRP device", output.ToString(), StringComparison.Ordinal);
        Assert.Empty(error.ToString());
    }

    [Fact]
    public async Task Live_creates_one_device_and_streams_protocol_events()
    {
        int port = GetFreePort();
        using var output = new StringWriter();
        using var error = new StringWriter();
        using var input = new BlockingTextReader();
        using var stop = new CancellationTokenSource();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Task<int> run = new VirtualDeviceCliApplication().RunAsync(
            ["live", "--port", port.ToString()],
            output,
            error,
            input,
            stop.Token);

        await WaitForOutputAsync(output, "Listening on", timeout.Token);
        {
            await using LlrpReader reader = LlrpReader.CreateBuilder("127.0.0.1")
                .WithPort(port)
                .WithConnectTimeout(TimeSpan.FromSeconds(2))
                .WithRequestTimeout(TimeSpan.FromSeconds(2))
                .WithProtocolVersionPolicy(LlrpProtocolVersionPolicy.Force101)
                .Build();

            await reader.ConnectAsync(timeout.Token);
            await reader.RefreshCapabilitiesAsync(timeout.Token);
            await WaitForOutputAsync(output, "[RX]", timeout.Token);
            await WaitForOutputAsync(output, "[TX]", timeout.Token);
        }

        stop.Cancel();
        int exitCode = await run;

        Assert.Equal(0, exitCode);
        Assert.Contains("LLRP Virtual Device Shell", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("Created virtual device", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("GET_READER_CAPABILITIES", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("GET_READER_CAPABILITIES_RESPONSE", output.ToString(), StringComparison.Ordinal);
        Assert.Empty(error.ToString());
    }

    [Fact]
    public async Task Shell_creates_starts_reports_and_destroys_one_server()
    {
        int port = GetFreePort();
        using var output = new StringWriter();
        using var error = new StringWriter();
        using var input = new StringReader(
            $"server create --name \"Shell Reader\" --port {port}\n" +
            "server status\n" +
            "server start\n" +
            "server restart\n" +
            "server status\n" +
            "logs off\n" +
            "server stop\n" +
            "server destroy\n" +
            "exit\n");

        int exitCode = await new VirtualDeviceCliApplication().RunAsync(
            [],
            output,
            error,
            input);

        string text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("Created virtual device 'Shell Reader'", text, StringComparison.Ordinal);
        Assert.Contains("Listening on", text, StringComparison.Ordinal);
        Assert.Contains("LLRP server restarted", text, StringComparison.Ordinal);
        Assert.Contains("SERVER STATUS", text, StringComparison.Ordinal);
        Assert.Contains("Event log streaming disabled", text, StringComparison.Ordinal);
        Assert.Contains("Server destroyed", text, StringComparison.Ordinal);
        Assert.Empty(error.ToString());
    }

    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static async Task WaitForOutputAsync(
        StringWriter output,
        string value,
        CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < 500; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (output.ToString().Contains(value, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            await Task.Delay(10, cancellationToken);
        }

        throw new TimeoutException($"Did not observe '{value}' in CLI output.");
    }

    private sealed class BlockingTextReader : TextReader
    {
        private readonly TaskCompletionSource<string?> _line =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
        {
            return new ValueTask<string?>(_line.Task.WaitAsync(cancellationToken));
        }
    }
}
