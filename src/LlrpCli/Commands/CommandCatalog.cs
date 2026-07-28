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
            CompletionCandidates =
            [
                "get",
                "defaults",
                "apply",
                "--dry-run",
                "--yes",
                "--antenna",
                "--tx-power",
                "--rx-sens",
                "--channel",
                "--keepalive-type",
                "--keepalive-interval",
                "--gpo-port",
                "--gpo-data",
                "periodic",
                "none",
                "true",
                "false"
            ],
        },
        new("tag", LiveCommandRoute.TagAccess, "tag read|write|lock|kill|erase|sequence <epc> [options]", "Read, write, lock, kill, erase, or sequence tag memory operations.", RequiresConnection: true)
        {
            CompletionCandidates = ["read", "write", "lock", "kill", "erase", "sequence", "--op", "--bank", "--word", "--count", "--data", "--privilege", "--target", "--kill-pwd", "--antenna", "--password", "--timeout", "--yes", "user", "tid", "epc", "reserved", "unlock", "perma-lock", "read:tid:0:2", "write:user:0:1234"],
        },
        new("inventory", LiveCommandRoute.Inventory, "inventory settings show|set|load|save|reset | start [--antennas <id,id|all>] | stop | status", "Manage SDK inventory intent and display tag reports.")
        {
            CompletionCandidates = ["settings", "start", "stop", "status", "show", "set", "load", "save", "reset", "--antennas", "--session", "--population", "--mode", "--tari", "--attach-bank", "--attach-ptr", "--attach-len", "--attach-pwd", "epc", "tid", "user", "reserved", "all", "none"],
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
        new("inspect", LiveCommandRoute.Inspect, "inspect <hex>", "Inspect basic header of an LLRP hexadecimal payload."),
        new("decode", LiveCommandRoute.Decode, "decode <hex>", "Decode LLRP hexadecimal payload into parameter tree."),
        new("validate", LiveCommandRoute.Validate, "validate <hex>", "Validate structural integrity of an LLRP payload."),
        new("encode", LiveCommandRoute.Encode, "encode <message-type-or-json>", "Encode message template to hex."),
        new("monitor", LiveCommandRoute.Monitor, "monitor [duration-sec]", "Monitor live LLRP frames in real-time.", RequiresConnection: true),
        new("clear", LiveCommandRoute.Clear, "clear", "Clear console screen.", Aliases: ["cls"]),
        new("help", LiveCommandRoute.Help, "help [command]", "Show command help or list commands.", Aliases: ["?"]),
        new("exit", LiveCommandRoute.Exit, "exit", "Exit session.", Aliases: ["quit", "q"]),
    ];

    public static CommandSpec? Find(string name)
    {
        return Commands.FirstOrDefault(c =>
            c.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ||
            c.Aliases.Any(a => a.Equals(name, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Returns the <see cref="CommandSpec"/> for the given name, or throws <see cref="InvalidOperationException"/> if not found.
    /// </summary>
    public static CommandSpec Require(string name)
        => Find(name) ?? throw new InvalidOperationException($"Command '{name}' not found in catalog.");

    /// <summary>
    /// Resolves a command name, respecting connection state. Returns false if the command is not
    /// found or requires a connection that is not currently established.
    /// </summary>
    public static bool TryResolve(string name, bool isConnected, out CommandSpec command)
    {
        CommandSpec? found = Find(name);
        if (found is null || (found.RequiresConnection && !isConnected))
        {
            command = default!;
            return false;
        }

        command = found;
        return true;
    }

    /// <summary>
    /// Returns completion suggestions for the current input, taking connection state into account.
    /// The <paramref name="cursor"/> parameter is accepted for API symmetry but not currently used for
    /// intra-token completion; suggestions are based on the token sequence preceding the trailing space.
    /// </summary>
    public static InputAssist Assist(string input, int cursor, bool isConnected)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return InputAssist.Empty;
        }

        string[] parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string firstWord = parts[0].ToLowerInvariant();

        CommandSpec? spec = Find(firstWord);
        if (spec is null)
        {
            var matches = Commands
                .Where(c => c.Name.StartsWith(firstWord, StringComparison.OrdinalIgnoreCase)
                            && (!c.RequiresConnection || isConnected))
                .Select(c => c.Name)
                .ToList();

            if (matches.Count == 1)
            {
                string match = matches[0];
                return new InputAssist([match], match[firstWord.Length..], $"Press Tab to complete '{match}'");
            }

            return new InputAssist(matches, string.Empty, matches.Count > 0 ? $"{matches.Count} matching commands" : string.Empty);
        }

        // Command requires connection but we are disconnected – no assist.
        if (spec.RequiresConnection && !isConnected)
        {
            return InputAssist.Empty;
        }

        if (parts.Length > 1 && spec.CompletionCandidates.Count > 0)
        {
            string lastToken = parts[^1].ToLowerInvariant();
            var matches = spec.CompletionCandidates
                .Where(c => c.StartsWith(lastToken, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (matches.Count == 1)
            {
                string match = matches[0];
                return new InputAssist([match], match[lastToken.Length..], $"Press Tab to complete '{match}'");
            }

            return new InputAssist(matches, string.Empty, matches.Count > 0 ? $"{matches.Count} matching options" : string.Empty);
        }

        // Input ends with whitespace but has no trailing token (e.g. "tag ") – return all candidates.
        if (input.Length > 0 && char.IsWhiteSpace(input[^1]) && spec.CompletionCandidates.Count > 0)
        {
            return new InputAssist(spec.CompletionCandidates.ToList(), string.Empty, $"{spec.CompletionCandidates.Count} matching options");
        }

        return new InputAssist([], string.Empty, spec.Usage);
    }

    public static InputAssist GetAssist(string input)
        => Assist(input, cursor: input.Length, isConnected: true);
}
