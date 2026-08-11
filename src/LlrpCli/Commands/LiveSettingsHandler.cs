using LlrpSdk;
using Spectre.Console;

namespace LlrpCli.Commands;

/// <summary>Implements the stable Live Shell settings command contract.</summary>
internal sealed class LiveSettingsHandler(IAnsiConsole console, LiveSessionContext session)
{
    public const string Usage = "settings show [--json] | defaults|default [--json|--yes] | edit [--from defaults|<file>] | validate <file> | apply <file> --yes | load <file> [--apply] | save <file>";

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
            case "defaults":
            case "default":
                await DefaultsAsync(reader, tokens, cancellationToken).ConfigureAwait(false);
                return;
            case "edit":
                await EditAsync(reader, tokens, cancellationToken).ConfigureAwait(false);
                return;
            case "load":
                await LoadAsync(reader, tokens, cancellationToken).ConfigureAwait(false);
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
        bool json = tokens.Any(static token => token.Equals("--json", StringComparison.OrdinalIgnoreCase));
        if (tokens.Length > 3 || (tokens.Length == 3 && !json))
        {
            throw new CliUsageException("Usage: settings show [--json]");
        }

        ReaderSettingsSnapshot snapshot = await reader.QuerySettingsAsync(cancellationToken).ConfigureAwait(false);
        if (json)
        {
            SettingsRenderer.RenderJson(console, reader, snapshot.Settings);
        }
        else
        {
            SettingsRenderer.RenderSummary(console, "Reader settings", snapshot.Settings, snapshot.ManagedRoSpec?.State);
        }
    }

    private async Task DefaultsAsync(LlrpReader reader, string[] tokens, CancellationToken cancellationToken)
    {
        bool json = tokens.Any(static token => token.Equals("--json", StringComparison.OrdinalIgnoreCase));
        bool apply = tokens.Any(static token => token.Equals("--yes", StringComparison.OrdinalIgnoreCase));
        if (tokens.Length > 3 || (tokens.Length == 3 && !json && !apply) || (json && apply))
        {
            throw new CliUsageException("Usage: settings defaults|default [--json|--yes]");
        }

        ReaderSettingsDefaults defaults = await reader.GetDefaultSettingsAsync(cancellationToken).ConfigureAwait(false);
        if (apply)
        {
            SettingsRenderer.RenderApplyImpact(console, defaults.Settings);
            ReaderSettingsSnapshot deployed = await ManagedSettingsWorkflow.ApplyAsync(reader, defaults.Settings, cancellationToken).ConfigureAwait(false);
            SettingsRenderer.RenderSummary(console, "Applied default reader settings", deployed.Settings, deployed.ManagedRoSpec?.State);
            console.MarkupLine("[bold springgreen2]✔ Default settings applied. Inventory remains Disabled until 'inventory start'.[/]");
        }
        else if (json)
        {
            SettingsRenderer.RenderJson(console, reader, defaults.Settings);
        }
        else
        {
            SettingsRenderer.RenderSummary(console, "SDK recommended settings", defaults.Settings);
        }
    }

    private async Task EditAsync(LlrpReader reader, string[] tokens, CancellationToken cancellationToken)
    {
        ReaderSettings sourceSettings;
        if (tokens.Length == 2)
        {
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

        EditorResult result = new SettingsEditor(console, reader).Edit(sourceSettings);

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

            SettingsRenderer.RenderApplyImpact(console, result.Settings);
            ReaderSettingsSnapshot deployed = await ManagedSettingsWorkflow.ApplyAsync(reader, result.Settings, cancellationToken).ConfigureAwait(false);
            SettingsRenderer.RenderSummary(console, "Deployed reader settings", deployed.Settings, deployed.ManagedRoSpec?.State);
            console.MarkupLine("[bold springgreen2]✔ Settings applied. Inventory remains Disabled until 'inventory start'.[/]");
        }
    }

    private async Task LoadAsync(LlrpReader reader, string[] tokens, CancellationToken cancellationToken)
    {
        bool apply = tokens.Any(static token => token.Equals("--apply", StringComparison.OrdinalIgnoreCase));
        string[] positional = tokens.Skip(2).Where(static token => !token.Equals("--apply", StringComparison.OrdinalIgnoreCase)).ToArray();
        
        if (positional.Length != 1)
        {
            throw new CliUsageException("Usage: settings load <file> [--apply]");
        }

        string path = positional[0];
        ReaderSettings settings = ManagedSettingsWorkflow.Load(reader, path);

        if (apply)
        {
            SettingsValidationResult validation = await ManagedSettingsWorkflow.ValidateAsync(reader, settings, cancellationToken).ConfigureAwait(false);
            SettingsRenderer.RenderValidation(console, validation);
            if (!validation.IsValid)
            {
                console.MarkupLine("[bold red]Apply aborted due to validation errors.[/]");
                return;
            }

            SettingsRenderer.RenderApplyImpact(console, settings);
            ReaderSettingsSnapshot deployed = await ManagedSettingsWorkflow.ApplyAsync(reader, settings, cancellationToken).ConfigureAwait(false);
            SettingsRenderer.RenderSummary(console, "Deployed reader settings", deployed.Settings, deployed.ManagedRoSpec?.State);
            console.MarkupLine("[bold springgreen2]✔ Settings loaded and applied. Inventory remains Disabled until 'inventory start'.[/]");
        }
        else
        {
            SettingsValidationResult validation = await ManagedSettingsWorkflow.ValidateAsync(reader, settings, cancellationToken).ConfigureAwait(false);
            SettingsRenderer.RenderValidation(console, validation);
            if (validation.IsValid)
            {
                console.MarkupLine("[bold springgreen2]✔ File loaded and validated successfully. Use --apply to push to the reader.[/]");
            }
        }
    }

    private async Task SaveAsync(LlrpReader reader, string[] tokens, CancellationToken cancellationToken)
    {
        if (tokens.Length != 3)
        {
            throw new CliUsageException("Usage: settings save <file>");
        }
        
        string path = tokens[2];
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
        string[] positional = tokens.Skip(2)
            .Where(static token => !token.Equals("--yes", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (!confirmed || positional.Length != 1)
        {
            throw new CliUsageException("Usage: settings apply <file> --yes");
        }

        ReaderSettings settings = ManagedSettingsWorkflow.Load(reader, positional[0]);
        SettingsValidationResult validation = await ManagedSettingsWorkflow.ValidateAsync(reader, settings, cancellationToken).ConfigureAwait(false);
        SettingsRenderer.RenderValidation(console, validation);
        if (!validation.IsValid)
        {
            console.MarkupLine("[bold red]Apply aborted due to validation errors.[/]");
            return;
        }

        SettingsRenderer.RenderApplyImpact(console, settings);
        ReaderSettingsSnapshot deployed = await ManagedSettingsWorkflow.ApplyAsync(reader, settings, cancellationToken).ConfigureAwait(false);
        SettingsRenderer.RenderSummary(console, "Applied reader settings", deployed.Settings, deployed.ManagedRoSpec?.State);
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
}
