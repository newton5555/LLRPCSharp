using LlrpSdk;
using Spectre.Console;

namespace LlrpCli.Commands;

/// <summary>Implements the stable Live Shell settings command contract.</summary>
internal sealed class LiveSettingsHandler(IAnsiConsole console, LiveSessionContext session)
{
    public const string Usage = "settings show [reader|draft|defaults] [--json] | edit [--from defaults|reader|generic] | validate [file] | apply [file] --yes | load <file> | save <file> [--source draft|reader|defaults] | discard";

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
            case "validate":
                await ValidateAsync(reader, tokens, cancellationToken).ConfigureAwait(false);
                return;
            case "apply":
                await ApplyAsync(reader, tokens, cancellationToken).ConfigureAwait(false);
                return;
            case "load" when tokens.Length == 3:
                SetDraft(ManagedSettingsWorkflow.Load(reader, tokens[2]), SettingsDraftInfo.FromFile(tokens[2]));
                console.MarkupLine("[bold springgreen2]✔ Settings draft loaded.[/]");
                return;
            case "save":
                await SaveAsync(reader, tokens, cancellationToken).ConfigureAwait(false);
                return;
            case "discard" when tokens.Length == 2:
                session.SettingsDraft = null;
                session.DraftInfo = null;
                console.MarkupLine("[bold springgreen2]✔ Local settings draft discarded.[/]");
                return;
            default:
                throw new CliUsageException(Usage);
        }
    }

    private async Task ShowAsync(LlrpReader reader, string[] tokens, CancellationToken cancellationToken)
    {
        bool json = tokens.Any(static token => token.Equals("--json", StringComparison.OrdinalIgnoreCase));
        string[] positional = tokens.Skip(2).Where(static token => !token.Equals("--json", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (positional.Length > 1)
        {
            throw new CliUsageException(Usage);
        }
        string source = positional.FirstOrDefault() ?? "reader";
        ReaderSettings settings;
        SettingsDraftInfo? info = null;
        InventoryRuntimeState? state = null;
        string title;
        switch (source.ToLowerInvariant())
        {
            case "reader":
                ReaderSettingsSnapshot snapshot = await reader.QuerySettingsAsync(cancellationToken).ConfigureAwait(false);
                settings = snapshot.Settings;
                state = snapshot.Inventory?.State;
                title = "Reader settings";
                break;
            case "draft":
                settings = RequireDraft();
                info = session.DraftInfo;
                title = "Settings draft";
                break;
            case "defaults":
                ReaderSettingsDefaults defaults = await reader.GetDefaultSettingsAsync(cancellationToken).ConfigureAwait(false);
                settings = defaults.Settings;
                info = SettingsDraftInfo.FromDefaults(defaults);
                title = "SDK recommended settings";
                break;
            default:
                throw new CliUsageException("settings show source must be reader, draft, or defaults.");
        }

        if (json)
        {
            SettingsRenderer.RenderJson(console, reader, settings);
        }
        else
        {
            SettingsRenderer.RenderSummary(console, title, settings, state, info);
        }
    }

    private async Task EditAsync(LlrpReader reader, string[] tokens, CancellationToken cancellationToken)
    {
        string? source = null;
        if (tokens.Length != 2)
        {
            if (tokens.Length != 4 || !tokens[2].Equals("--from", StringComparison.OrdinalIgnoreCase))
            {
                throw new CliUsageException(Usage);
            }
            source = tokens[3];
        }

        if (source is not null)
        {
            if (session.DraftInfo?.IsLocallyModified == true && !console.Confirm("Replace the locally modified draft?"))
            {
                return;
            }
            (ReaderSettings Settings, SettingsDraftInfo Info) resolved =
                await ManagedSettingsWorkflow.ResolveSourceAsync(reader, source, cancellationToken).ConfigureAwait(false);
            SetDraft(resolved.Settings, resolved.Info);
        }
        else if (session.SettingsDraft is null)
        {
            (ReaderSettings Settings, SettingsDraftInfo Info) resolved =
                await ManagedSettingsWorkflow.ResolveSourceAsync(reader, "defaults", cancellationToken).ConfigureAwait(false);
            SetDraft(resolved.Settings, resolved.Info);
        }

        ReaderSettings original = RequireDraft();
        ReaderSettings edited = new SettingsEditor(console, reader).Edit(original);
        if (!ReferenceEquals(original, edited))
        {
            session.SettingsDraft = edited;
            session.DraftInfo = (session.DraftInfo ?? SettingsDraftInfo.Generic).MarkLocallyModified();
            console.MarkupLine("[bold springgreen2]✔ Local settings draft updated.[/]");
        }
    }

    private async Task ValidateAsync(LlrpReader reader, string[] tokens, CancellationToken cancellationToken)
    {
        if (tokens.Length > 3)
        {
            throw new CliUsageException(Usage);
        }
        ReaderSettings settings = tokens.Length == 3 ? ManagedSettingsWorkflow.Load(reader, tokens[2]) : RequireDraft();
        SettingsValidationResult result = await ManagedSettingsWorkflow.ValidateAsync(reader, settings, cancellationToken).ConfigureAwait(false);
        SettingsRenderer.RenderValidation(console, result);
    }

    private async Task ApplyAsync(LlrpReader reader, string[] tokens, CancellationToken cancellationToken)
    {
        bool confirmed = tokens.Any(static token => token.Equals("--yes", StringComparison.OrdinalIgnoreCase));
        string[] positional = tokens.Skip(2).Where(static token => !token.Equals("--yes", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (!confirmed || positional.Length > 1)
        {
            throw new CliUsageException("Usage: settings apply [file] --yes");
        }

        string? path = positional.FirstOrDefault();
        ReaderSettings settings = path is null ? RequireDraft() : ManagedSettingsWorkflow.Load(reader, path);
        SettingsValidationResult validation = await ManagedSettingsWorkflow.ValidateAsync(reader, settings, cancellationToken).ConfigureAwait(false);
        SettingsRenderer.RenderValidation(console, validation);
        if (!validation.IsValid)
        {
            return;
        }

        SettingsRenderer.RenderApplyImpact(console, settings);
        ReaderSettingsSnapshot deployed = await ManagedSettingsWorkflow.ApplyAsync(reader, settings, cancellationToken).ConfigureAwait(false);
        SetDraft(settings, path is null ? session.DraftInfo ?? SettingsDraftInfo.Generic : SettingsDraftInfo.FromFile(path));
        SettingsRenderer.RenderSummary(console, "Deployed reader settings", deployed.Settings, deployed.Inventory?.State, session.DraftInfo);
        console.MarkupLine("[bold springgreen2]✔ Settings applied. Inventory remains Disabled until 'inventory start'.[/]");
    }

    private async Task SaveAsync(LlrpReader reader, string[] tokens, CancellationToken cancellationToken)
    {
        if (tokens.Length is not (3 or 5) || (tokens.Length == 5 && !tokens[3].Equals("--source", StringComparison.OrdinalIgnoreCase)))
        {
            throw new CliUsageException(Usage);
        }
        string source = tokens.Length == 5 ? tokens[4] : "draft";
        ReaderSettings settings = source.ToLowerInvariant() switch
        {
            "draft" => RequireDraft(),
            "reader" => (await reader.QuerySettingsAsync(cancellationToken).ConfigureAwait(false)).Settings,
            "defaults" => (await reader.GetDefaultSettingsAsync(cancellationToken).ConfigureAwait(false)).Settings,
            _ => throw new CliUsageException("settings save source must be draft, reader, or defaults."),
        };
        ManagedSettingsWorkflow.Save(reader, tokens[2], settings);
        console.MarkupLine($"[bold springgreen2]✔ {Markup.Escape(source)} settings saved.[/]");
    }

    private LlrpReader RequireReader()
    {
        if (session.Reader?.IsConnected == true)
        {
            return session.Reader;
        }
        throw new CliUsageException("Not connected. Run 'connect <host>' first.");
    }

    private ReaderSettings RequireDraft() => session.SettingsDraft ?? throw new CliUsageException(
        "No local settings draft. Run 'settings edit' or 'settings load <file>' first.");

    private void SetDraft(ReaderSettings settings, SettingsDraftInfo? info)
    {
        session.SettingsDraft = settings;
        session.DraftInfo = info;
    }
}
