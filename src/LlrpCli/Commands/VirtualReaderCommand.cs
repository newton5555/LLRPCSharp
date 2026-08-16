using System.ComponentModel;
using System.Net;
using LlrpNet.Core.Protocol;
using LlrpVirtualReader;
using Spectre.Console;
using Spectre.Console.Cli;

namespace LlrpCli.Commands;

public sealed class VirtualReaderCommandSettings : CommandSettings
{
    [CommandOption("--listen <IP>")]
    [DefaultValue("127.0.0.1")]
    [Description("Exact local IP address to bind.")]
    public string ListenAddress { get; init; } = "127.0.0.1";

    [CommandOption("--port <PORT>")]
    [DefaultValue(5084)]
    [Description("Exact TCP port to bind.")]
    public int Port { get; init; } = 5084;

    [CommandOption("--llrp <VERSION>")]
    [DefaultValue("1.0.1")]
    [Description("Protocol version: 1.0.1 or 1.1.")]
    public string LlrpVersion { get; init; } = "1.0.1";

    [CommandOption("--name <NAME>")]
    [DefaultValue("Virtual Reader")]
    public string Name { get; init; } = "Virtual Reader";

    [CommandOption("--tag <EPC>")]
    [Description("Optional replacement deterministic EPC, in hexadecimal form.")]
    public string? Tag { get; init; }

    [CommandOption("--interval-ms <MILLISECONDS>")]
    [DefaultValue(100)]
    public int ReportIntervalMilliseconds { get; init; } = 100;

    [CommandOption("--count <COUNT>")]
    [DefaultValue(0)]
    [Description("Maximum report messages; zero repeats until stopped.")]
    public int ReportCount { get; init; }

    [CommandOption("--strict")]
    public bool Strict { get; init; }
}

/// <summary>Runs the message-level virtual reader until Ctrl+C or cancellation.</summary>
public sealed class VirtualReaderCommand : AsyncCommand<VirtualReaderCommandSettings>
{
    private readonly IAnsiConsole _console;

    public VirtualReaderCommand() : this(AnsiConsole.Console) { }

    public VirtualReaderCommand(IAnsiConsole console)
    {
        _console = console ?? AnsiConsole.Console;
    }

    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        VirtualReaderCommandSettings settings,
        CancellationToken cancellationToken)
    {
        if (!IPAddress.TryParse(settings.ListenAddress, out IPAddress? listenAddress))
        {
            _console.MarkupLine($"[red]Invalid listen address: {Markup.Escape(settings.ListenAddress)}[/]");
            return 2;
        }

        if (settings.Port is <= 0 or > ushort.MaxValue)
        {
            _console.MarkupLine("[red]--port must be between 1 and 65535.[/]");
            return 2;
        }

        if (settings.ReportIntervalMilliseconds <= 0 || settings.ReportCount < 0)
        {
            _console.MarkupLine("[red]--interval-ms must be positive and --count cannot be negative.[/]");
            return 2;
        }

        LlrpProtocolVersion version = settings.LlrpVersion.Trim() switch
        {
            "1.0.1" => LlrpProtocolVersion.Version101,
            "1.1" => LlrpProtocolVersion.Version11,
            _ => throw new ArgumentException("--llrp must be 1.0.1 or 1.1."),
        };

        VirtualReaderOptions readerOptions = new()
        {
            ReaderName = settings.Name,
            ProtocolVersion = version,
            UseStrictStandardInventoryProfile = settings.Strict,
            Reports = new VirtualReaderReportOptions
            {
                ReportInterval = TimeSpan.FromMilliseconds(settings.ReportIntervalMilliseconds),
                ReportCount = settings.ReportCount,
                Repeat = true,
            },
        };
        if (!string.IsNullOrWhiteSpace(settings.Tag))
        {
            byte[] epc;
            try
            {
                epc = Convert.FromHexString(settings.Tag.Trim());
            }
            catch (FormatException exception)
            {
                _console.MarkupLine($"[red]Invalid --tag EPC: {Markup.Escape(exception.Message)}[/]");
                return 2;
            }

            readerOptions = readerOptions with
            {
                TagSource = new FixedVirtualTagSource(
                [
                    new VirtualTag { ElectronicProductCode = epc },
                ]),
            };
        }

        await using var host = new VirtualReaderHost(
            new VirtualReaderHostOptions
            {
                ListenAddress = listenAddress,
                Port = settings.Port,
                ReaderOptions = readerOptions,
            });
        await host.StartAsync(cancellationToken).ConfigureAwait(false);
        _console.MarkupLine(
            $"[green]Virtual reader '{Markup.Escape(settings.Name)}' listening on " +
            $"{Markup.Escape(FormatEndpoint(host.ListenAddress, host.Port))} using LLRP {settings.LlrpVersion}.[/]" );
        _console.MarkupLine("Press Ctrl+C to stop.");

        using var stopSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            stopSource.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, stopSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stopSource.IsCancellationRequested)
        {
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }

        return 0;
    }

    private static string FormatEndpoint(IPAddress address, int port) =>
        address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
            ? $"[{address}]:{port}"
            : $"{address}:{port}";
}
