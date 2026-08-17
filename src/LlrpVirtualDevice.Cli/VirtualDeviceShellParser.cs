using System.Text;

namespace LlrpVirtualDevice.Cli;

internal static class VirtualDeviceShellParser
{
    public static string[] Tokenize(string line)
    {
        ArgumentNullException.ThrowIfNull(line);

        var tokens = new List<string>();
        var token = new StringBuilder();
        char quote = '\0';
        bool escaped = false;

        foreach (char character in line)
        {
            if (escaped)
            {
                token.Append(character);
                escaped = false;
                continue;
            }

            if (character == '\\' && quote != '\'')
            {
                escaped = true;
                continue;
            }

            if (quote != '\0')
            {
                if (character == quote)
                {
                    quote = '\0';
                }
                else
                {
                    token.Append(character);
                }

                continue;
            }

            if (character is '\'' or '"')
            {
                quote = character;
            }
            else if (char.IsWhiteSpace(character))
            {
                AddToken(tokens, token);
            }
            else
            {
                token.Append(character);
            }
        }

        if (escaped)
        {
            token.Append('\\');
        }

        if (quote != '\0')
        {
            throw new ArgumentException("The command contains an unterminated quoted value.");
        }

        AddToken(tokens, token);
        return tokens.ToArray();
    }

    private static void AddToken(List<string> tokens, StringBuilder token)
    {
        if (token.Length == 0)
        {
            return;
        }

        tokens.Add(token.ToString());
        token.Clear();
    }
}
