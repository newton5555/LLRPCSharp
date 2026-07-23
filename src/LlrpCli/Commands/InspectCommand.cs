using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using LlrpNet.Core.Protocol;
using LlrpCli.Rendering;

namespace LlrpCli.Commands;

public sealed class InspectSettings : CommandSettings
{
    [CommandArgument(0, "<HEX>")]
    [Description("Hexadecimal string representing the LLRP frame.")]
    public string Hex { get; init; } = string.Empty;
}

public sealed class InspectCommand : Command<InspectSettings>
{
    private readonly IAnsiConsole _console;

    public InspectCommand() : this(AnsiConsole.Console) { }

    public InspectCommand(IAnsiConsole console)
    {
        _console = console ?? AnsiConsole.Console;
    }

    protected override int Execute(CommandContext context, InspectSettings settings, CancellationToken cancellationToken)
    {
        byte[] frame = Helpers.ParseHex(settings.Hex);
        LlrpMessageHeader header = Helpers.DecodeExactHeader(frame);
        FrameRenderer.RenderHeader(header, frame.Length, _console);
        return 0;
    }
}
