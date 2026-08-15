using System.ComponentModel;
using System.Text.Json;
using LlrpCli.Rendering;
using LlrpNet.Core.Diagnostics;
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
        if (TryIsPcapNgPath(settings.Hex, out byte[]? captureBytes))
        {
            return ExecutePcap(captureBytes!, settings, cancellationToken);
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

    private int ExecutePcap(byte[] capture, DecodeSettings settings, CancellationToken cancellationToken)
    {
        IReadOnlyList<LlrpCli.Pcap.PcapTcpSegment> segments = LlrpCli.Pcap.PcapNgReader.ReadTcpSegments(capture);
        IReadOnlyList<LlrpCli.Pcap.LlrpCapturedMessage> messages = LlrpCli.Pcap.LlrpStreamReassembler.Reassemble(segments);
        var registry = Helpers.CreateRegistry();

        if (string.Equals(settings.Output, "json", StringComparison.OrdinalIgnoreCase))
        {
            var decoded = new
            {
                capture = Path.GetFileName(settings.Hex),
                segmentCount = segments.Count,
                messageCount = messages.Count,
                messages = messages.Select(static m => new
                {
                    direction = m.Direction.ToString(),
                    source = $"{m.SrcIp}:{m.SrcPort}",
                    destination = $"{m.DstIp}:{m.DstPort}",
                    hex = Convert.ToHexString(m.Frame),
                }).ToArray(),
            };
            _output.WriteLine(JsonSerializer.Serialize(decoded, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }

        _console.MarkupLine($"[grey]Decoded [bold]{messages.Count}[/] LLRP message(s) from [bold]{Markup.Escape(Path.GetFileName(settings.Hex))}[/] ([bold]{segments.Count}[/] TCP segments)[/]");
        foreach (LlrpCli.Pcap.LlrpCapturedMessage message in messages)
        {
            string dirBadge = message.Direction == LlrpFrameDirection.Receive
                ? "[springgreen2 bold]RX[/]"
                : "[deepskyblue1 bold]TX[/]";
            try
            {
                LlrpMessageHeader header = LlrpMessageHeader.Decode(message.Frame);
                ILlrpMessage decoded = registry.DecodeMessage(message.Frame);
                _console.MarkupLine($"{dirBadge}  [bold]{Markup.Escape(decoded.GetType().Name)}[/]  [grey]ID {header.MessageId} · {message.Frame.Length} bytes · {message.SrcIp}:{message.SrcPort} -> {message.DstIp}:{message.DstPort}[/]");
                if (!string.Equals(settings.Output, "summary", StringComparison.OrdinalIgnoreCase))
                {
                    FrameRenderer.RenderDecodedMessage(decoded, message.Frame, _console);
                }
            }
            catch (Exception exception)
            {
                _console.MarkupLine($"{dirBadge}  [red]{Markup.Escape(exception.Message)}[/]  [grey]{message.Frame.Length} bytes[/]");
            }
        }
        return 0;
    }

    private static bool TryIsPcapNgPath(string value, out byte[]? bytes)
    {
        bytes = null;
        if (!File.Exists(value))
        {
            return false;
        }

        try
        {
            byte[] fileBytes = File.ReadAllBytes(value);
            if (fileBytes.Length >= 4 && fileBytes[0] == 0x0A && fileBytes[1] == 0x0D && fileBytes[2] == 0x0D && fileBytes[3] == 0x0A)
            {
                bytes = fileBytes;
                return true;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }
}
