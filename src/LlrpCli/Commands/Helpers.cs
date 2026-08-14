using System.Globalization;
using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Messages;
using LlrpNet.Protocol.Parameters;
using LlrpNet.Protocol.Registry;
using LlrpSdk.Extensions.Impinj;
using LlrpSdk.Extensions.Zebra;

using V101Messages = LlrpNet.Protocol.Messages.V1_0_1;
using V11Messages = LlrpNet.Protocol.Messages.V1_1;
using V20Messages = LlrpNet.Protocol.Messages.V2_0;

namespace LlrpCli.Commands;

public static class Helpers
{
    public static byte[] ParseHex(string value)
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

    public static uint ParseUInt32(string value, string option)
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

    public static LlrpMessageHeader DecodeExactHeader(ReadOnlySpan<byte> frame)
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

    public static LlrpCodecRegistry CreateRegistry()
    {
        var registry = new LlrpCodecRegistry();
        LlrpNet.Protocol.Registry.V1_0_1.V1_0_1ProtocolModule.Register(registry);
        LlrpNet.Protocol.Registry.V1_1.Llrp11StandardModule.Register(registry);
        LlrpNet.Protocol.Registry.V2_0.Llrp20StandardModule.Register(registry);
        ImpinjProtocolModule.Instance.Register(registry);
        ZebraProtocolModule.Instance.Register(registry);
        return registry;
    }

    public static bool TryParseLlrpVersion(string? value, out LlrpProtocolVersion version)
    {
        string normalized = value?.Trim().ToLowerInvariant() ?? "auto";
        version = normalized switch
        {
            "" or "auto" or "1" or "1.0.1" or "1.0" or "101" => LlrpProtocolVersion.Version101,
            "1.1" or "11" => LlrpProtocolVersion.Version11,
            "2" or "2.0" or "20" => LlrpProtocolVersion.Version20,
            _ => (LlrpProtocolVersion)0,
        };

        return version != (LlrpProtocolVersion)0 || normalized is "" or "auto" or "1.0.1" or "1.1" or "2.0";
    }

    public static string ParseRequestedData(LlrpProtocolVersion version, string raw)
    {
        string name = raw.Trim();
        bool defined = version switch
        {
            LlrpProtocolVersion.Version11 => Enum.IsDefined(typeof(LlrpNet.Protocol.Enumerations.V1_1.GetReaderCapabilitiesRequestedData), name),
            LlrpProtocolVersion.Version20 => Enum.IsDefined(typeof(LlrpNet.Protocol.Enumerations.V2_0.GetReaderCapabilitiesRequestedData), name),
            _ => Enum.IsDefined(typeof(LlrpNet.Protocol.Enumerations.V1_0_1.GetReaderCapabilitiesRequestedData), name),
        };
        if (!defined)
        {
            throw new CliUsageException($"'{raw}' is not a valid GET_READER_CAPABILITIES requested-data value.");
        }
        return name;
    }

    public static ILlrpMessage CreateEncodeMessage(
        string messageName,
        LlrpProtocolVersion version,
        uint messageId,
        uint? roSpecId,
        string requestedData)
    {
        string normalized = messageName.ToLowerInvariant();
        static uint Rospec(uint? value, string name) => value ?? throw new CliUsageException($"The encode message '{name}' requires --rospec-id <UInt32>.");

        if (normalized == "keepalive")
        {
            return version switch
            {
                LlrpProtocolVersion.Version11 => new V11Messages.KEEPALIVE(messageId),
                LlrpProtocolVersion.Version20 => new V20Messages.KEEPALIVE(messageId),
                _ => new V101Messages.KEEPALIVE(messageId),
            };
        }
        if (normalized == "keepalive-ack")
        {
            return version switch
            {
                LlrpProtocolVersion.Version11 => new V11Messages.KEEPALIVE_ACK(messageId),
                LlrpProtocolVersion.Version20 => new V20Messages.KEEPALIVE_ACK(messageId),
                _ => new V101Messages.KEEPALIVE_ACK(messageId),
            };
        }
        if (normalized == "get-rospecs")
        {
            return version switch
            {
                LlrpProtocolVersion.Version11 => new V11Messages.GET_ROSPECS(messageId),
                LlrpProtocolVersion.Version20 => new V20Messages.GET_ROSPECS(messageId),
                _ => new V101Messages.GET_ROSPECS(messageId),
            };
        }
        if (normalized == "get-reader-capabilities")
        {
            return version switch
            {
                LlrpProtocolVersion.Version11 => new V11Messages.GET_READER_CAPABILITIES(
                    messageId,
                    Enum.Parse<global::LlrpNet.Protocol.Enumerations.V1_1.GetReaderCapabilitiesRequestedData>(requestedData),
                    Array.Empty<ILlrpParameter>()),
                LlrpProtocolVersion.Version20 => new V20Messages.GET_READER_CAPABILITIES(
                    messageId,
                    Enum.Parse<global::LlrpNet.Protocol.Enumerations.V2_0.GetReaderCapabilitiesRequestedData>(requestedData),
                    Array.Empty<ILlrpParameter>()),
                _ => new V101Messages.GET_READER_CAPABILITIES(
                    messageId,
                    Enum.Parse<global::LlrpNet.Protocol.Enumerations.V1_0_1.GetReaderCapabilitiesRequestedData>(requestedData),
                    Array.Empty<ILlrpParameter>()),
            };
        }
        if (normalized == "delete-rospec") { uint id = Rospec(roSpecId, messageName); return version switch
        {
            LlrpProtocolVersion.Version11 => new V11Messages.DELETE_ROSPEC(messageId, id),
            LlrpProtocolVersion.Version20 => new V20Messages.DELETE_ROSPEC(messageId, id),
            _ => new V101Messages.DELETE_ROSPEC(messageId, id),
        }; }
        if (normalized == "start-rospec") { uint id = Rospec(roSpecId, messageName); return version switch
        {
            LlrpProtocolVersion.Version11 => new V11Messages.START_ROSPEC(messageId, id),
            LlrpProtocolVersion.Version20 => new V20Messages.START_ROSPEC(messageId, id),
            _ => new V101Messages.START_ROSPEC(messageId, id),
        }; }
        if (normalized == "stop-rospec") { uint id = Rospec(roSpecId, messageName); return version switch
        {
            LlrpProtocolVersion.Version11 => new V11Messages.STOP_ROSPEC(messageId, id),
            LlrpProtocolVersion.Version20 => new V20Messages.STOP_ROSPEC(messageId, id),
            _ => new V101Messages.STOP_ROSPEC(messageId, id),
        }; }
        if (normalized == "enable-rospec") { uint id = Rospec(roSpecId, messageName); return version switch
        {
            LlrpProtocolVersion.Version11 => new V11Messages.ENABLE_ROSPEC(messageId, id),
            LlrpProtocolVersion.Version20 => new V20Messages.ENABLE_ROSPEC(messageId, id),
            _ => new V101Messages.ENABLE_ROSPEC(messageId, id),
        }; }
        if (normalized == "disable-rospec") { uint id = Rospec(roSpecId, messageName); return version switch
        {
            LlrpProtocolVersion.Version11 => new V11Messages.DISABLE_ROSPEC(messageId, id),
            LlrpProtocolVersion.Version20 => new V20Messages.DISABLE_ROSPEC(messageId, id),
            _ => new V101Messages.DISABLE_ROSPEC(messageId, id),
        }; }

        throw new CliUsageException($"The encode message '{messageName}' is not supported.");
    }
}

public sealed class CliUsageException(string message) : Exception(message);
