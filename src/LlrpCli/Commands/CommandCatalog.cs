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
    Settings,
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
        new("connect", LiveCommandRoute.Connect, "connect [host] [port] [--llrp auto|1.0.1|1.1] [--vendor auto|impinj|seuic|none]", "Connect to an LLRP Reader.")
        {
            CompletionCandidates = ["--llrp", "--vendor", "auto", "1.0.1", "1.1", "impinj", "seuic", "none"],
        },
        new("disconnect", LiveCommandRoute.Disconnect, "disconnect", "Disconnect current Reader session.", RequiresConnection: true),
        new("status", LiveCommandRoute.Status, "status [--full]", "Show session status; --full refreshes managed settings and resources.")
        {
            CompletionCandidates = ["--full"],
        },
        new("caps", LiveCommandRoute.Capabilities, "caps [--raw|--json]", "Refresh reader capabilities; optionally show raw protocol data or JSON.", RequiresConnection: true)
        {
            CompletionCandidates = ["--raw", "--json"],
        },
        new("settings", LiveCommandRoute.Settings, LiveSettingsHandler.Usage, "Show, validate, apply, edit, or save ReaderSettings; use --replace-all for explicit destructive takeover.", RequiresConnection: true)
        {
            CompletionCandidates = ["show", "edit", "validate", "apply", "save", "--json", "--raw", "--yes", "--from", "--defaults", "--replace-all"],
        },
        new("tag", LiveCommandRoute.TagAccess, "tag read|write|lock|kill|erase|sequence <epc> [options]", "Read, write, lock, kill, erase, or sequence tag memory operations.", RequiresConnection: true)
        {
            CompletionCandidates = ["read", "write", "lock", "kill", "erase", "sequence", "--op", "--read", "--write", "--erase", "--lock", "--kill", "--bank", "--word", "--count", "--data", "--privilege", "--target", "--kill-pwd", "--antenna", "--password", "--timeout", "--yes", "user", "tid", "epc", "reserved", "unlock", "perma-lock", "read:tid:0:2", "write:user:0:1234"],
        },
        new("inventory", LiveCommandRoute.Inventory, "inventory start [--defaults|--settings <file>] [--replace-all] [--monitor live|frames|none] [--monitor-duration seconds] | stop | status [--refresh]", "Control managed inventory; --replace-all explicitly replaces foreign resources.")
        {
            CompletionCandidates = ["start", "stop", "status", "--defaults", "--settings", "--replace-all", "--monitor", "--monitor-duration", "--refresh", "live", "frames", "none", "30", "60"],
        },
        new("rospec", LiveCommandRoute.RoSpec, "rospec add [--id n] [AISpec options] | list|enable|disable|start|stop|delete [id]", "Expert ROSpec protocol resources; writes are available whenever connected.", RequiresConnection: true)
        {
            CompletionCandidates = ["add", "list", "enable", "disable", "start", "stop", "delete", "--id", "--antennas", "--mode", "--tari", "--session", "--population", "all"],
        },
        new("accessspec", LiveCommandRoute.AccessSpec, "accessspec list|enable|disable|delete [id]", "Expert AccessSpec protocol resources; writes are available whenever connected.", RequiresConnection: true)
        {
            CompletionCandidates = ["list", "enable", "disable", "delete"],
        },
        new("raw", LiveCommandRoute.Raw, "raw send|transact <hex> [--response-type type] --yes", "Send an exact LLRP frame; resource observation may become stale while DesiredState is retained.", RequiresConnection: true),
        new("sync", LiveCommandRoute.Synchronize, "sync", "Refresh the device ROSpec/AccessSpec snapshot without clearing DesiredState.", RequiresConnection: true),
        new("frames", LiveCommandRoute.Frames, "frames [count]", "Show recent captured LLRP message frames.")
        {
            CompletionCandidates = ["10", "20", "50", "100"],
        },
        new("inspect", LiveCommandRoute.Inspect, "inspect <hex>", "Inspect basic header of an LLRP hexadecimal payload."),
        new("decode", LiveCommandRoute.Decode, "decode <hex-or-pcapng> [--output text|summary|json] [--message-type NUMBER]", "Decode an LLRP hex frame or a .pcapng capture file into a parameter tree; --message-type filters by command code."),
        new("validate", LiveCommandRoute.Validate, "validate <hex>", "Validate structural integrity of an LLRP payload."),
        new("encode", LiveCommandRoute.Encode, "encode <message-type-or-json>", "Encode message template to hex."),
        new("monitor", LiveCommandRoute.Monitor, "monitor [live|frames] [duration-sec] [--type MessageName]", "Foreground monitor for tags or raw LLRP frames; Ctrl+C returns to the prompt.", RequiresConnection: true)
        {
            CompletionCandidates = ["live", "frames", "none", "--type", "KEEPALIVE", "RO_ACCESS_REPORT", "GET_READER_CAPABILITIES_RESPONSE", "GET_READER_CONFIG_RESPONSE", "KEEPALIVE_ACK"],
        },
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
            return isConnected
                ? new InputAssist(["status", "caps", "inventory", "settings"], "status", "Next: status — inspect the connected reader · Commands: status, caps, inventory, settings · Tab/→ accepts")
                : new InputAssist(["connect", "inspect", "decode", "validate", "encode", "help"], "connect", "Next: connect <host> — establish an LLRP session · Commands: connect, inspect, decode, validate, encode, help · Tab/→ accepts");
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

            return new InputAssist(matches, string.Empty, BuildCandidateHint("Commands", matches));
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

            return new InputAssist(matches, string.Empty, BuildCandidateHint("Options", matches));
        }

        // Input ends with whitespace but has no trailing token (e.g. "tag ") – return all candidates.
        if (input.Length > 0 && char.IsWhiteSpace(input[^1]) && spec.CompletionCandidates.Count > 0)
        {
            return new InputAssist(spec.CompletionCandidates.ToList(), string.Empty, BuildCandidateHint("Options", spec.CompletionCandidates));
        }

        return new InputAssist([], string.Empty, spec.Usage);
    }

    private static string BuildCandidateHint(string label, IReadOnlyList<string> candidates)
    {
        if (candidates.Count == 0)
        {
            return string.Empty;
        }

        const int displayedCandidateLimit = 8;
        string visibleCandidates = string.Join(", ", candidates.Take(displayedCandidateLimit));
        string remaining = candidates.Count > displayedCandidateLimit ? $", +{candidates.Count - displayedCandidateLimit}" : string.Empty;
        return $"{label}: {visibleCandidates}{remaining} · Tab cycles · Shift+Tab reverses";
    }

    public static InputAssist GetAssist(string input)
        => Assist(input, cursor: input.Length, isConnected: true);
}
