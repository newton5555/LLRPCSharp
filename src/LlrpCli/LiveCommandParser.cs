using LlrpCli.Commands;
using Spectre.Console.Cli;

namespace LlrpCli;

internal static class LiveCommandParser
{
    public static string[] Tokenize(string text)
    {
        var list = new List<string>();
        var sb = new System.Text.StringBuilder();
        bool inQuotes = false;

        foreach (char c in text)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (sb.Length > 0)
                {
                    list.Add(sb.ToString());
                    sb.Clear();
                }
            }
            else
            {
                sb.Append(c);
            }
        }

        if (sb.Length > 0)
        {
            list.Add(sb.ToString());
        }

        return list.ToArray();
    }

    public static CliConnectionOptions ParseConnect(string[] tokens)
    {
        if (tokens.Length < 2)
        {
            throw new CliUsageException(CommandCatalog.Require("connect").Usage);
        }

        string host = tokens[1];
        int port = 5084;
        string llrpVersion = "auto";
        string vendor = "auto";
        int nextToken = 2;

        if (tokens.Length > nextToken && int.TryParse(tokens[nextToken], out int parsedPort))
        {
            port = parsedPort;
            nextToken++;
        }

        while (nextToken < tokens.Length)
        {
            string option = tokens[nextToken];
            if (option.Equals("--llrp", StringComparison.OrdinalIgnoreCase) && nextToken + 1 < tokens.Length)
            {
                llrpVersion = tokens[nextToken + 1];
                nextToken += 2;
            }
            else if (option.Equals("--vendor", StringComparison.OrdinalIgnoreCase) && nextToken + 1 < tokens.Length)
            {
                vendor = tokens[nextToken + 1];
                nextToken += 2;
            }
            else
            {
                throw new CliUsageException(CommandCatalog.Require("connect").Usage);
            }
        }

        if (!CliConnectionOptions.TryCreate(host, port, llrpVersion, vendor, out CliConnectionOptions options, out string error))
        {
            throw new CliUsageException(error);
        }

        return options;
    }
}
