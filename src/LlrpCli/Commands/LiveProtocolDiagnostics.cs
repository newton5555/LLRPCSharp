using LlrpCli.Rendering;
using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Messages;
using Spectre.Console;

namespace LlrpCli.Commands;

/// <summary>
/// Handles protocol-only Live Shell commands that do not require a reader session.
/// </summary>
internal static class LiveProtocolDiagnostics
{
    public static void Inspect(string[] tokens, IAnsiConsole console)
    {
        if (tokens.Length < 2)
        {
            console.MarkupLine("[red]Usage:[/] inspect <hex-frame>");
            return;
        }

        OfflineProtocolTool.InspectFrame(tokens[1], console);
    }

    public static void Decode(string[] tokens, IAnsiConsole console)
    {
        if (tokens.Length < 2)
        {
            console.MarkupLine("[red]Usage:[/] decode <hex-frame|pcapng-file> [--output text|summary|json]");
            return;
        }

        string target = tokens[1];
        string outputFormat = "text";
        for (int index = 2; index < tokens.Length; index++)
        {
            if (index + 1 < tokens.Length && tokens[index].Equals("--output", StringComparison.OrdinalIgnoreCase))
            {
                outputFormat = tokens[++index];
                continue;
            }

            console.MarkupLine("[red]Usage:[/] decode <hex-frame|pcapng-file> [--output text|summary|json]");
            return;
        }

        byte[]? capture = OfflineProtocolTool.TryReadPcapNg(target);
        if (capture is not null)
        {
            OfflineProtocolTool.DecodePcap(target, capture, outputFormat, console);
            return;
        }

        OfflineProtocolTool.DecodeFrame(target, console);
    }

    public static void Validate(string[] tokens, IAnsiConsole console)
    {
        if (tokens.Length < 2)
        {
            console.MarkupLine("[red]Usage:[/] validate <hex-frame>");
            return;
        }

        OfflineProtocolTool.ValidateFrame(tokens[1], console);
    }

    public static void Encode(string[] tokens, IAnsiConsole console)
    {
        if (tokens.Length < 2)
        {
            console.MarkupLine("[red]Usage:[/] encode <message-name> [[--llrp VERSION]] [[--message-id ID]] [[--rospec-id ID]] [[--requested-data DATA]]");
            return;
        }

        string messageName = tokens[1];
        LlrpProtocolVersion version = LlrpProtocolVersion.Version101;
        uint messageId = 1;
        uint? roSpecId = null;
        string requestedData = "All";

        for (int index = 2; index < tokens.Length; index += 2)
        {
            if (index + 1 >= tokens.Length)
            {
                break;
            }

            if (tokens[index].Equals("--llrp", StringComparison.OrdinalIgnoreCase))
            {
                if (!Helpers.TryParseLlrpVersion(tokens[index + 1], out version))
                {
                    throw new CliUsageException("LLRP version must be auto, 1.0.1, 1.1, or 2.0.");
                }
            }
            else if (tokens[index].Equals("--message-id", StringComparison.OrdinalIgnoreCase))
            {
                messageId = Helpers.ParseUInt32(tokens[index + 1], "--message-id");
            }
            else if (tokens[index].Equals("--rospec-id", StringComparison.OrdinalIgnoreCase))
            {
                roSpecId = Helpers.ParseUInt32(tokens[index + 1], "--rospec-id");
            }
            else if (tokens[index].Equals("--requested-data", StringComparison.OrdinalIgnoreCase))
            {
                requestedData = tokens[index + 1];
            }
        }

        string normalizedRequestedData = Helpers.ParseRequestedData(version, requestedData);
        ILlrpMessage message = Helpers.CreateEncodeMessage(messageName, version, messageId, roSpecId, normalizedRequestedData);
        byte[] frame = Helpers.CreateRegistry().EncodeMessage(version, message);
        FrameRenderer.RenderEncodedHex(messageName, messageId, frame, console);
    }
}
