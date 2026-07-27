using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace LlrpCli.Commands;

public sealed class TagWriteDryRunSettings : CommandSettings
{
    [CommandArgument(0, "<EPC>")] public string Epc { get; init; } = string.Empty;
    [CommandOption("--bank <BANK>")] [DefaultValue("user")] public string Bank { get; init; } = "user";
    [CommandOption("--word <ADDRESS>")] public ushort WordPointer { get; init; }
    [CommandOption("--data <HEX_WORDS>")] public string Data { get; init; } = string.Empty;
    [CommandOption("--antenna <ID>")] public ushort AntennaId { get; init; }
    [CommandOption("--password <HEX>")] public string? Password { get; init; }
}

public sealed class TagWriteDryRunCommand(IAnsiConsole console) : Command<TagWriteDryRunSettings>
{
    private readonly IAnsiConsole _console = console ?? AnsiConsole.Console;
    public TagWriteDryRunCommand() : this(AnsiConsole.Console) { }
    protected override int Execute(CommandContext context, TagWriteDryRunSettings settings, CancellationToken cancellationToken)
    {
        TagAccessCliRequest input = TagAccessCliRequest.Create(settings.Epc, settings.Bank, settings.WordPointer, settings.AntennaId, settings.Password, null);
        TagAccessRenderer.RenderWriteDryRun(_console, input.ToWriteRequest(TagAccessCliRequest.ParseWords(settings.Data)));
        return 0;
    }
}
