using System.ComponentModel;
using System.Text.Json;
using LlrpSdk;
using Spectre.Console;
using Spectre.Console.Cli;

namespace LlrpCli.Commands;

public sealed class CapsCommandSettings : CommandSettings
{
    [CommandArgument(0, "<HOST>")]
    [Description("Hostname or IP address of the LLRP Reader.")]
    public string Host { get; init; } = string.Empty;

    [CommandOption("--port <PORT>")]
    [DefaultValue(5084)]
    public int Port { get; init; } = 5084;

    [CommandOption("--llrp <VERSION>")]
    [DefaultValue("auto")]
    public string LlrpVersion { get; init; } = "auto";

    [CommandOption("--vendor <VENDOR>")]
    [DefaultValue("auto")]
    public string Vendor { get; init; } = "auto";

    [CommandOption("--output <FORMAT>")]
    [Description("Result format: json or table.")]
    [DefaultValue("json")]
    public string Output { get; init; } = "json";
}

/// <summary>Connects, fetches GET_READER_CAPABILITIES, then disconnects.</summary>
public sealed class CapsCommand : AsyncCommand<CapsCommandSettings>
{
    private readonly IAnsiConsole console;

    public CapsCommand() : this(AnsiConsole.Console) { }

    public CapsCommand(IAnsiConsole console)
    {
        this.console = console ?? AnsiConsole.Console;
    }

    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        CapsCommandSettings settings,
        CancellationToken cancellationToken)
    {
        string output = settings.Output.Trim().ToLowerInvariant();
        if (output is not ("json" or "table"))
        {
            console.MarkupLine("[red]--output must be json or table.[/]");
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
            await reader.ConnectAsync(cancellationToken).ConfigureAwait(false);
            ReaderCapabilities capabilities = await reader.RefreshCapabilitiesAsync(cancellationToken).ConfigureAwait(false);
            if (output == "json")
            {
                console.WriteLine(JsonSerializer.Serialize(new
                {
                    success = true,
                    host = settings.Host,
                    port = settings.Port,
                    maxNumberOfAntennas = capabilities.MaxNumberOfAntennas,
                    canSetAntennaProperties = capabilities.CanSetAntennaProperties,
                    hasUtcClockCapability = capabilities.HasUtcClockCapability,
                    maximumReceiveSensitivityDbm = capabilities.MaximumReceiveSensitivityDbm,
                    txPowers = capabilities.TxPowers,
                    rxSensitivities = capabilities.RxSensitivities,
                    rfModes = capabilities.RfModes,
                    resourceLimits = capabilities.ResourceLimits,
                    additionalParameterCount = capabilities.AdditionalParameters.Count,
                }, new JsonSerializerOptions { WriteIndented = true }));
            }
            else
            {
                var table = new Table().Border(TableBorder.Rounded);
                table.AddColumn("[bold]Capability[/]");
                table.AddColumn("[bold]Value[/]");
                table.AddRow("Max Antennas", capabilities.MaxNumberOfAntennas.ToString());
                table.AddRow("Set Antenna Props", capabilities.CanSetAntennaProperties ? "Yes" : "No");
                table.AddRow("UTC Clock", capabilities.HasUtcClockCapability ? "Yes" : "No");
                table.AddRow("Max Rx Sensitivity (dBm)", capabilities.MaximumReceiveSensitivityDbm?.ToString() ?? "-");
                table.AddRow("Tx Power Entries", capabilities.TxPowers.Count.ToString());
                table.AddRow("Rx Sensitivity Entries", capabilities.RxSensitivities.Count.ToString());
                table.AddRow("RF Modes", capabilities.RfModes.Count.ToString());
                table.AddRow("Max ROSpecs", capabilities.ResourceLimits.MaxNumROSpecs?.ToString() ?? "unknown");
                table.AddRow("Max AccessSpecs", capabilities.ResourceLimits.MaxNumAccessSpecs?.ToString() ?? "unknown");
                table.AddRow("Max OpSpecs/AccessSpec", capabilities.ResourceLimits.MaxNumOpSpecsPerAccessSpec?.ToString() ?? "unknown");
                table.AddRow("Max Select Filters", capabilities.ResourceLimits.MaxNumSelectFiltersPerQuery?.ToString() ?? "unknown");
                console.Write(table);
            }
            return 0;
        }
        catch (Exception exception)
        {
            if (output == "json")
            {
                console.WriteLine(JsonSerializer.Serialize(new { success = false, error = exception.Message }, new JsonSerializerOptions { WriteIndented = true }));
            }
            else
            {
                console.MarkupLine($"[bold red]Capabilities failed:[/] {Markup.Escape(exception.Message)}");
            }
            return 1;
        }
    }
}
