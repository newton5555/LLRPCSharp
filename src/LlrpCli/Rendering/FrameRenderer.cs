using System.Reflection;
using System.Text;
using Spectre.Console;
using LlrpNet.Core.Diagnostics;
using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Messages;
using LlrpNet.Protocol.Parameters;
using LlrpCli.Terminal;
using LlrpCli.Commands;

namespace LlrpCli.Rendering;

public static class FrameRenderer
{
    public static void RenderObservedFrame(CapturedFrame frame, IAnsiConsole console, bool includeHexDump = true)
    {
        RenderFrameData(frame.Direction, frame.Timestamp, frame.Bytes, console, includeHexDump);
    }

    public static void RenderFrameData(LlrpFrameDirection direction, DateTimeOffset timestamp, byte[] rawFrame, IAnsiConsole console, bool includeHexDump = true)
    {
        LlrpMessageHeader header = LlrpMessageHeader.Decode(rawFrame);
        ILlrpMessage? message = null;
        try
        {
            message = Helpers.CreateRegistry().DecodeMessage(rawFrame);
        }
        catch { }

        string dirBadge = direction == LlrpFrameDirection.Transmit
            ? "[deepskyblue1 bold]→ TX[/]"
            : "[springgreen2 bold]← RX[/]";

        string name = message?.GetType().Name ?? $"MessageType({(ushort)header.MessageType})";
        string timeStr = timestamp.ToString("HH:mm:ss.fff");

        console.MarkupLine($"{dirBadge}  [bold]{Markup.Escape(name)}[/]  [grey]ID {header.MessageId} · {rawFrame.Length} bytes · {timeStr}[/]");

        if (message != null)
        {
            var tree = new Tree($"[bold green]{Markup.Escape(name)}[/] [grey](ID: {header.MessageId})[/]")
                .Style(new Style(Color.Grey70))
                .Guide(TreeGuide.Line);

            BuildObjectTree(tree, message, 0);
            console.Write(tree);
            console.WriteLine();
        }

        if (includeHexDump)
        {
            RenderHexDumpPanel(rawFrame, console);
        }
    }

    public static void RenderHeader(LlrpMessageHeader header, int totalFrameBytes, IAnsiConsole console)
    {
        console.WriteLine($"Version: {(byte)header.Version} ({header.Version})");
        console.WriteLine($"MessageType: {(ushort)header.MessageType}");
        console.WriteLine($"MessageId: {header.MessageId}");
        console.WriteLine($"MessageLength: {header.MessageLength}");
        console.WriteLine($"PayloadLength: {totalFrameBytes - LlrpMessageHeader.EncodedLength}");
    }

    public static void RenderDecodedMessage(ILlrpMessage message, byte[] rawFrame, IAnsiConsole console)
    {
        LlrpMessageHeader header = LlrpMessageHeader.Decode(rawFrame);

        console.MarkupLine($"[deepskyblue1 bold]→ DECODED[/]  [bold]{Markup.Escape(message.GetType().Name)}[/]  [grey]ID {header.MessageId} · {rawFrame.Length} bytes[/]");
        console.WriteLine();

        var tree = new Tree($"[bold green]{Markup.Escape(message.GetType().Name)}[/] [grey](ID: {header.MessageId})[/]")
            .Style(new Style(Color.Grey70))
            .Guide(TreeGuide.Line);

        BuildObjectTree(tree, message, 0);
        console.Write(tree);
        console.WriteLine();

        RenderHexDumpPanel(rawFrame, console);
    }

    public static void RenderValidationResult(bool isValid, string messageTypeName, int totalBytes, IAnsiConsole console)
    {
        if (isValid)
        {
            console.MarkupLine($"[bold springgreen2]✔ VALID LLRP FRAME[/]  Type: [bold]{Markup.Escape(messageTypeName)}[/] ({totalBytes} bytes)");
        }
        else
        {
            console.MarkupLine($"[bold red]✖ INVALID LLRP FRAME[/]");
        }
    }

    public static void RenderEncodedHex(string messageName, uint messageId, byte[] rawFrame, IAnsiConsole console)
    {
        console.WriteLine(Convert.ToHexString(rawFrame));
    }

    public static void RenderHexDumpPanel(byte[] bytes, IAnsiConsole console)
    {
        string hexDumpText = FormatHexDump(bytes);
        var hexPanel = new Panel(new Text(hexDumpText, new Style(Color.Grey85)))
            .Header("[grey] RAW HEX DUMP [/]")
            .Border(BoxBorder.Rounded)
            .BorderStyle(new Style(Color.Cyan1))
            .Padding(1, 0);

        console.Write(hexPanel);
    }

    public static string FormatHexDump(byte[] bytes)
    {
        var builder = new StringBuilder();
        for (var offset = 0; offset < bytes.Length; offset += 16)
        {
            var count = Math.Min(16, bytes.Length - offset);
            builder.Append($"{offset:X4}  ");
            for (var index = 0; index < 16; index++)
            {
                if (index < count)
                {
                    builder.Append($"{bytes[offset + index]:X2} ");
                }
                else
                {
                    builder.Append("   ");
                }
                if (index == 7)
                {
                    builder.Append(' ');
                }
            }
            builder.Append(" | ");
            for (var index = 0; index < count; index++)
            {
                var value = bytes[offset + index];
                builder.Append(value is >= 32 and <= 126 ? (char)value : '.');
            }
            if (offset + 16 < bytes.Length)
            {
                builder.AppendLine();
            }
        }
        return builder.ToString();
    }

    private static void BuildObjectTree(IHasTreeNodes parentNode, object target, int depth)
    {
        if (target is null || depth > 8)
        {
            return;
        }

        PropertyInfo[] properties = target.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
        foreach (PropertyInfo prop in properties)
        {
            if (prop.Name is "MessageId" or "MessageType" or "Version")
            {
                continue;
            }

            object? value = prop.GetValue(target);
            if (value is null)
            {
                continue;
            }

            string propName = prop.Name;

            if (value is System.Collections.IEnumerable enumerable and not string and not byte[])
            {
                var listNode = parentNode.AddNode($"[deepskyblue1]{Markup.Escape(propName)}[/] [grey](collection)[/]");
                int index = 0;
                foreach (object item in enumerable)
                {
                    if (item is null)
                    {
                        continue;
                    }

                    if (item.GetType().IsPrimitive || item is Enum || item is string)
                    {
                        listNode.AddNode($"[grey]{Markup.Escape($"[{index++}]:")}[/] [white]{Markup.Escape(item.ToString() ?? string.Empty)}[/]");
                    }
                    else
                    {
                        var itemNode = listNode.AddNode($"[green]{Markup.Escape(item.GetType().Name)}[/]");
                        BuildObjectTree(itemNode, item, depth + 1);
                    }
                }
            }
            else if (value.GetType().Assembly == typeof(LlrpMessageHeader).Assembly
                || value.GetType().Assembly == typeof(LlrpTlvParameterHeader).Assembly)
            {
                var childNode = parentNode.AddNode($"[deepskyblue1]{Markup.Escape(propName)}[/]: [green]{Markup.Escape(value.GetType().Name)}[/]");
                BuildObjectTree(childNode, value, depth + 1);
            }
            else
            {
                string displayValue = value switch
                {
                    byte[] byteArray => Convert.ToHexString(byteArray),
                    _ => value.ToString() ?? string.Empty
                };

                parentNode.AddNode($"[grey70]{Markup.Escape(propName)}:[/] [white]{Markup.Escape(displayValue)}[/]");
            }
        }
    }
}
