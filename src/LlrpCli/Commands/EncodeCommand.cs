using System.ComponentModel;
using LlrpCli.Rendering;
using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Messages;
using Spectre.Console;
using Spectre.Console.Cli;

namespace LlrpCli.Commands;

public sealed class EncodeSettings : CommandSettings
{
    [CommandArgument(0, "<MESSAGE>")]
    [Description("Name of a standard LLRP message to encode.")]
    public string MessageName { get; init; } = string.Empty;

    [CommandOption("--llrp <VERSION>")]
    [Description("Protocol version to encode: auto, 1.0.1, 1.1, or 2.0.")]
    [DefaultValue("auto")]
    public string LlrpVersion { get; init; } = "auto";

    [CommandOption("--message-id <UINT32>")]
    [Description("Message ID (decimal or 0x hex).")]
    [DefaultValue("1")]
    public string MessageIdRaw { get; init; } = "1";

    [CommandOption("--rospec-id <UINT32>")]
    [Description("ROSpec ID required by ROSpec messages.")]
    public string? RoSpecIdRaw { get; init; }

    [CommandOption("--requested-data <DATA>")]
    [Description("Requested data type for GetReaderCapabilities.")]
    [DefaultValue("All")]
    public string RequestedDataRaw { get; init; } = "All";
}

public sealed class EncodeCommand : Command<EncodeSettings>
{
    private readonly IAnsiConsole _console;

    public EncodeCommand() : this(AnsiConsole.Console) { }

    public EncodeCommand(IAnsiConsole console)
    {
        _console = console ?? AnsiConsole.Console;
    }

    protected override int Execute(CommandContext context, EncodeSettings settings, CancellationToken cancellationToken)
    {
        if (!Helpers.TryParseLlrpVersion(settings.LlrpVersion, out LlrpProtocolVersion version))
        {
            throw new CliUsageException("LLRP version must be auto, 1.0.1, 1.1, or 2.0.");
        }

        uint messageId = Helpers.ParseUInt32(settings.MessageIdRaw, "--message-id");
        uint? roSpecId = settings.RoSpecIdRaw is not null ? Helpers.ParseUInt32(settings.RoSpecIdRaw, "--rospec-id") : null;
        string requestedData = Helpers.ParseRequestedData(version, settings.RequestedDataRaw);

        ILlrpMessage message = Helpers.CreateEncodeMessage(settings.MessageName, version, messageId, roSpecId, requestedData);
        byte[] frame = Helpers.CreateRegistry().EncodeMessage(version, message);

        FrameRenderer.RenderEncodedHex(settings.MessageName, messageId, frame, _console);
        return 0;
    }
}
