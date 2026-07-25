using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using LlrpNet.Core.Diagnostics;
using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Messages;
using LlrpSdk;
using LlrpCli.Analysis;
using LlrpCli.Rendering;
using LlrpCli.Terminal;
using LlrpSdk.Extensions.Impinj;

namespace LlrpCli.Commands;

public sealed class LiveSettings : CommandSettings
{
    [CommandOption("--host <HOST>")]
    [Description("Optional LLRP Reader host to connect automatically on startup.")]
    public string? Host { get; init; }

    [CommandOption("--port <PORT>")]
    [Description("Optional TCP port for automatic connection.")]
    [DefaultValue(5084)]
    public int Port { get; init; } = 5084;

    [CommandOption("--llrp <VERSION>")]
    [Description("Protocol version policy for automatic connection: auto, 1.0.1, or 1.1.")]
    [DefaultValue("auto")]
    public string LlrpVersion { get; init; } = "auto";
}

public sealed class LiveCommand : AsyncCommand<LiveSettings>
{
    private readonly IAnsiConsole _console;
    private LlrpReader? _reader;
    private DelegateFrameObserver? _observer;
    private CancellationTokenSource? _inventoryCancellation;
    private Task? _inventoryPumpTask;
    private string? _currentHost;
    private int _currentPort = 5084;
    private bool _isMonitoring;
    private bool _isMonitoringTable;
    private Action<CapturedFrame>? _monitorFrameCallback;

    private sealed class TagStat
    {
        public required string Epc { get; init; }
        public ushort AntennaId { get; set; }
        public sbyte PeakRssi { get; set; }
        public long ReadCount { get; set; }
        public DateTime LastSeen { get; set; }
    }

    public LiveCommand() : this(AnsiConsole.Console) { }

    public LiveCommand(IAnsiConsole console)
    {
        _console = console ?? AnsiConsole.Console;
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, LiveSettings settings, CancellationToken cancellationToken)
    {
        RenderBanner();

        if (!string.IsNullOrWhiteSpace(settings.Host))
        {
            if (!ProtocolVersionPolicyParser.TryParse(settings.LlrpVersion, out LlrpProtocolVersionPolicy policy))
            {
                _console.MarkupLine("[bold red]✖ Invalid LLRP version:[/] use auto, 1.0.1, or 1.1.");
            }
            else
            {
                await ConnectToReaderAsync(settings.Host, settings.Port, policy, cancellationToken);
            }
        }

        using var editor = new TerminalLineEditor();

        while (!cancellationToken.IsCancellationRequested)
        {
            bool isConnected = _reader?.IsConnected == true;
            string promptState = isConnected
                ? $"[deepskyblue1 bold]📡 llrp[/] [springgreen2]({_currentHost}:{_currentPort})[/] [bold cyan]>[/]"
                : "[deepskyblue1 bold]📡 llrp[/] [grey](disconnected)[/] [bold cyan]>[/]";

            LineReadResult readResult = editor.ReadLine(
                promptState,
                (text, cursor) => CommandCatalog.Assist(text, cursor, isConnected));

            if (readResult.Text is null)
            {
                break;
            }

            if (readResult.Cancelled || string.IsNullOrWhiteSpace(readResult.Text))
            {
                continue;
            }

            string line = readResult.Text.Trim();
            string[] tokens = Tokenize(line);
            if (tokens.Length == 0)
            {
                continue;
            }

            string verb = tokens[0].ToLowerInvariant();
            if (verb is "exit" or "quit" or "q")
            {
                _console.MarkupLine("[grey]Exiting live mode... Bye![/]");
                break;
            }

            try
            {
                switch (verb)
                {
                    case "connect":
                        await HandleConnectAsync(tokens, cancellationToken);
                        break;
                    case "disconnect":
                        await HandleDisconnectAsync(cancellationToken);
                        break;
                    case "status":
                        HandleStatus();
                        break;
                    case "caps":
                        HandleCaps();
                        break;
                    case "inventory":
                        await HandleInventoryAsync(tokens, cancellationToken);
                        break;
                    case "monitor":
                        await HandleMonitorAsync(tokens, cancellationToken);
                        break;
                    case "frames":
                        HandleFrames(tokens);
                        break;
                    case "rospec":
                        await HandleRospecAsync(tokens, cancellationToken);
                        break;
                    case "accessspec":
                        await HandleAccessSpecAsync(tokens, cancellationToken);
                        break;
                    case "raw":
                        await HandleRawAsync(tokens, cancellationToken);
                        break;
                    case "sync":
                        await HandleSynchronizeStateAsync(cancellationToken);
                        break;
                    case "inspect":
                        HandleInspect(tokens);
                        break;
                    case "decode":
                        HandleDecode(tokens);
                        break;
                    case "validate":
                        HandleValidate(tokens);
                        break;
                    case "encode":
                        HandleEncode(tokens);
                        break;
                    case "clear":
                    case "cls":
                        _console.Clear();
                        RenderBanner();
                        break;
                    case "help":
                    case "?":
                        RenderHelp();
                        break;
                    default:
                        if (tokens.Length == 1 && (verb.Contains('.') || verb == "localhost" || verb == "127.0.0.1"))
                        {
                            await ConnectToReaderAsync(
                                tokens[0],
                                5084,
                                LlrpProtocolVersionPolicy.Auto,
                                cancellationToken);
                        }
                        else
                        {
                            _console.MarkupLine($"[red]Unknown command '{Markup.Escape(tokens[0])}'.[/] Type [cyan1]help[/] for available commands.");
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                _console.MarkupLine($"[red]Error:[/] {Markup.Escape(ex.Message)}");
            }

            _console.WriteLine();
        }

        if (_reader is not null)
        {
            await StopInventoryAsync(CancellationToken.None);
            await _reader.DisposeAsync();
            _reader = null;
        }

        return 0;
    }

    private async Task HandleConnectAsync(string[] tokens, CancellationToken cancellationToken)
    {
        string host;
        int port = 5084;

        if (tokens.Length < 2)
        {
            host = _console.Prompt(
                new TextPrompt<string>("[grey]Enter Reader Host/IP:[/]")
                    .DefaultValue("127.0.0.1"));
        }
        else
        {
            host = tokens[1];
            int nextToken = 2;
            if (tokens.Length > nextToken && int.TryParse(tokens[nextToken], out int parsedPort))
            {
                port = parsedPort;
                nextToken++;
            }

            LlrpProtocolVersionPolicy policy = LlrpProtocolVersionPolicy.Auto;
            if (tokens.Length == nextToken + 2 && tokens[nextToken].Equals("--llrp", StringComparison.OrdinalIgnoreCase))
            {
                if (!ProtocolVersionPolicyParser.TryParse(tokens[nextToken + 1], out policy))
                {
                    throw new CliUsageException("LLRP version must be auto, 1.0.1, or 1.1.");
                }
            }
            else if (tokens.Length != nextToken)
            {
                throw new CliUsageException("Usage: connect <host> [port] [--llrp auto|1.0.1|1.1]");
            }

            await ConnectToReaderAsync(host, port, policy, cancellationToken);
            return;
        }

        await ConnectToReaderAsync(host, port, LlrpProtocolVersionPolicy.Auto, cancellationToken);
    }

    private async Task ConnectToReaderAsync(
        string host,
        int port,
        LlrpProtocolVersionPolicy protocolVersionPolicy,
        CancellationToken cancellationToken)
    {
        if (_reader is not null)
        {
            await StopInventoryAsync(CancellationToken.None);
            await _reader.DisposeAsync();
            _reader = null;
        }

        _observer = new DelegateFrameObserver(frame =>
        {
            if (_isMonitoring)
            {
                FrameRenderer.RenderObservedFrame(frame, _console, includeHexDump: true);
                _console.WriteLine();
            }
            if (_isMonitoringTable)
            {
                _monitorFrameCallback?.Invoke(frame);
            }
        });

        _console.MarkupLine($"[grey]Connecting to LLRP Reader at[/] [cyan1]{Markup.Escape(host)}:{port}[/]...");

        var reader = LlrpReader.CreateBuilder(host)
            .WithPort(port)
            .WithConnectTimeout(TimeSpan.FromSeconds(5))
            .WithFrameObserver(_observer)
            .WithProtocolVersionPolicy(protocolVersionPolicy)
            .UseImpinj()
            .Build();

        try
        {
            await reader.ConnectAsync(cancellationToken);
            _reader = reader;
            _currentHost = host;
            _currentPort = port;
            UpdateWindowTitle($"{host}:{port}");

            _console.MarkupLine("[bold springgreen2]✔ Connected successfully![/]");
            _console.WriteLine();

            IReadOnlyList<CapturedFrame> frames = _observer.CapturedFrames;
            if (frames.Count > 0)
            {
                var rule = new Rule($"[bold cyan1]Exchanged Connection Negotiation LLRP Messages ({frames.Count})[/]");
                _console.Write(rule);

                foreach (CapturedFrame frame in frames)
                {
                    FrameRenderer.RenderObservedFrame(frame, _console, includeHexDump: true);
                    _console.WriteLine();
                }
            }

            HandleStatus();
        }
        catch (Exception ex)
        {
            await reader.DisposeAsync();
            _reader = null;
            _observer = null;
            UpdateWindowTitle("offline");
            _console.MarkupLine($"[bold red]✖ Connection failed:[/] {Markup.Escape(ex.Message)}");
        }
    }

    private async Task HandleDisconnectAsync(CancellationToken cancellationToken)
    {
        if (_reader is null || !_reader.IsConnected)
        {
            _console.MarkupLine("[yellow]Not connected to any reader.[/]");
            return;
        }

        await StopInventoryAsync(cancellationToken);
        await _reader.DisconnectAsync(cancellationToken);
        await _reader.DisposeAsync();
        _reader = null;
        _observer = null;
        UpdateWindowTitle("offline");
        _console.MarkupLine("[grey]Disconnected from reader.[/]");
    }

    private void HandleStatus()
    {
        if (_reader is null || !_reader.IsConnected)
        {
            _console.MarkupLine("[yellow]Status:[/] [red]Disconnected[/]");
            return;
        }

        var table = new Table();
        table.AddColumn("[bold grey70]Property[/]");
        table.AddColumn("[bold grey70]Value[/]");

        table.AddRow("Host", $"[cyan1]{_currentHost}:{_currentPort}[/]");
        table.AddRow("Connection State", $"[springgreen2]{_reader.ConnectionState}[/]");
        table.AddRow("Connection ID", $"[white]{_reader.ConnectionId}[/]");

        if (_reader.Identity is { } identity)
        {
            table.AddRow("Manufacturer ID", $"[cyan1]{identity.ManufacturerId}[/]");
            table.AddRow("Model ID", $"[springgreen2]{identity.ModelId}[/]");
            table.AddRow("Firmware Version", $"[yellow]{Markup.Escape(identity.FirmwareVersion)}[/]");
        }

        if (_observer != null)
        {
            table.AddRow("Total Captured Frames", $"[deepskyblue1]{_observer.CapturedFrames.Count}[/]");
        }

        var panel = new Panel(table)
            .Header("[bold deepskyblue1] ACTIVE SESSION STATUS [/]")
            .Border(BoxBorder.Rounded);

        _console.Write(panel);
    }

    private void HandleCaps()
    {
        if (_reader is null || !_reader.IsConnected)
        {
            _console.MarkupLine("[yellow]Not connected. Run 'connect <host>' first.[/]");
            return;
        }

        if (_reader.Capabilities is { } capabilities)
        {
            var table = new Table();
            table.AddColumn("[bold grey70]Capability[/]");
            table.AddColumn("[bold grey70]Value[/]");

            table.AddRow("Max Antennas", $"[white]{capabilities.MaxNumberOfAntennas}[/]");
            table.AddRow("Set Antenna Props", capabilities.CanSetAntennaProperties ? "[green]Yes[/]" : "[grey]No[/]");
            table.AddRow("UTC Clock", capabilities.HasUtcClockCapability ? "[green]Yes[/]" : "[grey]No[/]");
            table.AddRow("Additional Parameters", $"[cyan1]{capabilities.AdditionalParameters.Count}[/]");

            var panel = new Panel(table)
                .Header("[bold springgreen2] READER CAPABILITIES [/]")
                .Border(BoxBorder.Rounded);

            _console.Write(panel);
        }
        else
        {
            _console.MarkupLine("[yellow]No capability metadata retrieved from reader.[/]");
        }
    }

    private void HandleFrames(string[] tokens)
    {
        if (_observer is null || _observer.CapturedFrames.Count == 0)
        {
            _console.MarkupLine("[yellow]No frames captured yet.[/]");
            return;
        }

        int count = 10;
        if (tokens.Length >= 2 && int.TryParse(tokens[1], out int parsedCount))
        {
            count = parsedCount;
        }

        IReadOnlyList<CapturedFrame> frames = _observer.CapturedFrames;
        var recent = frames.TakeLast(count).ToList();

        var rule = new Rule($"[bold cyan1]Recent {recent.Count} LLRP Message Frames[/]");
        _console.Write(rule);

        foreach (CapturedFrame frame in recent)
        {
            FrameRenderer.RenderObservedFrame(frame, _console, includeHexDump: true);
            _console.WriteLine();
        }
    }

    private async Task HandleInventoryAsync(string[] tokens, CancellationToken cancellationToken)
    {
        if (_reader is null || !_reader.IsConnected)
        {
            _console.MarkupLine("[yellow]Not connected. Run 'connect <host>' first.[/]");
            return;
        }

        if (tokens.Length < 2)
        {
            _console.MarkupLine("[red]Usage:[/] inventory start [[antenna-id]] | stop | status");
            return;
        }

        switch (tokens[1].ToLowerInvariant())
        {
            case "start":
            {
                if (_inventoryPumpTask is { IsCompleted: false })
                {
                    _console.MarkupLine("[yellow]SDK-managed inventory is already running.[/]");
                    return;
                }

                ushort antennaId = 0;
                if (tokens.Length >= 3 && !ushort.TryParse(tokens[2], out antennaId))
                {
                    _console.MarkupLine("[red]Antenna identifier must be an unsigned 16-bit integer.[/]");
                    return;
                }

                var settings = new ReaderSettings
                {
                    AntennaIds = [antennaId],
                };
                await _reader.StartAsync(settings, cancellationToken);

                var inventoryCancellation = new CancellationTokenSource();
                _inventoryCancellation = inventoryCancellation;
                _inventoryPumpTask = PumpTagReportsAsync(_reader, inventoryCancellation.Token);
                string scope = antennaId == 0 ? "all antennas" : $"antenna {antennaId}";
                _console.MarkupLine($"[bold springgreen2]✔ SDK-managed inventory started for {scope}.[/]");
                break;
            }

            case "stop":
                await StopInventoryAsync(cancellationToken);
                _console.MarkupLine("[bold springgreen2]✔ SDK-managed inventory stopped.[/]");
                break;

            case "status":
                _console.MarkupLine(
                    _reader.OperationState == ReaderOperationState.Inventorying
                        ? "[springgreen2]SDK-managed inventory is running.[/]"
                        : $"[yellow]SDK-managed inventory is not running (state: {_reader.OperationState}).[/]");
                break;

            default:
                _console.MarkupLine("[red]Usage:[/] inventory start [[antenna-id]] | stop | status");
                break;
        }
    }

    private async Task HandleMonitorAsync(string[] tokens, CancellationToken cancellationToken)
    {
        if (_reader is null || !_reader.IsConnected)
        {
            _console.MarkupLine("[yellow]Not connected. Run 'connect <host>' first.[/]");
            return;
        }

        bool useTable = false;
        int seconds = 10;

        for (int i = 1; i < tokens.Length; i++)
        {
            string token = tokens[i].ToLowerInvariant();
            if (token is "--table" or "-t" or "table")
            {
                useTable = true;
            }
            else if (token is "--frames" or "-f" or "frames" or "raw")
            {
                useTable = false;
            }
            else if (int.TryParse(token, out int parsedSec))
            {
                seconds = parsedSec;
            }
        }

        if (!useTable)
        {
            _console.MarkupLine($"[bold springgreen2]📡 Listening to passive LLRP frame logs for {seconds} seconds...[/]");
            _console.MarkupLine("[grey]Raw Frame Mode: printing raw RX/TX frame trees and hex dumps.[/]");
            _console.WriteLine();

            _isMonitoringTable = false;
            _isMonitoring = true;
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(seconds), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Expected if user cancels
            }
            finally
            {
                _isMonitoring = false;
                _console.MarkupLine("[bold cyan1]✔ Passive frame monitoring ended.[/]");
            }
        }
        else
        {
            _console.MarkupLine($"[bold springgreen2]📊 Streaming live tag statistics table for {seconds} seconds...[/]");
            _console.MarkupLine("[grey]Live Table Mode: aggregated unique EPC tag counts, RSSI, and antennas.[/]");
            var tagStats = new System.Collections.Concurrent.ConcurrentDictionary<string, TagStat>();

            _monitorFrameCallback = frame =>
            {
                try
                {
                    ILlrpMessage msg = _reader.Registry.DecodeMessage(frame.Bytes);
                    IReadOnlyList<TagReport> reports = _reader.TranslateTagReports(msg);
                    foreach (TagReport report in reports)
                    {
                        string epc = Convert.ToHexString(report.ElectronicProductCode.Span);
                        if (string.IsNullOrEmpty(epc))
                        {
                            continue;
                        }

                        tagStats.AddOrUpdate(
                            epc,
                            key => new TagStat
                            {
                                Epc = key,
                                AntennaId = report.AntennaId ?? 0,
                                PeakRssi = report.PeakRssi ?? 0,
                                ReadCount = 1,
                                LastSeen = DateTime.Now
                            },
                            (key, existing) =>
                            {
                                existing.ReadCount++;
                                existing.AntennaId = report.AntennaId ?? existing.AntennaId;
                                existing.PeakRssi = report.PeakRssi ?? existing.PeakRssi;
                                existing.LastSeen = DateTime.Now;
                                return existing;
                            });
                    }
                }
                catch
                {
                    // Non-report messages ignored in tag table
                }
            };

            _isMonitoringTable = true;
            _isMonitoring = false;

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(seconds));

            var table = new Table
            {
                Border = TableBorder.Rounded,
                BorderStyle = new Style(Color.DeepSkyBlue1)
            };
            table.AddColumn("[bold deepskyblue1]🏷️ EPC (Hex)[/]");
            table.AddColumn("[bold springgreen2]📡 Antenna[/]");
            table.AddColumn("[bold yellow1]📶 Peak RSSI[/]");
            table.AddColumn("[bold cyan1]🔢 Read Count[/]");
            table.AddColumn("[bold grey70]🕒 Last Seen[/]");

            try
            {
                await _console.Live(table)
                    .AutoClear(false)
                    .Overflow(VerticalOverflow.Ellipsis)
                    .StartAsync(async ctx =>
                    {
                        while (!cts.IsCancellationRequested)
                        {
                            table.Rows.Clear();
                            var topTags = tagStats.Values
                                .OrderByDescending(t => t.LastSeen)
                                .Take(15)
                                .ToList();

                            long totalReads = tagStats.Values.Sum(t => t.ReadCount);

                            foreach (var tag in topTags)
                            {
                                table.AddRow(
                                    $"[bold white]{tag.Epc}[/]",
                                    $"[cyan1]{tag.AntennaId}[/]",
                                    $"[yellow1]{tag.PeakRssi} dBm[/]",
                                    $"[bold springgreen2]{tag.ReadCount:N0}[/]",
                                    $"[grey]{tag.LastSeen:HH:mm:ss.fff}[/]");
                            }

                            table.Title = new TableTitle(
                                $"[bold white on deepskyblue1] 🏷️ LIVE TAG MONITOR [/] [grey]Unique Tags:[/] [bold yellow]{tagStats.Count}[/] | [grey]Total Reads:[/] [bold springgreen2]{totalReads:N0}[/]");

                            ctx.Refresh();
                            try
                            {
                                await Task.Delay(100, cts.Token);
                            }
                            catch (OperationCanceledException)
                            {
                                break;
                            }
                        }
                    });
            }
            catch (OperationCanceledException)
            {
                // Expected when canceled
            }
            finally
            {
                _isMonitoringTable = false;
                _monitorFrameCallback = null;
                _console.MarkupLine($"[bold cyan1]✔ Live tag summary ended. Total Unique Tags: {tagStats.Count}[/]");
            }
        }
    }

    private async Task StopInventoryAsync(CancellationToken cancellationToken)
    {
        CancellationTokenSource? inventoryCancellation = _inventoryCancellation;
        Task? inventoryPumpTask = _inventoryPumpTask;
        _inventoryCancellation = null;
        _inventoryPumpTask = null;

        inventoryCancellation?.Cancel();
        try
        {
            if (_reader?.IsConnected == true && _reader.OperationState == ReaderOperationState.Inventorying)
            {
                await _reader.StopAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            if (inventoryPumpTask is not null)
            {
                try
                {
                    await inventoryPumpTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (inventoryCancellation?.IsCancellationRequested == true)
                {
                    // Stopping inventory owns cancellation of the report pump.
                }
            }

            inventoryCancellation?.Dispose();
        }
    }

    private async Task PumpTagReportsAsync(LlrpReader reader, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (TagReport report in reader.ReadTagReportsAsync(cancellationToken))
            {
                string epc = Convert.ToHexString(report.ElectronicProductCode.Span);
                string antenna = report.AntennaId?.ToString() ?? "-";
                string rssi = report.PeakRssi?.ToString() ?? "-";
                _console.MarkupLine(
                    $"[cyan1]TAG[/] EPC=[bold]{epc}[/] Antenna={antenna} RSSI={rssi}");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The inventory command explicitly stopped the report stream.
        }
        catch (Exception exception)
        {
            _console.MarkupLine($"[red]Inventory report stream failed:[/] {Markup.Escape(exception.Message)}");
        }
    }

    private async Task HandleRospecAsync(string[] tokens, CancellationToken cancellationToken)
    {
        if (_reader is null || !_reader.IsConnected)
        {
            _console.MarkupLine("[yellow]Not connected. Run 'connect <host>' first.[/]");
            return;
        }

        if (tokens.Length < 2)
        {
            _console.MarkupLine("[red]Usage:[/] rospec list|enable|disable|start|stop|delete [[id]]");
            return;
        }

        string subAction = tokens[1].ToLowerInvariant();
        uint rospecId = 1;
        if (tokens.Length >= 3 && uint.TryParse(tokens[2], out uint parsedId))
        {
            rospecId = parsedId;
        }

        int startIndex = _observer?.CapturedFrames.Count ?? 0;

        switch (subAction)
        {
            case "list":
                _console.MarkupLine("[grey]Querying installed ROSpecs...[/]");
                var rospecs = await _reader.RoSpecs.GetAllAsync(cancellationToken);
                _console.MarkupLine($"[green]Found {rospecs.Count} ROSpec(s).[/]");
                break;
            case "enable":
                _console.MarkupLine($"[grey]Enabling ROSpec {rospecId}...[/]");
                await _reader.RoSpecs.EnableAsync(rospecId, cancellationToken);
                _console.MarkupLine($"[bold springgreen2]✔ ROSpec {rospecId} Enabled![/]");
                break;
            case "disable":
                _console.MarkupLine($"[grey]Disabling ROSpec {rospecId}...[/]");
                await _reader.RoSpecs.DisableAsync(rospecId, cancellationToken);
                _console.MarkupLine($"[bold springgreen2]✔ ROSpec {rospecId} Disabled![/]");
                break;
            case "start":
                _console.MarkupLine($"[grey]Starting ROSpec {rospecId}...[/]");
                await _reader.RoSpecs.StartAsync(rospecId, cancellationToken);
                _console.MarkupLine($"[bold springgreen2]✔ ROSpec {rospecId} Started![/]");
                break;
            case "stop":
                _console.MarkupLine($"[grey]Stopping ROSpec {rospecId}...[/]");
                await _reader.RoSpecs.StopAsync(rospecId, cancellationToken);
                _console.MarkupLine($"[bold springgreen2]✔ ROSpec {rospecId} Stopped![/]");
                break;
            case "delete":
                _console.MarkupLine($"[grey]Deleting ROSpec {rospecId}...[/]");
                await _reader.RoSpecs.DeleteAsync(rospecId, cancellationToken);
                _console.MarkupLine($"[bold springgreen2]✔ ROSpec {rospecId} Deleted![/]");
                break;
            default:
                _console.MarkupLine($"[red]Unknown rospec sub-command '{subAction}'.[/]");
                return;
        }

        if (_observer != null)
        {
            IReadOnlyList<CapturedFrame> frames = _observer.CapturedFrames;
            if (frames.Count > startIndex)
            {
                var newFrames = frames.Skip(startIndex).ToList();
                foreach (CapturedFrame frame in newFrames)
                {
                    FrameRenderer.RenderObservedFrame(frame, _console, includeHexDump: true);
                    _console.WriteLine();
                }
            }
        }
    }

    private async Task HandleAccessSpecAsync(string[] tokens, CancellationToken cancellationToken)
    {
        if (_reader is null || !_reader.IsConnected)
        {
            _console.MarkupLine("[yellow]Not connected. Run 'connect <host>' first.[/]");
            return;
        }

        if (tokens.Length < 2)
        {
            _console.MarkupLine("[red]Usage:[/] accessspec list|enable|disable|delete [[id]]");
            return;
        }

        string subAction = tokens[1].ToLowerInvariant();
        uint accessSpecId = 1;
        if (tokens.Length >= 3 && uint.TryParse(tokens[2], out uint parsedId))
        {
            accessSpecId = parsedId;
        }

        switch (subAction)
        {
            case "list":
                _console.MarkupLine("[grey]Querying installed AccessSpecs...[/]");
                var accessSpecs = await _reader.AccessSpecs.GetAllAsync(cancellationToken);
                _console.MarkupLine($"[green]Found {accessSpecs.Count} AccessSpec(s).[/]");
                break;
            case "enable":
                _console.MarkupLine($"[grey]Enabling AccessSpec {accessSpecId}...[/]");
                await _reader.AccessSpecs.EnableAsync(accessSpecId, cancellationToken);
                _console.MarkupLine($"[bold springgreen2]✔ AccessSpec {accessSpecId} Enabled![/]");
                break;
            case "disable":
                _console.MarkupLine($"[grey]Disabling AccessSpec {accessSpecId}...[/]");
                await _reader.AccessSpecs.DisableAsync(accessSpecId, cancellationToken);
                _console.MarkupLine($"[bold springgreen2]✔ AccessSpec {accessSpecId} Disabled![/]");
                break;
            case "delete":
                _console.MarkupLine($"[grey]Deleting AccessSpec {accessSpecId}...[/]");
                await _reader.AccessSpecs.DeleteAsync(accessSpecId, cancellationToken);
                _console.MarkupLine($"[bold springgreen2]✔ AccessSpec {accessSpecId} Deleted![/]");
                break;
            default:
                _console.MarkupLine("[red]Usage:[/] accessspec list|enable|disable|delete [[id]]");
                break;
        }
    }

    private async Task HandleRawAsync(string[] tokens, CancellationToken cancellationToken)
    {
        if (_reader is null || !_reader.IsConnected)
        {
            _console.MarkupLine("[yellow]Not connected. Run 'connect <host>' first.[/]");
            return;
        }

        if (tokens.Length < 4)
        {
            throw new CliUsageException(
                "Usage: raw send|transact <hex-frame> [--response-type <type>] --yes");
        }

        string operation = tokens[1].ToLowerInvariant();
        byte[] requestFrame = Helpers.ParseHex(tokens[2]);
        LlrpMessageHeader requestHeader = Helpers.DecodeExactHeader(requestFrame);
        bool confirmed = tokens.Skip(3).Any(static token => token.Equals("--yes", StringComparison.OrdinalIgnoreCase));
        if (!confirmed)
        {
            throw new CliUsageException(
                "Raw protocol access can change reader state. Repeat the command with --yes to send it.");
        }

        switch (operation)
        {
            case "send":
                if (tokens.Length != 4 || !tokens[3].Equals("--yes", StringComparison.OrdinalIgnoreCase))
                {
                    throw new CliUsageException("Usage: raw send <hex-frame> --yes");
                }

                await _reader.Protocol.SendRawAsync(requestFrame, cancellationToken);
                _console.MarkupLine("[bold springgreen2]✔ Raw frame sent.[/]");
                break;

            case "transact":
                if (requestHeader.MessageId == 0)
                {
                    throw new CliUsageException("A raw transaction requires a non-zero message identifier.");
                }

                ushort? responseType = ParseRawResponseType(tokens.Skip(3).ToArray());
                ReadOnlyMemory<byte> response = await _reader.Protocol.TransactRawAsync(
                    requestFrame,
                    (header, _) => header.MessageId == requestHeader.MessageId &&
                        (!responseType.HasValue || (ushort)header.MessageType == responseType.Value),
                    cancellationToken: cancellationToken);
                _console.MarkupLine("[bold springgreen2]✔ Raw transaction completed.[/]");
                FrameRenderer.RenderFrameData(
                    LlrpFrameDirection.Receive,
                    DateTimeOffset.Now,
                    response.ToArray(),
                    _console);
                break;

            default:
                throw new CliUsageException("Usage: raw send|transact <hex-frame> [--response-type <type>] --yes");
        }

        if (!_reader.IsManagedStateSynchronized)
        {
            _console.MarkupLine(
                "[yellow]SDK-managed state is now unsynchronized. Run [cyan1]sync[/] before the next managed operation.[/]");
        }
    }

    private async Task HandleSynchronizeStateAsync(CancellationToken cancellationToken)
    {
        if (_reader is null || !_reader.IsConnected)
        {
            _console.MarkupLine("[yellow]Not connected. Run 'connect <host>' first.[/]");
            return;
        }

        _console.MarkupLine("[grey]Synchronizing reader-managed ROSpec and AccessSpec state...[/]");
        await _reader.SynchronizeStateAsync(cancellationToken);
        _console.MarkupLine("[bold springgreen2]✔ SDK-managed state synchronized.[/]");
    }

    private static ushort? ParseRawResponseType(string[] options)
    {
        ushort? responseType = null;
        for (int index = 0; index < options.Length; index++)
        {
            string option = options[index];
            if (option.Equals("--yes", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!option.Equals("--response-type", StringComparison.OrdinalIgnoreCase) || index + 1 >= options.Length)
            {
                throw new CliUsageException("Usage: raw transact <hex-frame> [--response-type <type>] --yes");
            }

            uint parsed = Helpers.ParseUInt32(options[++index], "--response-type");
            if (parsed > ushort.MaxValue || responseType.HasValue)
            {
                throw new CliUsageException("--response-type must be specified once as a UInt16 value.");
            }

            responseType = (ushort)parsed;
        }

        return responseType;
    }

    private void HandleInspect(string[] tokens)
    {
        if (tokens.Length < 2)
        {
            _console.MarkupLine("[red]Usage:[/] inspect <hex-frame>");
            return;
        }

        byte[] frame = Helpers.ParseHex(tokens[1]);
        LlrpMessageHeader header = Helpers.DecodeExactHeader(frame);
        FrameRenderer.RenderHeader(header, frame.Length, _console);
    }

    private void HandleDecode(string[] tokens)
    {
        if (tokens.Length < 2)
        {
            _console.MarkupLine("[red]Usage:[/] decode <hex-frame>");
            return;
        }

        byte[] frame = Helpers.ParseHex(tokens[1]);
        Helpers.DecodeExactHeader(frame);
        ILlrpMessage message = Helpers.CreateRegistry().DecodeMessage(frame);
        FrameRenderer.RenderDecodedMessage(message, frame, _console);
    }

    private void HandleValidate(string[] tokens)
    {
        if (tokens.Length < 2)
        {
            _console.MarkupLine("[red]Usage:[/] validate <hex-frame>");
            return;
        }

        byte[] frame = Helpers.ParseHex(tokens[1]);
        Helpers.DecodeExactHeader(frame);
        ILlrpMessage message = Helpers.CreateRegistry().DecodeMessage(frame);
        FrameRenderer.RenderValidationResult(isValid: true, message.GetType().Name, frame.Length, _console);
    }

    private void HandleEncode(string[] tokens)
    {
        if (tokens.Length < 2)
        {
            _console.MarkupLine("[red]Usage:[/] encode <message-name> [[--message-id ID]] [[--rospec-id ID]]");
            return;
        }

        string msgName = tokens[1];
        uint msgId = 1;
        uint? roSpecId = null;

        for (int i = 2; i < tokens.Length; i += 2)
        {
            if (i + 1 >= tokens.Length)
            {
                break;
            }
            if (tokens[i].Equals("--message-id", StringComparison.OrdinalIgnoreCase))
            {
                msgId = Helpers.ParseUInt32(tokens[i + 1], "--message-id");
            }
            else if (tokens[i].Equals("--rospec-id", StringComparison.OrdinalIgnoreCase))
            {
                roSpecId = Helpers.ParseUInt32(tokens[i + 1], "--rospec-id");
            }
        }

        ILlrpMessage message = msgName.ToLowerInvariant() switch
        {
            "keepalive" => new LlrpNet.Protocol.Messages.V1_0_1.KEEPALIVE(msgId),
            "keepalive-ack" => new LlrpNet.Protocol.Messages.V1_0_1.KEEPALIVE_ACK(msgId),
            "get-reader-capabilities" => new LlrpNet.Protocol.Messages.V1_0_1.GET_READER_CAPABILITIES(
                msgId,
                LlrpNet.Protocol.Enumerations.V1_0_1.GetReaderCapabilitiesRequestedData.All,
                Array.Empty<LlrpNet.Protocol.Parameters.ILlrpParameter>()),
            "get-rospecs" => new LlrpNet.Protocol.Messages.V1_0_1.GET_ROSPECS(msgId),
            "delete-rospec" => new LlrpNet.Protocol.Messages.V1_0_1.DELETE_ROSPEC(msgId, roSpecId ?? 1),
            "start-rospec" => new LlrpNet.Protocol.Messages.V1_0_1.START_ROSPEC(msgId, roSpecId ?? 1),
            "stop-rospec" => new LlrpNet.Protocol.Messages.V1_0_1.STOP_ROSPEC(msgId, roSpecId ?? 1),
            "enable-rospec" => new LlrpNet.Protocol.Messages.V1_0_1.ENABLE_ROSPEC(msgId, roSpecId ?? 1),
            "disable-rospec" => new LlrpNet.Protocol.Messages.V1_0_1.DISABLE_ROSPEC(msgId, roSpecId ?? 1),
            _ => throw new CliUsageException($"Encode message '{msgName}' is not supported."),
        };

        byte[] frame = Helpers.CreateRegistry().EncodeMessage(LlrpProtocolVersion.Version101, message);
        FrameRenderer.RenderEncodedHex(msgName, msgId, frame, _console);
    }

    private static void UpdateWindowTitle(string status)
    {
        try
        {
            Console.Title = $"LLRPCSharp Studio · {status}";
        }
        catch
        {
            // Ignore restricted terminals
        }
    }

    private void RenderBanner()
    {
        UpdateWindowTitle(_reader?.IsConnected == true ? $"{_currentHost}:{_currentPort}" : "offline");

        _console.Write(
            new FigletText("LLRPCSharp")
                .LeftJustified()
                .Color(Color.DeepSkyBlue1));

        var grid = new Grid();
        grid.AddColumn(new GridColumn().NoWrap().PadRight(2));
        grid.AddColumn(new GridColumn());

        grid.AddRow(
            "[bold springgreen2]📡 连接与通信[/]",
            "[grey][cyan1]connect <host> [[port]][/] 连接读写器 (如 [cyan1]connect 192.0.2.10[/])[/]");
        grid.AddRow(
            "[bold deepskyblue1]⚙️ ROSpec 与配置[/]",
            "[grey][cyan1]rospec list[/] / [cyan1]start[/] / [cyan1]stop[/] / [cyan1]caps[/] / [cyan1]status[/][/]");
        grid.AddRow(
            "[bold yellow1]🏷️ 托管盘点流[/]",
            "[grey][cyan1]inventory start|stop[/] 开启/停止 SDK 托管盘点[/]");
        grid.AddRow(
            "[bold cyan1]📡 被动推流监听[/]",
            "[grey][cyan1]monitor 10[/] 纯被动接收打印 10 秒 TCP 回调帧[/]");
        grid.AddRow(
            "[bold magenta1]🔍 协议诊断工具[/]",
            "[grey][cyan1]inspect <hex>[/] / [cyan1]decode <hex>[/] / [cyan1]frames[/] / [cyan1]help[/][/]");

        var panel = new Panel(grid)
        {
            Header = new PanelHeader("[bold white on deepskyblue1] 📡 LLRP C# SDK Terminal Studio [/] [grey70]v1.0.1[/]", Justify.Center),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.DeepSkyBlue1),
            Padding = new Padding(1, 0, 1, 0)
        };

        _console.Write(panel);
        _console.WriteLine();
    }

    private void RenderHelp()
    {
        var table = new Table
        {
            Border = TableBorder.Rounded,
            BorderStyle = new Style(Color.DeepSkyBlue1)
        };
        table.AddColumn("[bold deepskyblue1]📌 指令 (Command)[/]");
        table.AddColumn("[bold grey70]📝 说明 (Description)[/]");

        // 分组 1: 连接与会话状态
        table.AddRow("[bold yellow1]─── 🌐 连接与会话状态 (Connection & Status) ───[/]", "");
        table.AddRow("  [cyan1]connect <host> [[port]] [[--llrp auto|1.0.1|1.1]][/]", "连接到远程 RFID 读写器并完成版本协商");
        table.AddRow("  [cyan1]disconnect[/]", "断开当前读写器 TCP 会话");
        table.AddRow("  [cyan1]status[/]", "显示当前连接状态、协商版本与读写器元数据");
        table.AddRow("  [cyan1]caps[/]", "显示读写器硬件能力参数 (Capabilities)");

        // 分组 2: 高层托管盘点 (Managed Inventory)
        table.AddRow("[bold yellow1]─── 🚀 高层托管盘点 (Managed Inventory API) ───[/]", "");
        table.AddRow("  [cyan1]inventory start [[antenna-id]] | stop | status[/]", "一键托管盘点 (SDK 自动处理 ADD/ENABLE/START ROSpec 声明)");

        // 分组 3: 纯被动推流监听 (Passive Monitoring)
        table.AddRow("[bold yellow1]─── 📡 纯被动推流监听 (Passive Monitoring) ───[/]", "");
        table.AddRow("  [cyan1]monitor [[seconds]] [[--table | --frames]][/]", "被动推流监听 (--table 实时汇总表, --frames 原始报文树)");
        table.AddRow("  [cyan1]frames [[count]][/]", "展示最近捕获的原始收发 LLRP 帧日志");

        // 分组 4: 进阶底层资源操控 (Advanced Resource API)
        table.AddRow("[bold yellow1]─── ⚙️ 进阶底层资源操控 (Advanced Resource API) ───[/]", "");
        table.AddRow("  [cyan1]rospec list|enable|disable|start|stop|delete [[id]][/]", "声明式管理设备 ROSpec 资源 (独立控制指定 ROSpec ID)");
        table.AddRow("  [cyan1]accessspec list|enable|disable|delete [[id]][/]", "声明式管理设备 AccessSpec 资源 (密码/Memory 读写)");
        table.AddRow("  [cyan1]raw send|transact <hex> [[--response-type type]] --yes[/]", "精准发送或收发原始二进制 Hex 报文");
        table.AddRow("  [cyan1]sync[/]", "同步 Raw 操作后的托管状态与配置缓存");

        // 分组 5: 报文工具与终端
        table.AddRow("[bold yellow1]─── 🛠️ 报文工具与终端 (Codec & Utilities) ───[/]", "");
        table.AddRow("  [cyan1]inspect <hex>[/]", "检查单个 Hex 报文的 Header 结构");
        table.AddRow("  [cyan1]decode <hex>[/]", "将 Hex 报文解码为树状结构或 JSON");
        table.AddRow("  [cyan1]validate <hex>[/]", "校验 LLRP 报文结构完整性与长度");
        table.AddRow("  [cyan1]encode <msg>[/]", "将标准 LLRP 消息编码为 Hex 二进制");
        table.AddRow("  [cyan1]clear | cls[/]", "清空终端屏幕");
        table.AddRow("  [cyan1]exit | quit[/]", "退出交互式 Live Shell 终端");

        _console.Write(table);
    }

    private static string[] Tokenize(string text)
    {
        var list = new List<string>();
        var sb = new System.Text.StringBuilder();
        bool inQuotes = false;

        foreach (char c in text)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (sb.Length > 0)
                {
                    list.Add(sb.ToString());
                    sb.Clear();
                }
            }
            else
            {
                sb.Append(c);
            }
        }

        if (sb.Length > 0)
        {
            list.Add(sb.ToString());
        }

        return list.ToArray();
    }
}
