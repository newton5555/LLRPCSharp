using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;
using LlrpSdk;

namespace LlrpCli.Commands;

/// <summary>Displays the non-writing configuration baseline selected by the SDK for one initialized reader.</summary>
public sealed class ConfigDefaultsSettings : CommandSettings
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

public sealed class ConfigDefaultsCommand : AsyncCommand<ConfigDefaultsSettings>
{
    private readonly IAnsiConsole _console;

    public ConfigDefaultsCommand() : this(AnsiConsole.Console) { }

    public ConfigDefaultsCommand(IAnsiConsole console)
    {
        _console = console ?? AnsiConsole.Console;
    }

    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        ConfigDefaultsSettings settings,
        CancellationToken cancellationToken)
    {
        if (!ProtocolVersionPolicyParser.TryParse(settings.LlrpVersion, out LlrpProtocolVersionPolicy policy))
        {
            _console.MarkupLine("[bold red]✖ Invalid LLRP version:[/] use auto, 1.0.1, or 1.1.");
            return 2;
        }

        _console.MarkupLine($"[grey]Connecting to LLRP Reader at[/] [cyan1]{settings.Host}:{settings.Port}[/] to resolve SDK defaults...");
        var builder = LlrpReader.CreateBuilder(settings.Host)
            .WithPort(settings.Port)
            .WithConnectTimeout(TimeSpan.FromSeconds(5))
            .WithProtocolVersionPolicy(policy);
        await using LlrpReader reader = builder.Build();

        try
        {
            await reader.ConnectAsync(cancellationToken);
            ReaderConfigurationDefaultsResult result = reader.GetDefaultConfigurationResult();
            ConfigDefaultsRenderer.Render(_console, result);
            await reader.DisconnectAsync(cancellationToken);
            return 0;
        }
        catch (Exception ex)
        {
            _console.MarkupLine($"[bold red]✖ Default configuration lookup failed:[/] {Markup.Escape(ex.Message)}");
            return 1;
        }
    }
}

internal static class ConfigDefaultsRenderer
{
    public static void Render(IAnsiConsole console, ReaderConfigurationDefaultsResult result)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(result);

        ReaderConfiguration configuration = result.Configuration;
        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("[bold grey70]Setting[/]");
        table.AddColumn("[bold grey70]SDK default[/]");
        table.AddRow(
            "Profile source",
            result.IsGenericFallback
                ? "[yellow]Generic safe fallback[/]"
                : $"[cyan1]{Markup.Escape(result.ProviderId!)}[/] / [springgreen2]{Markup.Escape(result.ProfileId!)}[/]");
        table.AddRow("Keepalive", $"{configuration.Keepalive.TriggerType}, {configuration.Keepalive.IntervalMs} ms");
        table.AddRow("Antenna overrides", configuration.Antennas.Count.ToString());
        table.AddRow("GPO overrides", configuration.Gpos.Count.ToString());
        table.AddRow("Event overrides", HasEnabledEvent(configuration.Events) ? "[green]Present[/]" : "[grey]None[/]");
        table.AddRow("Extension defaults", configuration.Extensions.Count.ToString());

        console.Write(new Panel(table)
            .Header("[bold yellow] SDK CONFIGURATION DEFAULTS — NOT DEVICE STATE, NO WRITE [/]")
            .Border(BoxBorder.Rounded));
    }

    private static bool HasEnabledEvent(EventNotificationConfiguration events) =>
        events.HoppingEventEnabled || events.GpiEventEnabled || events.RoSpecEventEnabled ||
        events.ReportBufferWarningEnabled || events.ReaderExceptionEventEnabled || events.RfSurveyEventEnabled ||
        events.AiSpecEventEnabled || events.AntennaEventEnabled || events.ConnectionAttemptEventEnabled ||
        events.ConnectionCloseEventEnabled;
}
