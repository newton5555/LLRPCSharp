using System.Text.Json;
using LlrpNet.Protocol.Messages.V1_0_1;
using LlrpNet.Protocol.Registry;
using LlrpNet.Protocol.Registry.V1_0_1;

namespace LlrpCli.Tests;

public sealed class LlrpCliApplicationTests
{
    [Fact]
    public void HelpOption_PrintsHelp()
    {
        InvocationResult result = Invoke("--help");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("inspect", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(result.Error);
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

    private sealed record InvocationResult(
        int ExitCode,
        string Output,
        string Error);
}
