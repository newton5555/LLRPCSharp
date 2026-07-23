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
}

public sealed class ConnectCommand : AsyncCommand<ConnectSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, ConnectSettings settings, CancellationToken cancellationToken)
    {
        AnsiConsole.MarkupLine($"[grey]Connecting to LLRP Reader at[/] [cyan1]{settings.Host}:{settings.Port}[/]...");

        await using LlrpReader reader = LlrpReader.CreateBuilder(settings.Host)
            .WithPort(settings.Port)
            .WithConnectTimeout(TimeSpan.FromSeconds(5))
            .Build();

        try
        {
            await reader.ConnectAsync(cancellationToken);
            AnsiConsole.MarkupLine("[bold springgreen2]✔ Connected successfully![/]");
            AnsiConsole.WriteLine();

            if (reader.Identity is { } identity)
            {
                var table = new Table();
                table.AddColumn("[bold grey70]Property[/]");
                table.AddColumn("[bold grey70]Value[/]");

                table.AddRow("Manufacturer", $"[cyan1]{Markup.Escape(identity.ManufacturerName)}[/]");
                table.AddRow("Model", $"[springgreen2]{Markup.Escape(identity.ModelName)}[/]");
                table.AddRow("Firmware Version", $"[yellow]{Markup.Escape(identity.FirmwareVersion)}[/]");

                var panel = new Panel(table)
                    .Header("[bold deepskyblue1] READER IDENTITY [/]")
                    .Border(BoxBorder.Rounded);

                AnsiConsole.Write(panel);
            }

            await reader.DisconnectAsync(cancellationToken);
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[bold red]✖ Connection failed:[/] {Markup.Escape(ex.Message)}");
            return 1;
        }
    }
}
