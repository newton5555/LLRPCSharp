using Spectre.Console;
using LlrpCli.Rendering;
using LlrpCli.Terminal;
using LlrpSdk;

namespace LlrpCli.Commands;

/// <summary>
/// Owns reader creation, connection lifecycle, frame observation, and disposal for one Live Shell session.
/// </summary>
internal sealed class LiveConnectionHandler(
    IAnsiConsole console,
    LiveSessionContext session,
    LiveInventoryHandler inventory)
{
    public async Task<bool> ConnectAsync(CliConnectionOptions options, CancellationToken cancellationToken)
    {
        if (session.Reader is not null)
        {
            await inventory.StopAsync(CancellationToken.None);
            await session.Reader.DisposeAsync();
            session.Reader = null;
        }

        session.FrameObserver = new DelegateFrameObserver(frame =>
        {
            if (session.IsMonitoring)
            {
                FrameRenderer.RenderObservedFrame(frame, console, includeHexDump: true);
                console.WriteLine();
            }

            if (session.IsMonitoringTable)
            {
                session.MonitorFrameCallback?.Invoke(frame);
            }
        });

        console.MarkupLine($"[grey]Connecting to LLRP Reader at[/] [cyan1]{Markup.Escape(options.Host)}:{options.Port}[/]...");
        var builder = options.CreateReaderBuilder()
            .WithConnectTimeout(TimeSpan.FromSeconds(5))
            .WithFrameObserver(session.FrameObserver);
        options.RenderVendorMode(console);

        LlrpReader reader = builder.Build();
        try
        {
            await reader.ConnectAsync(cancellationToken);
            session.Reader = reader;
            session.Host = options.Host;
            session.Port = options.Port;
            UpdateWindowTitle($"{options.Host}:{options.Port}");

            console.MarkupLine("[bold springgreen2]✔ Connected successfully![/]");
            console.WriteLine();
            RenderNegotiationFrames(session.FrameObserver.CapturedFrames);
            return true;
        }
        catch (Exception exception)
        {
            await reader.DisposeAsync();
            session.Reader = null;
            session.FrameObserver = null;
            UpdateWindowTitle("offline");
            console.MarkupLine($"[bold red]✖ Connection failed:[/] {Markup.Escape(exception.Message)}");
            return false;
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        if (session.Reader is null || !session.Reader.IsConnected)
        {
            console.MarkupLine("[yellow]Not connected to any reader.[/]");
            return;
        }

        await inventory.StopAsync(cancellationToken);
        await session.Reader.DisconnectAsync(cancellationToken);
        await session.Reader.DisposeAsync();
        session.Reader = null;
        session.FrameObserver = null;
        UpdateWindowTitle("offline");
        console.MarkupLine("[grey]Disconnected from reader.[/]");
    }

    public async Task DisposeAsync()
    {
        if (session.Reader is null)
        {
            return;
        }

        await inventory.StopAsync(CancellationToken.None);
        await session.Reader.DisposeAsync();
        session.Reader = null;
        session.FrameObserver = null;
        UpdateWindowTitle("offline");
    }

    private void RenderNegotiationFrames(IReadOnlyList<CapturedFrame> frames)
    {
        if (frames.Count == 0)
        {
            return;
        }

        console.Write(new Rule($"[bold cyan1]Exchanged Connection Negotiation LLRP Messages ({frames.Count})[/]"));
        foreach (CapturedFrame frame in frames)
        {
            FrameRenderer.RenderObservedFrame(frame, console, includeHexDump: true);
            console.WriteLine();
        }
    }

    private static void UpdateWindowTitle(string status)
    {
        try
        {
            Console.Title = $"LLRPCSharp Studio · {status}";
        }
        catch
        {
            // Ignore restricted terminals.
        }
    }
}
