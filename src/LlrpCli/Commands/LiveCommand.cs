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

    [CommandOption("--vendor <VENDOR>")]
    [Description("Vendor extensions mode for automatic connection: auto, impinj, seuic, or none.")]
    [DefaultValue("auto")]
    public string Vendor { get; init; } = "auto";
}

public sealed class LiveCommand : AsyncCommand<LiveSettings>
{
    private readonly IAnsiConsole _console;
    private readonly LiveSessionContext _session = new();
    private readonly LiveInventoryHandler _inventoryHandler;
    private readonly LiveMonitorHandler _monitorHandler;
    private readonly LiveConnectionHandler _connectionHandler;
    private readonly LiveTagAccessHandler _tagAccessHandler;

    public LiveCommand() : this(AnsiConsole.Console) { }

    public LiveCommand(IAnsiConsole console)
    {
        _console = console ?? AnsiConsole.Console;
        _monitorHandler = new LiveMonitorHandler(_console, _session);
        _inventoryHandler = new LiveInventoryHandler(_console, _session, _monitorHandler);
        _connectionHandler = new LiveConnectionHandler(_console, _session, _inventoryHandler);
        _tagAccessHandler = new LiveTagAccessHandler(_console, _session);
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, LiveSettings settings, CancellationToken cancellationToken)
    {
        RenderBanner();

        if (!string.IsNullOrWhiteSpace(settings.Host))
        {
            if (!CliConnectionOptions.TryCreate(
                settings.Host,
                settings.Port,
                settings.LlrpVersion,
                settings.Vendor,
                out CliConnectionOptions options,
                out string error))
            {
                _console.MarkupLine($"[bold red]✖ Invalid connection option:[/] {Markup.Escape(error)}");
            }
            else
            {
                await ConnectToReaderAsync(options, cancellationToken);
            }
        }

        using var editor = new TerminalLineEditor();

        while (!cancellationToken.IsCancellationRequested)
        {
            bool isConnected = _session.Reader?.IsConnected == true;
            string promptState = isConnected
                ? $"[deepskyblue1 bold]📡 llrp[/] [springgreen2]({_session.Host}:{_session.Port})[/] [bold cyan]>[/]"
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
            string[] tokens = LiveCommandParser.Tokenize(line);
            if (tokens.Length == 0)
            {
                continue;
            }

            CommandSpec? command = CommandCatalog.Find(tokens[0]);
            if (command?.Route == LiveCommandRoute.Exit)
            {
                _console.MarkupLine("[grey]Exiting live mode... Bye![/]");
                break;
            }

            try
            {
                if (command is null)
                {
                    await HandleUnknownInputAsync(tokens, cancellationToken);
                }
                else
                {
                    switch (command.Route)
                    {
                    case LiveCommandRoute.Connect:
                        await HandleConnectAsync(tokens, cancellationToken);
                        break;
                    case LiveCommandRoute.Disconnect:
                        await _connectionHandler.DisconnectAsync(cancellationToken);
                        break;
                    case LiveCommandRoute.Status:
                        HandleStatus();
                        break;
                    case LiveCommandRoute.Capabilities:
                        HandleCaps();
                        break;
                    case LiveCommandRoute.Inventory:
                        await _inventoryHandler.HandleAsync(tokens, cancellationToken);
                        break;
                    case LiveCommandRoute.Monitor:
                        await _monitorHandler.HandleAsync(tokens, cancellationToken);
                        break;
                    case LiveCommandRoute.Frames:
                        HandleFrames(tokens);
                        break;
                    case LiveCommandRoute.RoSpec:
                        await HandleRospecAsync(tokens, cancellationToken);
                        break;
                    case LiveCommandRoute.AccessSpec:
                        await HandleAccessSpecAsync(tokens, cancellationToken);
                        break;
                    case LiveCommandRoute.Configuration:
                        await HandleConfigAsync(tokens, cancellationToken);
                        break;
                    case LiveCommandRoute.TagAccess:
                        await _tagAccessHandler.HandleAsync(tokens, cancellationToken);
                        break;
                    case LiveCommandRoute.Raw:
                        await HandleRawAsync(tokens, cancellationToken);
                        break;
                    case LiveCommandRoute.Synchronize:
                        await HandleSynchronizeStateAsync(cancellationToken);
                        break;
                    case LiveCommandRoute.Inspect:
                        LiveProtocolDiagnostics.Inspect(tokens, _console);
                        break;
                    case LiveCommandRoute.Decode:
                        LiveProtocolDiagnostics.Decode(tokens, _console);
                        break;
                    case LiveCommandRoute.Validate:
                        LiveProtocolDiagnostics.Validate(tokens, _console);
                        break;
                    case LiveCommandRoute.Encode:
                        LiveProtocolDiagnostics.Encode(tokens, _console);
                        break;
                    case LiveCommandRoute.Clear:
                        _console.Clear();
                        RenderBanner();
                        break;
                    case LiveCommandRoute.Help:
                        if (tokens.Length > 1)
                        {
                            RenderCommandHelp(tokens[1]);
                        }
                        else
                        {
                            RenderHelp();
                        }
                        break;
                    default:
                        throw new InvalidOperationException($"Unsupported live command route '{command.Route}'.");
                    }
                }
            }
            catch (Exception ex)
            {
                _console.MarkupLine($"[red]Error:[/] {Markup.Escape(ex.Message)}");
            }

            _console.WriteLine();
        }

        await _connectionHandler.DisposeAsync();

        return 0;
    }

    private async Task HandleUnknownInputAsync(string[] tokens, CancellationToken cancellationToken)
    {
        string verb = tokens[0].ToLowerInvariant();
        if (tokens.Length == 1 && (verb.Contains('.') || verb == "localhost" || verb == "127.0.0.1"))
        {
            await ConnectToReaderAsync(
                new CliConnectionOptions(
                    tokens[0],
                    5084,
                    LlrpProtocolVersionPolicy.Auto,
                    VendorExtensionMode.Auto),
                cancellationToken);
            return;
        }

        _console.MarkupLine($"[red]Unknown command '{Markup.Escape(tokens[0])}'.[/] Type [cyan1]help[/] for available commands.");
    }

    private async Task HandleConnectAsync(string[] tokens, CancellationToken cancellationToken)
    {
        if (tokens.Length < 2)
        {
            string host = _console.Prompt(
                new TextPrompt<string>("[grey]Enter Reader Host/IP:[/]")
                    .DefaultValue("127.0.0.1"));
            await ConnectToReaderAsync(
                new CliConnectionOptions(
                    host,
                    5084,
                    LlrpProtocolVersionPolicy.Auto,
                    VendorExtensionMode.Auto),
                cancellationToken);
            return;
        }

        await ConnectToReaderAsync(LiveCommandParser.ParseConnect(tokens), cancellationToken);
    }

    private async Task ConnectToReaderAsync(
        CliConnectionOptions options,
        CancellationToken cancellationToken)
    {
        if (await _connectionHandler.ConnectAsync(options, cancellationToken))
        {
            HandleStatus();
        }
    }

    private void HandleStatus()
    {
        if (_session.Reader is null || !_session.Reader.IsConnected)
        {
            _console.MarkupLine("[yellow]Status:[/] [red]Disconnected[/]");
            return;
        }

        var table = new Table();
        table.AddColumn("[bold grey70]Property[/]");
        table.AddColumn("[bold grey70]Value[/]");

        table.AddRow("Host", $"[cyan1]{_session.Host}:{_session.Port}[/]");
        table.AddRow("Connection State", $"[springgreen2]{_session.Reader.ConnectionState}[/]");
        table.AddRow("Connection ID", $"[white]{_session.Reader.ConnectionId}[/]");

        if (_session.Reader.Identity is { } identity)
        {
            table.AddRow("Manufacturer ID", $"[cyan1]{identity.ManufacturerId}[/]");
            table.AddRow("Model ID", $"[springgreen2]{identity.ModelId}[/]");
            table.AddRow("Firmware Version", $"[yellow]{Markup.Escape(identity.FirmwareVersion)}[/]");
        }

        string extensionsText = _session.Reader.Extensions.Count == 0
            ? "[yellow]None (Standard LLRP)[/]"
            : $"[springgreen2]{string.Join(", ", _session.Reader.Extensions.Select(static ext => ext.Id))}[/]";
        table.AddRow("Active Extensions", extensionsText);

        if (_session.FrameObserver != null)
        {
            table.AddRow("Total Captured Frames", $"[deepskyblue1]{_session.FrameObserver.CapturedFrames.Count}[/]");
        }

        var panel = new Panel(table)
            .Header("[bold deepskyblue1] ACTIVE SESSION STATUS [/]")
            .Border(BoxBorder.Rounded);

        _console.Write(panel);
    }

    private void HandleCaps()
    {
        if (_session.Reader is null || !_session.Reader.IsConnected)
        {
            _console.MarkupLine("[yellow]Not connected. Run 'connect <host>' first.[/]");
            return;
        }

        if (_session.Reader.Capabilities is { } capabilities)
        {
            var table = new Table();
            table.AddColumn("[bold grey70]Capability[/]");
            table.AddColumn("[bold grey70]Value[/]");

            table.AddRow("Max Antennas", $"[white]{capabilities.MaxNumberOfAntennas}[/]");
            table.AddRow("Set Antenna Props", capabilities.CanSetAntennaProperties ? "[green]Yes[/]" : "[grey]No[/]");
            table.AddRow("UTC Clock", capabilities.HasUtcClockCapability ? "[green]Yes[/]" : "[grey]No[/]");
            table.AddRow("Tx power entries", capabilities.TxPowers.Count.ToString());
            table.AddRow("Rx sensitivity entries", capabilities.RxSensitivities.Count.ToString());
            table.AddRow("Transmit frequencies", capabilities.TxFrequencies.Count.ToString());
            table.AddRow("Additional Parameters", $"[cyan1]{capabilities.AdditionalParameters.Count}[/]");

            var panel = new Panel(table)
                .Header("[bold springgreen2] READER CAPABILITIES [/]")
                .Border(BoxBorder.Rounded);

            _console.Write(panel);

            if (capabilities.TxPowers.Count > 0)
            {
                var txTable = new Table().Border(TableBorder.Rounded);
                txTable.AddColumn("[bold grey70]Tx index[/]");
                txTable.AddColumn("[bold grey70]dBm[/]");
                foreach (TxPowerEntry power in capabilities.TxPowers)
                {
                    txTable.AddRow(power.Index.ToString(), power.TransmitPowerDbm.ToString("F2"));
                }
                _console.Write(new Panel(txTable)
                    .Header("[bold yellow] TRANSMIT POWER TABLE — use index with config apply --tx-power [/]")
                    .Border(BoxBorder.Rounded));
            }

            if (capabilities.RxSensitivities.Count > 0)
            {
                var rxTable = new Table().Border(TableBorder.Rounded);
                rxTable.AddColumn("[bold grey70]Rx index[/]");
                rxTable.AddColumn("[bold grey70]dBm[/]");
                foreach (RxSensitivityEntry sensitivity in capabilities.RxSensitivities)
                {
                    rxTable.AddRow(sensitivity.Index.ToString(), sensitivity.ReceiveSensitivityDbm.ToString("F2"));
                }
                _console.Write(new Panel(rxTable)
                    .Header("[bold yellow] RECEIVE SENSITIVITY TABLE — use index with config apply --rx-sens [/]")
                    .Border(BoxBorder.Rounded));
            }

            if (capabilities.TxFrequencies.Count > 0)
            {
                var frequencyTable = new Table().Border(TableBorder.Rounded);
                frequencyTable.AddColumn("[bold grey70]Channel index[/]");
                frequencyTable.AddColumn("[bold grey70]Frequency (MHz)[/]");
                for (int index = 0; index < capabilities.TxFrequencies.Count; index++)
                {
                    frequencyTable.AddRow((index + 1).ToString(), (capabilities.TxFrequencies[index] / 1000.0).ToString("F3"));
                }
                _console.Write(new Panel(frequencyTable)
                    .Header("[bold yellow] FIXED FREQUENCY TABLE — channel index is reader-specific [/]")
                    .Border(BoxBorder.Rounded));
            }
        }
        else
        {
            _console.MarkupLine("[yellow]No capability metadata retrieved from reader.[/]");
        }
    }

    private void HandleFrames(string[] tokens)
    {
        if (_session.FrameObserver is null || _session.FrameObserver.CapturedFrames.Count == 0)
        {
            _console.MarkupLine("[yellow]No frames captured yet.[/]");
            return;
        }

        int count = 10;
        if (tokens.Length >= 2 && int.TryParse(tokens[1], out int parsedCount))
        {
            count = parsedCount;
        }

        IReadOnlyList<CapturedFrame> frames = _session.FrameObserver.CapturedFrames;
        var recent = frames.TakeLast(count).ToList();

        var rule = new Rule($"[bold cyan1]Recent {recent.Count} LLRP Message Frames[/]");
        _console.Write(rule);

        foreach (CapturedFrame frame in recent)
        {
            FrameRenderer.RenderObservedFrame(frame, _console, includeHexDump: true);
            _console.WriteLine();
        }
    }

    private async Task HandleRospecAsync(string[] tokens, CancellationToken cancellationToken)
    {
        if (_session.Reader is null || !_session.Reader.IsConnected)
        {
            _console.MarkupLine("[yellow]Not connected. Run 'connect <host>' first.[/]");
            return;
        }

        if (tokens.Length < 2)
        {
            _console.MarkupLine("[red]Usage:[/] rospec add [[--id n]] [[--antennas id,id|all]] [[--mode n]] [[--tari ns]] [[--session 0..3]] [[--population n]] | list|enable|disable|start|stop|delete [[id]]");
            return;
        }

        string subAction = tokens[1].ToLowerInvariant();
        uint rospecId = 1;
        if (tokens.Length >= 3 && uint.TryParse(tokens[2], out uint parsedId))
        {
            rospecId = parsedId;
        }

        switch (subAction)
        {
            case "add":
                var existingRoSpecs = await _session.Reader.RoSpecs.GetAllAsync(cancellationToken);
                if (existingRoSpecs.Count != 0)
                {
                    _console.MarkupLine("[yellow]Default ROSpec was not created: the reader already has ROSpec resources. Use 'rospec list' to inspect them.[/]");
                    return;
                }

                ReaderSettings defaultSettings = ParseDefaultRoSpecSettings(tokens);
                _console.MarkupLine($"[grey]Creating disabled SDK-default ROSpec {defaultSettings.RoSpecId}...[/]");
                await _session.Reader.RoSpecs.AddDefaultAsync(defaultSettings, cancellationToken);
                _console.MarkupLine($"[bold springgreen2]✔ Default ROSpec {defaultSettings.RoSpecId} Created (Disabled).[/] [grey]AISpec: antennas={string.Join(',', defaultSettings.AntennaIds)}, mode/tari={defaultSettings.ModeIndex}/{defaultSettings.Tari}, session/population={defaultSettings.Session}/{defaultSettings.TagPopulationEstimate}.[/]");
                break;
            case "list":
                _console.MarkupLine("[grey]Querying installed ROSpecs...[/]");
                var rospecs = await _session.Reader.RoSpecs.GetAllAsync(cancellationToken);
                _console.MarkupLine($"[green]Found {rospecs.Count} ROSpec(s).[/]");
                break;
            case "enable":
                _console.MarkupLine($"[grey]Enabling ROSpec {rospecId}...[/]");
                await _session.Reader.RoSpecs.EnableAsync(rospecId, cancellationToken);
                _console.MarkupLine($"[bold springgreen2]✔ ROSpec {rospecId} Enabled![/]");
                break;
            case "disable":
                _console.MarkupLine($"[grey]Disabling ROSpec {rospecId}...[/]");
                await _session.Reader.RoSpecs.DisableAsync(rospecId, cancellationToken);
                _console.MarkupLine($"[bold springgreen2]✔ ROSpec {rospecId} Disabled![/]");
                break;
            case "start":
                _console.MarkupLine($"[grey]Starting ROSpec {rospecId}...[/]");
                await _session.Reader.RoSpecs.StartAsync(rospecId, cancellationToken);
                _console.MarkupLine($"[bold springgreen2]✔ ROSpec {rospecId} Started![/]");
                break;
            case "stop":
                _console.MarkupLine($"[grey]Stopping ROSpec {rospecId}...[/]");
                await _session.Reader.RoSpecs.StopAsync(rospecId, cancellationToken);
                _console.MarkupLine($"[bold springgreen2]✔ ROSpec {rospecId} Stopped![/]");
                break;
            case "delete":
                _console.MarkupLine($"[grey]Deleting ROSpec {rospecId}...[/]");
                await _session.Reader.RoSpecs.DeleteAsync(rospecId, cancellationToken);
                _console.MarkupLine($"[bold springgreen2]✔ ROSpec {rospecId} Deleted![/]");
                break;
            default:
                _console.MarkupLine($"[red]Unknown rospec sub-command '{subAction}'.[/]");
                return;
        }

    }

    private static ReaderSettings ParseDefaultRoSpecSettings(string[] tokens)
    {
        var settings = new ReaderSettings();
        for (int index = 2; index < tokens.Length; index += 2)
        {
            if (index + 1 >= tokens.Length)
            {
                throw new CliUsageException($"Missing value for option '{tokens[index]}'.");
            }

            string option = tokens[index].ToLowerInvariant();
            string value = tokens[index + 1];
            settings = option switch
            {
                "--id" when uint.TryParse(value, out uint id) && id != 0 => settings with { RoSpecId = id },
                "--antennas" => settings with { AntennaIds = ParseRoSpecAntennaIds(value) },
                "--mode" when ushort.TryParse(value, out ushort mode) => settings with { ModeIndex = mode },
                "--tari" when ushort.TryParse(value, out ushort tari) => settings with { Tari = tari },
                "--session" when byte.TryParse(value, out byte session) && session <= 3 => settings with { Session = session },
                "--population" when ushort.TryParse(value, out ushort population) && population != 0 => settings with { TagPopulationEstimate = population },
                _ => throw new CliUsageException($"Invalid ROSpec add option '{tokens[index]}'."),
            };
        }

        return settings;
    }

    private static IReadOnlyList<ushort> ParseRoSpecAntennaIds(string value)
    {
        if (value.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            return [0];
        }

        string[] values = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (values.Length == 0 || !values.All(static item => ushort.TryParse(item, out ushort antennaId) && antennaId != 0))
        {
            throw new CliUsageException("--antennas must be all or a comma-separated list of non-zero UInt16 antenna IDs.");
        }

        return values.Select(static item => ushort.Parse(item)).Distinct().ToArray();
    }

    private async Task HandleAccessSpecAsync(string[] tokens, CancellationToken cancellationToken)
    {
        if (_session.Reader is null || !_session.Reader.IsConnected)
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
                var accessSpecs = await _session.Reader.AccessSpecs.GetAllAsync(cancellationToken);
                _console.MarkupLine($"[green]Found {accessSpecs.Count} AccessSpec(s).[/]");
                break;
            case "enable":
                _console.MarkupLine($"[grey]Enabling AccessSpec {accessSpecId}...[/]");
                await _session.Reader.AccessSpecs.EnableAsync(accessSpecId, cancellationToken);
                _console.MarkupLine($"[bold springgreen2]✔ AccessSpec {accessSpecId} Enabled![/]");
                break;
            case "disable":
                _console.MarkupLine($"[grey]Disabling AccessSpec {accessSpecId}...[/]");
                await _session.Reader.AccessSpecs.DisableAsync(accessSpecId, cancellationToken);
                _console.MarkupLine($"[bold springgreen2]✔ AccessSpec {accessSpecId} Disabled![/]");
                break;
            case "delete":
                _console.MarkupLine($"[grey]Deleting AccessSpec {accessSpecId}...[/]");
                await _session.Reader.AccessSpecs.DeleteAsync(accessSpecId, cancellationToken);
                _console.MarkupLine($"[bold springgreen2]✔ AccessSpec {accessSpecId} Deleted![/]");
                break;
            default:
                _console.MarkupLine("[red]Usage:[/] accessspec list|enable|disable|delete [[id]]");
                return;
        }

    }

    private async Task HandleRawAsync(string[] tokens, CancellationToken cancellationToken)
    {
        if (_session.Reader is null || !_session.Reader.IsConnected)
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

                await _session.Reader.Protocol.SendRawAsync(requestFrame, cancellationToken);
                _console.MarkupLine("[bold springgreen2]✔ Raw frame sent.[/]");
                break;

            case "transact":
                if (requestHeader.MessageId == 0)
                {
                    throw new CliUsageException("A raw transaction requires a non-zero message identifier.");
                }

                ushort? responseType = ParseRawResponseType(tokens.Skip(3).ToArray());
                ReadOnlyMemory<byte> response = await _session.Reader.Protocol.TransactRawAsync(
                    requestFrame,
                    (header, _) => header.MessageId == requestHeader.MessageId &&
                        (!responseType.HasValue || (ushort)header.MessageType == responseType.Value),
                    cancellationToken: cancellationToken);
                _console.MarkupLine("[bold springgreen2]✔ Raw transaction completed.[/]");
                break;

            default:
                throw new CliUsageException("Usage: raw send|transact <hex-frame> [--response-type <type>] --yes");
        }

        if (!_session.Reader.IsManagedStateSynchronized)
        {
            _console.MarkupLine(
                "[yellow]SDK-managed state is now unsynchronized. Run [cyan1]sync[/] before the next managed operation.[/]");
        }
    }

    private async Task HandleConfigAsync(string[] tokens, CancellationToken cancellationToken)
    {
        if (_session.Reader is null || !_session.Reader.IsConnected)
        {
            _console.MarkupLine("[yellow]Not connected. Run 'connect <host>' first.[/]");
            return;
        }

        if (tokens.Length < 2)
        {
            throw new CliUsageException("Usage: config get | config defaults | config apply [options] [--dry-run] --yes");
        }

        if (tokens[1].Equals("apply", StringComparison.OrdinalIgnoreCase))
        {
            await HandleConfigApplyAsync(tokens, cancellationToken);
            return;
        }
        if (tokens[1].Equals("defaults", StringComparison.OrdinalIgnoreCase) && tokens.Length == 2)
        {
            ConfigDefaultsRenderer.Render(_console, _session.Reader.GetDefaultConfigurationResult());
            return;
        }
        if (!tokens[1].Equals("get", StringComparison.OrdinalIgnoreCase) || tokens.Length != 2)
        {
            throw new CliUsageException("Usage: config get | config defaults | config apply [options] [--dry-run] --yes");
        }

        ReaderConfiguration configuration = await _session.Reader.QueryConfigurationAsync(cancellationToken);

        ConfigurationRenderer.Render(_console, configuration, _session.Reader.Capabilities);
    }

    private async Task HandleConfigApplyAsync(string[] tokens, CancellationToken cancellationToken)
    {
        ConfigApplySettings settings = ParseLiveConfigApply(tokens, out bool confirmed);
        if (!ConfigurationApplyUtilities.TryValidateRequestedChanges(settings, _session.Reader!.Capabilities, out string? error))
        {
            throw new CliUsageException(error!);
        }

        ReaderConfiguration current = await _session.Reader!.QueryConfigurationAsync(cancellationToken);
        ReaderConfiguration updated = ConfigurationApplyUtilities.BuildUpdatedConfiguration(settings, current);
        RenderLiveConfigChange(settings, updated, settings.DryRun || !confirmed);
        if (settings.DryRun)
        {
            return;
        }
        if (!confirmed)
        {
            _console.MarkupLine("[yellow]No configuration was written. Repeat with [cyan1]--yes[/] to confirm.[/]");
            return;
        }

        await _session.Reader.ApplyConfigurationAsync(updated, cancellationToken);
        _console.MarkupLine("[bold springgreen2]✔ Configuration applied successfully.[/]");
        _console.MarkupLine("[yellow]SDK-managed state is now unsynchronized. Run [cyan1]sync[/] before the next managed operation.[/]");
    }

    private static ConfigApplySettings ParseLiveConfigApply(string[] tokens, out bool confirmed)
    {
        string? keepaliveType = null;
        uint? keepaliveInterval = null;
        ushort? antennaId = null;
        ushort? transmitPower = null;
        ushort? receiverSensitivity = null;
        ushort? channelIndex = null;
        ushort? gpoPort = null;
        bool? gpoData = null;
        bool dryRun = false;
        confirmed = false;

        for (int index = 2; index < tokens.Length; index++)
        {
            string token = tokens[index];
            if (token.Equals("--dry-run", StringComparison.OrdinalIgnoreCase))
            {
                dryRun = true;
                continue;
            }
            if (token.Equals("--yes", StringComparison.OrdinalIgnoreCase))
            {
                confirmed = true;
                continue;
            }
            if (index + 1 >= tokens.Length)
            {
                throw new CliUsageException("A config apply option is missing its value.");
            }

            string value = tokens[++index];
            switch (token.ToLowerInvariant())
            {
                case "--keepalive-type": keepaliveType = value; break;
                case "--keepalive-interval": keepaliveInterval = Helpers.ParseUInt32(value, token); break;
                case "--antenna": antennaId = ParseUShort(value, token); break;
                case "--tx-power": transmitPower = ParseUShort(value, token); break;
                case "--rx-sens": receiverSensitivity = ParseUShort(value, token); break;
                case "--channel": channelIndex = ParseUShort(value, token); break;
                case "--gpo-port": gpoPort = ParseUShort(value, token); break;
                case "--gpo-data" when bool.TryParse(value, out bool parsed): gpoData = parsed; break;
                case "--gpo-data": throw new CliUsageException("--gpo-data must be true or false.");
                default: throw new CliUsageException($"Unknown config apply option '{token}'.");
            }
        }

        return new ConfigApplySettings
        {
            KeepaliveType = keepaliveType,
            KeepaliveInterval = keepaliveInterval,
            AntennaId = antennaId,
            TransmitPower = transmitPower,
            ReceiverSensitivity = receiverSensitivity,
            ChannelIndex = channelIndex,
            GpoPort = gpoPort,
            GpoData = gpoData,
            DryRun = dryRun
        };
    }

    private static ushort ParseUShort(string value, string option)
    {
        uint parsed = Helpers.ParseUInt32(value, option);
        if (parsed > ushort.MaxValue)
        {
            throw new CliUsageException($"{option} must be a UInt16 value.");
        }
        return (ushort)parsed;
    }

    private void RenderLiveConfigChange(ConfigApplySettings settings, ReaderConfiguration configuration, bool noWrite)
    {
        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("[bold grey70]Setting[/]");
        table.AddColumn("[bold grey70]Resolved value[/]");
        if (settings.KeepaliveType is not null || settings.KeepaliveInterval.HasValue)
        {
            table.AddRow("Keepalive", $"{configuration.Keepalive.TriggerType}, {configuration.Keepalive.IntervalMs} ms");
        }
        if (settings.AntennaId is ushort antennaId)
        {
            AntennaConfigurationSettings antenna = configuration.Antennas.Single(item => item.AntennaId == antennaId);
            table.AddRow($"Antenna {antennaId}", $"Tx index={antenna.TransmitPowerIndex}, Rx index={antenna.ReceiverSensitivityIndex}, Channel index={antenna.ChannelIndex}");
        }
        if (settings.GpoPort is ushort gpoPort)
        {
            GpoConfiguration gpo = configuration.Gpos.Single(item => item.GpoPortNumber == gpoPort);
            table.AddRow($"GPO {gpoPort}", gpo.GpoData ? "[green]High (1)[/]" : "[grey]Low (0)[/]");
        }

        string header = noWrite
            ? "[bold yellow] CONFIGURATION PREVIEW — NO DEVICE WRITE [/]"
            : "[bold yellow] CONFIGURATION CHANGE [/]";
        _console.Write(new Panel(table).Header(header).Border(BoxBorder.Rounded));
    }

    private async Task HandleSynchronizeStateAsync(CancellationToken cancellationToken)
    {
        if (_session.Reader is null || !_session.Reader.IsConnected)
        {
            _console.MarkupLine("[yellow]Not connected. Run 'connect <host>' first.[/]");
            return;
        }

        _console.MarkupLine("[grey]Synchronizing reader-managed ROSpec and AccessSpec state...[/]");
        await _session.Reader.SynchronizeStateAsync(cancellationToken);
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
        UpdateWindowTitle(_session.Reader?.IsConnected == true ? $"{_session.Host}:{_session.Port}" : "offline");

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
            "[grey][cyan1]inventory settings[/] 配置盘点意图；[cyan1]inventory start|stop[/] 控制 SDK 托管盘点[/]");
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
        table.AddRow("  [cyan1]connect <host> [[port]] [[--llrp auto|1.0.1|1.1]] [[--vendor auto|impinj|seuic|none]][/]", "连接读写器并完成版本协商/厂商扩展选择");
        table.AddRow("  [cyan1]disconnect[/]", "断开当前读写器 TCP 会话");
        table.AddRow("  [cyan1]status[/]", "显示当前连接状态、协商版本与读写器元数据");
        table.AddRow("  [cyan1]caps[/]", "显示读写器硬件能力参数 (Capabilities)");
        table.AddRow("  [cyan1]config get[/]", "查询当前读写器配置（只读，不影响托管盘点状态）");
        table.AddRow("  [cyan1]config defaults[/]", "显示 SDK 默认配置/Profile 来源（不读取或写入设备配置）");
        table.AddRow("  [cyan1]config apply [[options]] [[--dry-run]] --yes[/]", "预览或显式确认后写入可编辑配置");

        // 分组 2: 高层托管盘点 (Managed Inventory)
        table.AddRow("[bold yellow1]─── 🚀 高层托管盘点 (Managed Inventory API) ───[/]", "");
        table.AddRow("  [cyan1]inventory settings show|set|load|save|reset[/]", "管理下一次托管盘点的本地意图草稿；无需连接读写器");
        table.AddRow("  [cyan1]inventory start [[--antennas id,id|all]] [[--monitor live|frames|none]] [[--monitor-duration seconds]] | stop | status[/]", "按草稿启动盘点；Ctrl+C 或监控时间到都只退出监控");

        // 分组 3: 纯被动推流监听 (Passive Monitoring)
        table.AddRow("[bold yellow1]─── 📡 纯被动推流监听 (Passive Monitoring) ───[/]", "");
        table.AddRow("  [cyan1]monitor [[live|frames]] [[seconds]][/]", "前台监控：Live 标签汇总或原始报文；Ctrl+C 返回 Prompt");
        table.AddRow("  [cyan1]frames [[count]][/]", "展示最近捕获的原始收发 LLRP 帧日志");

        // 分组 4: 进阶底层资源操控 (Advanced Resource API)
        table.AddRow("[bold yellow1]─── ⚙️ 进阶底层资源操控 (Advanced Resource API) ───[/]", "");
        table.AddRow("  [cyan1]rospec add|list|enable|disable|start|stop|delete [[id]][/]", "创建默认 ROSpec 或管理设备现有 ROSpec 资源");
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

    private void RenderCommandHelp(string commandName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandName);
        CommandSpec? command = CommandCatalog.Find(commandName);
        if (command is null)
        {
            _console.MarkupLine($"[red]Unknown command '{Markup.Escape(commandName)}'.[/]");
            return;
        }

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("[bold grey70]Field[/]");
        table.AddColumn("[bold grey70]Value[/]");
        table.AddRow("Usage", $"[cyan1]{Markup.Escape(command.Usage)}[/]");
        table.AddRow("Description", Markup.Escape(command.Description));
        table.AddRow("Connection", command.RequiresConnection ? "[yellow]Required[/]" : "[grey]Not required[/]");
        if (command.Aliases.Length > 0)
        {
            table.AddRow("Aliases", Markup.Escape(string.Join(", ", command.Aliases)));
        }

        _console.Write(new Panel(table)
            .Header($"[bold deepskyblue1] HELP: {Markup.Escape(command.Name)} [/]")
            .Border(BoxBorder.Rounded));

        if (command.Name.Equals("config", StringComparison.OrdinalIgnoreCase))
        {
            var optionsTable = new Table().Border(TableBorder.Rounded);
            optionsTable.AddColumn("[bold cyan1]Option[/]");
            optionsTable.AddColumn("[bold grey70]Type / Format[/]");
            optionsTable.AddColumn("[bold grey70]Description[/]");

            optionsTable.AddRow("--dry-run", "Flag", "Show configuration change preview without writing to device");
            optionsTable.AddRow("--yes", "Flag", "Explicitly confirm configuration write to reader");
            optionsTable.AddRow("--antenna", "<ushort>", "Target antenna ID (e.g. 1)");
            optionsTable.AddRow("--tx-power", "<ushort index>", "Transmit-power table index, not dBm/mW; run caps first");
            optionsTable.AddRow("--rx-sens", "<ushort index>", "Receiver-sensitivity table index, not dBm; run caps first");
            optionsTable.AddRow("--channel", "<ushort index>", "Channel / frequency-table index; run caps first");
            optionsTable.AddRow("--keepalive-type", "none|periodic", "Keepalive trigger type");
            optionsTable.AddRow("--keepalive-interval", "<uint ms>", "Keepalive interval in milliseconds");
            optionsTable.AddRow("--gpo-port", "<ushort>", "GPO port number (e.g. 1)");
            optionsTable.AddRow("--gpo-data", "true|false", "GPO pin state");

            _console.Write(new Panel(optionsTable)
                .Header("[bold yellow] config apply [[options]] [/]")
                .Border(BoxBorder.Rounded));
        }
    }

}
