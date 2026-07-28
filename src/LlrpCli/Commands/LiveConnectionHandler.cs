using Spectre.Console;
using LlrpNet.Core.Diagnostics;
using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Messages.V1_0_1;
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
    private readonly object frameRenderLock = new();

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
            if (IsTagReport(frame) && !session.IsMonitoring)
            {
                // Live inventory aggregates tag reports. Frame monitor remains the explicit raw-report mode.
                return;
            }

            lock (frameRenderLock)
            {
                FrameRenderer.RenderObservedFrame(frame, console, includeHexDump: true);
                console.WriteLine();
            }
        });

        console.MarkupLine($"[grey]Connecting to LLRP Reader at[/] [cyan1]{Markup.Escape(options.Host)}:{options.Port}[/]...");
        var builder = options.CreateReaderBuilder()
            .WithConnectTimeout(TimeSpan.FromSeconds(5))
            .WithFrameObserver(session.FrameObserver);
        options.RenderVendorMode(console);

        LlrpReader reader = builder.Build();
        reader.ConnectionChanged += (sender, e) =>
        {
            if (e.PreviousState == ReaderConnectionState.Ready &&
                e.CurrentState is ReaderConnectionState.Faulted or ReaderConnectionState.Disconnected)
            {
                UpdateWindowTitle("offline");
                console.MarkupLine($"\n[bold red]✖ Reader disconnected ({Markup.Escape(options.Host)}:{options.Port}):[/] {Markup.Escape(e.Error?.Message ?? "Connection dropped")}");
            }
        };

        try
        {
            await reader.ConnectAsync(cancellationToken);
            session.Reader = reader;
            session.Host = options.Host;
            session.Port = options.Port;
            UpdateWindowTitle($"{options.Host}:{options.Port}");

            console.MarkupLine("[bold springgreen2]✔ Connected successfully![/]");
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

    private static bool IsTagReport(CapturedFrame frame)
    {
        if (frame.Direction != LlrpFrameDirection.Receive)
        {
            return false;
        }

        try
        {
            return LlrpMessageHeader.Decode(frame.Bytes).MessageType == RO_ACCESS_REPORT.MessageType;
        }
        catch
        {
            // If the header cannot be decoded, render it as a diagnostic frame instead of hiding it.
            return false;
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
