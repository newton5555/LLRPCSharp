using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Messages;
using LlrpSdk;
using LlrpCli.Analysis;
using LlrpCli.Rendering;
using LlrpCli.Terminal;

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
                ? $"[grey]llrp[/] [springgreen2]({_currentHost}:{_currentPort})[/] [bold]>[/]"
                : "[grey]llrp[/] [red](disconnected)[/] [bold]>[/]";

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
                    case "frames":
                        HandleFrames(tokens);
                        break;
                    case "rospec":
                        await HandleRospecAsync(tokens, cancellationToken);
                        break;
                    case "accessspec":
                        await HandleAccessSpecAsync(tokens, cancellationToken);
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

        _observer = new DelegateFrameObserver(_ => { });

        _console.MarkupLine($"[grey]Connecting to LLRP Reader at[/] [cyan1]{Markup.Escape(host)}:{port}[/]...");

        var reader = LlrpReader.CreateBuilder(host)
            .WithPort(port)
            .WithConnectTimeout(TimeSpan.FromSeconds(5))
            .WithFrameObserver(_observer)
            .WithProtocolVersionPolicy(protocolVersionPolicy)
            .Build();

        try
        {
            await reader.ConnectAsync(cancellationToken);
            _reader = reader;
            _currentHost = host;
            _currentPort = port;

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
            _console.MarkupLine("[red]Usage:[/] inventory start [antenna-id] | stop | status");
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
                _console.MarkupLine("[red]Usage:[/] inventory start [antenna-id] | stop | status");
                break;
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
            _console.MarkupLine("[red]Usage:[/] rospec list|enable|disable|start|stop|delete [id]");
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
            _console.MarkupLine("[red]Usage:[/] accessspec list|enable|disable|delete [id]");
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
                _console.MarkupLine("[red]Usage:[/] accessspec list|enable|disable|delete [id]");
                break;
        }
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
            _console.MarkupLine("[red]Usage:[/] encode <message-name> [--message-id ID] [--rospec-id ID]");
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

    private void RenderBanner()
    {
        var rule = new Rule("[bold deepskyblue1]LLRP C# SDK Interactive Live Shell[/]")
        {
            Style = Style.Parse("cyan1")
        };
        _console.Write(rule);
        _console.MarkupLine("[grey]Type [cyan1]connect <host> [port] --llrp 1.0.1[/] to force LLRP 1.0.1, or [cyan1]help[/] for commands.[/]");
        _console.WriteLine();
    }

    private void RenderHelp()
    {
        var table = new Table();
        table.AddColumn("[bold grey70]Command[/]");
        table.AddColumn("[bold grey70]Description[/]");

        table.AddRow("[cyan1]connect <host> [port] [--llrp auto|1.0.1|1.1][/]", "Connect to an LLRP Reader");
        table.AddRow("[cyan1]disconnect[/]", "Disconnect current Reader session");
        table.AddRow("[cyan1]status[/]", "Show current connection status and metadata");
        table.AddRow("[cyan1]caps[/]", "Display Reader capabilities");
        table.AddRow("[cyan1]frames [count][/]", "Show recent captured LLRP message frames");
        table.AddRow("[cyan1]rospec list|enable|disable|start|stop|delete[/]", "Manage ROSpecs");
        table.AddRow("[cyan1]inspect <hex>[/]", "Inspect raw hex LLRP header");
        table.AddRow("[cyan1]decode <hex>[/]", "Decode raw hex into parameter tree");
        table.AddRow("[cyan1]validate <hex>[/]", "Validate LLRP frame integrity");
        table.AddRow("[cyan1]encode <msg>[/]", "Encode standard LLRP message to hex");
        table.AddRow("[cyan1]clear[/]", "Clear console screen");
        table.AddRow("[cyan1]exit | quit[/]", "Exit interactive live shell");

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
