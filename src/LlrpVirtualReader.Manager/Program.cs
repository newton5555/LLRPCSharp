using System.Net;

namespace LlrpVirtualReader.Manager;

internal static class Program
{
    public static async Task Main(string[] args)
    {
        ManagerOptions options = ParseOptions(args);
        await using var manager = new VirtualReaderManager();
        VirtualReaderInstanceInfo instance = await manager.CreateAndStartAsync(
            new VirtualReaderInstanceOptions
            {
                Name = options.Name,
                PresetId = options.PresetId,
                ListenAddress = options.ListenAddress,
                Port = options.Port,
            });

        Console.WriteLine(
            $"Virtual LLRP reader instance '{instance.InstanceId}' ({instance.PresetId}) listening on " +
            $"{FormatEndpoint(instance.ListenAddress, instance.BoundPort)}. " +
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
        string name = "Virtual Reader";
        string preset = VirtualReaderPresetIds.Standard101Basic;

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
                    preset = VirtualReaderPresetIds.Standard101Strict;
                    break;
                case "--name" when index + 1 < args.Length:
                    name = args[++index];
                    break;
                case "--llrp" when index + 1 < args.Length:
                    preset = args[++index] switch
                    {
                        "1.0.1" => VirtualReaderPresetIds.Standard101Basic,
                        "1.1" => VirtualReaderPresetIds.Standard11Basic,
                        _ => throw new ArgumentException("--llrp must be 1.0.1 or 1.1."),
                    };
                    break;
                case "--help" or "-h":
                    throw new ArgumentException("Usage: LlrpVirtualReader.Manager [--listen <ip>] [--port <1-65535>] [--name <name>] [--llrp 1.0.1|1.1] [--strict]");
                default:
                    throw new ArgumentException("Usage: LlrpVirtualReader.Manager [--listen <ip>] [--port <1-65535>] [--name <name>] [--llrp 1.0.1|1.1] [--strict]");
            }
        }

        return new ManagerOptions(listenAddress, port, name, preset);
    }

    private static string FormatEndpoint(IPAddress address, int port) =>
        address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
            ? $"[{address}]:{port}"
            : $"{address}:{port}";

    private sealed record ManagerOptions(
        IPAddress ListenAddress,
        int Port,
        string Name,
        string PresetId);
}
