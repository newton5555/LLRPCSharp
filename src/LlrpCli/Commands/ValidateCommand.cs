using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using LlrpNet.Protocol.Messages;
using LlrpCli.Rendering;

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
        byte[] frame = Helpers.ParseHex(settings.Hex);
        Helpers.DecodeExactHeader(frame);
        ILlrpMessage message = Helpers.CreateRegistry().DecodeMessage(frame);

        FrameRenderer.RenderValidationResult(isValid: true, message.GetType().Name, frame.Length, _console);
        return 0;
    }
}
