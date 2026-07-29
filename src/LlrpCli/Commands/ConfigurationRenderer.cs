using Spectre.Console;
using LlrpSdk;
using LlrpSdk.Extensions.Impinj;

namespace LlrpCli.Commands;

internal static class ConfigurationRenderer
{
    public static void Render(IAnsiConsole console, ReaderConfiguration configuration, ReaderCapabilities? capabilities)
    {
        var overview = new Table().Border(TableBorder.Rounded);
        overview.AddColumn("[bold grey70]Setting[/]");
        overview.AddColumn("[bold grey70]Value[/]");
        overview.AddRow("Keepalive", $"[cyan1]{configuration.Keepalive.TriggerType}[/], {configuration.Keepalive.IntervalMs} ms");
        overview.AddRow("Antennas", configuration.Antennas.Count.ToString());
        overview.AddRow("GPI / GPO", $"{configuration.Gpis.Count} / {configuration.Gpos.Count}");
        overview.AddRow("Vendor settings", configuration.Extensions.Count == 0 ? "[grey]None[/]" : string.Join(", ", configuration.Extensions.Keys.Select(Markup.Escape)));

        var grid = new Grid();
        grid.AddColumn();
        grid.AddRow(new Panel(overview).Header("[bold yellow] READER CONFIGURATION [/]").Border(BoxBorder.Rounded));

        if (configuration.Antennas.Count > 0)
        {
            var antennas = new Table().Border(TableBorder.Rounded);
            antennas.AddColumn("[bold grey70]ID[/]");
            antennas.AddColumn("[bold grey70]Connected[/]");
            antennas.AddColumn("[bold grey70]Gain[/]");
            antennas.AddColumn("[bold grey70]Tx index[/]");
            antennas.AddColumn("[bold grey70]Tx dBm[/]");
            antennas.AddColumn("[bold grey70]Rx index[/]");
            antennas.AddColumn("[bold grey70]Rx dBm[/]");
            antennas.AddColumn("[bold grey70]Channel index[/]");
            foreach (AntennaConfigurationSettings antenna in configuration.Antennas)
            {
                TxPowerEntry? tx = capabilities?.TxPowers.FirstOrDefault(item => item.Index == antenna.TransmitPowerIndex);
                RxSensitivityEntry? rx = capabilities?.RxSensitivities.FirstOrDefault(item => item.Index == antenna.ReceiverSensitivityIndex);
                antennas.AddRow(
                    antenna.AntennaId.ToString(),
                    antenna.IsConnected is true ? "[green]Yes[/]" : antenna.IsConnected is false ? "[grey]No[/]" : "-",
                    antenna.Gain is short gain ? $"{gain} dB" : "-",
                    antenna.TransmitPowerIndex?.ToString() ?? "-",
                    tx is null ? "-" : $"{tx.TransmitPowerDbm:F2}",
                    antenna.ReceiverSensitivityIndex?.ToString() ?? "-",
                    rx is null ? "-" : $"{rx.ReceiveSensitivityDbm:F2}",
                    antenna.ChannelIndex?.ToString() ?? "-");
            }
            grid.AddRow(new Panel(antennas).Header("[bold yellow] ANTENNA CONFIGURATION — INDICES MAP THROUGH caps [/]").Border(BoxBorder.Rounded));
        }

        if (configuration.Gpos.Count > 0 || configuration.Gpis.Count > 0)
        {
            var gpio = new Table().Border(TableBorder.Rounded);
            gpio.AddColumn("[bold grey70]Kind[/]");
            gpio.AddColumn("[bold grey70]Port[/]");
            gpio.AddColumn("[bold grey70]Configured[/]");
            gpio.AddColumn("[bold grey70]State[/]");
            foreach (GpoConfiguration gpo in configuration.Gpos)
            {
                gpio.AddRow("GPO", gpo.GpoPortNumber.ToString(), "-", gpo.GpoData ? "[green]High (1)[/]" : "[grey]Low (0)[/]");
            }
            foreach (GpiStatus gpi in configuration.Gpis)
            {
                gpio.AddRow("GPI", gpi.GpiPortNumber.ToString(), gpi.Configured ? "[green]Yes[/]" : "[grey]No[/]", Markup.Escape(gpi.State.ToString()));
            }
            grid.AddRow(new Panel(gpio).Header("[bold yellow] GPIO STATUS [/]").Border(BoxBorder.Rounded));
        }

        var events = new Table().Border(TableBorder.Rounded);
        events.AddColumn("[bold grey70]Event[/]");
        events.AddColumn("[bold grey70]Enabled[/]");
        AddEvent(events, "Hopping", configuration.Events.HoppingEventEnabled);
        AddEvent(events, "GPI", configuration.Events.GpiEventEnabled);
        AddEvent(events, "ROSpec", configuration.Events.RoSpecEventEnabled);
        AddEvent(events, "Report buffer warning", configuration.Events.ReportBufferWarningEnabled);
        AddEvent(events, "Reader exception", configuration.Events.ReaderExceptionEventEnabled);
        AddEvent(events, "RF survey", configuration.Events.RfSurveyEventEnabled);
        AddEvent(events, "AISpec", configuration.Events.AiSpecEventEnabled);
        AddEvent(events, "Antenna", configuration.Events.AntennaEventEnabled);
        AddEvent(events, "Connection attempt", configuration.Events.ConnectionAttemptEventEnabled);
        AddEvent(events, "Connection close", configuration.Events.ConnectionCloseEventEnabled);
        grid.AddRow(new Panel(events).Header("[bold yellow] EVENT NOTIFICATIONS [/]").Border(BoxBorder.Rounded));

        if (configuration.Extensions.TryGetValue("impinj.InventorySettings", out object? extension) && extension is ImpinjReaderSettings impinj)
        {
            var vendor = new Table().Border(TableBorder.Rounded);
            vendor.AddColumn("[bold grey70]Impinj setting[/]");
            vendor.AddColumn("[bold grey70]Value[/]");
            vendor.AddRow("Regulatory region", impinj.RegulatoryRegion?.ToString() ?? "-");
            vendor.AddRow("Temperature", impinj.TemperatureCelsius is short temperature ? $"{temperature} °C" : "-");
            vendor.AddRow("Report buffer", impinj.ReportBufferMode?.ToString() ?? "-");
            vendor.AddRow("Link monitor", impinj.LinkMonitor is null ? "-" : $"Enabled={impinj.LinkMonitor.Enabled}, Threshold={impinj.LinkMonitor.LinkDownThreshold}");
            vendor.AddRow("AccessSpec", impinj.AccessSpec is null ? "-" : $"BlockWrite={impinj.AccessSpec.BlockWriteWordCount}, Retry={impinj.AccessSpec.OpSpecRetryCount}, Ordering={impinj.AccessSpec.OrderingMode}");
            vendor.AddRow("GPI debounce", impinj.GpiDebounce.Count == 0 ? "-" : string.Join(", ", impinj.GpiDebounce.Select(item => $"{item.GpiPortNumber}:{item.DebounceMilliseconds} ms")));
            grid.AddRow(new Panel(vendor).Header("[bold yellow] IMPINJ READER SETTINGS — READ ONLY [/]").Border(BoxBorder.Rounded));
        }

        console.Write(grid);
    }

    private static void AddEvent(Table table, string name, bool enabled) =>
        table.AddRow(name, enabled ? "[green]Enabled[/]" : "[grey]Disabled[/]");
}
