using System.Reflection;
using System.Text.Json;
using LlrpCli.Commands;
using LlrpNet.Core.Protocol;
using LlrpSdk;
using LlrpNet.Protocol.Enumerations.V1_0_1;
using LlrpNet.Protocol.Messages.V1_0_1;
using LlrpNet.Protocol.Parameters.V1_0_1;
using LlrpNet.Protocol.Registry;
using LlrpNet.Protocol.Registry.V1_0_1;
using Spectre.Console;
using V101Messages = LlrpNet.Protocol.Messages.V1_0_1;

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
        Assert.Contains("save", assist.Candidates, StringComparer.Ordinal);
        Assert.DoesNotContain("defaults", assist.Candidates, StringComparer.Ordinal);
        Assert.DoesNotContain("default", assist.Candidates, StringComparer.Ordinal);
        Assert.DoesNotContain("load", assist.Candidates, StringComparer.Ordinal);
        Assert.DoesNotContain("draft", assist.Candidates, StringComparer.Ordinal);
        Assert.DoesNotContain("discard", assist.Candidates, StringComparer.Ordinal);
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
        var message = Assert.IsType<V101Messages.DELETE_ROSPEC>(
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
    public void LiveCommandCatalog_ExposesExplicitInspectionOptions()
    {
        Assert.Equal("status [--full]", CommandCatalog.Require("status").Usage);
        Assert.Equal("caps [--raw|--json]", CommandCatalog.Require("caps").Usage);
        Assert.Contains("settings show [--json|--raw]", CommandCatalog.Require("settings").Usage, StringComparison.Ordinal);
        Assert.DoesNotContain("--apply", CommandCatalog.Require("settings").CompletionCandidates);
        Assert.Contains("--refresh", CommandCatalog.Require("inventory").CompletionCandidates);
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

    [Fact]
    public void ManualModeGuard_PromptsOnlyWhenNonManagedResourcesExist()
    {
        Assert.False(ManualModeGuard.ShouldPromptToDelete(0, 0)); // 无资源 -> 静默退出
        Assert.True(ManualModeGuard.ShouldPromptToDelete(1, 0));   // 有 ROSpec -> 确认
        Assert.True(ManualModeGuard.ShouldPromptToDelete(0, 1));   // 有 AccessSpec -> 确认
        Assert.True(ManualModeGuard.ShouldPromptToDelete(2, 3));
    }

    [Fact]
    public void ProtocolVersionParser_MapsTwoToForce20()
    {
        Assert.True(ProtocolVersionPolicyParser.TryParse("2", out LlrpProtocolVersionPolicy policy));
        Assert.Equal(LlrpProtocolVersionPolicy.Force20, policy);
    }

    [Fact]
    public void ProtocolVersionParser_AcceptsAliases()
    {
        Assert.Equal(LlrpProtocolVersionPolicy.Force101, ParseVersion("101"));
        Assert.Equal(LlrpProtocolVersionPolicy.Force11, ParseVersion("11"));
        Assert.Equal(LlrpProtocolVersionPolicy.Force20, ParseVersion("20"));
        Assert.Equal(LlrpProtocolVersionPolicy.Force20, ParseVersion("2.0"));
        Assert.Equal(LlrpProtocolVersionPolicy.Force101, ParseVersion("1.0.1"));
        Assert.Equal(LlrpProtocolVersionPolicy.Force11, ParseVersion("1.1"));
    }

    [Fact]
    public void VendorModeParser_AcceptsZebra()
    {
        Assert.True(VendorExtensionModeParser.TryParse("zebra", out VendorExtensionMode mode));
        Assert.Equal(VendorExtensionMode.Zebra, mode);
    }

    [Fact]
    public void LlrpVersionParser_MapsOfflineVersions()
    {
        Assert.True(Helpers.TryParseLlrpVersion("2.0", out LlrpProtocolVersion version));
        Assert.Equal(LlrpProtocolVersion.Version20, version);
        Assert.True(Helpers.TryParseLlrpVersion("auto", out _));
        Assert.Equal(LlrpProtocolVersion.Version101, ParseOffline("101"));
        Assert.Equal(LlrpProtocolVersion.Version11, ParseOffline("11"));
    }

    private static LlrpProtocolVersionPolicy ParseVersion(string value)
    {
        Assert.True(ProtocolVersionPolicyParser.TryParse(value, out LlrpProtocolVersionPolicy policy));
        return policy;
    }

    private static LlrpProtocolVersion ParseOffline(string value)
    {
        Assert.True(Helpers.TryParseLlrpVersion(value, out LlrpProtocolVersion version));
        return version;
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

    [Fact]
    public void PcapNgReader_ExtractsTcpSegmentsFromEnhancedPacketBlocks()
    {
        // One Ethernet/IPv4/TCP packet carrying a KEEPALIVE frame on port 5084,
        // wrapped in a minimal pcapng (SHB + IDB + EPB).
        byte[] ethernetPacket = Convert.FromHexString(
            // dst MAC    src MAC     type IPv4
            "30D0423F4A6E00162513E6ED0800" +
            // IPv4 header (20 bytes): v4/ihl5, proto TCP(6), src 192.168.40.50, dst 192.168.40.87
            "4500003C0000000040060000C0A82832C0A82857" +
            // TCP header (20 bytes): src 62697, dst 5084, data offset 5
            "F4E913DC00000000000000005002000000000000" +
            // LLRP KEEPALIVE frame: type 62, length 10, id 0x01020304
            "043E0000000A01020304");

        byte[] pcapng = BuildPcapNg(ethernetPacket);

        IReadOnlyList<LlrpCli.Pcap.PcapTcpSegment> segments = LlrpCli.Pcap.PcapNgReader.ReadTcpSegments(pcapng);
        Assert.Single(segments);
        Assert.Equal(5084u, segments[0].DstPort);
        Assert.Equal("192.168.40.50", segments[0].SrcIp);
        Assert.Equal("192.168.40.87", segments[0].DstIp);
        Assert.Equal("043E0000000A01020304", Convert.ToHexString(segments[0].Payload));
    }

    [Fact]
    public void PcapNgReader_ReassemblesCompleteLlrpFrameAndMarksDirection()
    {
        byte[] ethernetPacket = Convert.FromHexString(
            "30D0423F4A6E00162513E6ED0800" +
            "4500003C0000000040060000C0A82832C0A82857" +
            "F4E913DC00000000000000005002000000000000" +
            "043E0000000A01020304");
        byte[] pcapng = BuildPcapNg(ethernetPacket);

        IReadOnlyList<LlrpCli.Pcap.PcapTcpSegment> segments = LlrpCli.Pcap.PcapNgReader.ReadTcpSegments(pcapng);
        IReadOnlyList<LlrpCli.Pcap.LlrpCapturedMessage> messages = LlrpCli.Pcap.LlrpStreamReassembler.Reassemble(segments);

        Assert.Single(messages);
        Assert.Equal(LlrpNet.Core.Diagnostics.LlrpFrameDirection.Transmit, messages[0].Direction);
        Assert.Equal("043E0000000A01020304", Convert.ToHexString(messages[0].Frame));
    }

    private static byte[] BuildPcapNg(byte[] packet)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        WriteU32(writer, 0x0A0D0D0A); // SHB type
        WriteU32(writer, 28);          // SHB length
        WriteU32(writer, 0x1A2B3C4D);  // byte-order magic
        WriteU16(writer, 1);           // major
        WriteU16(writer, 0);           // minor
        WriteI64(writer, -1);          // section length
        WriteU32(writer, 0x0A0D0D0A);  // SHB trailer

        WriteU32(writer, 1);           // IDB type
        WriteU32(writer, 20);          // IDB length
        WriteU16(writer, 1);           // linktype Ethernet
        WriteU16(writer, 0);           // reserved
        WriteU32(writer, 0);           // snaplen
        WriteU32(writer, 1);           // IDB trailer

        int epbLength = 28 + packet.Length + ((4 - (packet.Length % 4)) % 4);
        WriteU32(writer, 6);           // EPB type
        WriteU32(writer, (uint)epbLength);
        WriteU32(writer, 0);           // interface id
        WriteU32(writer, 0);           // timestamp high
        WriteU32(writer, 0);           // timestamp low
        WriteU32(writer, (uint)packet.Length);
        WriteU32(writer, (uint)packet.Length);
        writer.Write(packet);
        int pad = (4 - (packet.Length % 4)) % 4;
        for (int i = 0; i < pad; i++) { writer.Write((byte)0); }
        WriteU32(writer, (uint)epbLength);

        return stream.ToArray();
    }

    private static void WriteU32(BinaryWriter writer, uint value) => writer.Write(value);
    private static void WriteU16(BinaryWriter writer, ushort value) => writer.Write(value);
    private static void WriteI64(BinaryWriter writer, long value) => writer.Write(value);

    private sealed record InvocationResult(
        int ExitCode,
        string Output,
        string Error);
}
