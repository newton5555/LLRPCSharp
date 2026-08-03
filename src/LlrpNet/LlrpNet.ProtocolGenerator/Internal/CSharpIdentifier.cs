using System.Globalization;
using System.Text;

namespace LlrpNet.ProtocolGenerator.Internal;

internal static class CSharpIdentifier
{
    private static readonly IReadOnlySet<string> Keywords = new HashSet<string>(StringComparer.Ordinal)
    {
        "abstract", "add", "alias", "and", "ascending", "as", "async", "await", "base", "bool",
        "break", "by", "byte", "case", "catch", "char", "checked", "class", "const", "continue",
        "decimal", "default", "delegate", "descending", "do", "double", "dynamic", "else", "enum",
        "equals", "event", "explicit", "extern", "false", "file", "finally", "fixed", "float", "for",
        "foreach", "from", "get", "global", "goto", "group", "if", "implicit", "in", "init", "int",
        "interface", "internal", "into", "is", "join", "let", "lock", "long", "managed", "nameof",
        "namespace", "new", "nint", "not", "notnull", "nuint", "null", "object", "on", "operator",
        "or", "orderby", "out", "override", "params", "partial", "private", "protected", "public",
        "readonly", "record", "ref", "remove", "required", "return", "sbyte", "scoped", "sealed",
        "select", "set", "short", "sizeof", "stackalloc", "static", "string", "struct", "switch",
        "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked", "unmanaged", "unsafe",
        "ushort", "using", "value", "var", "virtual", "void", "volatile", "when", "where", "while",
        "with", "yield",
    };

    public static string Normalize(string value, string fallback)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentException.ThrowIfNullOrWhiteSpace(fallback);

        var builder = new StringBuilder(value.Length + 1);
        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];
            if (index == 0 && !IsIdentifierStart(character) && IsIdentifierPart(character))
            {
                builder.Append('_');
                builder.Append(character);
            }
            else
            {
                bool accepted = index == 0 ? IsIdentifierStart(character) : IsIdentifierPart(character);
                builder.Append(accepted ? character : '_');
            }
        }

        if (builder.Length == 0)
        {
            builder.Append(fallback);
        }
        else if (!IsIdentifierStart(builder[0]))
        {
            builder.Insert(0, '_');
        }

        string identifier = builder.ToString();
        return Keywords.Contains(identifier) ? $"@{identifier}" : identifier;
    }

    public static bool TryEscapeQualifiedName(string value, out string escaped)
    {
        escaped = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string[] segments = value.Split('.');
        if (segments.Any(static segment => !IsValidUnescapedIdentifier(segment)))
        {
            return false;
        }

        escaped = string.Join(
            ".",
            segments.Select(static segment => Keywords.Contains(segment) ? $"@{segment}" : segment));
        return true;
    }

    public static string WithoutEscapePrefix(string identifier)
    {
        return identifier.Length > 0 && identifier[0] == '@' ? identifier[1..] : identifier;
    }

    private static bool IsValidUnescapedIdentifier(string value)
    {
        return value.Length > 0 && IsIdentifierStart(value[0]) && value.Skip(1).All(IsIdentifierPart);
    }

    private static bool IsIdentifierStart(char value)
    {
        UnicodeCategory category = char.GetUnicodeCategory(value);
        return value == '_' || category is UnicodeCategory.UppercaseLetter
            or UnicodeCategory.LowercaseLetter
            or UnicodeCategory.TitlecaseLetter
            or UnicodeCategory.ModifierLetter
            or UnicodeCategory.OtherLetter
            or UnicodeCategory.LetterNumber;
    }

    private static bool IsIdentifierPart(char value)
    {
        UnicodeCategory category = char.GetUnicodeCategory(value);
        return IsIdentifierStart(value) || category is UnicodeCategory.DecimalDigitNumber
            or UnicodeCategory.ConnectorPunctuation
            or UnicodeCategory.NonSpacingMark
            or UnicodeCategory.SpacingCombiningMark
            or UnicodeCategory.Format;
    }
}

internal sealed class CSharpIdentifierAllocator
{
    private readonly HashSet<string> allocated = new(StringComparer.Ordinal);

    public CSharpIdentifierAllocator(IEnumerable<string>? reserved = null)
    {
        if (reserved is null)
        {
            return;
        }

        foreach (string identifier in reserved)
        {
            allocated.Add(CSharpIdentifier.WithoutEscapePrefix(identifier));
        }
    }

    public string Allocate(string sourceName, string fallback)
    {
        string normalized = CSharpIdentifier.Normalize(sourceName, fallback);
        string unescaped = CSharpIdentifier.WithoutEscapePrefix(normalized);
        string candidate = unescaped;
        int suffix = 2;
        while (!allocated.Add(candidate))
        {
            candidate = $"{unescaped}_{suffix}";
            suffix++;
        }

        return normalized[0] == '@' && candidate == unescaped ? $"@{candidate}" : candidate;
    }
}
