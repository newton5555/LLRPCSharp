using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using LlrpSdk;

namespace LlrpCli.Commands;

public sealed class TagWriteDryRunSettings : CommandSettings
{
    [CommandArgument(0, "<HOST>")] public string Host { get; init; } = string.Empty;
    [CommandArgument(1, "<EPC>")] public string Epc { get; init; } = string.Empty;
    [CommandOption("--port <PORT>")] [DefaultValue(5084)] public int Port { get; init; } = 5084;
    [CommandOption("--llrp <VERSION>")] [DefaultValue("auto")] public string LlrpVersion { get; init; } = "auto";
    [CommandOption("--vendor <VENDOR>")] [DefaultValue("auto")] public string Vendor { get; init; } = "auto";
    [CommandOption("--bank <BANK>")] [DefaultValue("user")] public string Bank { get; init; } = "user";
    [CommandOption("--word <ADDRESS>")] public ushort WordPointer { get; init; }
    [CommandOption("--data <HEX_WORDS>")] public string Data { get; init; } = string.Empty;
    [CommandOption("--antenna <ID>")] public ushort AntennaId { get; init; }
    [CommandOption("--password <HEX>")] public string? Password { get; init; }
    [CommandOption("--timeout <SECONDS>")] public uint? TimeoutSeconds { get; init; }
    [CommandOption("--yes")] public bool Confirm { get; init; }
}

public sealed class TagWriteDryRunCommand(IAnsiConsole console) : AsyncCommand<TagWriteDryRunSettings>
{
    private readonly IAnsiConsole _console = console ?? AnsiConsole.Console;
    public TagWriteDryRunCommand() : this(AnsiConsole.Console) { }

    protected override async Task<int> ExecuteAsync(CommandContext context, TagWriteDryRunSettings settings, CancellationToken cancellationToken)
    {
        TagAccessCliRequest input = TagAccessCliRequest.Create(settings.Epc, settings.Bank, settings.WordPointer, settings.AntennaId, settings.Password, settings.TimeoutSeconds);
        WriteTagRequest request = input.ToWriteRequest(TagAccessCliRequest.ParseWords(settings.Data));
        if (!settings.Confirm)
        {
            TagAccessRenderer.RenderWriteDryRun(_console, request);
            return 0;
        }

        if (!CliConnectionOptions.TryCreate(settings.Host, settings.Port, settings.LlrpVersion, settings.Vendor, out CliConnectionOptions options, out string error))
        {
            throw new CliUsageException(error);
        }

        await using LlrpReader reader = options.CreateReaderBuilder().WithConnectTimeout(TimeSpan.FromSeconds(5)).Build();
        await reader.ConnectAsync(cancellationToken);
        try
        {
            TagAccessResult result = await TagAccessOperations.WriteAsync(reader, request, input.Timeout, cancellationToken);
            TagAccessRenderer.RenderOperationResult(_console, "WRITE", result);
            return result.Operation.Success ? 0 : 1;
        }
        finally
        {
            await reader.DisconnectAsync(CancellationToken.None);
        }
    }
}
