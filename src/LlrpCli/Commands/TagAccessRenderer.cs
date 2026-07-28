using Spectre.Console;
using LlrpSdk;

namespace LlrpCli.Commands;

internal static class TagAccessRenderer
{
    public static void RenderReadResult(IAnsiConsole console, TagAccessResult result)
    {
        string epc = Convert.ToHexString(result.Tag.ElectronicProductCode.Span);
        string status = result.Operation.Success ? "[bold springgreen2]SUCCESS[/]" : $"[bold red]FAILED ({Markup.Escape(result.Operation.Error ?? "unknown")})[/]";
        string data = result.Operation.ReadData.Count > 0
            ? string.Join(' ', result.Operation.ReadData.Select(word => word.ToString("X4")))
            : "(none)";
        console.MarkupLine($"[bold cyan1]TAG READ[/] EPC=[bold]{epc}[/] Status={status} Data=[yellow]{data}[/]");
    }

    public static void RenderOperationResult(IAnsiConsole console, string operationName, TagAccessResult result)
    {
        string epc = Convert.ToHexString(result.Tag.ElectronicProductCode.Span);
        string status = result.Operation.Success ? "[bold springgreen2]SUCCESS[/]" : $"[bold red]FAILED ({Markup.Escape(result.Operation.Error ?? "unknown")})[/]";
        console.MarkupLine($"[bold cyan1]TAG {operationName.ToUpperInvariant()}[/] EPC=[bold]{epc}[/] Status={status}");
    }

    public static void RenderWriteDryRun(IAnsiConsole console, WriteTagRequest request) =>
        console.MarkupLine($"[bold yellow]TAG WRITE DRY RUN — NO TAG MEMORY WAS WRITTEN[/] EPC={Convert.ToHexString(request.Selection.Data.Span)} Bank={request.MemoryBank} Word={request.WordPointer} Data={string.Join(' ', request.WriteData.Select(word => word.ToString("X4")))}");
}
