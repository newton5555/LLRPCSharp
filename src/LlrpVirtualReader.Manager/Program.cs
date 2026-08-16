using System.Net;

namespace LlrpVirtualReader.Manager;

internal static class Program
{
    public static async Task Main(string[] args)
    {
        ManagerOptions options = ParseOptions(args);
        await using var reader = new LlrpVirtualReader.VirtualReaderHost(
            new LlrpVirtualReader.VirtualReaderHostOptions
            {
                ListenAddress = options.ListenAddress,
                Port = options.Port,
                ReaderOptions = new LlrpVirtualReader.VirtualReaderOptions
                {
                    UseStrictStandardInventoryProfile = options.UseStrictStandardInventoryProfile,
                },
            });
        reader.Start();

        Console.WriteLine(
            $"Virtual LLRP 1.0.1 reader listening on {FormatEndpoint(reader.ListenAddress, reader.Port)}. " +
            "Press Ctrl+C to stop.");

        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            stopped.TrySetResult();
        };
        await stopped.Task.ConfigureAwait(false);
    }

    private static ManagerOptions ParseOptions(string[] args)
    {
        IPAddress listenAddress = IPAddress.Loopback;
        int port = 5084;
        bool strict = false;

        for (int index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--listen" when index + 1 < args.Length:
                    if (!IPAddress.TryParse(args[++index], out IPAddress? parsedAddress))
                    {
                        throw new ArgumentException($"Invalid listen address: {args[index]}");
                    }

                    listenAddress = parsedAddress;
                    break;
                case "--port" when index + 1 < args.Length:
                    if (!int.TryParse(args[++index], out port) || port is <= 0 or > ushort.MaxValue)
                    {
                        throw new ArgumentException("The port must be between 1 and 65535.");
                    }

                    break;
                case "--strict":
                    strict = true;
                    break;
                case "--help" or "-h":
                    throw new ArgumentException("Usage: LlrpVirtualReader.Manager [--listen <ip>] [--port <1-65535>] [--strict]");
                default:
                    throw new ArgumentException("Usage: LlrpVirtualReader.Manager [--listen <ip>] [--port <1-65535>] [--strict]");
            }
        }

        return new ManagerOptions(listenAddress, port, strict);
    }

    private static string FormatEndpoint(IPAddress address, int port) =>
        address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
            ? $"[{address}]:{port}"
            : $"{address}:{port}";

    private sealed record ManagerOptions(
        IPAddress ListenAddress,
        int Port,
        bool UseStrictStandardInventoryProfile);
}
