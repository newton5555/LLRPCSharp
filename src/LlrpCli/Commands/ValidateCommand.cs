using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace LlrpCli.Commands;

public sealed class ValidateSettings : CommandSettings
{
    [CommandArgument(0, "<HEX>")]
    [Description("Hexadecimal string representing the LLRP frame.")]
    public string Hex { get; init; } = string.Empty;
}

public sealed class ValidateCommand : Command<ValidateSettings>
{
    private readonly IAnsiConsole _console;

    public ValidateCommand() : this(AnsiConsole.Console) { }

    public ValidateCommand(IAnsiConsole console)
    {
        _console = console ?? AnsiConsole.Console;
    }

    protected override int Execute(CommandContext context, ValidateSettings settings, CancellationToken cancellationToken)
    {
        OfflineProtocolTool.ValidateFrame(settings.Hex, _console);
        return 0;
    }
}
