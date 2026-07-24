namespace LlrpCli.Commands;

public sealed record CommandSpec(
    string Name,
    string Usage,
    string Description,
    bool RequiresConnection = false,
    params string[] Aliases);

public sealed record InputAssist(
    IReadOnlyList<string> Candidates,
    string GhostSuffix,
    string Hint)
{
    public static InputAssist Empty { get; } = new([], string.Empty, string.Empty);
}

public static class CommandCatalog
{
    public static IReadOnlyList<CommandSpec> Commands { get; } =
    [
        new("connect", "connect [host] [port]", "Connect to an LLRP Reader."),
        new("disconnect", "disconnect", "Disconnect current Reader session.", RequiresConnection: true),
        new("status", "status", "Show current connection status and metadata."),
        new("caps", "caps", "Query Reader capabilities.", RequiresConnection: true),
        new("inventory", "inventory start [antenna-id] | stop | status", "Manage SDK inventory and display tag reports.", RequiresConnection: true),
        new("rospec", "rospec list|enable|disable|start|stop|delete [id]", "Manage ROSpecs.", RequiresConnection: true),
        new("frames", "frames [count]", "Show recent captured LLRP message frames."),
        new("monitor", "monitor [seconds]", "Stream live received/transmitted LLRP frames.", RequiresConnection: true),
        new("inspect", "inspect <hex>", "Inspect raw hex LLRP header."),
        new("decode", "decode <hex>", "Decode raw hex into parameter tree."),
        new("validate", "validate <hex>", "Validate LLRP frame integrity."),
        new("encode", "encode <message-name> [--message-id ID] [--rospec-id ID]", "Encode standard LLRP message into hex."),
        new("clear", "clear", "Clear console screen.", Aliases: ["cls"]),
        new("help", "help [command]", "Display command help.", Aliases: ["?"]),
        new("quit", "quit", "Exit interactive live shell.", Aliases: ["exit", "q"])
    ];

    public static CommandSpec? FindCommand(string value)
    {
        return Commands.FirstOrDefault(command =>
            command.Name.Equals(value, StringComparison.OrdinalIgnoreCase) ||
            command.Aliases.Contains(value, StringComparer.OrdinalIgnoreCase));
    }

    public static InputAssist Assist(string text, int cursor, bool isConnected)
    {
        cursor = Math.Clamp(cursor, 0, text.Length);
        string prefix = text[..cursor];
        string[] tokens = TokenizePrefix(prefix);
        string currentToken = tokens.Length > 0 && !prefix.EndsWith(' ') ? tokens[^1] : string.Empty;

        var candidates = GetCandidates(tokens, prefix.EndsWith(' '), isConnected, currentToken);
        string ghostSuffix = string.Empty;
        if (cursor == text.Length && !string.IsNullOrWhiteSpace(currentToken) && candidates.Count > 0)
        {
            string bestMatch = candidates[0];
            if (bestMatch.StartsWith(currentToken, StringComparison.OrdinalIgnoreCase))
            {
                ghostSuffix = bestMatch[currentToken.Length..];
            }
        }

        string hint = GetHint(tokens, candidates, isConnected);
        return new InputAssist(candidates, ghostSuffix, hint);
    }

    private static IReadOnlyList<string> GetCandidates(string[] tokens, bool endsWithSpace, bool isConnected, string currentToken)
    {
        if (tokens.Length == 0 || (tokens.Length == 1 && !endsWithSpace))
        {
            return Commands
                .Where(c => !c.RequiresConnection || isConnected)
                .Select(c => c.Name)
                .Where(name => name.StartsWith(currentToken, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        string commandName = tokens[0].ToLowerInvariant();
        if (commandName == "rospec")
        {
            string subToken = tokens.Length > 1 && !endsWithSpace ? tokens[^1] : string.Empty;
            string[] subCommands = ["list", "enable", "disable", "start", "stop", "delete"];
            return subCommands
                .Where(sc => sc.StartsWith(subToken, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (commandName == "inventory")
        {
            string subToken = tokens.Length > 1 && !endsWithSpace ? tokens[^1] : string.Empty;
            string[] subCommands = ["start", "stop", "status"];
            return subCommands
                .Where(sc => sc.StartsWith(subToken, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (commandName == "encode")
        {
            string msgToken = tokens.Length > 1 && !endsWithSpace ? tokens[^1] : string.Empty;
            string[] messages = ["keepalive", "keepalive-ack", "get-reader-capabilities", "get-rospecs", "delete-rospec", "start-rospec", "stop-rospec", "enable-rospec", "disable-rospec"];
            return messages
                .Where(msg => msg.StartsWith(msgToken, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (commandName == "frames")
        {
            return ["10", "20", "50", "100"];
        }

        return Array.Empty<string>();
    }

    private static string GetHint(string[] tokens, IReadOnlyList<string> candidates, bool isConnected)
    {
        if (tokens.Length == 0)
        {
            return "Type command or IP to connect.";
        }

        string commandName = tokens[0].ToLowerInvariant();
        CommandSpec? spec = FindCommand(commandName);
        if (spec != null)
        {
            return $"{spec.Usage} - {spec.Description}";
        }

        if (candidates.Count > 0)
        {
            return $"Candidates: {string.Join(", ", candidates.Take(4))}";
        }

        return string.Empty;
    }

    private static string[] TokenizePrefix(string prefix)
    {
        return prefix.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }
}
