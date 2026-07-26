using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;
using LlrpSdk;

namespace LlrpCli.Commands;

public sealed class ConfigGetSettings : CommandSettings
{
    [CommandArgument(0, "<HOST>")]
    [Description("Hostname or IP address of the LLRP Reader.")]
    public string Host { get; init; } = string.Empty;

    [CommandOption("--port <PORT>")]
    [Description("TCP port of the LLRP Reader.")]
    [DefaultValue(5084)]
    public int Port { get; init; } = 5084;

    [CommandOption("--llrp <VERSION>")]
    [Description("Protocol version policy: auto, 1.0.1, or 1.1.")]
    [DefaultValue("auto")]
    public string LlrpVersion { get; init; } = "auto";
}

public sealed class ConfigGetCommand : AsyncCommand<ConfigGetSettings>
{
    private readonly IAnsiConsole _console;

    public ConfigGetCommand() : this(AnsiConsole.Console) { }

    public ConfigGetCommand(IAnsiConsole console)
    {
        _console = console ?? AnsiConsole.Console;
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, ConfigGetSettings settings, CancellationToken cancellationToken)
    {
        if (!ProtocolVersionPolicyParser.TryParse(settings.LlrpVersion, out LlrpProtocolVersionPolicy policy))
        {
            _console.MarkupLine("[bold red]✖ Invalid LLRP version:[/] use auto, 1.0.1, or 1.1.");
            return 2;
        }

        _console.MarkupLine($"[grey]Connecting to LLRP Reader at[/] [cyan1]{settings.Host}:{settings.Port}[/] to query configuration...");

        var builder = LlrpReader.CreateBuilder(settings.Host)
            .WithPort(settings.Port)
            .WithConnectTimeout(TimeSpan.FromSeconds(5))
            .WithProtocolVersionPolicy(policy);

        await using LlrpReader reader = builder.Build();

        try
        {
            await reader.ConnectAsync(cancellationToken);
            ReaderConfiguration config = await reader.QuerySettingsAsync(cancellationToken);
            _console.MarkupLine("[bold springgreen2]✔ Configuration retrieved successfully![/]");
            _console.WriteLine();

            // Render Keepalive Settings
            var keepaliveTable = new Table().Border(TableBorder.Rounded);
            keepaliveTable.AddColumn("[bold grey70]Parameter[/]");
            keepaliveTable.AddColumn("[bold grey70]Value[/]");
            keepaliveTable.AddRow("Trigger Type", $"[cyan1]{config.Keepalive.TriggerType}[/]");
            keepaliveTable.AddRow("Interval (ms)", $"[springgreen2]{config.Keepalive.IntervalMs}[/]");

            // Render Event Notifications Settings
            var eventsTable = new Table().Border(TableBorder.Rounded);
            eventsTable.AddColumn("[bold grey70]Event Notification[/]");
            eventsTable.AddColumn("[bold grey70]Status[/]");
            eventsTable.AddRow("Hopping Event", config.Events.HoppingEventEnabled ? "[green]Enabled[/]" : "[grey]Disabled[/]");
            eventsTable.AddRow("GPI Event", config.Events.GpiEventEnabled ? "[green]Enabled[/]" : "[grey]Disabled[/]");
            eventsTable.AddRow("ROSpec Event", config.Events.RoSpecEventEnabled ? "[green]Enabled[/]" : "[grey]Disabled[/]");
            eventsTable.AddRow("Report Buffer Warning", config.Events.ReportBufferWarningEnabled ? "[green]Enabled[/]" : "[grey]Disabled[/]");
            eventsTable.AddRow("Reader Exception Event", config.Events.ReaderExceptionEventEnabled ? "[green]Enabled[/]" : "[grey]Disabled[/]");
            eventsTable.AddRow("RF Survey Event", config.Events.RfSurveyEventEnabled ? "[green]Enabled[/]" : "[grey]Disabled[/]");
            eventsTable.AddRow("AISpec Event", config.Events.AiSpecEventEnabled ? "[green]Enabled[/]" : "[grey]Disabled[/]");
            eventsTable.AddRow("Antenna Event", config.Events.AntennaEventEnabled ? "[green]Enabled[/]" : "[grey]Disabled[/]");
            eventsTable.AddRow("Connection Attempt Event", config.Events.ConnectionAttemptEventEnabled ? "[green]Enabled[/]" : "[grey]Disabled[/]");
            eventsTable.AddRow("Connection Close Event", config.Events.ConnectionCloseEventEnabled ? "[green]Enabled[/]" : "[grey]Disabled[/]");

            // Render Antennas Configuration Table
            var antennasTable = new Table().Border(TableBorder.Rounded);
            antennasTable.AddColumn("[bold grey70]Antenna ID[/]");
            antennasTable.AddColumn("[bold grey70]Connected[/]");
            antennasTable.AddColumn("[bold grey70]Gain (dB)[/]");
            antennasTable.AddColumn("[bold grey70]Tx Power Index[/]");
            antennasTable.AddColumn("[bold grey70]Rx Sensitivity Index[/]");
            antennasTable.AddColumn("[bold grey70]Channel Index[/]");
            foreach (var ant in config.Antennas)
            {
                antennasTable.AddRow(
                    ant.AntennaId.ToString(),
                    ant.IsConnected.HasValue ? (ant.IsConnected.Value ? "[green]Yes[/]" : "[grey]No[/]") : "-",
                    ant.Gain?.ToString() ?? "-",
                    ant.TransmitPowerIndex?.ToString() ?? "-",
                    ant.ReceiverSensitivityIndex?.ToString() ?? "-",
                    ant.ChannelIndex?.ToString() ?? "-"
                );
            }

            // Render GPO Write Data Status Table
            var gposTable = new Table().Border(TableBorder.Rounded);
            gposTable.AddColumn("[bold grey70]GPO Port[/]");
            gposTable.AddColumn("[bold grey70]State[/]");
            foreach (var gpo in config.Gpos)
            {
                gposTable.AddRow(gpo.GpoPortNumber.ToString(), gpo.GpoData ? "[green]High (1)[/]" : "[grey]Low (0)[/]");
            }

            // Render GPI Current State Table
            var gpisTable = new Table().Border(TableBorder.Rounded);
            gpisTable.AddColumn("[bold grey70]GPI Port[/]");
            gpisTable.AddColumn("[bold grey70]Configured[/]");
            gpisTable.AddColumn("[bold grey70]Current State[/]");
            foreach (var gpi in config.Gpis)
            {
                gpisTable.AddRow(
                    gpi.GpiPortNumber.ToString(),
                    gpi.Configured ? "[green]Yes[/]" : "[grey]No[/]",
                    gpi.State == GpiState.High ? "[green]High (1)[/]" : (gpi.State == GpiState.Low ? "[grey]Low (0)[/]" : "[yellow]Unknown[/]")
                );
            }

            var rootGrid = new Grid();
            rootGrid.AddColumn();
            rootGrid.AddRow(new Panel(keepaliveTable).Header("[bold yellow] KEEPALIVE SETTINGS [/]").Border(BoxBorder.Rounded));
            rootGrid.AddRow(new Panel(eventsTable).Header("[bold yellow] EVENT NOTIFICATIONS [/]").Border(BoxBorder.Rounded));
            rootGrid.AddRow(new Panel(antennasTable).Header("[bold yellow] ANTENNAS CONFIGURATION [/]").Border(BoxBorder.Rounded));

            if (config.Gpos.Count > 0)
            {
                rootGrid.AddRow(new Panel(gposTable).Header("[bold yellow] GPO STATE [/]").Border(BoxBorder.Rounded));
            }
            if (config.Gpis.Count > 0)
            {
                rootGrid.AddRow(new Panel(gpisTable).Header("[bold yellow] GPI STATE [/]").Border(BoxBorder.Rounded));
            }

            _console.Write(rootGrid);

            await reader.DisconnectAsync(cancellationToken);
            return 0;
        }
        catch (Exception ex)
        {
            _console.MarkupLine($"[bold red]✖ Query failed:[/] {Markup.Escape(ex.Message)}");
            return 1;
        }
    }
}
