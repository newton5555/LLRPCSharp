using LlrpCli.Rendering;
using LlrpNet.Core.Diagnostics;
using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Messages;
using Spectre.Console;
using System.Text.Json;

namespace LlrpCli.Commands;

/// <summary>
/// Shared offline protocol-tool logic used by both the standalone CLI commands and the Live Shell routes.
/// Each entry point only adapts its input parsing; the byte-decoding and rendering below is single-sourced.
/// </summary>
internal static class OfflineProtocolTool
{
    public static LlrpMessageHeader InspectFrame(string hex, IAnsiConsole console)
    {
        byte[] frame = Helpers.ParseHex(hex);
        LlrpMessageHeader header = Helpers.DecodeExactHeader(frame);
        FrameRenderer.RenderHeader(header, frame.Length, console);
        return header;
    }

    public static ILlrpMessage DecodeFrame(string hex, IAnsiConsole console)
    {
        byte[] frame = Helpers.ParseHex(hex);
        Helpers.DecodeExactHeader(frame);
        ILlrpMessage message = Helpers.CreateRegistry().DecodeMessage(frame);
        FrameRenderer.RenderDecodedMessage(message, frame, console);
        return message;
    }

    public static void ValidateFrame(string hex, IAnsiConsole console)
    {
        byte[] frame = Helpers.ParseHex(hex);
        Helpers.DecodeExactHeader(frame);
        ILlrpMessage message = Helpers.CreateRegistry().DecodeMessage(frame);
        FrameRenderer.RenderValidationResult(isValid: true, message.GetType().Name, frame.Length, console);
    }

    /// <summary>
    /// Returns the capture bytes when <paramref name="value"/> is a path to an existing .pcapng file
    /// (magic 0A 0D 0D 0A); returns null for a plain hex frame.
    /// </summary>
    public static byte[]? TryReadPcapNg(string value)
    {
        if (!System.IO.File.Exists(value))
        {
            return null;
        }

        byte[] fileBytes = System.IO.File.ReadAllBytes(value);
        if (fileBytes.Length >= 4 && fileBytes[0] == 0x0A && fileBytes[1] == 0x0D && fileBytes[2] == 0x0D && fileBytes[3] == 0x0A)
        {
            return fileBytes;
        }

        return null;
    }

    /// <summary>
    /// Decode a .pcapng capture and render each reassembled LLRP message as a header summary line
    /// (plus the full parameter tree unless <paramref name="outputFormat"/> is "summary").
    /// When <paramref name="outputFormat"/> is "json" the structured capture is written to
    /// <paramref name="output"/> when non-null, else to the console.
    /// </summary>
    public static void DecodePcap(string filePath, byte[] capture, string outputFormat, IAnsiConsole console, TextWriter? output = null)
    {
        IReadOnlyList<LlrpCli.Pcap.PcapTcpSegment> segments = LlrpCli.Pcap.PcapNgReader.ReadTcpSegments(capture);
        IReadOnlyList<LlrpCli.Pcap.LlrpCapturedMessage> messages = LlrpCli.Pcap.LlrpStreamReassembler.Reassemble(segments);
        var registry = Helpers.CreateRegistry();
        string fileName = Path.GetFileName(filePath);

        if (string.Equals(outputFormat, "json", StringComparison.OrdinalIgnoreCase))
        {
            var decoded = new
            {
                capture = fileName,
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
            string json = JsonSerializer.Serialize(decoded, new JsonSerializerOptions { WriteIndented = true });
            if (output is not null)
            {
                output.WriteLine(json);
            }
            else
            {
                console.MarkupLine(Markup.Escape(json));
            }

            return;
        }

        console.MarkupLine($"[grey]Decoded [bold]{messages.Count}[/] LLRP message(s) from [bold]{Markup.Escape(fileName)}[/] ([bold]{segments.Count}[/] TCP segments)[/]");
        foreach (LlrpCli.Pcap.LlrpCapturedMessage message in messages)
        {
            string dirBadge = message.Direction == LlrpFrameDirection.Receive
                ? "[springgreen2 bold]RX[/]"
                : "[deepskyblue1 bold]TX[/]";
            try
            {
                LlrpMessageHeader header = LlrpMessageHeader.Decode(message.Frame);
                ILlrpMessage decodedMessage = registry.DecodeMessage(message.Frame);
                console.MarkupLine($"{dirBadge}  [bold]{Markup.Escape(decodedMessage.GetType().Name)}[/]  [grey]ID {header.MessageId} · {message.Frame.Length} bytes · {message.SrcIp}:{message.SrcPort} -> {message.DstIp}:{message.DstPort}[/]");
                if (!string.Equals(outputFormat, "summary", StringComparison.OrdinalIgnoreCase))
                {
                    FrameRenderer.RenderDecodedMessage(decodedMessage, message.Frame, console);
                }
            }
            catch (Exception exception)
            {
                console.MarkupLine($"{dirBadge}  [red]{Markup.Escape(exception.Message)}[/]  [grey]{message.Frame.Length} bytes[/]");
            }
        }
    }
}