using System.ComponentModel;
using System.Text.Json;
using LlrpSdk;
using Spectre.Console;
using Spectre.Console.Cli;

namespace LlrpCli.Commands;

public sealed class OneShotInventorySettings : CommandSettings
{
    [CommandArgument(0, "<HOST>")]
    [Description("Hostname or IP address of the LLRP Reader.")]
    public string Host { get; init; } = string.Empty;

    [CommandOption("--port <PORT>")]
    [DefaultValue(5084)]
    public int Port { get; init; } = 5084;

    [CommandOption("--settings <FILE>")]
    [Description("ReaderSettings JSON file; SDK defaults are used when omitted.")]
    public string? SettingsFile { get; init; }

    [CommandOption("--duration <SECONDS>")]
    [Description("Positive inventory duration in seconds.")]
    [DefaultValue(10)]
    public int DurationSeconds { get; init; } = 10;

    [CommandOption("--output <FORMAT>")]
    [Description("Result format: json or table.")]
    [DefaultValue("json")]
    public string Output { get; init; } = "json";

    [CommandOption("--llrp <VERSION>")]
    [DefaultValue("auto")]
    public string LlrpVersion { get; init; } = "auto";

    [CommandOption("--vendor <VENDOR>")]
    [DefaultValue("auto")]
    public string Vendor { get; init; } = "auto";

    [CommandOption("--yes")]
    [Description("Confirm managed resource takeover and Reader configuration apply.")]
    public bool Confirmed { get; init; }
}

/// <summary>Connects, applies one settings source, inventories tags, and cleans managed resources.</summary>
public sealed class InventoryCommand : AsyncCommand<OneShotInventorySettings>
{
    private readonly IAnsiConsole console;

    public InventoryCommand() : this(AnsiConsole.Console) { }

    public InventoryCommand(IAnsiConsole console)
    {
        this.console = console ?? AnsiConsole.Console;
    }

    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        OneShotInventorySettings settings,
        CancellationToken cancellationToken)
    {
        string output = settings.Output.Trim().ToLowerInvariant();
        if (settings.DurationSeconds <= 0)
        {
            console.MarkupLine("[red]--duration must be a positive whole number of seconds.[/]");
            return 2;
        }
        if (output is not ("json" or "table"))
        {
            console.MarkupLine("[red]--output must be json or table.[/]");
            return 2;
        }
        if (!settings.Confirmed)
        {
            console.MarkupLine("[red]inventory applies ReaderSettings and takes over ROSpec/AccessSpec resources. Repeat with --yes.[/]");
            return 2;
        }
        if (!CliConnectionOptions.TryCreate(
            settings.Host,
            settings.Port,
            settings.LlrpVersion,
            settings.Vendor,
            out CliConnectionOptions options,
            out string error))
        {
            console.MarkupLine($"[red]{Markup.Escape(error)}[/]");
            return 2;
        }

        await using LlrpReader reader = options.CreateReaderBuilder()
            .WithConnectTimeout(TimeSpan.FromSeconds(5))
            .Build();
        try
        {
            if (output == "table")
            {
                console.MarkupLine($"[grey]Connecting to[/] [cyan1]{Markup.Escape(options.Host)}:{options.Port}[/]...");
            }
            await reader.ConnectAsync(cancellationToken).ConfigureAwait(false);
            ReaderSettings requested;
            string settingsSource;
            if (settings.SettingsFile is null)
            {
                ReaderSettingsDefaults defaults = await reader.GetDefaultSettingsAsync(cancellationToken).ConfigureAwait(false);
                requested = defaults.Settings;
                settingsSource = defaults.ProfileId;
            }
            else
            {
                requested = ManagedSettingsWorkflow.Load(reader, settings.SettingsFile);
                settingsSource = settings.SettingsFile;
            }

            InventorySettings inventory = requested.Inventory ?? throw new CliUsageException(
                "inventory requires Settings with managed Inventory intent.");
            // A one-shot inventory always starts immediately and is bounded by the command duration.
            requested = requested with
            {
                Inventory = inventory with
                {
                    StartTrigger = new InventoryStartTrigger(),
                    StopTrigger = new InventoryStopTrigger(),
                },
            };

            SettingsValidationResult validation = await ManagedSettingsWorkflow.ValidateAsync(
                reader,
                requested,
                cancellationToken).ConfigureAwait(false);
            if (!validation.IsValid)
            {
                RenderValidationFailure(output, validation);
                return 3;
            }

            await ManagedSettingsWorkflow.ApplyAsync(reader, requested, cancellationToken).ConfigureAwait(false);
            var tags = new Dictionary<string, ScanTagAccumulator>(StringComparer.Ordinal);
            InventorySession? inventorySession = null;
            try
            {
                inventorySession = await reader.StartInventoryAsync(cancellationToken).ConfigureAwait(false);
                using var duration = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                duration.CancelAfter(TimeSpan.FromSeconds(settings.DurationSeconds));
                try
                {
                    await foreach (TagReport report in inventorySession.ReadReportsAsync(duration.Token).ConfigureAwait(false))
                    {
                        string epc = Convert.ToHexString(report.ElectronicProductCode.Span);
                        if (!tags.TryGetValue(epc, out ScanTagAccumulator? tag))
                        {
                            tag = new ScanTagAccumulator(epc);
                            tags.Add(epc, tag);
                        }
                        tag.Observe(report);
                    }
                }
                catch (OperationCanceledException) when (duration.IsCancellationRequested)
                {
                }
            }
            finally
            {
                if (inventorySession is not null && reader.OperationState == ReaderOperationState.Inventorying)
                {
                    await inventorySession.StopAsync(CancellationToken.None).ConfigureAwait(false);
                }
                if (reader.CurrentInventorySettings is not null)
                {
                    await reader.ClearManagedSettingsAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }

            RenderResult(settings, output, reader, settingsSource, tags.Values);
            return 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return 1;
        }
        catch (Exception exception)
        {
            if (output == "json")
            {
                console.WriteLine(JsonSerializer.Serialize(new { success = false, error = exception.Message }, JsonOptions));
            }
            else
            {
                console.MarkupLine($"[bold red]Scan failed:[/] {Markup.Escape(exception.Message)}");
            }
            return 1;
        }
    }

    private void RenderValidationFailure(string output, SettingsValidationResult validation)
    {
        if (output == "json")
        {
            console.WriteLine(JsonSerializer.Serialize(new
            {
                success = false,
                error = "settings_validation_failed",
                diagnostics = validation.Diagnostics,
            }, JsonOptions));
        }
        else
        {
            SettingsRenderer.RenderValidation(console, validation);
        }
    }

    private void RenderResult(
        OneShotInventorySettings settings,
        string output,
        LlrpReader reader,
        string settingsSource,
        IEnumerable<ScanTagAccumulator> values)
    {
        ScanTagResult[] tags = values
            .OrderBy(static item => item.Epc, StringComparer.Ordinal)
            .Select(static item => item.ToResult())
            .ToArray();
        if (output == "json")
        {
            console.WriteLine(JsonSerializer.Serialize(new
            {
                success = true,
                reader = new
                {
                    host = settings.Host,
                    port = settings.Port,
                    manufacturerId = reader.Identity?.ManufacturerId,
                    modelId = reader.Identity?.ModelId,
                    firmware = reader.Identity?.FirmwareVersion,
                },
                settingsSource,
                durationSeconds = settings.DurationSeconds,
                uniqueTagCount = tags.Length,
                tags,
                managedInventoryCleared = true,
                readerConfigurationRetained = true,
            }, JsonOptions));
            return;
        }

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("[bold]EPC[/]");
        table.AddColumn("[bold]Seen[/]");
        table.AddColumn("[bold]Antennas[/]");
        table.AddColumn("[bold]Peak RSSI[/]");
        foreach (ScanTagResult tag in tags)
        {
            table.AddRow(
                Markup.Escape(tag.Epc),
                tag.SeenCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Markup.Escape(string.Join(',', tag.AntennaIds)),
                tag.PeakRssi?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-");
        }
        console.Write(new Panel(table)
            .Header($"[bold deepskyblue1] Scan complete: {tags.Length} unique tags [/]")
            .Border(BoxBorder.Rounded));
        console.MarkupLine("[grey]Managed inventory resources were cleared; Reader configuration remains applied.[/]");
    }

    private static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private sealed class ScanTagAccumulator(string epc)
    {
        private readonly HashSet<ushort> antennas = [];

        public string Epc { get; } = epc;
        public int SeenCount { get; private set; }
        public sbyte? PeakRssi { get; private set; }
        public ushort? ChannelIndex { get; private set; }
        public IReadOnlyDictionary<string, object?>? Extensions { get; private set; }

        public void Observe(TagReport report)
        {
            SeenCount += report.SeenCount ?? 1;
            if (report.AntennaId is ushort antennaId)
            {
                antennas.Add(antennaId);
            }
            if (report.PeakRssi is sbyte rssi && (PeakRssi is null || rssi > PeakRssi))
            {
                PeakRssi = rssi;
            }
            ChannelIndex = report.ChannelIndex ?? ChannelIndex;
            Extensions = report.Extensions ?? Extensions;
        }

        public ScanTagResult ToResult() => new(
            Epc,
            SeenCount,
            antennas.Order().ToArray(),
            PeakRssi,
            ChannelIndex,
            Extensions);
    }

    private sealed record ScanTagResult(
        string Epc,
        int SeenCount,
        IReadOnlyList<ushort> AntennaIds,
        sbyte? PeakRssi,
        ushort? ChannelIndex,
        IReadOnlyDictionary<string, object?>? Extensions);
}
