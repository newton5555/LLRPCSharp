using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using LlrpNet.Core.Diagnostics;
using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Messages;
using LlrpSdk;
using LlrpCli.Rendering;
using LlrpCli.Terminal;
using LlrpSdk.Extensions.Impinj;

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

    [CommandOption("--llrp <VERSION>")]
    [Description("Protocol version policy: auto, 1.0.1, or 1.1.")]
    [DefaultValue("auto")]
    public string LlrpVersion { get; init; } = "auto";

    [CommandOption("--vendor <VENDOR>")]
    [Description("Vendor extensions mode: auto, impinj, or none.")]
    [DefaultValue("auto")]
    public string Vendor { get; init; } = "auto";
}

public sealed class MonitorCommand : AsyncCommand<MonitorSettings>
{
    private readonly IAnsiConsole _console;

    public MonitorCommand() : this(AnsiConsole.Console) { }

    public MonitorCommand(IAnsiConsole console)
    {
        _console = console ?? AnsiConsole.Console;
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, MonitorSettings settings, CancellationToken cancellationToken)
    {
        if (!ProtocolVersionPolicyParser.TryParse(settings.LlrpVersion, out LlrpProtocolVersionPolicy policy))
        {
            _console.MarkupLine("[bold red]✖ Invalid LLRP version:[/] use auto, 1.0.1, or 1.1.");
            return 2;
        }

        _console.MarkupLine($"[grey]Starting LLRP Frame Monitor on[/] [cyan1]{settings.Host}:{settings.Port}[/]...");

        var observer = new DelegateFrameObserver(frame =>
        {
            FrameRenderer.RenderObservedFrame(frame, _console, includeHexDump: true);
            _console.WriteLine();
        });

        var builder = LlrpReader.CreateBuilder(settings.Host)
            .WithPort(settings.Port)
            .WithFrameObserver(observer)
            .WithProtocolVersionPolicy(policy);

        if (string.Equals(settings.Vendor, "none", StringComparison.OrdinalIgnoreCase))
        {
            _console.MarkupLine("[grey]Vendor extensions:[/] [yellow]disabled (pure standard LLRP mode)[/]");
        }
        else if (string.Equals(settings.Vendor, "impinj", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(settings.Vendor, "auto", StringComparison.OrdinalIgnoreCase))
        {
            builder.UseImpinj();
            _console.MarkupLine("[grey]Vendor extensions:[/] [springgreen2]Impinj enabled[/]");
        }
        else
        {
            _console.MarkupLine($"[bold red]✖ Invalid vendor option:[/] '{Markup.Escape(settings.Vendor)}' (use auto, impinj, or none).");
            return 2;
        }

        await using LlrpReader reader = builder.Build();

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
            _console.MarkupLine("[bold springgreen2]✔ Connected! Streaming LLRP frames... (Press Ctrl+C to stop)[/]");
            _console.WriteLine();

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
            _console.MarkupLine("[grey]Monitoring stopped by user.[/]");
        }
        catch (Exception ex)
        {
            _console.MarkupLine($"[bold red]✖ Monitor error:[/] {Markup.Escape(ex.Message)}");
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
}
