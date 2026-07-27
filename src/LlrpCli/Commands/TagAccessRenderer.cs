using Spectre.Console;
using LlrpSdk;

namespace LlrpCli.Commands;

internal static class TagAccessRenderer
{
    public static void RenderReadResult(IAnsiConsole console, TagAccessResult result) =>
        console.MarkupLine($"[bold springgreen2]TAG READ[/] EPC={Convert.ToHexString(result.Tag.ElectronicProductCode.Span)} Success={result.Operation.Success} Data={string.Join(' ', result.Operation.ReadData.Select(word => word.ToString("X4")))}");

    public static void RenderWriteDryRun(IAnsiConsole console, WriteTagRequest request) =>
        console.MarkupLine($"[bold yellow]TAG WRITE DRY RUN — NO TAG MEMORY WAS WRITTEN[/] EPC={Convert.ToHexString(request.Selection.Data.Span)} Bank={request.MemoryBank} Word={request.WordPointer} Data={string.Join(' ', request.WriteData.Select(word => word.ToString("X4")))}");
}
