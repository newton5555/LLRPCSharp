using LlrpCli.Rendering;
using LlrpSdk;
using Spectre.Console;

namespace LlrpCli.Commands;

/// <summary>
/// Owns SDK-managed inventory and its Live Shell tag-report stream.
/// </summary>
internal sealed class LiveInventoryHandler(
    IAnsiConsole console,
    LiveSessionContext session,
    LiveMonitorHandler monitor)
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
            case "start":
                {
                    ValidateStartOptions(tokens);
                    if (!EnsureConnected())
                    {
                        return;
                    }
                    LlrpReader reader = session.Reader!;
                    if (reader.ResourceMode == ReaderResourceMode.ManualResources &&
                        !await ManualModeGuard.TryAutoExitManualModeAsync(console, reader, cancellationToken).ConfigureAwait(false))
                    {
                        return;
                    }
                    // A fresh SDK connection intentionally does not query ROSpec resources during the connection
                    // handshake. Refresh the managed snapshot here so an already-deployed SDK ROSpec (14150) can
                    // be started directly from the CLI without requiring a prior manual `settings show` command.
                    if (reader.CurrentInventorySettings is null)
                    {
                        await reader.QuerySettingsAsync(cancellationToken).ConfigureAwait(false);
                    }
                    if (reader.OperationState == ReaderOperationState.Inventorying)
                    {
                        console.MarkupLine("[yellow]SDK-managed inventory is already running.[/]");
                        return;
                    }

                    bool oneShot = tokens.Any(static token => token.Equals("--defaults", StringComparison.OrdinalIgnoreCase))
                        || tokens.Any(static token => token.Equals("--settings", StringComparison.OrdinalIgnoreCase));
                    if (oneShot)
                    {
                        ReaderSettings requested = await OneShotInventorySourceAsync(reader, tokens, cancellationToken).ConfigureAwait(false);
                        SettingsValidationResult validation = await ManagedSettingsWorkflow.ValidateAsync(
                            reader, requested, cancellationToken).ConfigureAwait(false);
                        if (!validation.IsValid)
                        {
                            SettingsRenderer.RenderValidation(console, validation);
                            console.MarkupLine("[bold red]inventory start aborted due to validation errors.[/]");
                            return;
                        }
                        if (reader.ResourceMode == ReaderResourceMode.ManualResources &&
                            !await ManualModeGuard.TryAutoExitManualModeAsync(console, reader, cancellationToken).ConfigureAwait(false))
                        {
                            return;
                        }
                        SettingsRenderer.RenderApplyImpact(console, requested);
                        // Deploy remains Disabled (StartTrigger not wired here) exactly like settings apply;
                        // the explicit StartInventoryAsync below activates it.
                        await ManagedSettingsWorkflow.DeployAsync(reader, requested, cancellationToken).ConfigureAwait(false);
                    }
                    else if (reader.CurrentInventorySettings is null)
                    {
                        throw new CliUsageException("The reader has no deployed Inventory. Run 'settings apply <file> --yes' with Inventory or 'settings apply --defaults --yes', then 'inventory start'.");
                    }

                    LiveMonitorMode monitorMode = ParseStartMonitorMode(tokens);
                    int? monitorDurationSeconds = ParseStartMonitorDurationSeconds(tokens);
                    if (monitorDurationSeconds is not null && monitorMode == LiveMonitorMode.None)
                    {
                        throw new CliUsageException("inventory start --monitor-duration requires --monitor live or --monitor frames.");
                    }

                    session.InventorySession = await reader.StartInventoryAsync(cancellationToken);
                    RenderStartedSummary(session.InventorySession.Settings);
                    await monitor.MonitorAsync(monitorMode, monitorDurationSeconds, filterType: null, cancellationToken);
                    break;
                }

            case "stop":
                if (tokens.Length != 2)
                {
                    throw new CliUsageException("Usage: inventory stop");
                }
                if (!EnsureConnected())
                {
                    return;
                }
                await StopAsync(cancellationToken);
                console.MarkupLine("[bold springgreen2]✔ SDK-managed inventory stopped.[/]");
                break;

            case "status":
                bool refresh = tokens.Length == 3 && tokens[2].Equals("--refresh", StringComparison.OrdinalIgnoreCase);
                if (tokens.Length > 3 || (tokens.Length == 3 && !refresh))
                {
                    throw new CliUsageException("Usage: inventory status [--refresh]");
                }
                if (!EnsureConnected())
                {
                    return;
                }
                if (refresh)
                {
                    await session.Reader!.QuerySettingsAsync(cancellationToken).ConfigureAwait(false);
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

    private static async Task<ReaderSettings> OneShotInventorySourceAsync(
        LlrpReader reader,
        string[] tokens,
        CancellationToken cancellationToken)
    {
        for (int index = 2; index < tokens.Length; index += 2)
        {
            if (tokens[index].Equals("--settings", StringComparison.OrdinalIgnoreCase)
                && index + 1 < tokens.Length)
            {
                return ManagedSettingsWorkflow.Load(reader, tokens[index + 1]);
            }
        }

        ReaderSettingsDefaults defaults = await reader.GetDefaultSettingsAsync(cancellationToken).ConfigureAwait(false);
        return defaults.Settings;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (session.InventorySession is { } activeSession)
        {
            await activeSession.StopAsync(cancellationToken).ConfigureAwait(false);
            session.InventorySession = null;
        }
        else if (session.Reader?.IsConnected == true && session.Reader.OperationState == ReaderOperationState.Inventorying)
        {
            await session.Reader.StopAsync(cancellationToken).ConfigureAwait(false);
        }
    }


    private static LiveMonitorMode ParseStartMonitorMode(string[] tokens)
    {
        for (int index = 2; index < tokens.Length; index += 2)
        {
            if (tokens[index].Equals("--monitor", StringComparison.OrdinalIgnoreCase))
            {
                return ParseMonitorMode(tokens[index + 1]);
            }
        }
        return LiveMonitorMode.Live;
    }

    private static void ValidateStartOptions(string[] tokens)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool useDefaults = false;
        bool useSettings = false;
        for (int index = 2; index < tokens.Length; index += 2)
        {
            string option = tokens[index];
            bool knownOption = option.Equals("--monitor", StringComparison.OrdinalIgnoreCase)
                || option.Equals("--monitor-duration", StringComparison.OrdinalIgnoreCase)
                || option.Equals("--defaults", StringComparison.OrdinalIgnoreCase)
                || option.Equals("--settings", StringComparison.OrdinalIgnoreCase);
            if (!knownOption || index + 1 >= tokens.Length)
            {
                throw new CliUsageException("Usage: inventory start [--defaults|--settings <file>] [--monitor live|frames|none] [--monitor-duration seconds]");
            }
            if (!seen.Add(option))
            {
                throw new CliUsageException($"{option} may only be specified once.");
            }

            if (option.Equals("--defaults", StringComparison.OrdinalIgnoreCase))
            {
                useDefaults = true;
            }
            else if (option.Equals("--settings", StringComparison.OrdinalIgnoreCase))
            {
                useSettings = true;
            }
        }

        if (useDefaults && useSettings)
        {
            throw new CliUsageException("inventory start accepts either --defaults or --settings, not both.");
        }
    }

    private static int? ParseStartMonitorDurationSeconds(string[] tokens)
    {
        for (int index = 2; index < tokens.Length; index += 2)
        {
            if (tokens[index].Equals("--monitor-duration", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= tokens.Length || !int.TryParse(tokens[index + 1], out int seconds) || seconds <= 0)
                {
                    throw new CliUsageException("--monitor-duration must be a positive whole number of seconds.");
                }

                return seconds;
            }
        }

        return null;
    }

    private static LiveMonitorMode ParseMonitorMode(string value) => value.ToLowerInvariant() switch
    {
        "live" => LiveMonitorMode.Live,
        "frames" => LiveMonitorMode.Frames,
        "none" => LiveMonitorMode.None,
        _ => throw new CliUsageException("--monitor must be live, frames, or none.")
    };

    private void RenderStartedSummary(InventorySettings settings)
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

        if (session.Reader.CurrentInventorySettings is { } settings)
        {
            string scope = settings.AntennaIds.Count == 1 && settings.AntennaIds[0] == 0 ? "All" : string.Join(',', settings.AntennaIds);
            console.MarkupLine($"  [dim]Antenna:[/] {scope} | [dim]Session:[/] {settings.Session} | [dim]Pop:[/] {settings.TagPopulationEstimate} | [dim]Mode:[/] {settings.ModeIndex} | [dim]Tari:[/] {settings.Tari}");
            if (settings.AttachedData.Enabled)
            {
                console.MarkupLine($"  [dim]AttachedData:[/] Bank={settings.AttachedData.MemoryBank}, Ptr={settings.AttachedData.WordPointer}, Len={settings.AttachedData.WordCount}");
            }

            console.MarkupLine("  [dim]Source:[/] Reader-deployed high-level settings. Use 'settings show' to inspect it.");
        }
        else
        {
            console.MarkupLine("  [yellow]No deployed high-level Inventory. Run 'settings apply <file> --yes' with Inventory or 'settings apply --defaults --yes', then 'inventory start'.[/]");
        }
    }

    private void RenderUsage()
    {
        console.MarkupLine("[red]Usage:[/] inventory start [[--monitor live|frames|none]] [[--monitor-duration seconds]] | stop | status [[--refresh]]");
    }

}
