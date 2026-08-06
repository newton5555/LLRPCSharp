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

        byte[] frame = Helpers.ParseHex(tokens[1]);
        LlrpMessageHeader header = Helpers.DecodeExactHeader(frame);
        FrameRenderer.RenderHeader(header, frame.Length, console);
    }

    public static void Decode(string[] tokens, IAnsiConsole console)
    {
        if (tokens.Length < 2)
        {
            console.MarkupLine("[red]Usage:[/] decode <hex-frame>");
            return;
        }

        byte[] frame = Helpers.ParseHex(tokens[1]);
        Helpers.DecodeExactHeader(frame);
        ILlrpMessage message = Helpers.CreateRegistry().DecodeMessage(frame);
        FrameRenderer.RenderDecodedMessage(message, frame, console);
    }

    public static void Validate(string[] tokens, IAnsiConsole console)
    {
        if (tokens.Length < 2)
        {
            console.MarkupLine("[red]Usage:[/] validate <hex-frame>");
            return;
        }

        byte[] frame = Helpers.ParseHex(tokens[1]);
        Helpers.DecodeExactHeader(frame);
        ILlrpMessage message = Helpers.CreateRegistry().DecodeMessage(frame);
        FrameRenderer.RenderValidationResult(isValid: true, message.GetType().Name, frame.Length, console);
    }

    public static void Encode(string[] tokens, IAnsiConsole console)
    {
        if (tokens.Length < 2)
        {
            console.MarkupLine("[red]Usage:[/] encode <message-name> [[--message-id ID]] [[--rospec-id ID]]");
            return;
        }

        string messageName = tokens[1];
        uint messageId = 1;
        uint? roSpecId = null;

        for (int index = 2; index < tokens.Length; index += 2)
        {
            if (index + 1 >= tokens.Length)
            {
                break;
            }

            if (tokens[index].Equals("--message-id", StringComparison.OrdinalIgnoreCase))
            {
                messageId = Helpers.ParseUInt32(tokens[index + 1], "--message-id");
            }
            else if (tokens[index].Equals("--rospec-id", StringComparison.OrdinalIgnoreCase))
            {
                roSpecId = Helpers.ParseUInt32(tokens[index + 1], "--rospec-id");
            }
        }

        ILlrpMessage message = messageName.ToLowerInvariant() switch
        {
            "keepalive" => new LlrpNet.Protocol.Messages.V1_0_1.KEEPALIVE(messageId),
            "keepalive-ack" => new LlrpNet.Protocol.Messages.V1_0_1.KEEPALIVE_ACK(messageId),
            "get-reader-capabilities" => new LlrpNet.Protocol.Messages.V1_0_1.GET_READER_CAPABILITIES(
                messageId,
                LlrpNet.Protocol.Enumerations.V1_0_1.GetReaderCapabilitiesRequestedData.All,
                Array.Empty<LlrpNet.Protocol.Parameters.ILlrpParameter>()),
            "get-rospecs" => new LlrpNet.Protocol.Messages.V1_0_1.GET_ROSPECS(messageId),
            "delete-rospec" => new LlrpNet.Protocol.Messages.V1_0_1.DELETE_ROSPEC(messageId, roSpecId ?? 1),
            "start-rospec" => new LlrpNet.Protocol.Messages.V1_0_1.START_ROSPEC(messageId, roSpecId ?? 1),
            "stop-rospec" => new LlrpNet.Protocol.Messages.V1_0_1.STOP_ROSPEC(messageId, roSpecId ?? 1),
            "enable-rospec" => new LlrpNet.Protocol.Messages.V1_0_1.ENABLE_ROSPEC(messageId, roSpecId ?? 1),
            "disable-rospec" => new LlrpNet.Protocol.Messages.V1_0_1.DISABLE_ROSPEC(messageId, roSpecId ?? 1),
            _ => throw new CliUsageException($"Encode message '{messageName}' is not supported."),
        };

        byte[] frame = Helpers.CreateRegistry().EncodeMessage(LlrpProtocolVersion.Version101, message);
        FrameRenderer.RenderEncodedHex(messageName, messageId, frame, console);
    }
}
