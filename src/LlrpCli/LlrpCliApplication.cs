using System.Globalization;
using System.Text.Json;
using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Messages;
using LlrpNet.Protocol.Messages.V1_0_1;
using LlrpNet.Protocol.Registry;
using LlrpNet.Protocol.Registry.V1_0_1;

namespace LlrpCli;

/// <summary>
/// Hosts the command-line protocol tools independently from process-global console state.
/// </summary>
public sealed class LlrpCliApplication
{
    /// <summary>
    /// Executes one CLI invocation.
    /// </summary>
    /// <param name="args">Command-line arguments excluding the executable name.</param>
    /// <param name="output">Standard output destination.</param>
    /// <param name="error">Standard error destination.</param>
    /// <returns>Zero on success, two for usage errors, or three for invalid protocol input.</returns>
    public int Run(
        IReadOnlyList<string> args,
        TextWriter output,
        TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        if (args.Count == 0 || IsHelp(args[0]))
        {
            WriteHelp(output);
            return 0;
        }

        try
        {
            return args[0].ToLowerInvariant() switch
            {
                "inspect" => RunInspect(args, output),
                "decode" => RunDecode(args, output),
                "validate" => RunValidate(args, output),
                "encode" => RunEncode(args, output),
                _ => ThrowUsage($"Unknown command '{args[0]}'."),
            };
        }
        catch (CliUsageException exception)
        {
            error.WriteLine(exception.Message);
            error.WriteLine("Run 'llrp --help' for usage.");
            return 2;
        }
        catch (Exception exception) when (
            exception is FormatException
                or OverflowException
                or ArgumentException
                or LlrpProtocolException)
        {
            error.WriteLine($"Invalid LLRP input: {exception.Message}");
            return 3;
        }
    }

    private static int RunInspect(
        IReadOnlyList<string> args,
        TextWriter output)
    {
        byte[] frame = ReadSingleFrameArgument(args, "inspect");
        LlrpMessageHeader header = DecodeExactHeader(frame);

        output.WriteLine($"Version: {(byte)header.Version} ({header.Version})");
        output.WriteLine($"MessageType: {header.MessageType}");
        output.WriteLine($"MessageId: {header.MessageId}");
        output.WriteLine($"MessageLength: {header.MessageLength}");
        output.WriteLine($"PayloadLength: {frame.Length - LlrpMessageHeader.EncodedLength}");
        return 0;
    }

    private static int RunDecode(
        IReadOnlyList<string> args,
        TextWriter output)
    {
        byte[] frame = ReadSingleFrameArgument(args, "decode");
        LlrpMessageHeader header = DecodeExactHeader(frame);
        ILlrpMessage message = CreateRegistry().DecodeMessage(frame);
        var decoded = new
        {
            protocolVersion = (byte)header.Version,
            messageType = header.MessageType,
            messageId = header.MessageId,
            messageLength = header.MessageLength,
            model = message.GetType().FullName,
            rawHex = Convert.ToHexString(frame),
        };

        output.WriteLine(JsonSerializer.Serialize(decoded));
        return 0;
    }

    private static int RunValidate(
        IReadOnlyList<string> args,
        TextWriter output)
    {
        byte[] frame = ReadSingleFrameArgument(args, "validate");
        DecodeExactHeader(frame);
        ILlrpMessage message = CreateRegistry().DecodeMessage(frame);
        output.WriteLine($"Valid: {message.GetType().Name}");
        return 0;
    }

    private static int RunEncode(
        IReadOnlyList<string> args,
        TextWriter output)
    {
        if (args.Count < 2)
        {
            return ThrowUsage("The encode command requires a message name.");
        }

        EncodeOptions options = ParseEncodeOptions(args);
        ILlrpMessage message = CreateMessage(args[1], options);
        byte[] frame = CreateRegistry().EncodeMessage(LlrpProtocolVersion.Version101, message);
        output.WriteLine(Convert.ToHexString(frame));
        return 0;
    }

    private static ILlrpMessage CreateMessage(
        string messageName,
        EncodeOptions options)
    {
        return messageName.ToLowerInvariant() switch
        {
            "keepalive" => new Keepalive(options.MessageId),
            "keepalive-ack" => new KeepaliveAck(options.MessageId),
            "get-reader-capabilities" => new GetReaderCapabilities(
                options.MessageId,
                options.RequestedData),
            "get-rospecs" => new GetRoSpecs(options.MessageId),
            "delete-rospec" => new DeleteRoSpec(options.MessageId, RequireRoSpecId(options, messageName)),
            "start-rospec" => new StartRoSpec(options.MessageId, RequireRoSpecId(options, messageName)),
            "stop-rospec" => new StopRoSpec(options.MessageId, RequireRoSpecId(options, messageName)),
            "enable-rospec" => new EnableRoSpec(options.MessageId, RequireRoSpecId(options, messageName)),
            "disable-rospec" => new DisableRoSpec(options.MessageId, RequireRoSpecId(options, messageName)),
            _ => throw new CliUsageException($"The encode message '{messageName}' is not supported."),
        };
    }

    private static EncodeOptions ParseEncodeOptions(IReadOnlyList<string> args)
    {
        uint messageId = 1;
        uint? roSpecId = null;
        GetReaderCapabilitiesRequestedData requestedData = GetReaderCapabilitiesRequestedData.All;
        var seenOptions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int index = 2; index < args.Count; index += 2)
        {
            string option = args[index];
            if (!option.StartsWith("--", StringComparison.Ordinal) || index + 1 >= args.Count)
            {
                throw new CliUsageException($"Encode option '{option}' requires a value.");
            }

            if (!seenOptions.Add(option))
            {
                throw new CliUsageException($"Encode option '{option}' was specified more than once.");
            }

            string value = args[index + 1];
            switch (option.ToLowerInvariant())
            {
                case "--message-id":
                    messageId = ParseUInt32(value, option);
                    break;
                case "--rospec-id":
                    roSpecId = ParseUInt32(value, option);
                    break;
                case "--requested-data":
                    if (!Enum.TryParse(value, ignoreCase: true, out requestedData)
                        || !Enum.IsDefined(requestedData))
                    {
                        throw new CliUsageException(
                            $"'{value}' is not a valid GET_READER_CAPABILITIES requested-data value.");
                    }

                    break;
                default:
                    throw new CliUsageException($"Unknown encode option '{option}'.");
            }
        }

        return new EncodeOptions(messageId, roSpecId, requestedData);
    }

    private static byte[] ReadSingleFrameArgument(
        IReadOnlyList<string> args,
        string command)
    {
        if (args.Count != 2)
        {
            throw new CliUsageException($"The {command} command requires exactly one hexadecimal frame.");
        }

        return ParseHex(args[1]);
    }

    private static byte[] ParseHex(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var normalized = new char[value.Length];
        int length = 0;
        foreach (char character in value)
        {
            if (char.IsWhiteSpace(character) || character is ':' or '-')
            {
                continue;
            }

            if (!Uri.IsHexDigit(character))
            {
                throw new FormatException($"Character '{character}' is not hexadecimal.");
            }

            normalized[length++] = character;
        }

        if (length == 0)
        {
            throw new FormatException("A hexadecimal frame cannot be empty.");
        }

        if ((length & 1) != 0)
        {
            throw new FormatException("A hexadecimal frame must contain an even number of digits.");
        }

        return Convert.FromHexString(normalized.AsSpan(0, length));
    }

    private static LlrpMessageHeader DecodeExactHeader(ReadOnlySpan<byte> frame)
    {
        LlrpMessageHeader header = LlrpMessageHeader.Decode(frame);
        if (header.MessageLength != frame.Length)
        {
            throw new LlrpProtocolException(
                LlrpProtocolErrorCode.InvalidMessageLength,
                $"The frame contains {frame.Length} octets, but its header declares {header.MessageLength}.");
        }

        return header;
    }

    private static LlrpCodecRegistry CreateRegistry()
    {
        var registry = new LlrpCodecRegistry();
        Llrp101StandardModule.Register(registry);
        return registry;
    }

    private static uint ParseUInt32(string value, string option)
    {
        NumberStyles styles = NumberStyles.None;
        ReadOnlySpan<char> digits = value.AsSpan();
        if (digits.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            styles = NumberStyles.AllowHexSpecifier;
            digits = digits[2..];
        }

        if (digits.IsEmpty
            || !uint.TryParse(digits, styles, CultureInfo.InvariantCulture, out uint result))
        {
            throw new CliUsageException($"Encode option '{option}' requires a UInt32 value.");
        }

        return result;
    }

    private static uint RequireRoSpecId(EncodeOptions options, string messageName)
    {
        return options.RoSpecId
            ?? throw new CliUsageException(
                $"The encode message '{messageName}' requires --rospec-id <UInt32>.");
    }

    private static bool IsHelp(string value)
    {
        return value is "help" or "--help" or "-h";
    }

    private static int ThrowUsage(string message)
    {
        throw new CliUsageException(message);
    }

    private static void WriteHelp(TextWriter output)
    {
        output.WriteLine("LLRP C# SDK protocol tools");
        output.WriteLine();
        output.WriteLine("Usage:");
        output.WriteLine("  llrp inspect <hex-frame>");
        output.WriteLine("  llrp decode <hex-frame>");
        output.WriteLine("  llrp validate <hex-frame>");
        output.WriteLine("  llrp encode <message> [options]");
        output.WriteLine();
        output.WriteLine("Encode messages:");
        output.WriteLine("  keepalive | keepalive-ack | get-reader-capabilities | get-rospecs");
        output.WriteLine("  delete-rospec | start-rospec | stop-rospec | enable-rospec | disable-rospec");
        output.WriteLine();
        output.WriteLine("Encode options:");
        output.WriteLine("  --message-id <UInt32>       Defaults to 1; decimal or 0x-prefixed hex.");
        output.WriteLine("  --rospec-id <UInt32>        Required by ROSpec-ID messages.");
        output.WriteLine("  --requested-data <name>     Defaults to All for get-reader-capabilities.");
    }

    private sealed record EncodeOptions(
        uint MessageId,
        uint? RoSpecId,
        GetReaderCapabilitiesRequestedData RequestedData);

    private sealed class CliUsageException(string message) : Exception(message);
}
