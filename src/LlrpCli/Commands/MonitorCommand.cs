using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using LlrpNet.Core.Diagnostics;
using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Messages;
using LlrpSdk;
using LlrpCli.Rendering;

namespace LlrpCli.Commands;

public sealed class MonitorSettings : CommandSettings
{
    [CommandArgument(0, "<HOST>")]
    [Description("Hostname or IP address of the LLRP Reader.")]
    public string Host { get; init; } = string.Empty;

    [CommandOption("--port <PORT>")]
    [Description("TCP port of the LLRP Reader.")]
    [DefaultValue(5084)]
    public int Port { get; init; } = 5084;

    [CommandOption("--duration <SECONDS>")]
    [Description("Monitoring duration in seconds (0 = run until Ctrl+C).")]
    [DefaultValue(30)]
    public int DurationSeconds { get; init; } = 30;
}

public sealed class MonitorCommand : AsyncCommand<MonitorSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, MonitorSettings settings, CancellationToken cancellationToken)
    {
        AnsiConsole.MarkupLine($"[grey]Starting LLRP Frame Monitor on[/] [cyan1]{settings.Host}:{settings.Port}[/]...");

        var observer = new ConsoleFrameObserver();
        await using LlrpReader reader = LlrpReader.CreateBuilder(settings.Host)
            .WithPort(settings.Port)
            .WithFrameObserver(observer)
            .Build();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        ConsoleCancelEventHandler handler = (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };
        Console.CancelKeyPress += handler;

        try
        {
            await reader.ConnectAsync(cts.Token);
            AnsiConsole.MarkupLine("[bold springgreen2]✔ Connected! Streaming LLRP frames... (Press Ctrl+C to stop)[/]");
            AnsiConsole.WriteLine();

            DateTimeOffset started = DateTimeOffset.UtcNow;
            while (!cts.IsCancellationRequested &&
                   (settings.DurationSeconds == 0 || DateTimeOffset.UtcNow - started < TimeSpan.FromSeconds(settings.DurationSeconds)))
            {
                if (!reader.IsConnected)
                {
                    throw new IOException("Reader disconnected unexpectedly during monitoring.");
                }

                await Task.Delay(200, cts.Token);
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            AnsiConsole.MarkupLine("[grey]Monitoring stopped by user.[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[bold red]✖ Monitor error:[/] {Markup.Escape(ex.Message)}");
            return 1;
        }
        finally
        {
            Console.CancelKeyPress -= handler;
            if (reader.IsConnected)
            {
                await reader.DisconnectAsync(CancellationToken.None);
            }
        }

        return 0;
    }

    private sealed class ConsoleFrameObserver : ILlrpFrameObserver
    {
        private static readonly object ConsoleLock = new();

        public ValueTask ObserveAsync(LlrpFrameObservation observation, CancellationToken cancellationToken = default)
        {
            byte[] frameCopy = observation.FrameBytes.ToArray();
            bool isTx = observation.Direction == LlrpFrameDirection.Transmit;
            string directionBadge = isTx ? "[deepskyblue1 bold]→ TX[/]" : "[springgreen2 bold]← RX[/]";
            string borderColor = isTx ? "deepskyblue1" : "springgreen2";

            lock (ConsoleLock)
            {
                try
                {
                    LlrpMessageHeader header = LlrpMessageHeader.Decode(frameCopy);
                    ILlrpMessage message = Helpers.CreateRegistry().DecodeMessage(frameCopy);

                    AnsiConsole.MarkupLine($"{directionBadge}  [bold]{Markup.Escape(message.GetType().Name)}[/]  [grey]ID {header.MessageId} · {frameCopy.Length} bytes · {observation.Timestamp:HH:mm:ss.fff}[/]");
                    FrameRenderer.RenderHexDumpPanel(frameCopy, AnsiConsole.Console);
                    AnsiConsole.WriteLine();
                }
                catch
                {
                    AnsiConsole.MarkupLine($"{directionBadge}  [grey]Raw Frame ({frameCopy.Length} bytes)[/]");
                    FrameRenderer.RenderHexDumpPanel(frameCopy, AnsiConsole.Console);
                    AnsiConsole.WriteLine();
                }
            }

            return ValueTask.CompletedTask;
        }
    }
}
