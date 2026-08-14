using System.ComponentModel;
using System.Text.Json;
using LlrpSdk;
using Spectre.Console;
using Spectre.Console.Cli;

namespace LlrpCli.Commands;

public sealed class StatusCommandSettings : CommandSettings
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

/// <summary>Connects, reports negotiated identity/version/extension status, then disconnects.</summary>
public sealed class StatusCommand : AsyncCommand<StatusCommandSettings>
{
    private readonly IAnsiConsole console;

    public StatusCommand() : this(AnsiConsole.Console) { }

    public StatusCommand(IAnsiConsole console)
    {
        this.console = console ?? AnsiConsole.Console;
    }

    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        StatusCommandSettings settings,
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
            var result = new
            {
                success = true,
                host = settings.Host,
                port = settings.Port,
                connectionState = reader.ConnectionState.ToString(),
                connectionId = reader.ConnectionId,
                negotiatedVersion = reader.NegotiatedVersion.ToString(),
                operationState = reader.OperationState.ToString(),
                resourceMode = reader.ResourceMode.ToString(),
                managedStateSynchronized = reader.IsManagedStateSynchronized,
                manufacturerId = reader.Identity?.ManufacturerId,
                modelId = reader.Identity?.ModelId,
                firmware = reader.Identity?.FirmwareVersion,
                activeExtensions = reader.Extensions.Select(static ext => ext.Id).ToArray(),
            };
            if (output == "json")
            {
                console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            }
            else
            {
                var table = new Table().Border(TableBorder.Rounded);
                table.AddColumn("[bold]Property[/]");
                table.AddColumn("[bold]Value[/]");
                table.AddRow("Host", $"{settings.Host}:{settings.Port}");
                table.AddRow("LLRP Version", reader.NegotiatedVersion.ToString());
                table.AddRow("Manufacturer", reader.Identity?.ManufacturerId.ToString() ?? "-");
                table.AddRow("Model", reader.Identity?.ModelId.ToString() ?? "-");
                table.AddRow("Firmware", reader.Identity?.FirmwareVersion ?? "-");
                table.AddRow("Extensions", string.Join(", ", reader.Extensions.Select(static ext => ext.Id)));
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
                console.MarkupLine($"[bold red]Status failed:[/] {Markup.Escape(exception.Message)}");
            }
            return 1;
        }
    }
}
