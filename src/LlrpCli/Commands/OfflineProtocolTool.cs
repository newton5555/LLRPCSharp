using LlrpCli.Rendering;
using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Messages;
using Spectre.Console;

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
}
