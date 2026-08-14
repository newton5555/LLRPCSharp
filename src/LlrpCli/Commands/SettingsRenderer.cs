using LlrpNet.Protocol.Enumerations.V1_0_1;
using LlrpNet.Protocol.Messages.V1_0_1;
using LlrpNet.Protocol.Parameters.V1_0_1;
using LlrpSdk;
using LlrpCli.Rendering;
using Spectre.Console;

namespace LlrpCli.Commands;

internal static class SettingsRenderer
{
    public static void RenderSummary(
        IAnsiConsole console,
        string title,
        ReaderSettings settings,
        InventoryRuntimeState? inventoryState = null)
    {
        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("[bold grey70]Area[/]");
        table.AddColumn("[bold grey70]Value[/]");

        KeepaliveConfiguration keepalive = settings.Configuration.Keepalive;
        table.AddRow("Keepalive", keepalive.TriggerType == LlrpSdk.KeepaliveTriggerType.Periodic
            ? $"Periodic, {keepalive.IntervalMs} ms"
            : "Disabled");

        if (settings.Inventory is not { } inventory)
        {
            table.AddRow("Inventory", "Not configured");
        }
        else
        {
            string antennas = inventory.AntennaIds.Count == 1 && inventory.AntennaIds[0] == 0
                ? "all"
                : string.Join(',', inventory.AntennaIds);
            table.AddRow("Inventory state", inventoryState?.ToString() ?? "Draft");
            table.AddRow("Antennas", antennas);
            table.AddRow("Singulation", $"S{inventory.Session}, population {inventory.TagPopulationEstimate}");
            table.AddRow("RF", $"mode {inventory.ModeIndex}, Tari {inventory.Tari}");
            table.AddRow("Reports", $"{inventory.Report.Trigger}, N={inventory.ReportEveryNTags}");
            table.AddRow("Filters", inventory.Filters.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
            table.AddRow("Attached data", inventory.AttachedData.Enabled
                ? $"bank {inventory.AttachedData.MemoryBank}, word {inventory.AttachedData.WordPointer}, count {inventory.AttachedData.WordCount}"
                : "Disabled");
            if (inventory.Extensions.Count != 0)
            {
                table.AddRow("Vendor extensions", Markup.Escape(string.Join(", ", inventory.Extensions.Keys.Order())));
            }
        }

        console.Write(new Panel(table)
            .Header($"[bold deepskyblue1] {Markup.Escape(title)} [/]")
            .Border(BoxBorder.Rounded));
    }

    public static void RenderJson(IAnsiConsole console, LlrpReader reader, ReaderSettings settings) =>
        console.WriteLine(ReaderSettingsSerializer.SerializeToJson(
            settings,
            ManagedSettingsWorkflow.GetSerializationContributors(reader)));

    public static void RenderResources(IAnsiConsole console, ReaderSettingsSnapshot snapshot)
    {
        RenderResourceParameters(console, "ROSpec", snapshot.RoSpecs);
        RenderResourceParameters(console, "AccessSpec", snapshot.AccessSpecs);
    }

    private static void RenderResourceParameters(IAnsiConsole console, string title, IReadOnlyList<LlrpNet.Protocol.Parameters.ILlrpParameter> parameters)
    {
        console.MarkupLine($"[bold cyan1]{Markup.Escape(title)} response: {parameters.Count} item(s)[/]");
        foreach (object parameter in parameters)
        {
            FrameRenderer.RenderObjectTree(parameter, parameter.GetType().Name, console);
        }
    }

    public static void RenderValidation(IAnsiConsole console, SettingsValidationResult result)
    {
        if (result.Diagnostics.Count == 0)
        {
            console.MarkupLine("[bold springgreen2]✔ Settings are valid for the connected reader.[/]");
            return;
        }

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("[bold]Severity[/]");
        table.AddColumn("[bold]Code[/]");
        table.AddColumn("[bold]Path[/]");
        table.AddColumn("[bold]Message[/]");
        foreach (SettingsDiagnostic diagnostic in result.Diagnostics)
        {
            string severity = diagnostic.Severity == SettingsDiagnosticSeverity.Error
                ? "[red]ERROR[/]"
                : "[yellow]WARNING[/]";
            table.AddRow(
                severity,
                Markup.Escape(diagnostic.Code),
                Markup.Escape(diagnostic.Path),
                Markup.Escape(diagnostic.Message));
        }
        console.Write(table);
    }

    public static void RenderApplyResultJson(IAnsiConsole console, SettingsValidationResult validation, bool isConfirmed)
    {
        string status = isConfirmed ? (validation.IsValid ? "accepted" : "rejected") : "requested";
        console.MarkupLine($"[bold]apply.status=[/]{status} valid={validation.IsValid}");
    }

    public static void RenderApplyImpact(IAnsiConsole console, ReaderSettings settings)
    {
        if (settings.Inventory is null)
        {
            console.MarkupLine("[yellow]Apply will write Reader configuration only; managed inventory resources are unchanged.[/]");
            return;
        }

        console.MarkupLine("[yellow]Apply will delete existing ROSpec/AccessSpec resources, write Reader configuration, and deploy SDK ROSpec 14150 in Disabled state.[/]");
        if (settings.Inventory.AttachedData.Enabled)
        {
            console.MarkupLine("[yellow]AttachedData also deploys SDK AccessSpec 14151.[/]");
        }
    }
}
