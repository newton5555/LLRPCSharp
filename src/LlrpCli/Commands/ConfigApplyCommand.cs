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
            
            // Modify Keepalive settings if provided
            KeepaliveConfiguration keepalive = current.Keepalive;
            if (settings.KeepaliveType != null)
            {
                if (string.Equals(settings.KeepaliveType, "none", StringComparison.OrdinalIgnoreCase))
                {
                    keepalive = new KeepaliveConfiguration { TriggerType = LlrpSdk.KeepaliveTriggerType.None, IntervalMs = 0 };
                }
                else if (string.Equals(settings.KeepaliveType, "periodic", StringComparison.OrdinalIgnoreCase))
                {
                    keepalive = new KeepaliveConfiguration 
                    { 
                        TriggerType = LlrpSdk.KeepaliveTriggerType.Periodic, 
                        IntervalMs = settings.KeepaliveInterval ?? current.Keepalive.IntervalMs 
                    };
                }
                else
                {
                    _console.MarkupLine("[bold red]✖ Invalid keepalive type:[/] use none or periodic.");
                    return 2;
                }
            }
            else if (settings.KeepaliveInterval.HasValue)
            {
                keepalive = new KeepaliveConfiguration 
                { 
                    TriggerType = LlrpSdk.KeepaliveTriggerType.Periodic, 
                    IntervalMs = settings.KeepaliveInterval.Value 
                };
            }

            // Modify Antenna configuration if provided
            var antennas = current.Antennas.ToList();
            if (settings.AntennaId.HasValue)
            {
                ushort id = settings.AntennaId.Value;
                var matchIndex = antennas.FindIndex(a => a.AntennaId == id);
                var original = matchIndex >= 0 ? antennas[matchIndex] : new AntennaConfigurationSettings { AntennaId = id };

                var updated = new AntennaConfigurationSettings
                {
                    AntennaId = id,
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

            // Modify GPO configuration if provided
            var gpos = current.Gpos.ToList();
            if (settings.GpoPort.HasValue)
            {
                ushort port = settings.GpoPort.Value;
                bool data = settings.GpoData ?? false;
                
                var matchIndex = gpos.FindIndex(g => g.GpoPortNumber == port);
                var updated = new GpoConfiguration { GpoPortNumber = port, GpoData = data };

                if (matchIndex >= 0)
                {
                    gpos[matchIndex] = updated;
                }
                else
                {
                    gpos.Add(updated);
                }
            }

            // Construct new config to apply
            var updatedConfig = new ReaderConfiguration
            {
                Keepalive = keepalive,
                Antennas = antennas,
                Gpos = gpos,
                Gpis = current.Gpis,
                Events = current.Events
            };

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
}
