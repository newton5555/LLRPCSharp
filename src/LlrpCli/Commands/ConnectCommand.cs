using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using LlrpSdk;

namespace LlrpCli.Commands;

public sealed class ConnectSettings : CommandSettings
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

public sealed class ConnectCommand : AsyncCommand<ConnectSettings>
{
    private readonly IAnsiConsole _console;

    public ConnectCommand() : this(AnsiConsole.Console) { }

    public ConnectCommand(IAnsiConsole console)
    {
        _console = console ?? AnsiConsole.Console;
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, ConnectSettings settings, CancellationToken cancellationToken)
    {
        if (!ProtocolVersionPolicyParser.TryParse(settings.LlrpVersion, out LlrpProtocolVersionPolicy policy))
        {
            _console.MarkupLine("[bold red]✖ Invalid LLRP version:[/] use auto, 1.0.1, or 1.1.");
            return 2;
        }

        _console.MarkupLine($"[grey]Connecting to LLRP Reader at[/] [cyan1]{settings.Host}:{settings.Port}[/]...");

        await using LlrpReader reader = LlrpReader.CreateBuilder(settings.Host)
            .WithPort(settings.Port)
            .WithConnectTimeout(TimeSpan.FromSeconds(5))
            .WithProtocolVersionPolicy(policy)
            .Build();

        try
        {
            await reader.ConnectAsync(cancellationToken);
            _console.MarkupLine("[bold springgreen2]✔ Connected successfully![/]");
            _console.WriteLine();

            if (reader.Identity is { } identity)
            {
                var table = new Table();
                table.AddColumn("[bold grey70]Property[/]");
                table.AddColumn("[bold grey70]Value[/]");

                table.AddRow("Manufacturer ID", $"[cyan1]{identity.ManufacturerId}[/]");
                table.AddRow("Model ID", $"[springgreen2]{identity.ModelId}[/]");
                table.AddRow("Firmware Version", $"[yellow]{Markup.Escape(identity.FirmwareVersion)}[/]");

                if (reader.Capabilities is { } capabilities)
                {
                    table.AddRow("Max Antennas", $"[white]{capabilities.MaxNumberOfAntennas}[/]");
                    table.AddRow("Set Antenna Properties", capabilities.CanSetAntennaProperties ? "[green]Yes[/]" : "[grey]No[/]");
                    table.AddRow("UTC Clock Support", capabilities.HasUtcClockCapability ? "[green]Yes[/]" : "[grey]No[/]");
                }

                var panel = new Panel(table)
                    .Header("[bold deepskyblue1] READER IDENTITY & CAPABILITIES [/]")
                    .Border(BoxBorder.Rounded);

                _console.Write(panel);
            }

            await reader.DisconnectAsync(cancellationToken);
            return 0;
        }
        catch (Exception ex)
        {
            _console.MarkupLine($"[bold red]✖ Connection failed:[/] {Markup.Escape(ex.Message)}");
            return 1;
        }
    }
}
