using LlrpDevice.Virtual;
using LlrpDevice.Virtual.Hosting;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace LlrpVirtualDevice.Cli;

internal sealed class VirtualDeviceShell
{
    private readonly IAnsiConsole _console;
    private readonly TextReader _input;
    private readonly object _writeGate = new();
    private IVirtualLlrpDeviceHost? _host;
    private VirtualLlrpDeviceHostOptions? _hostOptions;
    private bool _logsEnabled = true;

    public VirtualDeviceShell(IAnsiConsole console, TextReader input)
    {
        _console = console ?? throw new ArgumentNullException(nameof(console));
        _input = input ?? throw new ArgumentNullException(nameof(input));
    }

    public async Task<int> RunAsync(
        VirtualDeviceLaunchOptions? initialLaunch,
        bool autoStart,
        CancellationToken cancellationToken)
    {
        RenderBanner();
        PrintHelp();
        using var stopSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            stopSource.Cancel();
        };

        Console.CancelKeyPress += cancelHandler;
        try
        {
            if (initialLaunch is not null)
            {
                await CreateServerAsync(initialLaunch, autoStart, stopSource.Token).ConfigureAwait(false);
            }

            while (!stopSource.IsCancellationRequested)
            {
                WritePrompt();
                string? line;
                try
                {
                    line = await _input.ReadLineAsync(stopSource.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stopSource.IsCancellationRequested)
                {
                    break;
                }

                if (line is null)
                {
                    break;
                }

                WriteInputTerminator();
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                bool shouldExit = await ExecuteLineAsync(line, stopSource.Token).ConfigureAwait(false);
                if (shouldExit)
                {
                    break;
                }
            }

            return 0;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
            await DisposeServerAsync().ConfigureAwait(false);
        }
    }

    private async Task<bool> ExecuteLineAsync(string line, CancellationToken cancellationToken)
    {
        try
        {
            string[] tokens = VirtualDeviceShellParser.Tokenize(line);
            if (tokens.Length == 0)
            {
                return false;
            }

            string command = tokens[0].ToLowerInvariant();
            return command switch
            {
                "help" or "?" => ExecuteHelp(tokens),
                "server" => await ExecuteServerAsync(tokens, cancellationToken).ConfigureAwait(false),
                "create" => await ExecuteServerAsync(PrependServer(tokens, "create"), cancellationToken).ConfigureAwait(false),
                "run" => await ExecuteServerAsync(PrependServer(tokens, "run"), cancellationToken).ConfigureAwait(false),
                "start" => await ExecuteServerAsync(PrependServer(tokens, "start"), cancellationToken).ConfigureAwait(false),
                "stop" => await ExecuteServerAsync(PrependServer(tokens, "stop"), cancellationToken).ConfigureAwait(false),
                "restart" => await ExecuteServerAsync(PrependServer(tokens, "restart"), cancellationToken).ConfigureAwait(false),
                "status" => await ExecuteServerAsync(PrependServer(tokens, "status"), cancellationToken).ConfigureAwait(false),
                "destroy" => await ExecuteServerAsync(PrependServer(tokens, "destroy"), cancellationToken).ConfigureAwait(false),
                "logs" => ExecuteLogs(tokens),
                "presets" => ExecutePresets(tokens),
                "caps" or "profiles" => ExecuteCapabilityProfiles(tokens),
                "validate" => ExecuteValidate(tokens),
                "clear" or "cls" => ExecuteClear(tokens),
                "exit" or "quit" or "q" => true,
                _ => throw new ArgumentException($"Unknown command '{tokens[0]}'. Type 'help' for available commands."),
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return true;
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or InvalidDataException
                or FormatException
                or OverflowException
                or System.Text.Json.JsonException)
        {
            WriteError(exception.Message);
            return false;
        }
        catch (Exception exception)
        {
            WriteError($"Virtual device command failed: {exception.Message}");
            return false;
        }
    }

    private bool ExecuteHelp(string[] tokens)
    {
        if (tokens.Length > 2)
        {
            throw new ArgumentException("Usage: help [server|create|run|logs|status]");
        }

        if (tokens.Length == 2)
        {
            switch (tokens[1].ToLowerInvariant())
            {
                case "server":
                case "create":
                case "run":
                case "start":
                case "stop":
                case "restart":
                case "status":
                case "destroy":
                    PrintServerHelp();
                    return false;
                case "logs":
                    PrintLogsHelp();
                    return false;
                case "caps":
                case "profiles":
                    ExecuteCapabilityProfiles(tokens);
                    return false;
                default:
                    throw new ArgumentException($"Unknown help topic '{tokens[1]}'.");
            }
        }

        PrintHelp();
        return false;
    }

    private async Task<bool> ExecuteServerAsync(string[] tokens, CancellationToken cancellationToken)
    {
        if (tokens.Length < 2)
        {
            PrintServerHelp();
            return false;
        }

        string subcommand = tokens[1].ToLowerInvariant();
        switch (subcommand)
        {
            case "create":
                await ExecuteCreateAsync(tokens, cancellationToken, startAfterCreate: false).ConfigureAwait(false);
                return false;
            case "run":
                await ExecuteCreateAsync(tokens, cancellationToken, startAfterCreate: true).ConfigureAwait(false);
                return false;
            case "start":
                RequireNoArguments(tokens, 2, "Usage: server start");
                await StartServerAsync(cancellationToken).ConfigureAwait(false);
                return false;
            case "stop":
                RequireNoArguments(tokens, 2, "Usage: server stop");
                await StopServerAsync(cancellationToken).ConfigureAwait(false);
                return false;
            case "restart":
                RequireNoArguments(tokens, 2, "Usage: server restart");
                await RestartServerAsync(cancellationToken).ConfigureAwait(false);
                return false;
            case "status":
                RequireNoArguments(tokens, 2, "Usage: server status");
                RenderServerStatus();
                return false;
            case "destroy":
                RequireNoArguments(tokens, 2, "Usage: server destroy");
                await DisposeServerAsync(cancellationToken).ConfigureAwait(false);
                WriteCommandLine("Server destroyed; no virtual device is currently created.");
                return false;
            case "help":
                PrintServerHelp();
                return false;
            default:
                throw new ArgumentException($"Unknown server command '{tokens[1]}'.");
        }
    }

    private async Task ExecuteCreateAsync(
        string[] tokens,
        CancellationToken cancellationToken,
        bool startAfterCreate)
    {
        if (tokens.Length == 3 && tokens[2] is "--help" or "-h")
        {
            PrintCreateHelp();
            return;
        }

        bool start = startAfterCreate;
        var launchTokens = new List<string> { "create" };
        for (int index = 2; index < tokens.Length; index++)
        {
            if (tokens[index] is "--start")
            {
                start = true;
                continue;
            }

            launchTokens.Add(tokens[index]);
        }

        VirtualDeviceLaunchOptions launch = VirtualDeviceCliApplication.ParseLaunchOptions(
            launchTokens,
            start: 1,
            command: "server create");
        await CreateServerAsync(launch, start, cancellationToken).ConfigureAwait(false);
    }

    private async Task CreateServerAsync(
        VirtualDeviceLaunchOptions launch,
        bool start,
        CancellationToken cancellationToken)
    {
        if (_host is not null)
        {
            throw new InvalidOperationException(
                "A virtual device already exists. Stop and destroy it before creating another one.");
        }

        VirtualLlrpDeviceHostOptions hostOptions = VirtualDeviceCliApplication.BuildHostOptions(launch);
        var host = new VirtualLlrpDeviceHost(hostOptions);
        _host = host;
        _hostOptions = hostOptions;
        AttachHostEvents(host);

        WriteCommandLine(
            $"Created virtual device '{host.Device.Identity.Name}' in state {host.State}. " +
            "Use 'server start' to bind the LLRP listener.");

        if (start)
        {
            await StartServerAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task StartServerAsync(CancellationToken cancellationToken)
    {
        IVirtualLlrpDeviceHost host = RequireHost();
        if (host.State is VirtualLlrpDeviceHostState.Running or VirtualLlrpDeviceHostState.Starting)
        {
            throw new InvalidOperationException("The virtual LLRP server is already running or starting.");
        }

        await host.StartAsync(cancellationToken).ConfigureAwait(false);
        WriteCommandLine(
            $"Listening on {VirtualDeviceCliApplication.FormatEndpoint(host.ListenAddress, host.BoundPort)} " +
            $"using LLRP {VirtualDeviceCliApplication.FormatProtocolVersion(_hostOptions!.Server.ProtocolVersion)}.");
    }

    private async Task StopServerAsync(CancellationToken cancellationToken)
    {
        IVirtualLlrpDeviceHost host = RequireHost();
        if (host.State is VirtualLlrpDeviceHostState.Created or VirtualLlrpDeviceHostState.Stopped)
        {
            WriteCommandLine($"Server is already {host.State.ToString().ToLowerInvariant()}.");
            return;
        }

        await host.StopAsync(cancellationToken).ConfigureAwait(false);
        WriteCommandLine("LLRP server stopped.");
    }

    private async Task RestartServerAsync(CancellationToken cancellationToken)
    {
        IVirtualLlrpDeviceHost host = RequireHost();
        if (host.State == VirtualLlrpDeviceHostState.Created)
        {
            await StartServerAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        await host.RestartAsync(cancellationToken).ConfigureAwait(false);
        WriteCommandLine(
            $"LLRP server restarted on {VirtualDeviceCliApplication.FormatEndpoint(host.ListenAddress, host.BoundPort)}.");
    }

    private async Task DisposeServerAsync(CancellationToken cancellationToken = default)
    {
        IVirtualLlrpDeviceHost? host = _host;
        if (host is null)
        {
            return;
        }

        try
        {
            if (host.State is not (VirtualLlrpDeviceHostState.Created or VirtualLlrpDeviceHostState.Stopped))
            {
                await host.StopAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            DetachHostEvents(host);
            await host.DisposeAsync().ConfigureAwait(false);
            _host = null;
            _hostOptions = null;
        }
    }

    private bool ExecuteLogs(string[] tokens)
    {
        if (tokens.Length > 2)
        {
            throw new ArgumentException("Usage: logs [on|off|status]");
        }

        if (tokens.Length == 1 || tokens[1].Equals("status", StringComparison.OrdinalIgnoreCase))
        {
            WriteCommandLine($"Event log streaming is {(_logsEnabled ? "on" : "off")}.");
            return false;
        }

        switch (tokens[1].ToLowerInvariant())
        {
            case "on":
                _logsEnabled = true;
                WriteCommandLine("Event log streaming enabled.");
                return false;
            case "off":
                _logsEnabled = false;
                WriteCommandLine("Event log streaming disabled.");
                return false;
            default:
                throw new ArgumentException("Usage: logs [on|off|status]");
        }
    }

    private bool ExecutePresets(string[] tokens)
    {
        RequireNoArguments(tokens, 1, "Usage: presets");
        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("[bold]Preset[/]");
        table.AddColumn("[bold]Protocol[/]");
        table.AddColumn("[bold]Description[/]");
        foreach (VirtualDevicePreset preset in VirtualDevicePresets.All)
        {
            table.AddRow(
                Markup.Escape(preset.Id),
                Markup.Escape(preset.ProtocolVersion),
                Markup.Escape(preset.Description));
        }

        WriteRenderable(table);
        return false;
    }

    private bool ExecuteCapabilityProfiles(string[] tokens)
    {
        RequireNoArguments(tokens, 1, "Usage: caps");
        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("[bold]Capability profile[/]");
        table.AddColumn("[bold]LLRP[/]");
        table.AddColumn("[bold]Antennas[/]");
        foreach (VirtualDeviceCapabilityProfile profile in VirtualDeviceCapabilityProfileCatalog.All)
        {
            table.AddRow(
                Markup.Escape(profile.Id),
                Markup.Escape(profile.ProtocolVersion),
                profile.Capabilities.MaxNumberOfAntennas.ToString());
        }

        WriteRenderable(table);
        return false;
    }

    private bool ExecuteValidate(string[] tokens)
    {
        if (tokens.Length != 3 || !tokens[1].Equals("--config", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Usage: validate --config <PATH>");
        }

        VirtualDeviceConfigurationDocument document = VirtualDeviceConfiguration.Load(tokens[2]);
        _ = VirtualDeviceCliApplication.BuildHostOptions(
            new VirtualDeviceLaunchOptions { ConfigPath = tokens[2] });
        WriteCommandLine(
            $"Configuration is valid: capability profile '{document.CapabilityProfileId}', " +
            $"inventory source '{document.InventoryDataSource}', " +
            $"LLRP {document.ProtocolVersion ?? VirtualDevicePresets.Get(document.PresetId).ProtocolVersion}.");
        return false;
    }

    private bool ExecuteClear(string[] tokens)
    {
        RequireNoArguments(tokens, 1, "Usage: clear");
        _console.Clear();
        RenderBanner();
        return false;
    }

    private void RenderBanner()
    {
        lock (_writeGate)
        {
            _console.Write(new Rule("[bold deepskyblue1] LLRP Virtual Device Shell [/]"));
            _console.MarkupLine("[grey]One process hosts one virtual LLRP device.[/]");
            _console.MarkupLine("[grey]Type [cyan]help[/] for commands; [cyan]server create[/] prepares the device.[/]");
            _console.WriteLine();
        }
    }

    private void PrintHelp()
    {
        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("[bold]Command[/]");
        table.AddColumn("[bold]Description[/]");
        table.AddRow(Markup.Escape("server create [options]"), "Create one virtual device host without binding TCP.");
        table.AddRow(Markup.Escape("server run [options]"), "Create and start one virtual device host.");
        table.AddRow("server start", "Start the LLRP TCP listener.");
        table.AddRow("server stop", "Stop the listener and connected sessions.");
        table.AddRow("server restart", "Restart the same virtual device instance.");
        table.AddRow("server status", "Show lifecycle, endpoint, protocol, and client count.");
        table.AddRow("server destroy", "Dispose the current device so another can be created.");
        table.AddRow("logs on|off|status", "Control lifecycle, client, and RX/TX event output.");
        table.AddRow("presets", "List built-in device presets.");
        table.AddRow("caps", "List capability profiles; currently llrp1.0.1_standard.");
        table.AddRow("validate --config PATH", "Validate one local device configuration.");
        table.AddRow("clear", "Clear the terminal.");
        table.AddRow("exit", "Stop and dispose the device, then leave the shell.");
        WriteRenderable(table);
    }

    private void PrintServerHelp()
    {
        _console.MarkupLine("[bold deepskyblue1]Server commands[/]");
        _console.MarkupLine($"  [cyan]{Markup.Escape("server create [--start] [options]")}[/]");
        _console.MarkupLine($"  [cyan]{Markup.Escape("server run [options]")}[/]");
        _console.MarkupLine("  [cyan]server start|stop|restart|status|destroy[/]");
        _console.MarkupLine("[grey]Create options: --config, --caps, --data-source, --listen, --port, --llrp, --name, --tag, RF and report options.[/]");
    }

    private void PrintCreateHelp()
    {
        _console.MarkupLine("[bold deepskyblue1]Usage:[/] server create [--start] [options]");
        _console.MarkupLine("  [cyan]--config PATH[/]   Load one local JSON device configuration.");
        _console.MarkupLine("  [cyan]--caps ID[/]       Select capability profile; default llrp1.0.1_standard.");
        _console.MarkupLine("  [cyan]--data-source X[/] Use default or a separate inventory JSON path.");
        _console.MarkupLine("  [cyan]--listen IP[/]     Listen address; default is 127.0.0.1.");
        _console.MarkupLine("  [cyan]--port PORT[/]     TCP port; default is 5084.");
        _console.MarkupLine("  [cyan]--llrp VERSION[/]  1.0.1, 1.1, or 2.0.");
        _console.MarkupLine("[grey]IP/port are creation-time endpoint options and are not stored in the device config.[/]");
    }

    private void PrintLogsHelp()
    {
        _console.MarkupLine($"[bold deepskyblue1]Usage:[/] {Markup.Escape("logs [on|off|status]")}");
        _console.MarkupLine("Event output includes lifecycle changes, clients, and decoded RX/TX LLRP messages.");
    }

    private void RenderServerStatus()
    {
        if (_host is null)
        {
            _console.MarkupLine("[yellow]No virtual device has been created.[/]");
            return;
        }

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("[bold]Property[/]");
        table.AddColumn("[bold]Value[/]");
        AddStatusRow(table, "State", _host.State.ToString());
        AddStatusRow(table, "Device", _host.Device.Identity.Name);
        AddStatusRow(table, "Capability profile", _hostOptions!.Device.CapabilityProfileId ?? "direct SDK options");
        AddStatusRow(table, "Inventory data source", _hostOptions!.InventoryDataSource?.Id ?? "device options");
        AddStatusRow(table, "Listen address", _host.ListenAddress.ToString());
        AddStatusRow(table, "Configured port", _host.ConfiguredPort.ToString());
        AddStatusRow(table, "Bound port", _host.BoundPort.ToString());
        AddStatusRow(table, "Protocol", VirtualDeviceCliApplication.FormatProtocolVersion(_hostOptions!.Server.ProtocolVersion));
        AddStatusRow(table, "Connected clients", _host.ConnectedClientCount.ToString());
        AddStatusRow(table, "Event logs", _logsEnabled ? "on" : "off");
        WriteRenderable(new Panel(table).Header("[bold deepskyblue1] SERVER STATUS [/]"));
    }

    private void AddStatusRow(Table table, string name, string value)
    {
        table.AddRow(Markup.Escape(name), Markup.Escape(value));
    }

    private void WritePrompt()
    {
        string state = _host is null
            ? "[grey](empty)[/]"
            : $"[springgreen2]({_host.State.ToString().ToLowerInvariant()})[/]";
        lock (_writeGate)
        {
            _console.Markup($"[deepskyblue1 bold]vdev[/] {state} [bold cyan]>[/] ");
        }
    }

    private void WriteInputTerminator()
    {
        lock (_writeGate)
        {
            _console.WriteLine();
        }
    }

    private void WriteCommandLine(string message)
    {
        lock (_writeGate)
        {
            _console.MarkupLine($"[grey]{DateTimeOffset.Now:HH:mm:ss.fff}[/] {Markup.Escape(message)}");
        }
    }

    private void WriteError(string message)
    {
        lock (_writeGate)
        {
            _console.MarkupLine($"[bold red]Error:[/] {Markup.Escape(message)}");
        }
    }

    private void WriteEvent(string category, string message)
    {
        if (!_logsEnabled)
        {
            return;
        }

        string color = category switch
        {
            "RX" => "springgreen2",
            "TX" => "deepskyblue1",
            "client" => "yellow",
            _ => "orchid",
        };
        string categoryLabel = Markup.Escape($"[{category}]");
        lock (_writeGate)
        {
            _console.MarkupLine(
                $"[grey]{DateTimeOffset.Now:HH:mm:ss.fff}[/] [bold {color}]{categoryLabel}[/] {Markup.Escape(message)}");
        }
    }

    private void AttachHostEvents(IVirtualLlrpDeviceHost host)
    {
        host.LifecycleChanged += OnLifecycleChanged;
        host.ClientChanged += OnClientChanged;
        host.MessageObserved += OnMessageObserved;
    }

    private void DetachHostEvents(IVirtualLlrpDeviceHost host)
    {
        host.LifecycleChanged -= OnLifecycleChanged;
        host.ClientChanged -= OnClientChanged;
        host.MessageObserved -= OnMessageObserved;
    }

    private void OnLifecycleChanged(object? sender, VirtualLlrpDeviceHostLifecycleChangedEventArgs args)
    {
        string detail = $"{args.PreviousState} -> {args.CurrentState}";
        if (args.CurrentState == VirtualLlrpDeviceHostState.Running && _host is not null)
        {
            detail += $"; listening on {VirtualDeviceCliApplication.FormatEndpoint(_host.ListenAddress, _host.BoundPort)}";
        }

        if (args.Error is not null)
        {
            detail += $"; error={args.Error.Message}";
        }

        WriteEvent("server", detail);
    }

    private void OnClientChanged(object? sender, VirtualLlrpDeviceHostClientChangedEventArgs args)
    {
        WriteEvent(
            "client",
            $"{(args.Connected ? "connected" : "disconnected")} {args.Client.ConnectionId} " +
            $"remote={args.Client.RemoteEndPoint?.ToString() ?? "unknown"}");
    }

    private void OnMessageObserved(object? sender, VirtualLlrpDeviceHostMessageObservedEventArgs args)
    {
        WriteEvent(
            args.Incoming ? "RX" : "TX",
            $"{args.Detail ?? "LLRP message"} " +
            $"version={VirtualDeviceCliApplication.FormatProtocolVersion(args.Version)} " +
            $"type={args.MessageType} id={args.MessageId} connection={args.ConnectionId}");
    }

    private IVirtualLlrpDeviceHost RequireHost() =>
        _host ?? throw new InvalidOperationException(
            "No virtual device has been created. Run 'server create' first.");

    private void WriteRenderable(IRenderable renderable)
    {
        lock (_writeGate)
        {
            _console.Write(renderable);
            _console.WriteLine();
        }
    }

    private static string[] PrependServer(string[] tokens, string subcommand) =>
        ["server", subcommand, .. tokens.Skip(1)];

    private static void RequireNoArguments(string[] tokens, int start, string usage)
    {
        if (tokens.Length > start)
        {
            throw new ArgumentException(usage);
        }
    }
}
