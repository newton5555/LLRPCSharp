using System.Net;

namespace LlrpVirtualReader.Manager;

internal static class Program
{
    public static async Task Main(string[] args)
    {
        ManagerOptions options = ParseOptions(args);
        if (options.ValidateConfig || options.ConfigPath is not null)
        {
            if (options.ConfigPath is null)
            {
                throw new ArgumentException("--config is required with --validate-config or configuration startup.");
            }

            VirtualReaderConfiguration configuration = VirtualReaderConfiguration.Load(options.ConfigPath);
            if (options.ValidateConfig)
            {
                Console.WriteLine(
                    $"Configuration is valid: {configuration.Instances.Count} instance(s), " +
                    $"{configuration.Presets.Count} local preset(s).");
                return;
            }

            if (options.ListPresets)
            {
                foreach (IVirtualReaderPresetContributor preset in configuration.CreateCatalog().Presets)
                {
                    Console.WriteLine($"{preset.Id} - {preset.Description}");
                }

                return;
            }

            await using var configuredManager = new VirtualReaderManager(configuration.CreateCatalog());
            VirtualReaderInstanceInfo configuredInstance = await configuredManager.CreateAndStartAsync(
                configuration.BuildInstanceOptions(options.InstanceId));
            Console.WriteLine(
                $"Configured virtual LLRP reader '{configuredInstance.Name}' ({configuredInstance.PresetId}) " +
                $"listening on {FormatEndpoint(configuredInstance.ListenAddress, configuredInstance.BoundPort)}. " +
                "Press Ctrl+C to stop.");
            await WaitForCtrlCAsync();
            return;
        }

        if (options.ListPresets)
        {
            foreach (IVirtualReaderPresetContributor preset in new VirtualReaderPresetCatalog().Presets)
            {
                Console.WriteLine($"{preset.Id} - {preset.Description}");
            }

            return;
        }

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

    private static async Task WaitForCtrlCAsync()
    {
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            stopped.TrySetResult();
        };
        Console.CancelKeyPress += cancelHandler;
        try
        {
            await stopped.Task.ConfigureAwait(false);
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    private static ManagerOptions ParseOptions(string[] args)
    {
        IPAddress listenAddress = IPAddress.Loopback;
        int port = 5084;
        string name = "Virtual Reader";
        string preset = VirtualReaderPresetIds.Standard101Basic;
        string? configPath = null;
        string? instanceId = null;
        bool validateConfig = false;
        bool listPresets = false;

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
                case "--config" when index + 1 < args.Length:
                    configPath = args[++index];
                    break;
                case "--instance" when index + 1 < args.Length:
                    instanceId = args[++index];
                    break;
                case "--validate-config":
                    validateConfig = true;
                    break;
                case "--list-presets":
                    listPresets = true;
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
                    throw new ArgumentException("Usage: LlrpVirtualReader.Manager [--config <path>] [--instance <id>] [--validate-config] [--list-presets] [--listen <ip>] [--port <1-65535>] [--name <name>] [--llrp 1.0.1|1.1] [--strict]");
                default:
                    throw new ArgumentException("Usage: LlrpVirtualReader.Manager [--config <path>] [--instance <id>] [--validate-config] [--list-presets] [--listen <ip>] [--port <1-65535>] [--name <name>] [--llrp 1.0.1|1.1] [--strict]");
            }
        }

        return new ManagerOptions(
            listenAddress,
            port,
            name,
            preset,
            configPath,
            instanceId,
            validateConfig,
            listPresets);
    }

    private static string FormatEndpoint(IPAddress address, int port) =>
        address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
            ? $"[{address}]:{port}"
            : $"{address}:{port}";

    private sealed record ManagerOptions(
        IPAddress ListenAddress,
        int Port,
        string Name,
        string PresetId,
        string? ConfigPath,
        string? InstanceId,
        bool ValidateConfig,
        bool ListPresets);
}
