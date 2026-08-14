using LlrpSdk;
using Spectre.Console;

namespace LlrpCli.Commands;

/// <summary>Implements the stable Live Shell settings command contract.</summary>
internal sealed class LiveSettingsHandler(IAnsiConsole console, LiveSessionContext session)
{
    public const string Usage = "settings show [--json|--raw] | validate <file> | apply [--defaults|<file>] --yes [--json] | edit [--from reader|defaults|<file>] | save <file>";

    public async Task HandleAsync(string[] tokens, CancellationToken cancellationToken)
    {
        LlrpReader reader = RequireReader();
        if (tokens.Length < 2)
        {
            throw new CliUsageException(Usage);
        }

        switch (tokens[1].ToLowerInvariant())
        {
            case "show":
                await ShowAsync(reader, tokens, cancellationToken).ConfigureAwait(false);
                return;
            case "edit":
                await EditAsync(reader, tokens, cancellationToken).ConfigureAwait(false);
                return;
            case "save":
                await SaveAsync(reader, tokens, cancellationToken).ConfigureAwait(false);
                return;
            case "validate":
                await ValidateAsync(reader, tokens, cancellationToken).ConfigureAwait(false);
                return;
            case "apply":
                await ApplyAsync(reader, tokens, cancellationToken).ConfigureAwait(false);
                return;
            default:
                throw new CliUsageException(Usage);
        }
    }

    private async Task ShowAsync(LlrpReader reader, string[] tokens, CancellationToken cancellationToken)
    {
        if (reader.ResourceMode == ReaderResourceMode.ManualResources)
        {
            throw new CliUsageException(
                "Manual resource mode is active. Exit it before 'settings show', or use 'settings apply <file> --yes' " +
                "with Inventory / 'settings apply --defaults --yes' to replace manual resources with SDK-managed state.");
        }

        if (!reader.IsManagedStateSynchronized)
        {
            throw new CliUsageException(
                "SDK-managed state is unknown after raw or manual resource access. Run 'sync' before 'settings show', " +
                "or use 'settings apply <file> --yes' with Inventory / 'settings apply --defaults --yes' to force a managed takeover.");
        }

        bool json = tokens.Any(static token => token.Equals("--json", StringComparison.OrdinalIgnoreCase));
        bool raw = tokens.Any(static token => token.Equals("--raw", StringComparison.OrdinalIgnoreCase));
        if (tokens.Length > 3 || (tokens.Length == 3 && !json && !raw) || (json && raw))
        {
            throw new CliUsageException("Usage: settings show [--json|--raw]");
        }

        ReaderSettingsSnapshot snapshot = await reader.QuerySettingsAsync(cancellationToken).ConfigureAwait(false);
        if (json)
        {
            SettingsRenderer.RenderJson(console, reader, snapshot.Settings);
        }
        else
        {
            SettingsRenderer.RenderSummary(console, "Reader settings", snapshot.Settings, snapshot.ManagedRoSpec?.State);
            if (raw)
            {
                SettingsRenderer.RenderResources(console, snapshot);
            }
        }
    }

    private async Task EditAsync(LlrpReader reader, string[] tokens, CancellationToken cancellationToken)
    {
        ReaderSettings sourceSettings;
        if (tokens.Length == 2)
        {
            EnsureSettingsQueryAvailable(reader, "settings edit");
            sourceSettings = (await reader.QuerySettingsAsync(cancellationToken).ConfigureAwait(false)).Settings;
        }
        else if (tokens.Length == 4 && tokens[2].Equals("--from", StringComparison.OrdinalIgnoreCase))
        {
            string source = tokens[3];
            if (source.Equals("defaults", StringComparison.OrdinalIgnoreCase))
            {
                sourceSettings = (await reader.GetDefaultSettingsAsync(cancellationToken).ConfigureAwait(false)).Settings;
            }
            else
            {
                sourceSettings = ManagedSettingsWorkflow.Load(reader, source);
            }
        }
        else
        {
            throw new CliUsageException("Usage: settings edit [--from defaults|<file>]");
        }

        EditorResult result = await new SettingsEditor(console, reader)
            .EditAsync(sourceSettings, cancellationToken)
            .ConfigureAwait(false);

        if (result.Action == EditorResultAction.Discard)
        {
            console.MarkupLine("[bold springgreen2]✔ Edit cancelled.[/]");
            return;
        }

        if (result.Action == EditorResultAction.SaveToFile)
        {
            string path = console.Prompt(new TextPrompt<string>("[grey]File path to save to:[/]"));
            ManagedSettingsWorkflow.Save(reader, path, result.Settings);
            console.MarkupLine($"[bold springgreen2]✔ Settings saved to {Markup.Escape(path)}.[/]");
            return;
        }

        if (result.Action == EditorResultAction.Apply)
        {
            SettingsValidationResult validation = await ManagedSettingsWorkflow.ValidateAsync(reader, result.Settings, cancellationToken).ConfigureAwait(false);
            SettingsRenderer.RenderValidation(console, validation);
            if (!validation.IsValid)
            {
                console.MarkupLine("[bold red]Apply aborted due to validation errors.[/]");
                return;
            }

            EnsureSettingsApplyCanProceed(reader, result.Settings);
            SettingsRenderer.RenderApplyImpact(console, result.Settings);
            if (!console.Confirm("Apply these settings to the connected reader?", defaultValue: false))
            {
                console.MarkupLine("[yellow]Apply cancelled.[/]");
                return;
            }
            ReaderSettingsSnapshot deployed = await ManagedSettingsWorkflow.ApplyAsync(reader, result.Settings, cancellationToken).ConfigureAwait(false);
            SettingsRenderer.RenderSummary(console, "Deployed reader settings", deployed.Settings, deployed.ManagedRoSpec?.State);
            console.MarkupLine("[bold springgreen2]✔ Settings applied. Inventory remains Disabled until 'inventory start'.[/]");
        }
    }

    private async Task SaveAsync(LlrpReader reader, string[] tokens, CancellationToken cancellationToken)
    {
        if (tokens.Length != 3)
        {
            throw new CliUsageException("Usage: settings save <file>");
        }
        
        string path = tokens[2];
        EnsureSettingsQueryAvailable(reader, "settings save");
        ReaderSettings settings = (await reader.QuerySettingsAsync(cancellationToken).ConfigureAwait(false)).Settings;
        ManagedSettingsWorkflow.Save(reader, path, settings);
        console.MarkupLine($"[bold springgreen2]✔ Reader settings saved to {Markup.Escape(path)}.[/]");
    }

    private async Task ValidateAsync(LlrpReader reader, string[] tokens, CancellationToken cancellationToken)
    {
        if (tokens.Length != 3)
        {
            throw new CliUsageException("Usage: settings validate <file>");
        }

        string path = tokens[2];
        ReaderSettings settings = ManagedSettingsWorkflow.Load(reader, path);
        SettingsValidationResult result = await ManagedSettingsWorkflow.ValidateAsync(reader, settings, cancellationToken).ConfigureAwait(false);
        SettingsRenderer.RenderValidation(console, result);
    }

    private async Task ApplyAsync(LlrpReader reader, string[] tokens, CancellationToken cancellationToken)
    {
        bool confirmed = tokens.Any(static token => token.Equals("--yes", StringComparison.OrdinalIgnoreCase));
        bool useDefaults = tokens.Any(static token => token.Equals("--defaults", StringComparison.OrdinalIgnoreCase));
        bool json = tokens.Any(static token => token.Equals("--json", StringComparison.OrdinalIgnoreCase));
        string[] positional = tokens.Skip(2)
            .Where(static token => !token.Equals("--yes", StringComparison.OrdinalIgnoreCase))
            .Where(static token => !token.Equals("--defaults", StringComparison.OrdinalIgnoreCase))
            .Where(static token => !token.Equals("--json", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        bool hasFile = positional.Length == 1;
        if (!confirmed || (useDefaults && hasFile) || (!useDefaults && !hasFile))
        {
            throw new CliUsageException("Usage: settings apply --defaults --yes  |  settings apply <file> --yes [--json]");
        }

        ReaderSettings settings = useDefaults
            ? (await reader.GetDefaultSettingsAsync(cancellationToken).ConfigureAwait(false)).Settings
            : ManagedSettingsWorkflow.Load(reader, positional[0]);

        SettingsValidationResult validation = await ManagedSettingsWorkflow.ValidateAsync(reader, settings, cancellationToken).ConfigureAwait(false);
        if (json)
        {
            SettingsRenderer.RenderApplyResultJson(console, validation, isConfirmed: true);
        }
        SettingsRenderer.RenderValidation(console, validation);
        if (!validation.IsValid)
        {
            console.MarkupLine("[bold red]Apply aborted due to validation errors.[/]");
            return;
        }

        EnsureSettingsApplyCanProceed(reader, settings);
        if (!await ManualModeGuard.TryAutoExitManualModeAsync(console, reader, cancellationToken).ConfigureAwait(false))
        {
            return;
        }
        SettingsRenderer.RenderApplyImpact(console, settings);
        ReaderSettingsSnapshot deployed = await ManagedSettingsWorkflow.DeployAsync(reader, settings, cancellationToken).ConfigureAwait(false);
        SettingsRenderer.RenderSummary(console, "Applied settings", deployed.Settings, deployed.ManagedRoSpec?.State);
        console.MarkupLine("[bold springgreen2]✔ Settings applied. Inventory remains Disabled until 'inventory start'.[/]");
    }

    private LlrpReader RequireReader()
    {
        if (session.Reader?.IsConnected == true)
        {
            return session.Reader;
        }
        throw new CliUsageException("Not connected. Run 'connect <host>' first.");
    }

    private static void EnsureSettingsQueryAvailable(LlrpReader reader, string command)
    {
        if (reader.ResourceMode == ReaderResourceMode.ManualResources)
        {
            throw new CliUsageException(
                $"Manual resource mode is active. Exit it before '{command}', or use 'settings apply <file> --yes' " +
                "with Inventory / 'settings apply --defaults --yes' to replace manual resources with SDK-managed state.");
        }

        if (!reader.IsManagedStateSynchronized)
        {
            throw new CliUsageException(
                $"SDK-managed state is unknown after raw or manual resource access. Run 'sync' before '{command}', " +
                "or use 'settings apply <file> --yes' with Inventory / 'settings apply --defaults --yes' to force a managed takeover.");
        }
    }

    private static void EnsureSettingsApplyCanProceed(LlrpReader reader, ReaderSettings settings)
    {
        if (!reader.IsManagedStateSynchronized && settings.Inventory is null)
        {
            throw new CliUsageException(
                "SDK-managed state is unknown. The settings file must include Inventory to force a takeover; " +
                "otherwise run 'sync' first.");
        }
    }
}
