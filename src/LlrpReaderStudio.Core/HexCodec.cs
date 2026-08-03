using System.Globalization;

namespace LlrpReaderStudio.Core;

public static class HexCodec
{
    public static byte[] ParseBytes(string? value)
    {
        string normalized = Normalize(value);
        if ((normalized.Length & 1) != 0)
        {
            throw new FormatException("Hexadecimal byte data must contain an even number of digits.");
        }

        return Convert.FromHexString(normalized);
    }

    public static ushort[] ParseWords(string? value)
    {
        string normalized = Normalize(value);
        if ((normalized.Length & 3) != 0)
        {
            throw new FormatException("Tag write data must contain complete 16-bit words (four hex digits each).");
        }

        var words = new ushort[normalized.Length / 4];
        for (int index = 0; index < words.Length; index++)
        {
            words[index] = ushort.Parse(
                normalized.AsSpan(index * 4, 4),
                NumberStyles.AllowHexSpecifier,
                CultureInfo.InvariantCulture);
        }

        return words;
    }

    public static string FormatBytes(ReadOnlyMemory<byte> value) => Convert.ToHexString(value.Span);

    public static string FormatWords(IEnumerable<ushort> words) =>
        string.Concat(words.Select(static word => word.ToString("X4", CultureInfo.InvariantCulture)));

    private static string Normalize(string? value)
    {
        return new string((value ?? string.Empty)
            .Where(static character => !char.IsWhiteSpace(character) && character is not '-' and not ':')
            .ToArray());
    }
}
