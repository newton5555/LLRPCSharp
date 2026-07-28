using Spectre.Console;
using LlrpSdk;

namespace LlrpCli.Commands;

internal sealed class LiveTagAccessHandler(IAnsiConsole console, LiveSessionContext session)
{
    public async Task HandleAsync(string[] tokens, CancellationToken cancellationToken)
    {
        if (session.Reader is null || !session.Reader.IsConnected)
        {
            console.MarkupLine("[yellow]Not connected. Run 'connect <host>' first.[/]");
            return;
        }

        if (tokens.Length < 3)
        {
            throw new CliUsageException("Usage: tag read|write|lock|kill|erase <epc> [options]");
        }

        string action = tokens[1].ToLowerInvariant();
        string epc = tokens[2];

        switch (action)
        {
            case "read":
            {
                var options = ParseOptions(tokens, 3);
                TagAccessCliRequest input = TagAccessCliRequest.Create(epc, options.Bank, options.WordPointer, options.AntennaId, options.Password, options.TimeoutSeconds);
                ReadTagRequest req = input.ToReadRequest(options.WordCount);
                TagAccessResult result = await TagAccessOperations.ReadAsync(session.Reader, req, input.Timeout, cancellationToken);
                TagAccessRenderer.RenderReadResult(console, result);
                break;
            }

            case "write":
            {
                var options = ParseOptions(tokens, 3);
                TagAccessCliRequest input = TagAccessCliRequest.Create(epc, options.Bank, options.WordPointer, options.AntennaId, options.Password, options.TimeoutSeconds);
                WriteTagRequest req = input.ToWriteRequest(TagAccessCliRequest.ParseWords(options.Data ?? string.Empty));
                if (options.DryRun)
                {
                    TagAccessRenderer.RenderWriteDryRun(console, req);
                    return;
                }
                TagAccessResult result = await TagAccessOperations.WriteAsync(session.Reader, req, input.Timeout, cancellationToken);
                TagAccessRenderer.RenderOperationResult(console, "WRITE", result);
                break;
            }

            case "lock":
            {
                var options = ParseOptions(tokens, 3);
                TagAccessCliRequest input = TagAccessCliRequest.Create(epc, "user", 0, options.AntennaId, options.Password, options.TimeoutSeconds);
                TagLockMode lockMode = ParseLockMode(options.Privilege);
                string target = (options.Target ?? "all").ToLowerInvariant();

                LockTagRequest req = new()
                {
                    Selection = input.CreateSelection(),
                    AntennaId = options.AntennaId,
                    AccessPassword = input.AccessPassword,
                    UserMemoryLockMode = target is "user" or "all" ? lockMode : TagLockMode.NoChange,
                    EpcMemoryLockMode = target is "epc" or "all" ? lockMode : TagLockMode.NoChange,
                    TidMemoryLockMode = target is "tid" or "all" ? lockMode : TagLockMode.NoChange,
                    AccessPasswordLockMode = target is "access-pwd" or "all" ? lockMode : TagLockMode.NoChange,
                    KillPasswordLockMode = target is "kill-pwd" or "all" ? lockMode : TagLockMode.NoChange,
                };

                TagAccessResult result = await TagAccessOperations.LockAsync(session.Reader, req, input.Timeout, cancellationToken);
                TagAccessRenderer.RenderOperationResult(console, "LOCK", result);
                break;
            }

            case "kill":
            {
                var options = ParseOptions(tokens, 3);
                uint killPassword = string.IsNullOrWhiteSpace(options.KillPassword) ? 0 : TagAccessCliRequest.ParseUInt32Hex(options.KillPassword, "--kill-pwd");
                byte[] epcBytes = TagAccessCliRequest.ParseHex(epc, "EPC");

                KillTagRequest req = new()
                {
                    Selection = new TagSelection
                    {
                        MemoryBank = TagMemoryBank.ElectronicProductCode,
                        BitPointer = 32,
                        BitLength = checked((ushort)(epcBytes.Length * 8)),
                        Mask = epcBytes,
                        Data = epcBytes,
                    },
                    AntennaId = options.AntennaId,
                    KillPassword = killPassword,
                };

                TimeSpan? timeout = options.TimeoutSeconds is null ? null : TimeSpan.FromSeconds(options.TimeoutSeconds.Value);
                TagAccessResult result = await TagAccessOperations.KillAsync(session.Reader, req, timeout, cancellationToken);
                TagAccessRenderer.RenderOperationResult(console, "KILL", result);
                break;
            }

            case "erase" or "block-erase":
            {
                var options = ParseOptions(tokens, 3);
                TagAccessCliRequest input = TagAccessCliRequest.Create(epc, options.Bank, options.WordPointer, options.AntennaId, options.Password, options.TimeoutSeconds);
                BlockEraseTagRequest req = input.ToBlockEraseRequest(options.WordCount);
                TagAccessResult result = await TagAccessOperations.BlockEraseAsync(session.Reader, req, input.Timeout, cancellationToken);
                TagAccessRenderer.RenderOperationResult(console, "ERASE", result);
                break;
            }

            default:
                throw new CliUsageException("Usage: tag read|write|lock|kill|erase <epc> [options]");
        }
    }

    private static TagLockMode ParseLockMode(string? privilege) => (privilege ?? "lock").ToLowerInvariant() switch
    {
        "accessible" or "unlock" => TagLockMode.Accessible,
        "always-accessible" or "perma-unlock" => TagLockMode.AlwaysAccessible,
        "secured" or "lock" => TagLockMode.SecuredWrite,
        "perma-lock" or "always-not-writable" => TagLockMode.AlwaysNotWritable,
        "no-change" => TagLockMode.NoChange,
        _ => throw new CliUsageException("Privilege mode must be unlock, perma-unlock, lock, perma-lock, or no-change.")
    };

    private static (string Bank, ushort WordPointer, ushort WordCount, ushort AntennaId, string? Password, uint? TimeoutSeconds, string? Data, string? Privilege, string? Target, string? KillPassword, bool DryRun) ParseOptions(string[] tokens, int startIndex)
    {
        string bank = "user"; ushort word = 0; ushort count = 0; ushort antenna = 0; string? password = null; uint? timeout = null; string? data = null; string? privilege = null; string? target = null; string? killPassword = null; bool dryRun = false;

        for (int index = startIndex; index < tokens.Length; index++)
        {
            if (tokens[index].Equals("--dry-run", StringComparison.OrdinalIgnoreCase))
            {
                dryRun = true;
                continue;
            }

            if (index + 1 >= tokens.Length)
            {
                throw new CliUsageException($"Missing value for option '{tokens[index]}'.");
            }

            string value = tokens[index + 1];
            switch (tokens[index].ToLowerInvariant())
            {
                case "--bank": bank = value; index++; break;
                case "--word" when ushort.TryParse(value, out word): index++; break;
                case "--count" when ushort.TryParse(value, out count): index++; break;
                case "--antenna" when ushort.TryParse(value, out antenna): index++; break;
                case "--password": password = value; index++; break;
                case "--timeout" when uint.TryParse(value, out uint seconds): timeout = seconds; index++; break;
                case "--data": data = value; index++; break;
                case "--privilege": privilege = value; index++; break;
                case "--target": target = value; index++; break;
                case "--kill-pwd": killPassword = value; index++; break;
                default: throw new CliUsageException($"Invalid option '{tokens[index]}'.");
            }
        }

        return (bank, word, count, antenna, password, timeout, data, privilege, target, killPassword, dryRun);
    }
}
