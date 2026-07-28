using System.Text.Json;
using System.Reflection;
using LlrpCli.Commands;
using LlrpNet.Protocol.Messages.V1_0_1;
using LlrpNet.Protocol.Registry;
using LlrpNet.Protocol.Registry.V1_0_1;
using Spectre.Console;

namespace LlrpCli.Tests;

public sealed class LlrpCliApplicationTests
{
    [Fact]
    public void LiveHelp_RendersUsageContainingLiteralOptionPlaceholders()
    {
        using var output = new StringWriter();
        IAnsiConsole console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(output)
        });
        var command = new LiveCommand(console);
        MethodInfo renderHelp = typeof(LiveCommand).GetMethod(
            "RenderHelp",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        Exception? exception = Record.Exception(() => renderHelp.Invoke(command, null));

        Assert.Null(exception);
        Assert.Contains("config apply [options]", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void CommandCatalog_ConfigProvidesUsageAndLiveSubcommandCandidates()
    {
        CommandSpec config = CommandCatalog.Require("config");
        InputAssist assist = CommandCatalog.Assist("config ", cursor: 7, isConnected: true);

        Assert.Equal("config get | defaults | apply [options] [--dry-run] --yes", config.Usage);
        Assert.Contains("get", assist.Candidates, StringComparer.Ordinal);
        Assert.Contains("defaults", assist.Candidates, StringComparer.Ordinal);
        Assert.Contains("apply", assist.Candidates, StringComparer.Ordinal);
    }

    [Fact]
    public void CommandCatalog_ResolvesAliasesToOneCanonicalRoute()
    {
        bool resolved = CommandCatalog.TryResolve("cls", isConnected: false, out CommandSpec command);

        Assert.True(resolved);
        Assert.Equal("clear", command.Name);
        Assert.Equal(LiveCommandRoute.Clear, command.Route);
    }

    [Fact]
    public void CommandCatalog_HidesConnectedOnlyCommandsUntilConnected()
    {
        Assert.False(CommandCatalog.TryResolve("config", isConnected: false, out _));
        Assert.True(CommandCatalog.TryResolve("config", isConnected: true, out CommandSpec command));
        Assert.Equal(LiveCommandRoute.Configuration, command.Route);
    }

    [Fact]
    public void LiveHelp_ForConfig_RendersCatalogUsageWithoutTreatingOptionsAsMarkup()
    {
        using var output = new StringWriter();
        IAnsiConsole console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(output)
        });
        var command = new LiveCommand(console);
        MethodInfo renderCommandHelp = typeof(LiveCommand).GetMethod(
            "RenderCommandHelp",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        Exception? exception = Record.Exception(() => renderCommandHelp.Invoke(command, ["config"]));

        Assert.Null(exception);
        Assert.Contains("config get | defaults | apply [options] [--dry-run] --yes", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void HelpOption_PrintsOnlyOfflineProtocolTools()
    {
        InvocationResult result = Invoke("--help");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("inspect", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("decode", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("validate", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("encode", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("monitor", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tag", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(result.Error);
    }

    [Fact]
    public void CommandCatalog_ConfigApplyPowerOptionExplainsIndexSemantics()
    {
        InputAssist assist = CommandCatalog.Assist("config apply --tx-power ", cursor: 24, isConnected: true);

        Assert.Contains("index", assist.Hint, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not dBm", assist.Hint, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Inspect_PrintsValidatedHeaderFields()
    {
        InvocationResult result = Invoke(
            "inspect",
            "04:3E-00 00 00 0A 01 02 03 04");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("MessageType: 62", result.Output, StringComparison.Ordinal);
        Assert.Contains("MessageId: 16909060", result.Output, StringComparison.Ordinal);
        Assert.Contains("PayloadLength: 0", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Decode_PrintsMachineReadableKnownMessageSummary()
    {
        InvocationResult result = Invoke(
            "decode",
            "043E0000000A01020304");

        Assert.Equal(0, result.ExitCode);
        using JsonDocument document = JsonDocument.Parse(result.Output.Trim());
        JsonElement root = document.RootElement;
        Assert.Equal(62, root.GetProperty("messageType").GetInt32());
        Assert.Equal(0x01020304U, root.GetProperty("messageId").GetUInt32());
        Assert.EndsWith(".KEEPALIVE", root.GetProperty("model").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_RejectsFrameWhoseDeclaredLengthIsNotExact()
    {
        InvocationResult result = Invoke(
            "validate",
            "043E0000000B01020304");

        Assert.Equal(3, result.ExitCode);
        Assert.Contains("declares 11", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Encode_GetRoSpecs_UsesMessageIdAndNormativeWireLayout()
    {
        InvocationResult result = Invoke(
            "encode",
            "get-rospecs",
            "--message-id",
            "0x01020304");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("041A0000000A01020304", result.Output.Trim());
    }

    [Fact]
    public void Encode_RoSpecIdMessage_RequiresAndEncodesRoSpecId()
    {
        InvocationResult missing = Invoke("encode", "delete-rospec");
        InvocationResult encoded = Invoke(
            "encode",
            "delete-rospec",
            "--message-id",
            "7",
            "--rospec-id",
            "0xA1B2C3D4");

        Assert.Equal(2, missing.ExitCode);
        Assert.Contains("--rospec-id", missing.Error, StringComparison.Ordinal);
        Assert.Equal(0, encoded.ExitCode);

        LlrpCodecRegistry registry = CreateRegistry();
        var message = Assert.IsType<DeleteRoSpec>(
            registry.DecodeMessage(Convert.FromHexString(encoded.Output.Trim())));
        Assert.Equal((uint)7, message.MessageId);
        Assert.Equal(0xA1B2C3D4U, message.ROSpecID);
    }

    [Theory]
    [InlineData("decode", "ABC")]
    [InlineData("inspect", "GG")]
    [InlineData("validate", "")]
    public void ProtocolTools_RejectMalformedHex(string command, string frame)
    {
        InvocationResult result = Invoke(command, frame);

        Assert.Equal(3, result.ExitCode);
        Assert.Contains("Invalid LLRP input", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownCommand_IsAUsageError()
    {
        InvocationResult result = Invoke("invent");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("Unknown command", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void CommandCatalog_TagRequiresConnectionAndOffersAccessCandidates()
    {
        Assert.False(CommandCatalog.TryResolve("tag", isConnected: false, out _));
        Assert.True(CommandCatalog.TryResolve("tag", isConnected: true, out CommandSpec tag));
        InputAssist assist = CommandCatalog.Assist("tag ", cursor: 4, isConnected: true);

        Assert.Equal(LiveCommandRoute.TagAccess, tag.Route);
        Assert.Contains("read", assist.Candidates, StringComparer.Ordinal);
        Assert.Contains("write", assist.Candidates, StringComparer.Ordinal);
        Assert.Contains("sequence", assist.Candidates, StringComparer.Ordinal);
        Assert.Contains("--yes", assist.Candidates, StringComparer.Ordinal);
    }

    [Fact]
    public void ReaderSettingsSerializer_SerializesAndDeserializesCorrectly()
    {
        var original = new LlrpSdk.ReaderSettings
        {
            Session = 2,
            TagPopulationEstimate = 64,
            ModeIndex = 1,
            Tari = 12500,
            AttachedData = new LlrpSdk.AttachedDataOptions
            {
                Enabled = true,
                MemoryBank = 2,
                WordPointer = 0,
                WordCount = 6,
                AccessPassword = "00000000"
            }
        };

        string json = LlrpSdk.ReaderSettingsSerializer.SerializeToJson(original);
        LlrpSdk.ReaderSettings deserialized = LlrpSdk.ReaderSettingsSerializer.DeserializeFromJson(json);

        Assert.Equal((byte)2, deserialized.Session);
        Assert.Equal((ushort)64, deserialized.TagPopulationEstimate);
        Assert.Equal((ushort)1, deserialized.ModeIndex);
        Assert.Equal((ushort)12500, deserialized.Tari);
        Assert.True(deserialized.AttachedData.Enabled);
        Assert.Equal((ushort)2, deserialized.AttachedData.MemoryBank);
    }

    [Fact]
    public void ReaderSettingsSerializer_RejectsUntypedVendorExtensions()
    {
        var settings = new LlrpSdk.ReaderSettings
        {
            Extensions = new Dictionary<string, object?>
            {
                ["impinj.inventoryReport"] = new object(),
            },
        };

        Assert.Throws<NotSupportedException>(() => LlrpSdk.ReaderSettingsSerializer.SerializeToJson(settings));
    }

    [Fact]
    public async Task InventorySettings_SetUpdatesTheSessionDraft()
    {
        (LiveInventoryHandler handler, LiveSessionContext session) = CreateInventoryHandler();
        await handler.HandleAsync(["inventory", "settings", "set", "--session", "2", "--population", "64", "--mode", "1"], CancellationToken.None);

        Assert.Equal((byte)2, session.DesiredInventorySettings.Session);
        Assert.Equal((ushort)64, session.DesiredInventorySettings.TagPopulationEstimate);
        Assert.Equal((ushort)1, session.DesiredInventorySettings.ModeIndex);
    }

    [Fact]
    public async Task InventorySettings_GetAndDirectOptionsAreFriendlyAliases()
    {
        (LiveInventoryHandler handler, LiveSessionContext session) = CreateInventoryHandler();

        await handler.HandleAsync(["inventory", "settings", "--antennas", "1"], CancellationToken.None);
        await handler.HandleAsync(["inventory", "settings", "get"], CancellationToken.None);

        Assert.Equal([(ushort)1], session.DesiredInventorySettings.AntennaIds);
    }

    [Fact]
    public void InventoryStart_AntennasOverrideTheDraftOnlyForThisStart()
    {
        (LiveInventoryHandler handler, LiveSessionContext session) = CreateInventoryHandler();
        session.DesiredInventorySettings = new LlrpSdk.ReaderSettings { AntennaIds = [2] };
        string[] tokens = ["inventory", "start", "--antennas", "1,3"];
        System.Reflection.MethodInfo? parseMethod = typeof(LiveInventoryHandler)
            .GetMethod("ParseStartSettings", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var settings = (LlrpSdk.ReaderSettings)parseMethod!.Invoke(handler, [tokens])!;

        Assert.Equal([2], session.DesiredInventorySettings.AntennaIds);
        Assert.Equal([1, 3], settings.AntennaIds);
    }

    [Fact]
    public async Task InventorySettings_SetAttachedDataUpdatesTheSessionDraft()
    {
        (LiveInventoryHandler handler, LiveSessionContext session) = CreateInventoryHandler();
        await handler.HandleAsync(["inventory", "settings", "set", "--attach-bank", "tid", "--attach-len", "6"], CancellationToken.None);

        Assert.True(session.DesiredInventorySettings.AttachedData.Enabled);
        Assert.Equal((ushort)2, session.DesiredInventorySettings.AttachedData.MemoryBank);
        Assert.Equal((ushort)6, session.DesiredInventorySettings.AttachedData.WordCount);
    }

    [Fact]
    public async Task InventorySettings_ResetRestoresSdkDefaults()
    {
        (LiveInventoryHandler handler, LiveSessionContext session) = CreateInventoryHandler();
        session.DesiredInventorySettings = new LlrpSdk.ReaderSettings { Session = 3, AntennaIds = [1] };
        await handler.HandleAsync(["inventory", "settings", "reset"], CancellationToken.None);

        Assert.Equal((byte)0, session.DesiredInventorySettings.Session);
        Assert.Equal([(ushort)0], session.DesiredInventorySettings.AntennaIds);
    }

    [Fact]
    public async Task InventorySettings_LoadThenSetUpdatesTheDraft()
    {
        // 写入临时 JSON settings 文件
        string tempFile = System.IO.Path.GetTempFileName() + ".json";
        var fileSettings = new LlrpSdk.ReaderSettings { Session = 3, TagPopulationEstimate = 128 };
        System.IO.File.WriteAllText(tempFile, LlrpSdk.ReaderSettingsSerializer.SerializeToJson(fileSettings));
        try
        {
            (LiveInventoryHandler handler, LiveSessionContext session) = CreateInventoryHandler();
            await handler.HandleAsync(["inventory", "settings", "load", tempFile], CancellationToken.None);
            await handler.HandleAsync(["inventory", "settings", "set", "--session", "2"], CancellationToken.None);

            Assert.Equal((byte)2, session.DesiredInventorySettings.Session);
            Assert.Equal((ushort)128, session.DesiredInventorySettings.TagPopulationEstimate);
        }
        finally
        {
            System.IO.File.Delete(tempFile);
        }
    }

    [Fact]
    public void CommandCatalog_InventoryOffersSettingsAndAntennaCandidates()
    {
        CommandSpec inv = CommandCatalog.Require("inventory");
        InputAssist assist = CommandCatalog.Assist("inventory ", cursor: 10, isConnected: true);

        Assert.Contains("settings", inv.CompletionCandidates, StringComparer.Ordinal);
        Assert.Contains("--antennas", inv.CompletionCandidates, StringComparer.Ordinal);
        Assert.Contains("start", assist.Candidates, StringComparer.Ordinal);
        Assert.Contains("stop", assist.Candidates, StringComparer.Ordinal);
    }

    private static InvocationResult Invoke(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        int exitCode = new LlrpCliApplication().Run(args, output, error);
        return new InvocationResult(exitCode, output.ToString(), error.ToString());
    }

    private static LlrpCodecRegistry CreateRegistry()
    {
        var registry = new LlrpCodecRegistry();
        Llrp101StandardModule.Register(registry);
        return registry;
    }

    private static (LiveInventoryHandler Handler, LiveSessionContext Session) CreateInventoryHandler()
    {
        var output = new StringWriter();
        IAnsiConsole console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(output)
        });
        var session = new LiveSessionContext();
        return (new LiveInventoryHandler(console, session, new LiveMonitorHandler(console, session)), session);
    }

    private sealed record InvocationResult(
        int ExitCode,
        string Output,
        string Error);
}
