using LlrpSdk;
using Spectre.Console;

namespace LlrpCli.Commands;

/// <summary>
/// Guards managed-mode calls that must not run while manual ROSpec/AccessSpec control is active.
/// Leaving the compatibility mode never deletes resources. Resource replacement is controlled by
/// the SDK's explicit <see cref="ResourceTakeoverPolicy.ReplaceAll"/> policy instead.
/// </summary>
internal static class ManualModeGuard
{
    /// <summary>Retained for source compatibility with CLI policy tests; exiting manual mode never prompts to delete.</summary>
    internal static bool ShouldPromptToDelete(int roSpecCount, int accessSpecCount) => false;

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

        console.MarkupLine("[yellow]Leaving manual resource mode; existing ROSpec/AccessSpec resources will be preserved.[/]");
        await reader.ExitManualResourceModeAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }
}
