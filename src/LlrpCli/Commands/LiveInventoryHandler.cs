using Spectre.Console;
using LlrpSdk;

namespace LlrpCli.Commands;

/// <summary>
/// Owns SDK-managed inventory and its Live Shell tag-report stream.
/// </summary>
internal sealed class LiveInventoryHandler(IAnsiConsole console, LiveSessionContext session)
{
    public async Task HandleAsync(string[] tokens, CancellationToken cancellationToken)
    {
        if (session.Reader is null || !session.Reader.IsConnected)
        {
            console.MarkupLine("[yellow]Not connected. Run 'connect <host>' first.[/]");
            return;
        }

        if (tokens.Length < 2)
        {
            console.MarkupLine("[red]Usage:[/] inventory start [[antenna-id]] | stop | status");
            return;
        }

        switch (tokens[1].ToLowerInvariant())
        {
            case "start":
            {
                if (session.InventoryPumpTask is { IsCompleted: false })
                {
                    console.MarkupLine("[yellow]SDK-managed inventory is already running.[/]");
                    return;
                }

                ushort antennaId = 0;
                if (tokens.Length >= 3 && !ushort.TryParse(tokens[2], out antennaId))
                {
                    console.MarkupLine("[red]Antenna identifier must be an unsigned 16-bit integer.[/]");
                    return;
                }

                var settings = new ReaderSettings
                {
                    AntennaIds = [antennaId],
                };
                await session.Reader.StartAsync(settings, cancellationToken);

                var inventoryCancellation = new CancellationTokenSource();
                session.InventoryCancellation = inventoryCancellation;
                session.InventoryPumpTask = PumpTagReportsAsync(session.Reader, inventoryCancellation.Token);
                string scope = antennaId == 0 ? "all antennas" : $"antenna {antennaId}";
                console.MarkupLine($"[bold springgreen2]✔ SDK-managed inventory started for {scope}.[/]");
                break;
            }

            case "stop":
                await StopAsync(cancellationToken);
                console.MarkupLine("[bold springgreen2]✔ SDK-managed inventory stopped.[/]");
                break;

            case "status":
                console.MarkupLine(
                    session.Reader.OperationState == ReaderOperationState.Inventorying
                        ? "[springgreen2]SDK-managed inventory is running.[/]"
                        : $"[yellow]SDK-managed inventory is not running (state: {session.Reader.OperationState}).[/]");
                break;

            default:
                console.MarkupLine("[red]Usage:[/] inventory start [[antenna-id]] | stop | status");
                break;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        CancellationTokenSource? inventoryCancellation = session.InventoryCancellation;
        Task? inventoryPumpTask = session.InventoryPumpTask;
        session.InventoryCancellation = null;
        session.InventoryPumpTask = null;

        inventoryCancellation?.Cancel();
        try
        {
            if (session.Reader?.IsConnected == true && session.Reader.OperationState == ReaderOperationState.Inventorying)
            {
                await session.Reader.StopAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            if (inventoryPumpTask is not null)
            {
                try
                {
                    await inventoryPumpTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (inventoryCancellation?.IsCancellationRequested == true)
                {
                    // Stopping inventory owns cancellation of the report pump.
                }
            }

            inventoryCancellation?.Dispose();
        }
    }

    private async Task PumpTagReportsAsync(LlrpReader reader, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (TagReport report in reader.ReadTagReportsAsync(cancellationToken))
            {
                string epc = Convert.ToHexString(report.ElectronicProductCode.Span);
                string antenna = report.AntennaId?.ToString() ?? "-";
                string rssi = report.PeakRssi?.ToString() ?? "-";
                console.MarkupLine(
                    $"[cyan1]TAG[/] EPC=[bold]{epc}[/] Antenna={antenna} RSSI={rssi}");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The inventory command explicitly stopped the report stream.
        }
        catch (Exception exception)
        {
            console.MarkupLine($"[red]Inventory report stream failed:[/] {Markup.Escape(exception.Message)}");
        }
    }
}
