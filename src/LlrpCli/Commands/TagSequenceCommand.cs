using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using LlrpSdk;

namespace LlrpCli.Commands;

/// <summary>
/// Executes a sequence of C1G2 tag access operations through one temporary AccessSpec.
/// </summary>
public sealed class TagSequenceSettings : CommandSettings
{
    [CommandArgument(0, "<HOST>")] public string Host { get; init; } = string.Empty;
    [CommandArgument(1, "<EPC>")] public string Epc { get; init; } = string.Empty;
    [CommandOption("--op <OPERATION>")] public string[] Operations { get; init; } = [];
    [CommandOption("--port <PORT>")] [DefaultValue(5084)] public int Port { get; init; } = 5084;
    [CommandOption("--llrp <VERSION>")] [DefaultValue("auto")] public string LlrpVersion { get; init; } = "auto";
    [CommandOption("--vendor <VENDOR>")] [DefaultValue("auto")] public string Vendor { get; init; } = "auto";
    [CommandOption("--antenna <ID>")] public ushort AntennaId { get; init; }
    [CommandOption("--password <HEX>")] public string? Password { get; init; }
    [CommandOption("--timeout <SECONDS>")] public uint? TimeoutSeconds { get; init; }
    [CommandOption("--yes")] public bool Confirm { get; init; }
}

public sealed class TagSequenceCommand(IAnsiConsole console) : AsyncCommand<TagSequenceSettings>
{
    private readonly IAnsiConsole _console = console ?? AnsiConsole.Console;
    public TagSequenceCommand() : this(AnsiConsole.Console) { }

    protected override async Task<int> ExecuteAsync(CommandContext context, TagSequenceSettings settings, CancellationToken cancellationToken)
    {
        if (!CliConnectionOptions.TryCreate(settings.Host, settings.Port, settings.LlrpVersion, settings.Vendor, out CliConnectionOptions options, out string error))
        {
            throw new CliUsageException(error);
        }

        string[] tokens = BuildParserTokens(settings);
        (TagAccessSequenceRequest request, TimeSpan? timeout) = LiveTagAccessHandler.ParseSequenceRequest(settings.Epc, tokens, 0);
        if (!settings.Confirm && request.Operations.Any(operation => operation is not ReadTagRequest))
        {
            throw new CliUsageException("tag sequence with write, erase, lock, or kill operations requires --yes.");
        }

        await using LlrpReader reader = options.CreateReaderBuilder().WithConnectTimeout(TimeSpan.FromSeconds(5)).Build();
        await reader.ConnectAsync(cancellationToken);
        try
        {
            TagAccessSequenceResult result = await reader.ExecuteTagAccessSequenceAsync(request, timeout, cancellationToken);
            TagAccessRenderer.RenderSequenceResult(_console, result);
            return result.Operations.All(operation => operation.Success) ? 0 : 1;
        }
        finally
        {
            await reader.DisconnectAsync(CancellationToken.None);
        }
    }

    private static string[] BuildParserTokens(TagSequenceSettings settings)
    {
        var tokens = new List<string>();
        foreach (string operation in settings.Operations)
        {
            tokens.Add("--op");
            tokens.Add(operation);
        }
        if (!string.IsNullOrWhiteSpace(settings.Password)) { tokens.Add("--password"); tokens.Add(settings.Password); }
        if (settings.AntennaId != 0) { tokens.Add("--antenna"); tokens.Add(settings.AntennaId.ToString()); }
        if (settings.TimeoutSeconds is { } timeout) { tokens.Add("--timeout"); tokens.Add(timeout.ToString()); }
        return tokens.ToArray();
    }
}
