using LlrpNet.Protocol.Parameters;
using LlrpSdk;
using Spectre.Console;

namespace LlrpCli.Commands;

/// <summary>
/// Guards managed-mode calls that must not run while manual ROSpec/AccessSpec control is active.
/// When manual mode holds resources, the caller asks for confirmation before deleting them and
/// returning to managed mode; when no manual resources exist it exits silently.
/// </summary>
internal static class ManualModeGuard
{
    /// <summary>Pure decision for whether manual resources require a deletion confirmation.</summary>
    internal static bool ShouldPromptToDelete(int roSpecCount, int accessSpecCount) => roSpecCount > 0 || accessSpecCount > 0;

    public static async Task<bool> TryAutoExitManualModeAsync(
        IAnsiConsole console,
        LlrpReader reader,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(reader);

        if (reader.ResourceMode != ReaderResourceMode.ManualResources)
        {
            return true;
        }

        IReadOnlyList<ILlrpParameter> roSpecs = await reader.RoSpecs.GetAllAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<ILlrpParameter> accessSpecs = await reader.AccessSpecs.GetAllAsync(cancellationToken).ConfigureAwait(false);

        if (roSpecs.Count == 0 && accessSpecs.Count == 0)
        {
            await reader.ExitManualResourceModeAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }

        console.MarkupLine($"[yellow]Manual mode holds {roSpecs.Count} ROSpec / {accessSpecs.Count} AccessSpec.[/]");
        if (!console.Confirm("Delete them and return to managed mode?", defaultValue: false))
        {
            console.MarkupLine("[yellow]Cancelled. Run 'manual off' to release manual resources, or confirm deletion.[/]");
            return false;
        }

        await reader.ExitManualResourceModeAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }
}
