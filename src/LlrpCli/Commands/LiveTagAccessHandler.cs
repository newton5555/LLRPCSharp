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
            throw new CliUsageException("Usage: tag read|write|lock|kill|erase|sequence <epc> [options]");
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
                RequireConfirmation(options.Confirm, "tag write");
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

                RequireConfirmation(options.Confirm, "tag lock");
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
                RequireConfirmation(options.Confirm, "tag kill");
                TagAccessResult result = await TagAccessOperations.KillAsync(session.Reader, req, timeout, cancellationToken);
                TagAccessRenderer.RenderOperationResult(console, "KILL", result);
                break;
            }

            case "erase" or "block-erase":
            {
                var options = ParseOptions(tokens, 3);
                TagAccessCliRequest input = TagAccessCliRequest.Create(epc, options.Bank, options.WordPointer, options.AntennaId, options.Password, options.TimeoutSeconds);
                BlockEraseTagRequest req = input.ToBlockEraseRequest(options.WordCount);
                RequireConfirmation(options.Confirm, "tag erase");
                TagAccessResult result = await TagAccessOperations.BlockEraseAsync(session.Reader, req, input.Timeout, cancellationToken);
                TagAccessRenderer.RenderOperationResult(console, "ERASE", result);
                break;
            }

            case "sequence":
            {
                (TagAccessSequenceRequest request, TimeSpan? timeout) = ParseSequenceRequest(epc, tokens, 3);
                if (!tokens.Contains("--yes", StringComparer.OrdinalIgnoreCase) && request.Operations.Any(static operation => operation is not ReadTagRequest))
                {
                    throw new CliUsageException("tag sequence with write, erase, lock, or kill operations requires --yes.");
                }
                TagAccessSequenceResult result = await session.Reader.ExecuteTagAccessSequenceAsync(request, timeout, cancellationToken);
                TagAccessRenderer.RenderSequenceResult(console, result);
                break;
            }

            default:
                throw new CliUsageException("Usage: tag read|write|lock|kill|erase|sequence <epc> [options]");
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

    internal static (TagAccessSequenceRequest Request, TimeSpan? Timeout) ParseSequenceRequest(string epc, string[] tokens, int startIndex)
    {
        string? password = null;
        ushort antenna = 0;
        uint? timeoutSeconds = null;
        var operationSpecs = new List<string>();

        for (int index = startIndex; index < tokens.Length; index += 2)
        {
            if (index + 1 >= tokens.Length)
            {
                throw new CliUsageException($"Missing value for option '{tokens[index]}'.");
            }

            string value = tokens[index + 1];
            switch (tokens[index].ToLowerInvariant())
            {
                case "--op": operationSpecs.Add(value); break;
                case "--password": password = value; break;
                case "--antenna" when ushort.TryParse(value, out ushort parsedAntenna): antenna = parsedAntenna; break;
                case "--timeout" when uint.TryParse(value, out uint parsedTimeout): timeoutSeconds = parsedTimeout; break;
                default: throw new CliUsageException($"Invalid tag sequence option '{tokens[index]}'.");
            }
        }

        if (operationSpecs.Count == 0)
        {
            throw new CliUsageException("tag sequence requires at least one --op <read:...|write:...|erase:...|lock:...|kill:...> value.");
        }

        TagAccessCliRequest baseRequest = TagAccessCliRequest.Create(epc, "epc", 0, antenna, password, timeoutSeconds);
        TagSelection selection = baseRequest.CreateSelection();
        var operations = new List<TagAccessRequest>(operationSpecs.Count);
        foreach (string specification in operationSpecs)
        {
            operations.Add(ParseSequenceOperation(specification, selection, antenna, baseRequest.AccessPassword));
        }

        return (new TagAccessSequenceRequest { Operations = operations }, baseRequest.Timeout);
    }

    private static TagAccessRequest ParseSequenceOperation(
        string specification,
        TagSelection selection,
        ushort antenna,
        uint accessPassword)
    {
        string[] parts = specification.Split(':', StringSplitOptions.TrimEntries);
        string kind = parts[0].ToLowerInvariant();
        return kind switch
        {
            "read" when parts.Length == 4 => new ReadTagRequest
            {
                Selection = selection, AntennaId = antenna, AccessPassword = accessPassword,
                MemoryBank = TagAccessCliRequest.ParseBank(parts[1]), WordPointer = ParseUshort(parts[2], "word"), WordCount = ParseUshort(parts[3], "count")
            },
            "write" when parts.Length == 4 => new WriteTagRequest
            {
                Selection = selection, AntennaId = antenna, AccessPassword = accessPassword,
                MemoryBank = TagAccessCliRequest.ParseBank(parts[1]), WordPointer = ParseUshort(parts[2], "word"), WriteData = TagAccessCliRequest.ParseWords(parts[3])
            },
            "erase" when parts.Length == 4 => new BlockEraseTagRequest
            {
                Selection = selection, AntennaId = antenna, AccessPassword = accessPassword,
                MemoryBank = TagAccessCliRequest.ParseBank(parts[1]), WordPointer = ParseUshort(parts[2], "word"), WordCount = ParseUshort(parts[3], "count")
            },
            "lock" when parts.Length == 3 => CreateSequenceLock(selection, antenna, accessPassword, parts[1], parts[2]),
            "kill" when parts.Length == 2 => new KillTagRequest
            {
                Selection = selection, AntennaId = antenna, KillPassword = TagAccessCliRequest.ParseUInt32Hex(parts[1], "kill password")
            },
            _ => throw new CliUsageException($"Invalid tag sequence operation '{specification}'. Use read:bank:word:count, write:bank:word:hex, erase:bank:word:count, lock:target:privilege, or kill:password."),
        };
    }

    private static LockTagRequest CreateSequenceLock(TagSelection selection, ushort antenna, uint accessPassword, string target, string privilege)
    {
        TagLockMode mode = ParseLockMode(privilege);
        string normalizedTarget = target.ToLowerInvariant();
        return new LockTagRequest
        {
            Selection = selection,
            AntennaId = antenna,
            AccessPassword = accessPassword,
            UserMemoryLockMode = normalizedTarget is "user" or "all" ? mode : TagLockMode.NoChange,
            EpcMemoryLockMode = normalizedTarget is "epc" or "all" ? mode : TagLockMode.NoChange,
            TidMemoryLockMode = normalizedTarget is "tid" or "all" ? mode : TagLockMode.NoChange,
            AccessPasswordLockMode = normalizedTarget is "access-pwd" or "all" ? mode : TagLockMode.NoChange,
            KillPasswordLockMode = normalizedTarget is "kill-pwd" or "all" ? mode : TagLockMode.NoChange,
        };
    }

    private static ushort ParseUshort(string value, string name) => ushort.TryParse(value, out ushort parsed)
        ? parsed
        : throw new CliUsageException($"Sequence {name} must be a UInt16 value.");

    private static void RequireConfirmation(bool confirmed, string command)
    {
        if (!confirmed)
        {
            throw new CliUsageException($"{command} modifies tag state and requires --yes.");
        }
    }

    private static (string Bank, ushort WordPointer, ushort WordCount, ushort AntennaId, string? Password, uint? TimeoutSeconds, string? Data, string? Privilege, string? Target, string? KillPassword, bool DryRun, bool Confirm) ParseOptions(string[] tokens, int startIndex)
    {
        string bank = "user"; ushort word = 0; ushort count = 0; ushort antenna = 0; string? password = null; uint? timeout = null; string? data = null; string? privilege = null; string? target = null; string? killPassword = null; bool dryRun = false; bool confirm = false;

        for (int index = startIndex; index < tokens.Length; index++)
        {
            if (tokens[index].Equals("--dry-run", StringComparison.OrdinalIgnoreCase))
            {
                dryRun = true;
                continue;
            }
            if (tokens[index].Equals("--yes", StringComparison.OrdinalIgnoreCase))
            {
                confirm = true;
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

        return (bank, word, count, antenna, password, timeout, data, privilege, target, killPassword, dryRun, confirm);
    }
}
