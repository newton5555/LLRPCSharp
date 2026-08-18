using System.Net;
using LlrpDevice.Virtual.Hosting;
using Spectre.Console;

namespace LlrpVirtualDevice.Cli;

/// <summary>Standalone command-line consumer for exactly one virtual LLRP device.</summary>
public sealed class VirtualDeviceCliApplication
{
    public Task<int> RunAsync(
        IReadOnlyList<string> args,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(args, output, error, Console.In, cancellationToken);
    }

    public async Task<int> RunAsync(
        IReadOnlyList<string> args,
        TextWriter output,
        TextWriter error,
        TextReader input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(input);

        if (args.Count == 0)
        {
            return await RunShellAsync(output, input, cancellationToken).ConfigureAwait(false);
        }

        if (HasHelp(args))
        {
            PrintRootHelp(output);
            return 0;
        }

        string command = args[0].StartsWith("-", StringComparison.Ordinal)
            ? "run"
            : args[0].Trim().ToLowerInvariant();
        int optionStart = command == "run" && args[0].StartsWith("-", StringComparison.Ordinal) ? 0 : 1;

        try
        {
            if (command is "run" or "start" or "live" or "shell" &&
                optionStart < args.Count &&
                args[optionStart] is "--help" or "-h")
            {
                if (command is "live")
                {
                    PrintLiveHelp(output);
                }
                else if (command is "shell")
                {
                    PrintShellHelp(output);
                }
                else
                {
                    PrintRunHelp(output);
                }

                return 0;
            }

            return command switch
            {
                "run" or "start" => await RunDeviceAsync(
                        ParseLaunchOptions(args, optionStart, "run"),
                        output,
                        cancellationToken)
                    .ConfigureAwait(false),
                "live" => await RunShellAsync(
                        output,
                        input,
                        cancellationToken,
                        ParseLaunchOptions(args, optionStart, "live"),
                        autoStart: true)
                    .ConfigureAwait(false),
                "shell" => await RunShellAsync(
                        output,
                        input,
                        cancellationToken)
                    .ConfigureAwait(false),
                "validate" => ValidateConfiguration(args, optionStart, output),
                "presets" or "list-presets" => ListPresets(args, optionStart, output),
                "caps" or "list-caps" => ListCapabilityProfiles(args, optionStart, output),
                "help" => PrintCommandHelp(args, optionStart, output),
                _ => throw new ArgumentException($"Unknown command '{command}'."),
            };
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or InvalidDataException
                or FormatException
                or OverflowException
                or System.Text.Json.JsonException)
        {
            await error.WriteLineAsync($"Invalid virtual-device input: {exception.Message}").ConfigureAwait(false);
            await error.WriteLineAsync("Run 'llrp-virtual-device --help' for usage.").ConfigureAwait(false);
            return 2;
        }
        catch (Exception exception)
        {
            await error.WriteLineAsync($"Virtual device failed: {exception.Message}").ConfigureAwait(false);
            return 1;
        }
    }

    private static async Task<int> RunDeviceAsync(
        VirtualDeviceLaunchOptions launch,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        VirtualDeviceHostOptions hostOptions = BuildHostOptions(launch);
        await using IVirtualDeviceHost host = VirtualLlrpDeviceHost.Create(hostOptions);
        using var stopSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            stopSource.Cancel();
        };

        Console.CancelKeyPress += cancelHandler;
        try
        {
            await host.StartAsync(stopSource.Token).ConfigureAwait(false);
            await output.WriteLineAsync(
                    $"Virtual LLRP device '{hostOptions.Name ?? VirtualDeviceProfiles.Get(hostOptions.ProfileId).Name}' listening on " +
                    $"{FormatEndpoint(host.ListenAddress, host.BoundPort)} using LLRP " +
                    $"{FormatProtocolVersion(hostOptions.ProtocolVersion)}.")
                .ConfigureAwait(false);
            await output.WriteLineAsync("Press Ctrl+C to stop.").ConfigureAwait(false);

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, stopSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stopSource.IsCancellationRequested)
            {
                // Cancellation is the normal foreground shutdown path.
            }

            return 0;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
            if (host.State is not (VirtualLlrpDeviceHostState.Created or VirtualLlrpDeviceHostState.Stopped))
            {
                await host.StopAsync().ConfigureAwait(false);
            }
        }
    }

    private static async Task<int> RunShellAsync(
        TextWriter output,
        TextReader input,
        CancellationToken cancellationToken,
        VirtualDeviceLaunchOptions? initialLaunch = null,
        bool autoStart = false)
    {
        var settings = new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(output),
            Ansi = AnsiSupport.Detect,
        };
        IAnsiConsole console = AnsiConsole.Create(settings);
        var shell = new VirtualDeviceShell(console, input);
        return await shell.RunAsync(initialLaunch, autoStart, cancellationToken).ConfigureAwait(false);
    }

    internal static VirtualDeviceHostOptions BuildHostOptions(VirtualDeviceLaunchOptions launch)
    {
        VirtualDeviceConfigurationDocument? document = launch.ConfigPath is null
            ? null
            : VirtualDeviceConfiguration.Load(launch.ConfigPath);
        return VirtualDeviceHostOptionsBuilder.Build(launch, document);
    }

    internal static VirtualDeviceLaunchOptions ParseLaunchOptions(
        IReadOnlyList<string> args,
        int start,
        string command)
    {
        var options = new VirtualDeviceLaunchOptions();
        for (int index = start; index < args.Count; index++)
        {
            string option = args[index];
            options = option switch
            {
                "--config" => options with { ConfigPath = ReadString(args, ref index, option) },
                "--preset" => options with { PresetId = ReadString(args, ref index, option) },
                "--caps" or "--profile" => options with
                {
                    CapabilityProfileId = ReadString(args, ref index, option),
                },
                "--data-source" => options with
                {
                    InventoryDataSource = ReadString(args, ref index, option),
                },
                "--listen" => options with { ListenAddress = ReadString(args, ref index, option) },
                "--port" => options with { Port = ReadInt(args, ref index, option) },
                "--llrp" => options with { ProtocolVersion = ReadString(args, ref index, option) },
                "--name" => options with { Name = ReadString(args, ref index, option) },
                "--tag" => options with { Tag = ReadString(args, ref index, option) },
                "--interval-ms" => options with { ReportIntervalMilliseconds = ReadInt(args, ref index, option) },
                "--count" => options with { ReportCount = ReadInt(args, ref index, option) },
                "--rf-scenario" => options with { RfScenario = ReadString(args, ref index, option) },
                "--seed" => options with { RandomSeed = ReadInt(args, ref index, option) },
                "--detection-probability" => options with { DetectionProbability = ReadDouble(args, ref index, option) },
                "--single-tag-probability" => options with { SingleTagProbability = ReadDouble(args, ref index, option) },
                "--presence-cycle-rounds" => options with { PresenceCycleRounds = ReadInt(args, ref index, option) },
                "--rssi-jitter-db" => options with { RssiJitterDb = ReadInt(args, ref index, option) },
                "--max-tags-per-round" => options with { MaxTagsPerRound = ReadInt(args, ref index, option) },
                "--max-client-connections" => options with { MaximumClientConnections = ReadInt(args, ref index, option) },
                "--keepalive-ms" => options with { KeepAliveIntervalMilliseconds = ReadInt(args, ref index, option) },
                "--strict" => options with { Strict = true },
                "--allow-implicit-stop-on-disable" => options with { AllowImplicitStopOnDisable = true },
                "--help" or "-h" => throw new ArgumentException($"--help must be used immediately after the {command} command."),
                _ => throw new ArgumentException($"Unknown {command} option '{option}'."),
            };
        }

        return options;
    }

    private static int ValidateConfiguration(
        IReadOnlyList<string> args,
        int start,
        TextWriter output)
    {
        if (start < args.Count && args[start] is "--help" or "-h")
        {
            PrintValidateHelp(output);
            return 0;
        }

        string? path = null;
        for (int index = start; index < args.Count; index++)
        {
            if (args[index] != "--config")
            {
                throw new ArgumentException($"Unknown validate option '{args[index]}'.");
            }

            path = ReadString(args, ref index, "--config");
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("validate requires --config <PATH>.");
        }

        VirtualDeviceConfigurationDocument document = VirtualDeviceConfiguration.Load(path);
        _ = VirtualDeviceHostOptionsBuilder.Build(new VirtualDeviceLaunchOptions(), document);
        output.WriteLine(
            $"Configuration is valid: one virtual device, capability profile '" +
            $"{document.CapabilityProfileId}', inventory source '{document.InventoryDataSource}', " +
            $"LLRP {document.ProtocolVersion ?? VirtualDevicePresets.Get(document.PresetId).ProtocolVersion}.");
        return 0;
    }

    private static int ListPresets(
        IReadOnlyList<string> args,
        int start,
        TextWriter output)
    {
        if (start < args.Count)
        {
            if (start + 1 == args.Count && args[start] is "--help" or "-h")
            {
                PrintPresetsHelp(output);
                return 0;
            }

            throw new ArgumentException($"Unknown presets option '{args[start]}'.");
        }

        foreach (VirtualDevicePreset preset in VirtualDevicePresets.All)
        {
            output.WriteLine($"{preset.Id} - {preset.Description}");
        }

        return 0;
    }

    private static int ListCapabilityProfiles(
        IReadOnlyList<string> args,
        int start,
        TextWriter output)
    {
        if (start < args.Count)
        {
            if (start + 1 == args.Count && args[start] is "--help" or "-h")
            {
                output.WriteLine("Usage: llrp-virtual-device caps");
                return 0;
            }

            throw new ArgumentException($"Unknown caps option '{args[start]}'.");
        }

        foreach (VirtualDeviceProfileInfo profile in VirtualDeviceProfiles.All)
        {
            output.WriteLine(
                $"{profile.Id} - LLRP {profile.ProtocolVersion}, " +
                $"{profile.MaxNumberOfAntennas} antennas");
        }

        return 0;
    }

    private static int PrintCommandHelp(
        IReadOnlyList<string> args,
        int start,
        TextWriter output)
    {
        if (start >= args.Count)
        {
            PrintRootHelp(output);
            return 0;
        }

        switch (args[start])
        {
            case "run" or "start":
                PrintRunHelp(output);
                return 0;
            case "live":
                PrintLiveHelp(output);
                return 0;
            case "shell":
                PrintShellHelp(output);
                return 0;
            case "validate":
                PrintValidateHelp(output);
                return 0;
            case "presets" or "list-presets":
                PrintPresetsHelp(output);
                return 0;
            case "caps" or "list-caps":
                output.WriteLine("Usage: llrp-virtual-device caps");
                return 0;
            default:
                throw new ArgumentException($"Unknown help topic '{args[start]}'.");
        }
    }

    private static bool HasHelp(IReadOnlyList<string> args) =>
        args.Count == 1 && args[0] is "--help" or "-h";

    private static string ReadString(IReadOnlyList<string> args, ref int index, string option)
    {
        if (++index >= args.Count || string.IsNullOrWhiteSpace(args[index]))
        {
            throw new ArgumentException($"{option} requires a value.");
        }

        return args[index];
    }

    private static int ReadInt(IReadOnlyList<string> args, ref int index, string option)
    {
        string value = ReadString(args, ref index, option);
        return int.TryParse(value, out int parsed)
            ? parsed
            : throw new ArgumentException($"{option} requires an integer value.");
    }

    private static double ReadDouble(IReadOnlyList<string> args, ref int index, string option)
    {
        string value = ReadString(args, ref index, option);
        return double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double parsed)
            ? parsed
            : throw new ArgumentException($"{option} requires a numeric value.");
    }

    internal static string FormatEndpoint(IPAddress address, int port) =>
        address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
            ? $"[{address}]:{port}"
            : $"{address}:{port}";

    internal static string FormatProtocolVersion(VirtualDeviceProtocolVersion version) => version switch
    {
        VirtualDeviceProtocolVersion.Llrp101 => "1.0.1",
        VirtualDeviceProtocolVersion.Llrp11 => "1.1",
        VirtualDeviceProtocolVersion.Llrp20 => "2.0",
        _ => version.ToString(),
    };

    private static void PrintRootHelp(TextWriter output)
    {
        output.WriteLine("LLRP Virtual Device CLI - one process hosts one virtual LLRP device.");
        output.WriteLine();
        output.WriteLine("Usage:");
        output.WriteLine("  llrp-virtual-device              Enter the interactive device shell.");
        output.WriteLine("  llrp-virtual-device shell         Enter the interactive device shell.");
        output.WriteLine("  llrp-virtual-device run [options]");
        output.WriteLine("  llrp-virtual-device live [options]");
        output.WriteLine("  llrp-virtual-device validate --config <PATH>");
        output.WriteLine("  llrp-virtual-device presets");
        output.WriteLine("  llrp-virtual-device caps");
        output.WriteLine();
        output.WriteLine("No arguments enter the shell without creating a device.");
        output.WriteLine("The run command stays in the foreground; Ctrl+C stops the device.");
        output.WriteLine("The live command creates and starts one device, then enters the shell with RX/TX events enabled.");
        output.WriteLine("There is no multi-device manager or automatic restart in this CLI.");
    }

    private static void PrintRunHelp(TextWriter output)
    {
        output.WriteLine("Usage: llrp-virtual-device run [options]");
        PrintLaunchOptionsHelp(output);
    }

    private static void PrintLiveHelp(TextWriter output)
    {
        output.WriteLine("Usage: llrp-virtual-device live [options]");
        output.WriteLine("Automatically creates and starts one virtual device, then enters the interactive shell.");
        output.WriteLine("RX/TX lines appear when an LLRP client connects and exchanges messages with the device.");
        PrintLaunchOptionsHelp(output);
    }

    private static void PrintShellHelp(TextWriter output)
    {
        output.WriteLine("Usage: llrp-virtual-device shell");
        output.WriteLine("Enter the interactive one-device shell without creating a server.");
        output.WriteLine("Type 'help' inside the shell for lifecycle and log commands.");
    }

    private static void PrintLaunchOptionsHelp(TextWriter output)
    {
        output.WriteLine();
        output.WriteLine("  --config <PATH>                 Load one local JSON device configuration.");
        output.WriteLine("  --preset <ID>                   Built-in preset identifier.");
        output.WriteLine("  --caps <ID>                     Capability profile (default llrp1.0.1_standard).");
        output.WriteLine("  --data-source <PATH|default>    Independent inventory tag data source.");
        output.WriteLine("  --listen <IP>                   Exact listen address (default 127.0.0.1).");
        output.WriteLine("  --port <PORT>                   Exact TCP port (default 5084).");
        output.WriteLine("  --llrp <1.0.1|1.1|2.0>          Protocol version.");
        output.WriteLine("  --name <NAME>                   Device display name.");
        output.WriteLine("  --tag <EPC>                     Replace the deterministic EPC with one value.");
        output.WriteLine("  --interval-ms <N>               Report interval in milliseconds.");
        output.WriteLine("  --count <N>                     Maximum report messages; zero repeats.");
        output.WriteLine("  --rf-scenario <static|moving-tags|noisy>");
        output.WriteLine("  --seed <N>                      Deterministic RF simulation seed.");
        output.WriteLine("  --detection-probability <N>     Noisy scenario detection probability.");
        output.WriteLine("  --single-tag-probability <N>   Probability that a round returns one tag.");
        output.WriteLine("  --presence-cycle-rounds <N>    Moving-tag presence cycle.");
        output.WriteLine("  --rssi-jitter-db <N>            Noisy RSSI jitter.");
        output.WriteLine("  --max-tags-per-round <N>       Per-round observation limit.");
        output.WriteLine("  --max-client-connections <N>   Maximum connected LLRP clients.");
        output.WriteLine("  --keepalive-ms <N>              Device KEEPALIVE interval.");
        output.WriteLine("  --strict                        Enable strict standard inventory checks.");
        output.WriteLine("  --allow-implicit-stop-on-disable  Allow DISABLE_ROSPEC to stop Active ROSpecs implicitly.");
    }

    private static void PrintValidateHelp(TextWriter output) =>
        output.WriteLine("Usage: llrp-virtual-device validate --config <PATH>");

    private static void PrintPresetsHelp(TextWriter output) =>
        output.WriteLine("Usage: llrp-virtual-device presets");

}
