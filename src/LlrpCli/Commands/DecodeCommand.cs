using System.ComponentModel;
using System.Text.Json;
using LlrpCli.Rendering;
using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Messages;
using Spectre.Console;
using Spectre.Console.Cli;

namespace LlrpCli.Commands;

public sealed class DecodeSettings : CommandSettings
{
    [CommandArgument(0, "<HEX_OR_PCAP>")]
    [Description("LLRP frame as hex, or a Wireshark .pcapng file path to decode all captured LLRP frames.")]
    public string Hex { get; init; } = string.Empty;

    [CommandOption("--output <FORMAT>")]
    [Description("Output format: json, text, or summary.")]
    [DefaultValue("json")]
    public string Output { get; init; } = "json";

    [CommandOption("--message-type <UINT16>")]
    [Description("Only decode captured LLRP messages whose message type (command code) equals this number.")]
    public string? MessageTypeRaw { get; init; }
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
        byte[]? captureBytes = OfflineProtocolTool.TryReadPcapNg(settings.Hex);
        if (captureBytes is not null)
        {
            ushort? messageTypeFilter = settings.MessageTypeRaw is null
                ? null
                : Helpers.ParseUInt16(settings.MessageTypeRaw, "--message-type");
            OfflineProtocolTool.DecodePcap(settings.Hex, captureBytes, settings.Output, _console, _output, messageTypeFilter);
            return 0;
        }

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