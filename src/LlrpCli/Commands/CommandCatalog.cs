namespace LlrpCli.Commands;

public enum LiveCommandRoute
{
    Connect,
    Disconnect,
    Status,
    Capabilities,
    Inventory,
    Monitor,
    Frames,
    RoSpec,
    AccessSpec,
    Configuration,
    TagAccess,
    Raw,
    Synchronize,
    Inspect,
    Decode,
    Validate,
    Encode,
    Clear,
    Help,
    Exit,
}

public sealed record CommandSpec(
    string Name,
    LiveCommandRoute Route,
    string Usage,
    string Description,
    bool RequiresConnection = false,
    params string[] Aliases)
{
    public IReadOnlyList<string> CompletionCandidates { get; init; } = [];
}

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
        new("connect", LiveCommandRoute.Connect, "connect [host] [port] [--llrp auto|1.0.1|1.1] [--vendor auto|impinj|none]", "Connect to an LLRP Reader.")
        {
            CompletionCandidates = ["--llrp", "--vendor", "auto", "1.0.1", "1.1", "impinj", "none"],
        },
        new("disconnect", LiveCommandRoute.Disconnect, "disconnect", "Disconnect current Reader session.", RequiresConnection: true),
        new("status", LiveCommandRoute.Status, "status", "Show current connection status and metadata."),
        new("caps", LiveCommandRoute.Capabilities, "caps", "Query Reader capabilities.", RequiresConnection: true),
        new("config", LiveCommandRoute.Configuration, "config get | defaults | apply [options] [--dry-run] --yes", "Query, resolve defaults, or safely apply reader configuration.", RequiresConnection: true)
        {
            CompletionCandidates = ["get", "defaults", "apply", "--dry-run", "--yes"],
        },
        new("tag", LiveCommandRoute.TagAccess, "tag read|write <epc> --bank <bank> --word <address> (--count <words>|--data <hex-words>)", "Read tag memory or inspect a write request.", RequiresConnection: true)
        {
            CompletionCandidates = ["read", "write", "--bank", "--word", "--count", "--data", "--antenna", "--password", "--timeout", "user", "tid", "epc", "reserved"],
        },
        new("inventory", LiveCommandRoute.Inventory, "inventory start [antenna-id] | stop | status", "Manage SDK inventory and display tag reports.", RequiresConnection: true)
        {
            CompletionCandidates = ["start", "stop", "status"],
        },
        new("rospec", LiveCommandRoute.RoSpec, "rospec add|list|enable|disable|start|stop|delete [id]", "Manage ROSpecs.", RequiresConnection: true)
        {
            CompletionCandidates = ["list", "enable", "disable", "start", "stop", "delete"],
        },
        new("accessspec", LiveCommandRoute.AccessSpec, "accessspec list|enable|disable|delete [id]", "Manage AccessSpecs.", RequiresConnection: true)
        {
            CompletionCandidates = ["list", "enable", "disable", "delete"],
        },
        new("raw", LiveCommandRoute.Raw, "raw send|transact <hex> [--response-type type] --yes", "Send an exact LLRP frame.", RequiresConnection: true),
        new("sync", LiveCommandRoute.Synchronize, "sync", "Synchronize SDK-managed resource state after raw access.", RequiresConnection: true),
        new("frames", LiveCommandRoute.Frames, "frames [count]", "Show recent captured LLRP message frames.")
        {
            CompletionCandidates = ["10", "20", "50", "100"],
        },
        new("monitor", LiveCommandRoute.Monitor, "monitor [seconds]", "Stream live received/transmitted LLRP frames.", RequiresConnection: true)
        {
            CompletionCandidates = ["10", "30", "60", "--frames", "--table"],
        },
        new("inspect", LiveCommandRoute.Inspect, "inspect <hex>", "Inspect raw hex LLRP header."),
        new("decode", LiveCommandRoute.Decode, "decode <hex>", "Decode raw hex into parameter tree."),
        new("validate", LiveCommandRoute.Validate, "validate <hex>", "Validate LLRP frame integrity."),
        new("encode", LiveCommandRoute.Encode, "encode <message-name> [--message-id ID] [--rospec-id ID]", "Encode standard LLRP message into hex.")
        {
            CompletionCandidates =
            [
                "keepalive",
                "keepalive-ack",
                "get-reader-capabilities",
                "get-rospecs",
                "delete-rospec",
                "start-rospec",
                "stop-rospec",
                "enable-rospec",
                "disable-rospec",
            ],
        },
        new("clear", LiveCommandRoute.Clear, "clear", "Clear console screen.", Aliases: ["cls"]),
        new("help", LiveCommandRoute.Help, "help [command]", "Display command help.", Aliases: ["?"]),
        new("quit", LiveCommandRoute.Exit, "quit", "Exit interactive live shell.", Aliases: ["exit", "q"]),
    ];

    public static CommandSpec Require(string value)
    {
        return FindCommand(value) ?? throw new InvalidOperationException($"Command '{value}' is not registered.");
    }

    public static CommandSpec? FindCommand(string value)
    {
        return Commands.FirstOrDefault(command =>
            command.Name.Equals(value, StringComparison.OrdinalIgnoreCase) ||
            command.Aliases.Contains(value, StringComparer.OrdinalIgnoreCase));
    }

    public static bool TryResolve(string value, bool isConnected, out CommandSpec command)
    {
        CommandSpec? resolved = FindCommand(value);
        if (resolved is null || (resolved.RequiresConnection && !isConnected))
        {
            command = null!;
            return false;
        }

        command = resolved;
        return true;
    }

    public static InputAssist Assist(string text, int cursor, bool isConnected)
    {
        cursor = Math.Clamp(cursor, 0, text.Length);
        string prefix = text[..cursor];
        string[] tokens = TokenizePrefix(prefix);
        string currentToken = tokens.Length > 0 && !prefix.EndsWith(' ') ? tokens[^1] : string.Empty;

        IReadOnlyList<string> candidates = GetCandidates(tokens, prefix.EndsWith(' '), isConnected, currentToken);
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
                .Where(command => !command.RequiresConnection || isConnected)
                .Select(command => command.Name)
                .Where(name => name.StartsWith(currentToken, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        CommandSpec? command = FindCommand(tokens[0]);
        if (command is not null)
        {
            string argumentToken = tokens.Length > 1 && !endsWithSpace ? tokens[^1] : string.Empty;
            return command.CompletionCandidates
                .Where(candidate => candidate.StartsWith(argumentToken, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        return Array.Empty<string>();
    }

    private static string GetHint(string[] tokens, IReadOnlyList<string> candidates, bool isConnected)
    {
        if (tokens.Length == 0)
        {
            return "Type command or IP to connect.";
        }

        CommandSpec? spec = FindCommand(tokens[0]);
        if (spec is not null)
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
