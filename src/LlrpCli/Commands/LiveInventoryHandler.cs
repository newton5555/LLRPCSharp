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
        if (tokens.Length < 2)
        {
            RenderUsage();
            return;
        }

        switch (tokens[1].ToLowerInvariant())
        {
            case "settings":
                HandleSettings(tokens);
                return;

            case "start":
            {
                if (!EnsureConnected())
                {
                    return;
                }
                LlrpReader reader = session.Reader!;
                if (session.InventoryPumpTask is { IsCompleted: false })
                {
                    console.MarkupLine("[yellow]SDK-managed inventory is already running.[/]");
                    return;
                }

                ReaderSettings settings = ParseStartSettings(tokens);
                await reader.StartAsync(settings, cancellationToken);

                var inventoryCancellation = new CancellationTokenSource();
                session.InventoryCancellation = inventoryCancellation;
                session.InventoryPumpTask = PumpTagReportsAsync(reader, inventoryCancellation.Token);

                RenderStartedSummary(settings);
                break;
            }

            case "stop":
                if (!EnsureConnected())
                {
                    return;
                }
                await StopAsync(cancellationToken);
                console.MarkupLine("[bold springgreen2]✔ SDK-managed inventory stopped.[/]");
                break;

            case "status":
                if (!EnsureConnected())
                {
                    return;
                }
                RenderStatus();
                break;

            default:
                RenderUsage();
                break;
        }
    }

    private bool EnsureConnected()
    {
        if (session.Reader is not null && session.Reader.IsConnected)
        {
            return true;
        }

        console.MarkupLine("[yellow]Not connected. Run 'connect <host>' first.[/]");
        return false;
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

    private void HandleSettings(string[] tokens)
    {
        if (tokens.Length < 3)
        {
            console.MarkupLine("[red]Usage:[/] inventory settings show|set [options]|load <path>|save <path>|reset");
            return;
        }

        switch (tokens[2].ToLowerInvariant())
        {
            case "show" when tokens.Length == 3:
                RenderSettings(" INVENTORY SETTINGS DRAFT ", session.DesiredInventorySettings);
                return;

            case "reset" when tokens.Length == 3:
                session.DesiredInventorySettings = new ReaderSettings();
                console.MarkupLine("[bold springgreen2]✔ Inventory settings reset to SDK defaults.[/]");
                return;

            case "load" when tokens.Length == 4:
                session.DesiredInventorySettings = ReaderSettingsSerializer.LoadFromFile(tokens[3]);
                console.MarkupLine($"[bold springgreen2]✔ Inventory settings loaded from[/] [cyan1]{Markup.Escape(tokens[3])}[/].");
                return;

            case "save" when tokens.Length == 4:
                ReaderSettingsSerializer.SaveToFile(tokens[3], session.DesiredInventorySettings);
                console.MarkupLine($"[bold springgreen2]✔ Inventory settings saved to[/] [cyan1]{Markup.Escape(tokens[3])}[/].");
                return;

            case "set":
                session.DesiredInventorySettings = ParseSettingsOptions(session.DesiredInventorySettings, tokens, 3);
                console.MarkupLine("[bold springgreen2]✔ Inventory settings draft updated.[/]");
                RenderSettings(" INVENTORY SETTINGS DRAFT ", session.DesiredInventorySettings);
                return;

            default:
                console.MarkupLine("[red]Usage:[/] inventory settings show|set [options]|load <path>|save <path>|reset");
                return;
        }
    }

    private ReaderSettings ParseStartSettings(string[] tokens)
    {
        if (tokens.Length == 2)
        {
            return session.DesiredInventorySettings;
        }

        if (tokens.Length != 4 || !tokens[2].Equals("--antennas", StringComparison.OrdinalIgnoreCase))
        {
            throw new CliUsageException("Usage: inventory start [--antennas <id,id|all>]");
        }

        return session.DesiredInventorySettings with { AntennaIds = ParseAntennaIds(tokens[3]) };
    }

    private static ReaderSettings ParseSettingsOptions(ReaderSettings baseSettings, string[] tokens, int startIndex)
    {
        if (tokens.Length == startIndex)
        {
            throw new CliUsageException("inventory settings set requires at least one option.");
        }

        IReadOnlyList<ushort> antennas = baseSettings.AntennaIds;
        byte? sessionVal = null;
        ushort? populationVal = null;
        ushort? modeVal = null;
        ushort? tariVal = null;

        bool attachEnable = baseSettings.AttachedData.Enabled;
        ushort attachBank = baseSettings.AttachedData.MemoryBank;
        ushort attachPtr = baseSettings.AttachedData.WordPointer;
        ushort attachLen = baseSettings.AttachedData.WordCount;
        string attachPwd = baseSettings.AttachedData.AccessPassword;

        for (int index = startIndex; index < tokens.Length; index += 2)
        {
            if (index + 1 >= tokens.Length)
            {
                throw new CliUsageException($"Missing value for option '{tokens[index]}'.");
            }

            string value = tokens[index + 1];
            switch (tokens[index].ToLowerInvariant())
            {
                case "--antennas":
                    antennas = ParseAntennaIds(value);
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
                    attachEnable = !value.Equals("none", StringComparison.OrdinalIgnoreCase);
                    if (attachEnable)
                    {
                        attachBank = ParseMemoryBank(value);
                    }
                    break;

                case "--attach-ptr" when ushort.TryParse(value, out ushort ptr):
                    attachEnable = true;
                    attachPtr = ptr;
                    break;

                case "--attach-len" when ushort.TryParse(value, out ushort len):
                    attachEnable = true;
                    attachLen = len;
                    break;

                case "--attach-pwd" when value.Length == 8 && uint.TryParse(value, System.Globalization.NumberStyles.AllowHexSpecifier, System.Globalization.CultureInfo.InvariantCulture, out _):
                    attachEnable = true;
                    attachPwd = value.ToUpperInvariant();
                    break;

                default:
                    throw new CliUsageException($"Invalid inventory option '{tokens[index]}'.");
            }
        }

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
            AntennaIds = antennas,
            Session = sessionVal ?? baseSettings.Session,
            TagPopulationEstimate = populationVal ?? baseSettings.TagPopulationEstimate,
            ModeIndex = modeVal ?? baseSettings.ModeIndex,
            Tari = tariVal ?? baseSettings.Tari,
            AttachedData = finalAttached
        };
    }

    private static IReadOnlyList<ushort> ParseAntennaIds(string value)
    {
        if (value.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            return [0];
        }

        string[] parts = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || !parts.All(static item => ushort.TryParse(item, out _)))
        {
            throw new CliUsageException("--antennas must be all or a comma-separated list of UInt16 antenna IDs.");
        }

        ushort[] parsed = parts.Select(static item => ushort.Parse(item)).Distinct().ToArray();
        if (parsed.Contains((ushort)0))
        {
            throw new CliUsageException("Antenna ID 0 selects all antennas; use --antennas all instead of combining it with explicit IDs.");
        }

        return parsed;
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

            if (AreEquivalentInventorySettings(settings, session.DesiredInventorySettings))
            {
                console.MarkupLine("  [dim]Draft:[/] matches the currently running inventory settings.");
            }
            else
            {
                console.MarkupLine("  [yellow]Draft differs from the running settings; it will apply on the next inventory start.[/]");
            }
        }
    }

    private static bool AreEquivalentInventorySettings(ReaderSettings left, ReaderSettings right)
    {
        if (left.RoSpecId != right.RoSpecId ||
            left.Priority != right.Priority ||
            left.InventoryParameterSpecId != right.InventoryParameterSpecId ||
            left.ReportEveryNTags != right.ReportEveryNTags ||
            left.Session != right.Session ||
            left.TagPopulationEstimate != right.TagPopulationEstimate ||
            left.ModeIndex != right.ModeIndex ||
            left.Tari != right.Tari ||
            left.AttachedData != right.AttachedData ||
            left.StartTrigger != right.StartTrigger ||
            left.StopTrigger != right.StopTrigger ||
            !left.AntennaIds.SequenceEqual(right.AntennaIds) ||
            left.Extensions.Count != right.Extensions.Count)
        {
            return false;
        }

        return left.Extensions.All(pair =>
            right.Extensions.TryGetValue(pair.Key, out object? value) && Equals(pair.Value, value));
    }

    private void RenderSettings(string header, ReaderSettings settings)
    {
        string antennas = settings.AntennaIds.Count == 1 && settings.AntennaIds[0] == 0
            ? "All"
            : string.Join(',', settings.AntennaIds);
        string attached = settings.AttachedData.Enabled
            ? $"Bank={settings.AttachedData.MemoryBank}, Ptr={settings.AttachedData.WordPointer}, Len={settings.AttachedData.WordCount}"
            : "Disabled";

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("[bold grey70]Setting[/]");
        table.AddColumn("[bold grey70]Value[/]");
        table.AddRow("Antennas", Markup.Escape(antennas));
        table.AddRow("Session", settings.Session.ToString());
        table.AddRow("Population", settings.TagPopulationEstimate.ToString());
        table.AddRow("Mode / Tari", $"{settings.ModeIndex} / {settings.Tari}");
        table.AddRow("Attached data", Markup.Escape(attached));
        table.AddRow("Vendor extensions", settings.Extensions.Count.ToString());
        console.Write(new Panel(table).Header($"[bold yellow]{header}[/]").Border(BoxBorder.Rounded));
    }

    private void RenderUsage()
    {
        console.MarkupLine("[red]Usage:[/] inventory settings show|set|load|save|reset | start [--antennas <id,id|all>] | stop | status");
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
