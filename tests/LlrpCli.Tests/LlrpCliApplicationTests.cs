using System.Text.Json;
using DeleteRoSpec = LlrpNet.Protocol.Messages.V1_0_1.DELETE_ROSPEC;
using LlrpNet.Protocol.Parameters.V1_0_1;
using LlrpNet.Protocol.Enumerations.V1_0_1;
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
        Assert.NotEmpty(output.ToString());
    }

    [Fact]
    public void CommandCatalog_SettingsExposesOnlyTheStableSubcommands()
    {
        CommandSpec config = CommandCatalog.Require("settings");
        InputAssist assist = CommandCatalog.Assist("settings ", cursor: 9, isConnected: true);

        Assert.Equal(LiveSettingsHandler.Usage, config.Usage);
        Assert.Contains("show", assist.Candidates, StringComparer.Ordinal);
        Assert.Contains("edit", assist.Candidates, StringComparer.Ordinal);
        Assert.Contains("validate", assist.Candidates, StringComparer.Ordinal);
        Assert.Contains("apply", assist.Candidates, StringComparer.Ordinal);
        Assert.Contains("load", assist.Candidates, StringComparer.Ordinal);
        Assert.Contains("save", assist.Candidates, StringComparer.Ordinal);
        Assert.Contains("discard", assist.Candidates, StringComparer.Ordinal);
        Assert.DoesNotContain("get", config.CompletionCandidates, StringComparer.Ordinal);
        Assert.DoesNotContain("wizard", config.CompletionCandidates, StringComparer.Ordinal);
        Assert.DoesNotContain("export", config.CompletionCandidates, StringComparer.Ordinal);
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
        Assert.False(CommandCatalog.TryResolve("settings", isConnected: false, out _));
        Assert.True(CommandCatalog.TryResolve("settings", isConnected: true, out CommandSpec command));
        Assert.Equal(LiveCommandRoute.Settings, command.Route);
    }

    [Fact]
    public void LiveHelp_ForSettings_RendersCatalogUsageWithoutTreatingOptionsAsMarkup()
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

        Exception? exception = Record.Exception(() => renderCommandHelp.Invoke(command, ["settings"]));

        Assert.Null(exception);
        Assert.Contains("settings show", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("edit", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("apply", output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("settings get", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void HelpOption_PrintsProtocolToolsAndOneShotInventory()
    {
        InvocationResult result = Invoke("--help");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("inspect", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("decode", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("validate", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("encode", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("inventory", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("monitor", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(result.Error);
    }

    [Fact]
    public void CommandCatalog_ListsCandidateNamesInsteadOfOnlyACount()
    {
        InputAssist assist = CommandCatalog.Assist("in", cursor: 2, isConnected: true);

        Assert.Contains("Commands:", assist.Hint, StringComparison.Ordinal);
        Assert.Contains("inspect", assist.Hint, StringComparison.Ordinal);
        Assert.Contains("inventory", assist.Hint, StringComparison.Ordinal);
        Assert.DoesNotContain("matching commands", assist.Hint, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CommandCatalog_EmptyOfflinePromptOffersConnectAsTheNextAction()
    {
        InputAssist assist = CommandCatalog.Assist(string.Empty, cursor: 0, isConnected: false);

        Assert.Contains("connect", assist.Candidates, StringComparer.Ordinal);
        Assert.Equal("connect", assist.GhostSuffix);
        Assert.Contains("Next: connect", assist.Hint, StringComparison.Ordinal);
    }

    [Fact]
    public void CommandCatalog_SettingsApplyOffersConfirmation()
    {
        InputAssist assist = CommandCatalog.Assist("settings apply ", cursor: 15, isConnected: true);

        Assert.Contains("apply", assist.Candidates, StringComparer.Ordinal);
    }

    [Fact]
    public void LiveSessionContext_StartsWithoutAnImplicitDraft()
    {
        var session = new LiveSessionContext();

        Assert.Null(session.DraftInfo);
        Assert.Null(session.SettingsDraft);
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
        var original = new LlrpSdk.InventorySettings
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

        string json = LlrpSdk.InventorySettingsSerializer.SerializeToJson(original);
        LlrpSdk.InventorySettings deserialized = LlrpSdk.InventorySettingsSerializer.DeserializeFromJson(json);

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
        var settings = new LlrpSdk.InventorySettings
        {
            Extensions = new Dictionary<string, object?>
            {
                ["impinj.inventoryReport"] = new object(),
            },
        };

        Assert.Throws<NotSupportedException>(() => LlrpSdk.InventorySettingsSerializer.SerializeToJson(settings));
    }

    [Fact]
    public void InventoryStart_MonitorDurationIsParsedAsPositiveSeconds()
    {
        System.Reflection.MethodInfo? parseMethod = typeof(LiveInventoryHandler)
            .GetMethod("ParseStartMonitorDurationSeconds", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);

        int? seconds = (int?)parseMethod!.Invoke(
            null,
            [new[] { "inventory", "start", "--monitor", "live", "--monitor-duration", "30" }]);

        Assert.Equal(30, seconds);
    }

    [Fact]
    public void CommandCatalog_InventoryHasNoSecondTemporarySessionPath()
    {
        CommandSpec inv = CommandCatalog.Require("inventory");
        InputAssist assist = CommandCatalog.Assist("inventory ", cursor: 10, isConnected: true);

        Assert.DoesNotContain("settings", inv.CompletionCandidates, StringComparer.Ordinal);
        Assert.DoesNotContain("--antennas", inv.CompletionCandidates, StringComparer.Ordinal);
        Assert.Contains("start", assist.Candidates, StringComparer.Ordinal);
        Assert.Contains("stop", assist.Candidates, StringComparer.Ordinal);
        Assert.Null(CommandCatalog.Find("session"));
    }

    [Fact]
    public void Inventory_RequiresExplicitConfirmationBeforeConnecting()
    {
        InvocationResult result = Invoke("inventory", "reader.local");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("--yes", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Inventory_RejectsInvalidDurationBeforeConnecting()
    {
        InvocationResult result = Invoke("inventory", "reader.local", "--duration", "0", "--yes");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("--duration", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Inventory_OutputFormatIsCaseInsensitive()
    {
        InvocationResult result = Invoke("inventory", "reader.local", "--output", "JSON");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("--yes", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("--output must", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void InventoryHelp_DescribesSettingsDurationOutputAndConfirmation()
    {
        InvocationResult result = Invoke("inventory", "--help");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("--settings", result.Output, StringComparison.Ordinal);
        Assert.Contains("--duration", result.Output, StringComparison.Ordinal);
        Assert.Contains("--output", result.Output, StringComparison.Ordinal);
        Assert.Contains("--yes", result.Output, StringComparison.Ordinal);
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
