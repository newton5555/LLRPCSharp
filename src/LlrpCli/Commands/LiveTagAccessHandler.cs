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

        if (tokens.Length < 3 || (tokens[1] is not "read" and not "write"))
        {
            throw new CliUsageException("Usage: tag read|write <epc> --bank <bank> --word <address> (--count <words>|--data <hex-words>)");
        }

        var options = Parse(tokens);
        TagAccessCliRequest input = TagAccessCliRequest.Create(tokens[2], options.Bank, options.WordPointer, options.AntennaId, options.Password, options.TimeoutSeconds);
        if (tokens[1].Equals("write", StringComparison.OrdinalIgnoreCase))
        {
            TagAccessRenderer.RenderWriteDryRun(console, input.ToWriteRequest(TagAccessCliRequest.ParseWords(options.Data ?? string.Empty)));
            return;
        }

        TagAccessResult result = await TagAccessOperations.ReadAsync(session.Reader, input.ToReadRequest(options.WordCount), input.Timeout, cancellationToken);
        TagAccessRenderer.RenderReadResult(console, result);
    }

    private static (string Bank, ushort WordPointer, ushort WordCount, ushort AntennaId, string? Password, uint? TimeoutSeconds, string? Data) Parse(string[] tokens)
    {
        string bank = "user"; ushort word = 0; ushort count = 0; ushort antenna = 0; string? password = null; uint? timeout = null; string? data = null;
        for (int index = 3; index < tokens.Length; index += 2)
        {
            if (index + 1 >= tokens.Length)
            {
                throw new CliUsageException($"Missing value for {tokens[index]}.");
            }
            string value = tokens[index + 1];
            switch (tokens[index].ToLowerInvariant())
            {
                case "--bank": bank = value; break;
                case "--word" when ushort.TryParse(value, out word): break;
                case "--count" when ushort.TryParse(value, out count): break;
                case "--antenna" when ushort.TryParse(value, out antenna): break;
                case "--password": password = value; break;
                case "--timeout" when uint.TryParse(value, out uint seconds): timeout = seconds; break;
                case "--data": data = value; break;
                default: throw new CliUsageException($"Invalid tag option '{tokens[index]}'.");
            }
        }
        return (bank, word, count, antenna, password, timeout, data);
    }
}
