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
            RenderUsage();
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

                ReaderSettings settings = ParseStartSettings(tokens);
                await session.Reader.StartAsync(settings, cancellationToken);

                var inventoryCancellation = new CancellationTokenSource();
                session.InventoryCancellation = inventoryCancellation;
                session.InventoryPumpTask = PumpTagReportsAsync(session.Reader, inventoryCancellation.Token);

                RenderStartedSummary(settings);
                break;
            }

            case "stop":
                await StopAsync(cancellationToken);
                console.MarkupLine("[bold springgreen2]✔ SDK-managed inventory stopped.[/]");
                break;

            case "status":
                RenderStatus();
                break;

            default:
                RenderUsage();
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

    private ReaderSettings ParseStartSettings(string[] tokens)
    {
        ReaderSettings baseSettings = new();
        int argStartIndex = 2;

        // Check if index 2 is an explicit settings file
        for (int i = 2; i < tokens.Length - 1; i++)
        {
            if (tokens[i].Equals("--settings", StringComparison.OrdinalIgnoreCase) ||
                tokens[i].Equals("--config", StringComparison.OrdinalIgnoreCase))
            {
                baseSettings = ReaderSettingsSerializer.LoadFromFile(tokens[i + 1]);
                break;
            }
        }

        // Positional antenna-id check (backward compatibility)
        ushort? positionalAntenna = null;
        if (tokens.Length >= 3 && !tokens[2].StartsWith('-') && ushort.TryParse(tokens[2], out ushort parsedPositional))
        {
            positionalAntenna = parsedPositional;
            argStartIndex = 3;
        }

        ushort? antennaId = positionalAntenna;
        byte? sessionVal = null;
        ushort? populationVal = null;
        ushort? modeVal = null;
        ushort? tariVal = null;

        bool attachEnable = baseSettings.AttachedData.Enabled;
        ushort attachBank = baseSettings.AttachedData.MemoryBank;
        ushort attachPtr = baseSettings.AttachedData.WordPointer;
        ushort attachLen = baseSettings.AttachedData.WordCount;
        string attachPwd = baseSettings.AttachedData.AccessPassword;

        for (int index = argStartIndex; index < tokens.Length; index += 2)
        {
            if (tokens[index].Equals("--settings", StringComparison.OrdinalIgnoreCase) ||
                tokens[index].Equals("--config", StringComparison.OrdinalIgnoreCase))
            {
                continue; // Processed earlier
            }

            if (index + 1 >= tokens.Length)
            {
                throw new CliUsageException($"Missing value for option '{tokens[index]}'.");
            }

            string value = tokens[index + 1];
            switch (tokens[index].ToLowerInvariant())
            {
                case "--antenna" when ushort.TryParse(value, out ushort ant):
                    antennaId = ant;
                    break;

                case "--session" when byte.TryParse(value, out byte s) && s <= 3:
                    sessionVal = s;
                    break;

                case "--population" when ushort.TryParse(value, out ushort p):
                    populationVal = p;
                    break;

                case "--mode" when ushort.TryParse(value, out ushort m):
                    modeVal = m;
                    break;

                case "--tari" when ushort.TryParse(value, out ushort t):
                    tariVal = t;
                    break;

                case "--attach-bank":
                    attachEnable = true;
                    attachBank = ParseMemoryBank(value);
                    break;

                case "--attach-ptr" when ushort.TryParse(value, out ushort ptr):
                    attachEnable = true;
                    attachPtr = ptr;
                    break;

                case "--attach-len" when ushort.TryParse(value, out ushort len):
                    attachEnable = true;
                    attachLen = len;
                    break;

                case "--attach-pwd":
                    attachEnable = true;
                    attachPwd = value;
                    break;

                default:
                    throw new CliUsageException($"Invalid inventory option '{tokens[index]}'.");
            }
        }

        var finalAntennas = antennaId.HasValue ? new[] { antennaId.Value } : baseSettings.AntennaIds;
        var finalAttached = attachEnable
            ? new AttachedDataOptions
            {
                Enabled = true,
                MemoryBank = attachBank,
                WordPointer = attachPtr,
                WordCount = attachLen,
                AccessPassword = attachPwd
            }
            : baseSettings.AttachedData;

        return baseSettings with
        {
            AntennaIds = finalAntennas,
            Session = sessionVal ?? baseSettings.Session,
            TagPopulationEstimate = populationVal ?? baseSettings.TagPopulationEstimate,
            ModeIndex = modeVal ?? baseSettings.ModeIndex,
            Tari = tariVal ?? baseSettings.Tari,
            AttachedData = finalAttached
        };
    }

    private static ushort ParseMemoryBank(string bank) => bank.ToLowerInvariant() switch
    {
        "reserved" or "0" => 0,
        "epc" or "1" => 1,
        "tid" or "2" => 2,
        "user" or "3" => 3,
        _ => throw new CliUsageException($"Invalid memory bank '{bank}'. Valid values: reserved|0, epc|1, tid|2, user|3.")
    };

    private void RenderStartedSummary(ReaderSettings settings)
    {
        string scope = settings.AntennaIds.Count == 1 && settings.AntennaIds[0] == 0 ? "All antennas" : $"Antenna {string.Join(',', settings.AntennaIds)}";
        string attachedInfo = settings.AttachedData.Enabled
            ? $"AttachedData [Bank={settings.AttachedData.MemoryBank}, Ptr={settings.AttachedData.WordPointer}, Len={settings.AttachedData.WordCount}]"
            : "No AttachedData";

        console.MarkupLine($"[bold springgreen2]✔ SDK-managed inventory started.[/] ({scope}, Session={settings.Session}, Pop={settings.TagPopulationEstimate}, Mode={settings.ModeIndex}, Tari={settings.Tari}, {attachedInfo})");
    }

    private void RenderStatus()
    {
        if (session.Reader is null)
        {
            return;
        }
        bool isRunning = session.Reader.OperationState == ReaderOperationState.Inventorying;
        string statusText = isRunning ? "[springgreen2]SDK-managed inventory is running.[/]" : $"[yellow]SDK-managed inventory is not running (state: {session.Reader.OperationState}).[/]";
        console.MarkupLine(statusText);

        if (session.Reader.CurrentSettings is { } settings)
        {
            string scope = settings.AntennaIds.Count == 1 && settings.AntennaIds[0] == 0 ? "All" : string.Join(',', settings.AntennaIds);
            console.MarkupLine($"  [dim]Antenna:[/] {scope} | [dim]Session:[/] {settings.Session} | [dim]Pop:[/] {settings.TagPopulationEstimate} | [dim]Mode:[/] {settings.ModeIndex} | [dim]Tari:[/] {settings.Tari}");
            if (settings.AttachedData.Enabled)
            {
                console.MarkupLine($"  [dim]AttachedData:[/] Bank={settings.AttachedData.MemoryBank}, Ptr={settings.AttachedData.WordPointer}, Len={settings.AttachedData.WordCount}");
            }
        }
    }

    private void RenderUsage()
    {
        console.MarkupLine("[red]Usage:[/] inventory start [[antenna-id]] [--session <0..3>] [--population <n>] [--mode <idx>] [--tari <nsec>] [--attach-bank <epc|tid|user|reserved>] [--attach-ptr <n>] [--attach-len <n>] [--settings <path>] | stop | status");
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

                string extra = string.Empty;
                if (report.AccessOperationResults is { Count: > 0 } ops && ops[0].ReadData.Count > 0)
                {
                    extra = $" Data=[yellow]{string.Join(' ', ops[0].ReadData.Select(w => w.ToString("X4")))}[/]";
                }

                console.MarkupLine(
                    $"[cyan1]TAG[/] EPC=[bold]{epc}[/] Antenna={antenna} RSSI={rssi}{extra}");
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
