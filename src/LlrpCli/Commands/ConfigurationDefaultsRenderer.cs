using System;
using Spectre.Console;
using LlrpSdk;

namespace LlrpCli.Commands;

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
