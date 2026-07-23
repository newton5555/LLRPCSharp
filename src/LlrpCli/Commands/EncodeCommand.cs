using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Messages;
using LlrpNet.Protocol.Messages.V1_0_1;
using LlrpCli.Rendering;

namespace LlrpCli.Commands;

public sealed class EncodeSettings : CommandSettings
{
    [CommandArgument(0, "<MESSAGE>")]
    [Description("Name of the standard LLRP message to encode.")]
    public string MessageName { get; init; } = string.Empty;

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
        uint messageId = Helpers.ParseUInt32(settings.MessageIdRaw, "--message-id");
        uint? roSpecId = settings.RoSpecIdRaw is not null ? Helpers.ParseUInt32(settings.RoSpecIdRaw, "--rospec-id") : null;

        if (!Enum.TryParse(settings.RequestedDataRaw, ignoreCase: true, out GetReaderCapabilitiesRequestedData requestedData)
            || !Enum.IsDefined(requestedData))
        {
            throw new CliUsageException($"'{settings.RequestedDataRaw}' is not a valid GET_READER_CAPABILITIES requested-data value.");
        }

        ILlrpMessage message = CreateMessage(settings.MessageName, messageId, roSpecId, requestedData);
        byte[] frame = Helpers.CreateRegistry().EncodeMessage(LlrpProtocolVersion.Version101, message);

        FrameRenderer.RenderEncodedHex(settings.MessageName, messageId, frame, _console);
        return 0;
    }

    private static ILlrpMessage CreateMessage(
        string messageName,
        uint messageId,
        uint? roSpecId,
        GetReaderCapabilitiesRequestedData requestedData)
    {
        return messageName.ToLowerInvariant() switch
        {
            "keepalive" => new Keepalive(messageId),
            "keepalive-ack" => new KeepaliveAck(messageId),
            "get-reader-capabilities" => new GetReaderCapabilities(messageId, requestedData),
            "get-rospecs" => new GetRoSpecs(messageId),
            "delete-rospec" => new DeleteRoSpec(messageId, RequireRoSpecId(roSpecId, messageName)),
            "start-rospec" => new StartRoSpec(messageId, RequireRoSpecId(roSpecId, messageName)),
            "stop-rospec" => new StopRoSpec(messageId, RequireRoSpecId(roSpecId, messageName)),
            "enable-rospec" => new EnableRoSpec(messageId, RequireRoSpecId(roSpecId, messageName)),
            "disable-rospec" => new DisableRoSpec(messageId, RequireRoSpecId(roSpecId, messageName)),
            _ => throw new CliUsageException($"The encode message '{messageName}' is not supported."),
        };
    }

    private static uint RequireRoSpecId(uint? roSpecId, string messageName)
    {
        return roSpecId ?? throw new CliUsageException($"The encode message '{messageName}' requires --rospec-id <UInt32>.");
    }
}
