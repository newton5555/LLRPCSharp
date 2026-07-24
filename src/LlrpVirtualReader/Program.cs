namespace LlrpVirtualReader;

internal static class Program
{
    public static async Task Main(string[] args)
    {
        int port = ParsePort(args);
        await using var reader = new VirtualReaderHost(port);
        reader.Start();

        Console.WriteLine($"Virtual LLRP 1.0.1 reader listening on 127.0.0.1:{reader.Port}. Press Ctrl+C to stop.");

        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            stopped.TrySetResult();
        };
        await stopped.Task.ConfigureAwait(false);
    }

    private static int ParsePort(string[] args)
    {
        if (args.Length == 0)
        {
            return 5084;
        }

        if (args.Length == 2 && args[0] == "--port" &&
            int.TryParse(args[1], out int port) && port is > 0 and <= ushort.MaxValue)
        {
            return port;
        }

        throw new ArgumentException("Usage: LlrpVirtualReader [--port <1-65535>]");
    }
}
