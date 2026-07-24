using System.ComponentModel;
using System.Text.Json;
using Spectre.Console;
using Spectre.Console.Cli;
using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Messages;
using LlrpCli.Rendering;

namespace LlrpCli.Commands;

public sealed class DecodeSettings : CommandSettings
{
    [CommandArgument(0, "<HEX>")]
    [Description("Hexadecimal string representing the LLRP frame.")]
    public string Hex { get; init; } = string.Empty;

    [CommandOption("--output <FORMAT>")]
    [Description("Output format: json or text.")]
    [DefaultValue("json")]
    public string Output { get; init; } = "json";
}

public sealed class DecodeCommand : Command<DecodeSettings>
{
    private readonly IAnsiConsole _console;
    private readonly TextWriter _output;

    public DecodeCommand() : this(AnsiConsole.Console, Console.Out) { }

    public DecodeCommand(IAnsiConsole console, TextWriter output)
    {
        _console = console ?? AnsiConsole.Console;
        _output = output ?? TextWriter.Null;
    }

    protected override int Execute(CommandContext context, DecodeSettings settings, CancellationToken cancellationToken)
    {
        byte[] frame = Helpers.ParseHex(settings.Hex);
        LlrpMessageHeader header = Helpers.DecodeExactHeader(frame);
        ILlrpMessage message = Helpers.CreateRegistry().DecodeMessage(frame);

        if (string.Equals(settings.Output, "json", StringComparison.OrdinalIgnoreCase))
        {
            var decoded = new
            {
                protocolVersion = (byte)header.Version,
                messageType = header.MessageType,
                messageId = header.MessageId,
                messageLength = header.MessageLength,
                model = message.GetType().FullName,
                rawHex = Convert.ToHexString(frame),
            };

            _output.WriteLine(JsonSerializer.Serialize(decoded));
        }
        else
        {
            FrameRenderer.RenderDecodedMessage(message, frame, _console);
        }

        return 0;
    }
}
