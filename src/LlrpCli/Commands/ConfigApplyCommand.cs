using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;
using LlrpSdk;

namespace LlrpCli.Commands;

public sealed class ConfigApplySettings : CommandSettings
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

    [CommandOption("--keepalive-type <TYPE>")]
    [Description("Keepalive trigger type: none or periodic.")]
    public string? KeepaliveType { get; init; }

    [CommandOption("--keepalive-interval <INTERVAL_MS>")]
    [Description("Periodic keepalive interval in milliseconds.")]
    public uint? KeepaliveInterval { get; init; }

    [CommandOption("--antenna <ID>")]
    [Description("Antenna ID to configure.")]
    public ushort? AntennaId { get; init; }

    [CommandOption("--tx-power <INDEX>")]
    [Description("Transmit power level index.")]
    public ushort? TransmitPower { get; init; }

    [CommandOption("--rx-sens <INDEX>")]
    [Description("Receiver sensitivity index.")]
    public ushort? ReceiverSensitivity { get; init; }

    [CommandOption("--channel <INDEX>")]
    [Description("Channel index.")]
    public ushort? ChannelIndex { get; init; }

    [CommandOption("--gpo-port <PORT>")]
    [Description("GPO Port number to write state.")]
    public ushort? GpoPort { get; init; }

    [CommandOption("--gpo-data <TRUE_FALSE>")]
    [Description("GPO state data (true/false).")]
    public bool? GpoData { get; init; }

    [CommandOption("--dry-run")]
    [Description("Show the resolved configuration change without sending SET_READER_CONFIG.")]
    public bool DryRun { get; init; }
}

public sealed class ConfigApplyCommand : AsyncCommand<ConfigApplySettings>
{
    private readonly IAnsiConsole _console;

    public ConfigApplyCommand() : this(AnsiConsole.Console) { }

    public ConfigApplyCommand(IAnsiConsole console)
    {
        _console = console ?? AnsiConsole.Console;
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, ConfigApplySettings settings, CancellationToken cancellationToken)
    {
        if (!ProtocolVersionPolicyParser.TryParse(settings.LlrpVersion, out LlrpProtocolVersionPolicy policy))
        {
            _console.MarkupLine("[bold red]✖ Invalid LLRP version:[/] use auto, 1.0.1, or 1.1.");
            return 2;
        }
        if (!TryValidateRequestedChanges(settings, out string? validationError))
        {
            _console.MarkupLine($"[bold red]✖ Invalid configuration change:[/] {Markup.Escape(validationError!)}");
            return 2;
        }

        _console.MarkupLine($"[grey]Connecting to LLRP Reader at[/] [cyan1]{settings.Host}:{settings.Port}[/] to apply settings...");

        var builder = LlrpReader.CreateBuilder(settings.Host)
            .WithPort(settings.Port)
            .WithConnectTimeout(TimeSpan.FromSeconds(5))
            .WithProtocolVersionPolicy(policy);

        await using LlrpReader reader = builder.Build();

        try
        {
            await reader.ConnectAsync(cancellationToken);
            
            // Query current configuration first
            ReaderConfiguration current = await reader.QuerySettingsAsync(cancellationToken);
            
            ReaderConfiguration updatedConfig = BuildUpdatedConfiguration(settings, current);

            if (settings.DryRun)
            {
                RenderDryRun(settings, updatedConfig);
                await reader.DisconnectAsync(cancellationToken);
                return 0;
            }

            await reader.ApplySettingsAsync(updatedConfig, cancellationToken);
            _console.MarkupLine("[bold springgreen2]✔ Configuration applied successfully![/]");

            await reader.DisconnectAsync(cancellationToken);
            return 0;
        }
        catch (Exception ex)
        {
            _console.MarkupLine($"[bold red]✖ Apply failed:[/] {Markup.Escape(ex.Message)}");
            return 1;
        }
    }

    internal static bool TryValidateRequestedChanges(ConfigApplySettings settings, out string? error)
    {
        bool antennaValueSpecified = settings.TransmitPower.HasValue ||
            settings.ReceiverSensitivity.HasValue ||
            settings.ChannelIndex.HasValue;
        bool keepaliveSpecified = settings.KeepaliveType is not null || settings.KeepaliveInterval.HasValue;
        bool gpoSpecified = settings.GpoPort.HasValue || settings.GpoData.HasValue;

        if (!keepaliveSpecified && !antennaValueSpecified && !gpoSpecified)
        {
            error = "Specify at least one editable setting; config apply never sends an empty write.";
            return false;
        }
        if (settings.KeepaliveType is not null &&
            !settings.KeepaliveType.Equals("none", StringComparison.OrdinalIgnoreCase) &&
            !settings.KeepaliveType.Equals("periodic", StringComparison.OrdinalIgnoreCase))
        {
            error = "--keepalive-type must be none or periodic.";
            return false;
        }
        if (antennaValueSpecified && !settings.AntennaId.HasValue)
        {
            error = "--tx-power, --rx-sens, and --channel require --antenna <ID>.";
            return false;
        }
        if (settings.AntennaId.HasValue && !antennaValueSpecified)
        {
            error = "--antenna <ID> requires at least one of --tx-power, --rx-sens, or --channel.";
            return false;
        }
        if (settings.GpoPort.HasValue != settings.GpoData.HasValue)
        {
            error = "--gpo-port <PORT> and --gpo-data <true|false> must be specified together.";
            return false;
        }

        error = null;
        return true;
    }

    internal static ReaderConfiguration BuildUpdatedConfiguration(
        ConfigApplySettings settings,
        ReaderConfiguration current)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(current);

        KeepaliveConfiguration keepalive = settings.KeepaliveType?.ToLowerInvariant() switch
        {
            "none" => new KeepaliveConfiguration { TriggerType = LlrpSdk.KeepaliveTriggerType.None, IntervalMs = 0 },
            "periodic" => new KeepaliveConfiguration
            {
                TriggerType = LlrpSdk.KeepaliveTriggerType.Periodic,
                IntervalMs = settings.KeepaliveInterval ?? current.Keepalive.IntervalMs
            },
            null when settings.KeepaliveInterval.HasValue => new KeepaliveConfiguration
            {
                TriggerType = LlrpSdk.KeepaliveTriggerType.Periodic,
                IntervalMs = settings.KeepaliveInterval.Value
            },
            null => current.Keepalive,
            _ => throw new CliUsageException("Invalid keepalive type: use none or periodic.")
        };

        var antennas = current.Antennas.ToList();
        if (settings.AntennaId is ushort antennaId)
        {
            int matchIndex = antennas.FindIndex(item => item.AntennaId == antennaId);
            AntennaConfigurationSettings original = matchIndex >= 0
                ? antennas[matchIndex]
                : new AntennaConfigurationSettings { AntennaId = antennaId };
            var updated = new AntennaConfigurationSettings
            {
                AntennaId = antennaId,
                IsConnected = original.IsConnected,
                Gain = original.Gain,
                TransmitPowerIndex = settings.TransmitPower ?? original.TransmitPowerIndex,
                ReceiverSensitivityIndex = settings.ReceiverSensitivity ?? original.ReceiverSensitivityIndex,
                ChannelIndex = settings.ChannelIndex ?? original.ChannelIndex
            };
            if (matchIndex >= 0)
            {
                antennas[matchIndex] = updated;
            }
            else
            {
                antennas.Add(updated);
            }
        }

        var gpos = current.Gpos.ToList();
        if (settings.GpoPort is ushort gpoPort)
        {
            int matchIndex = gpos.FindIndex(item => item.GpoPortNumber == gpoPort);
            var updated = new GpoConfiguration { GpoPortNumber = gpoPort, GpoData = settings.GpoData!.Value };
            if (matchIndex >= 0)
            {
                gpos[matchIndex] = updated;
            }
            else
            {
                gpos.Add(updated);
            }
        }

        return new ReaderConfiguration
        {
            Keepalive = keepalive,
            Antennas = antennas,
            Gpos = gpos,
            Gpis = current.Gpis,
            Events = current.Events
        };
    }

    private void RenderDryRun(ConfigApplySettings settings, ReaderConfiguration configuration)
    {
        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("[bold grey70]Setting[/]");
        table.AddColumn("[bold grey70]Resolved value[/]");

        if (settings.KeepaliveType is not null || settings.KeepaliveInterval.HasValue)
        {
            table.AddRow(
                "Keepalive",
                $"[cyan1]{configuration.Keepalive.TriggerType}[/], [springgreen2]{configuration.Keepalive.IntervalMs} ms[/]");
        }
        if (settings.AntennaId is ushort antennaId)
        {
            AntennaConfigurationSettings antenna = configuration.Antennas.Single(item => item.AntennaId == antennaId);
            table.AddRow(
                $"Antenna {antennaId}",
                $"Tx={antenna.TransmitPowerIndex?.ToString() ?? "-"}, Rx={antenna.ReceiverSensitivityIndex?.ToString() ?? "-"}, Channel={antenna.ChannelIndex?.ToString() ?? "-"}");
        }
        if (settings.GpoPort is ushort gpoPort)
        {
            GpoConfiguration gpo = configuration.Gpos.Single(item => item.GpoPortNumber == gpoPort);
            table.AddRow($"GPO {gpoPort}", gpo.GpoData ? "[green]High (1)[/]" : "[grey]Low (0)[/]");
        }

        _console.Write(new Panel(table)
            .Header("[bold yellow] DRY RUN — NO DEVICE CONFIGURATION WAS WRITTEN [/]")
            .Border(BoxBorder.Rounded));
    }
}
