using System.Globalization;
using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Registry;
using LlrpNet.Protocol.Registry.V1_0_1;

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
        Llrp101StandardModule.Register(registry);
        return registry;
    }
}

public sealed class CliUsageException(string message) : Exception(message);
