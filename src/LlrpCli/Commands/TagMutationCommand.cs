using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using LlrpSdk;

namespace LlrpCli.Commands;

/// <summary>Settings shared by the confirmed one-shot standard tag mutation commands.</summary>
public sealed class TagMutationSettings : CommandSettings
{
    [CommandArgument(0, "<HOST>")] public string Host { get; init; } = string.Empty;
    [CommandArgument(1, "<EPC>")] public string Epc { get; init; } = string.Empty;
    [CommandOption("--port <PORT>")] [DefaultValue(5084)] public int Port { get; init; } = 5084;
    [CommandOption("--llrp <VERSION>")] [DefaultValue("auto")] public string LlrpVersion { get; init; } = "auto";
    [CommandOption("--vendor <VENDOR>")] [DefaultValue("auto")] public string Vendor { get; init; } = "auto";
    [CommandOption("--bank <BANK>")] [DefaultValue("user")] public string Bank { get; init; } = "user";
    [CommandOption("--word <ADDRESS>")] public ushort WordPointer { get; init; }
    [CommandOption("--count <WORDS>")] public ushort WordCount { get; init; }
    [CommandOption("--antenna <ID>")] public ushort AntennaId { get; init; }
    [CommandOption("--password <HEX>")] public string? Password { get; init; }
    [CommandOption("--kill-pwd <HEX>")] public string? KillPassword { get; init; }
    [CommandOption("--privilege <MODE>")] [DefaultValue("lock")] public string Privilege { get; init; } = "lock";
    [CommandOption("--target <TARGET>")] [DefaultValue("all")] public string Target { get; init; } = "all";
    [CommandOption("--timeout <SECONDS>")] public uint? TimeoutSeconds { get; init; }
    [CommandOption("--yes")] public bool Confirm { get; init; }
}

/// <summary>Executes lock, kill, or block-erase through the SDK after explicit confirmation.</summary>
public sealed class TagMutationCommand(IAnsiConsole console) : AsyncCommand<TagMutationSettings>
{
    private readonly IAnsiConsole _console = console ?? AnsiConsole.Console;
    public TagMutationCommand() : this(AnsiConsole.Console) { }

    protected override async Task<int> ExecuteAsync(CommandContext context, TagMutationSettings settings, CancellationToken cancellationToken)
    {
        if (!settings.Confirm)
        {
            throw new CliUsageException($"tag {context.Name} modifies or destroys tag state and requires --yes.");
        }
        if (!CliConnectionOptions.TryCreate(settings.Host, settings.Port, settings.LlrpVersion, settings.Vendor, out CliConnectionOptions options, out string error))
        {
            throw new CliUsageException(error);
        }

        TagAccessCliRequest input = TagAccessCliRequest.Create(
            settings.Epc, settings.Bank, settings.WordPointer, settings.AntennaId, settings.Password, settings.TimeoutSeconds);
        await using LlrpReader reader = options.CreateReaderBuilder().WithConnectTimeout(TimeSpan.FromSeconds(5)).Build();
        await reader.ConnectAsync(cancellationToken);
        try
        {
            (string label, TagAccessResult result) = context.Name.ToLowerInvariant() switch
            {
                "lock" => ("LOCK", await TagAccessOperations.LockAsync(reader, CreateLockRequest(input, settings), input.Timeout, cancellationToken)),
                "erase" => ("ERASE", await TagAccessOperations.BlockEraseAsync(reader, input.ToBlockEraseRequest(settings.WordCount), input.Timeout, cancellationToken)),
                "kill" => ("KILL", await TagAccessOperations.KillAsync(reader, new KillTagRequest
                {
                    Selection = input.CreateSelection(),
                    AntennaId = settings.AntennaId,
                    KillPassword = string.IsNullOrWhiteSpace(settings.KillPassword)
                        ? 0
                        : TagAccessCliRequest.ParseUInt32Hex(settings.KillPassword, "--kill-pwd"),
                }, input.Timeout, cancellationToken)),
                _ => throw new InvalidOperationException($"Unsupported tag mutation command '{context.Name}'."),
            };
            TagAccessRenderer.RenderOperationResult(_console, label, result);
            return result.Operation.Success ? 0 : 1;
        }
        finally
        {
            await reader.DisconnectAsync(CancellationToken.None);
        }
    }

    private static LockTagRequest CreateLockRequest(TagAccessCliRequest input, TagMutationSettings settings)
    {
        TagLockMode mode = ParseLockMode(settings.Privilege);
        string target = settings.Target.ToLowerInvariant();
        return new LockTagRequest
        {
            Selection = input.CreateSelection(),
            AntennaId = settings.AntennaId,
            AccessPassword = input.AccessPassword,
            UserMemoryLockMode = target is "user" or "all" ? mode : TagLockMode.NoChange,
            EpcMemoryLockMode = target is "epc" or "all" ? mode : TagLockMode.NoChange,
            TidMemoryLockMode = target is "tid" or "all" ? mode : TagLockMode.NoChange,
            AccessPasswordLockMode = target is "access-pwd" or "all" ? mode : TagLockMode.NoChange,
            KillPasswordLockMode = target is "kill-pwd" or "all" ? mode : TagLockMode.NoChange,
        };
    }

    private static TagLockMode ParseLockMode(string privilege) => privilege.ToLowerInvariant() switch
    {
        "accessible" or "unlock" => TagLockMode.Accessible,
        "always-accessible" or "perma-unlock" => TagLockMode.AlwaysAccessible,
        "secured" or "lock" => TagLockMode.SecuredWrite,
        "perma-lock" or "always-not-writable" => TagLockMode.AlwaysNotWritable,
        "no-change" => TagLockMode.NoChange,
        _ => throw new CliUsageException("Privilege mode must be unlock, perma-unlock, lock, perma-lock, or no-change."),
    };
}
