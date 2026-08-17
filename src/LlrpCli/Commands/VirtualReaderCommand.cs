using System.ComponentModel;
using System.Net;
using System.Text.Json;
using LlrpNet.Core.Protocol;
using LlrpVirtualReader;
using LlrpVirtualReader.Manager;
using Spectre.Console;
using Spectre.Console.Cli;

namespace LlrpCli.Commands;

public sealed class VirtualReaderCommandSettings : CommandSettings
{
    [CommandOption("--config <PATH>")]
    [Description("Load a local JSON reader/preset configuration document.")]
    public string? ConfigPath { get; init; }

    [CommandOption("--instance <ID>")]
    [Description("Select one instance from --config.")]
    public string? InstanceId { get; init; }

    [CommandOption("--validate-config")]
    [Description("Validate --config and exit without binding or starting a reader.")]
    public bool ValidateConfig { get; init; }

    [CommandOption("--list-presets")]
    [Description("List built-in or local presets and exit.")]
    public bool ListPresets { get; init; }

    [CommandOption("--listen <IP>")]
    [DefaultValue("127.0.0.1")]
    [Description("Exact local IP address to bind.")]
    public string ListenAddress { get; init; } = "127.0.0.1";

    [CommandOption("--port <PORT>")]
    [DefaultValue(5084)]
    [Description("Exact TCP port to bind.")]
    public int Port { get; init; } = 5084;

    [CommandOption("--llrp <VERSION>")]
    [DefaultValue("1.0.1")]
    [Description("Protocol version: 1.0.1 or 1.1.")]
    public string LlrpVersion { get; init; } = "1.0.1";

    [CommandOption("--name <NAME>")]
    [DefaultValue("Virtual Reader")]
    public string Name { get; init; } = "Virtual Reader";

    [CommandOption("--tag <EPC>")]
    [Description("Optional replacement deterministic EPC, in hexadecimal form.")]
    public string? Tag { get; init; }

    [CommandOption("--interval-ms <MILLISECONDS>")]
    [DefaultValue(100)]
    public int ReportIntervalMilliseconds { get; init; } = 100;

    [CommandOption("--count <COUNT>")]
    [DefaultValue(0)]
    [Description("Maximum report messages; zero repeats until stopped.")]
    public int ReportCount { get; init; }

    [CommandOption("--rf-scenario <SCENARIO>")]
    [DefaultValue("static")]
    [Description("RF-observable scenario: static, moving-tags, or noisy.")]
    public string RfScenario { get; init; } = "static";

    [CommandOption("--seed <SEED>")]
    [DefaultValue(2026)]
    [Description("Deterministic RF simulation seed.")]
    public int RandomSeed { get; init; } = 2026;

    [CommandOption("--strict")]
    public bool Strict { get; init; }
}

/// <summary>Runs the message-level virtual reader until Ctrl+C or cancellation.</summary>
public sealed class VirtualReaderCommand : AsyncCommand<VirtualReaderCommandSettings>
{
    private readonly IAnsiConsole _console;

    public VirtualReaderCommand() : this(AnsiConsole.Console) { }

    public VirtualReaderCommand(IAnsiConsole console)
    {
        _console = console ?? AnsiConsole.Console;
    }

    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        VirtualReaderCommandSettings settings,
        CancellationToken cancellationToken)
    {
        if (settings.ValidateConfig || !string.IsNullOrWhiteSpace(settings.ConfigPath))
        {
            return await ExecuteConfigurationAsync(settings, cancellationToken).ConfigureAwait(false);
        }

        if (settings.ListPresets)
        {
            PrintPresets(new VirtualReaderPresetCatalog().Presets);
            return 0;
        }

        if (!IPAddress.TryParse(settings.ListenAddress, out IPAddress? listenAddress))
        {
            _console.MarkupLine($"[red]Invalid listen address: {Markup.Escape(settings.ListenAddress)}[/]");
            return 2;
        }

        if (settings.Port is <= 0 or > ushort.MaxValue)
        {
            _console.MarkupLine("[red]--port must be between 1 and 65535.[/]");
            return 2;
        }

        if (settings.ReportIntervalMilliseconds <= 0 || settings.ReportCount < 0)
        {
            _console.MarkupLine("[red]--interval-ms must be positive and --count cannot be negative.[/]");
            return 2;
        }

        VirtualReaderRfScenario rfScenario;
        try
        {
            rfScenario = VirtualReaderConfiguration.ParseRfScenario(settings.RfScenario);
        }
        catch (InvalidDataException exception)
        {
            _console.MarkupLine($"[red]{Markup.Escape(exception.Message)}[/]");
            return 2;
        }

        LlrpProtocolVersion version = settings.LlrpVersion.Trim() switch
        {
            "1.0.1" => LlrpProtocolVersion.Version101,
            "1.1" => LlrpProtocolVersion.Version11,
            _ => throw new ArgumentException("--llrp must be 1.0.1 or 1.1."),
        };

        VirtualReaderOptions readerOptions = new()
        {
            ReaderName = settings.Name,
            ProtocolVersion = version,
            UseStrictStandardInventoryProfile = settings.Strict,
            Reports = new VirtualReaderReportOptions
            {
                ReportInterval = TimeSpan.FromMilliseconds(settings.ReportIntervalMilliseconds),
                ReportCount = settings.ReportCount,
                Repeat = true,
            },
            RfSimulation = new VirtualReaderRfSimulationOptions
            {
                Scenario = rfScenario,
                RandomSeed = settings.RandomSeed,
            },
        };
        if (!string.IsNullOrWhiteSpace(settings.Tag))
        {
            byte[] epc;
            try
            {
                epc = Convert.FromHexString(settings.Tag.Trim());
            }
            catch (FormatException exception)
            {
                _console.MarkupLine($"[red]Invalid --tag EPC: {Markup.Escape(exception.Message)}[/]");
                return 2;
            }

            readerOptions = readerOptions with
            {
                TagSource = new FixedVirtualTagSource(
                [
                    new VirtualTag { ElectronicProductCode = epc },
                ]),
            };
        }

        string presetId = version == LlrpProtocolVersion.Version11
            ? VirtualReaderPresetIds.Standard11Basic
            : settings.Strict
                ? VirtualReaderPresetIds.Standard101Strict
                : VirtualReaderPresetIds.Standard101Basic;
        await using var manager = new VirtualReaderManager();
        VirtualReaderInstanceInfo instance = await manager.CreateAndStartAsync(
            new VirtualReaderInstanceOptions
            {
                Name = settings.Name,
                PresetId = presetId,
                ListenAddress = listenAddress,
                Port = settings.Port,
                ReaderOptions = readerOptions,
            },
            cancellationToken).ConfigureAwait(false);
        _console.MarkupLine(
            $"[green]Virtual reader '{Markup.Escape(settings.Name)}' listening on " +
            $"{Markup.Escape(FormatEndpoint(instance.ListenAddress, instance.BoundPort))} using LLRP {settings.LlrpVersion}.[/]" );
        _console.MarkupLine("Press Ctrl+C to stop.");

        await WaitForStopAsync(cancellationToken).ConfigureAwait(false);

        return 0;
    }

    private async Task<int> ExecuteConfigurationAsync(
        VirtualReaderCommandSettings settings,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.ConfigPath))
        {
            _console.MarkupLine("[red]--config is required with --validate-config or configuration startup.[/]");
            return 2;
        }

        VirtualReaderConfiguration configuration;
        try
        {
            configuration = VirtualReaderConfiguration.Load(settings.ConfigPath);
        }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidDataException or FormatException or ArgumentException)
        {
            _console.MarkupLine($"[red]Invalid virtual-reader configuration: {Markup.Escape(exception.Message)}[/]");
            return 2;
        }

        if (settings.ValidateConfig)
        {
            _console.MarkupLine(
                $"[green]Configuration is valid: {configuration.Instances.Count} instance(s), " +
                $"{configuration.Presets.Count} local preset(s).[/]");
            return 0;
        }

        if (settings.ListPresets)
        {
            PrintPresets(configuration.CreateCatalog().Presets);
            return 0;
        }

        await using var manager = new VirtualReaderManager(configuration.CreateCatalog());
        VirtualReaderInstanceInfo instance = await manager.CreateAndStartAsync(
            configuration.BuildInstanceOptions(settings.InstanceId),
            cancellationToken).ConfigureAwait(false);
        _console.MarkupLine(
            $"[green]Configured virtual reader '{Markup.Escape(instance.Name)}' listening on " +
            $"{Markup.Escape(FormatEndpoint(instance.ListenAddress, instance.BoundPort))} " +
            $"using LLRP {instance.ProtocolVersion}.[/]");
        _console.MarkupLine("Configuration was loaded explicitly; press Ctrl+C to stop.");
        await WaitForStopAsync(cancellationToken).ConfigureAwait(false);
        return 0;
    }

    private async Task WaitForStopAsync(CancellationToken cancellationToken)
    {
        using var stopSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            stopSource.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, stopSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stopSource.IsCancellationRequested)
        {
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    private void PrintPresets(IReadOnlyList<IVirtualReaderPresetContributor> presets)
    {
        foreach (IVirtualReaderPresetContributor preset in presets)
        {
            _console.MarkupLine($"{Markup.Escape(preset.Id)} - {Markup.Escape(preset.Description)}");
        }
    }

    private static string FormatEndpoint(IPAddress address, int port) =>
        address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
            ? $"[{address}]:{port}"
            : $"{address}:{port}";
}
