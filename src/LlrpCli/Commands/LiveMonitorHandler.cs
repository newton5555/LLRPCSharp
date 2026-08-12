using System.Collections.Concurrent;
using LlrpCli.Rendering;
using LlrpCli.Terminal;
using LlrpSdk;
using Spectre.Console;

namespace LlrpCli.Commands;

internal enum LiveMonitorMode
{
    None,
    Live,
    Frames,
}

/// <summary>Owns foreground tag and raw-frame monitor scopes for one Live Shell session.</summary>
internal sealed class LiveMonitorHandler(IAnsiConsole console, LiveSessionContext session)
{
    private sealed class TagStat
    {
        public required string Epc { get; init; }
        public ushort AntennaId { get; set; }
        public sbyte PeakRssi { get; set; }
        public long ReadCount { get; set; }
        public DateTime LastSeen { get; set; }
    }

    public async Task HandleAsync(string[] tokens, CancellationToken cancellationToken)
    {
        if (session.Reader is null || !session.Reader.IsConnected)
        {
            console.MarkupLine("[yellow]Not connected. Run 'connect <host>' first.[/]");
            return;
        }

        (LiveMonitorMode mode, int? seconds, string? filterType) = ParseMonitorArguments(tokens, startIndex: 1);
        await MonitorAsync(mode, seconds, filterType, cancellationToken);
    }

    /// <summary>Runs an exclusive foreground monitor. Ctrl+C leaves the monitor but does not stop inventory.</summary>
    public async Task MonitorAsync(LiveMonitorMode mode, int? seconds, string? filterType, CancellationToken cancellationToken)
    {
        if (mode == LiveMonitorMode.None)
        {
            return;
        }
        if (session.Reader is null || !session.Reader.IsConnected)
        {
            console.MarkupLine("[yellow]Not connected. Run 'connect <host>' first.[/]");
            return;
        }

        session.BeginMonitor(mode, filterType);

        using var monitorCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (seconds is int duration)
        {
            monitorCancellation.CancelAfter(TimeSpan.FromSeconds(duration));
        }

        ConsoleCancelEventHandler cancelHandler = (_, args) =>
        {
            args.Cancel = true;
            monitorCancellation.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;
        try
        {
            if (mode == LiveMonitorMode.Frames)
            {
                await MonitorFramesAsync(monitorCancellation.Token, filterType);
            }
            else if (string.Equals(filterType, "KEEPALIVE", StringComparison.OrdinalIgnoreCase))
            {
                await MonitorKeepaliveTableAsync(monitorCancellation.Token);
            }
            else
            {
                await MonitorTagTableAsync(monitorCancellation.Token);
            }
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
            IReadOnlyList<CapturedFrame> deferredFrames = session.EndMonitor();
            RenderDeferredFrames(deferredFrames);
        }

    }

    public static (LiveMonitorMode Mode, int? Seconds, string? FilterType) ParseMonitorArguments(string[] tokens, int startIndex)
    {
        LiveMonitorMode? mode = null;
        int? seconds = null;
        string? filterType = null;
        for (int index = startIndex; index < tokens.Length; index++)
        {
            string token = tokens[index];
            string lowerToken = token.ToLowerInvariant();
            
            if (lowerToken == "--type" && index + 1 < tokens.Length)
            {
                filterType = tokens[index + 1];
                index++;
                continue;
            }

            mode = lowerToken switch
            {
                "live" or "--live" or "--table" or "-t" => LiveMonitorMode.Live,
                "frames" or "--frames" or "-f" or "raw" => LiveMonitorMode.Frames,
                "none" => LiveMonitorMode.None,
                _ when int.TryParse(token, out int parsed) && parsed > 0 => mode,
                _ => throw new CliUsageException("Usage: monitor [live|frames] [duration-sec] [--type MessageName]")
            };
            if (int.TryParse(token, out int parsedSeconds) && parsedSeconds > 0)
            {
                seconds = parsedSeconds;
            }
        }
        
        LiveMonitorMode resolvedMode = mode ?? (filterType != null ? LiveMonitorMode.Frames : LiveMonitorMode.Live);
        return (resolvedMode, seconds, filterType);
    }

    private async Task MonitorFramesAsync(CancellationToken cancellationToken, string? filterType)
    {
        string extraInfo = filterType != null ? $" (Filtered by type: [yellow]{filterType}[/])" : "";
        console.MarkupLine($"[bold springgreen2]📡 Monitoring raw LLRP frames{extraInfo}. Press Ctrl+C to return to the prompt; inventory keeps running.[/]");
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Ctrl+C, a duration expiry, or host cancellation ends only this monitor scope.
        }
        finally
        {
            console.MarkupLine("[bold cyan1]✔ Frame monitor ended; inventory state was unchanged.[/]");
        }
    }

    private async Task MonitorTagTableAsync(CancellationToken cancellationToken)
    {
        console.MarkupLine("[bold springgreen2]📊 Monitoring live tag statistics. Press Ctrl+C to return to the prompt; inventory keeps running.[/]");
        var tagStats = new ConcurrentDictionary<string, TagStat>();
        InventorySession? inventorySession = session.InventorySession;
        EventHandler<TagReportEventArgs>? reportHandler = (_, args) =>
        {
            ObserveTagReport(tagStats, args.Report);
        };

        DateTime? lastKeepalive = null;
        EventHandler<EventArgs>? keepaliveHandler = (_, _) => lastKeepalive = DateTime.Now;

        Task reportPump = Task.CompletedTask;
        if (inventorySession is not null)
        {
            reportPump = PumpSessionReportsAsync(inventorySession, tagStats, cancellationToken);
        }
        else
        {
            session.Reader!.TagsReported += reportHandler;
        }
        session.Reader!.KeepaliveReceived += keepaliveHandler;
        var table = new Table { Border = TableBorder.Rounded, BorderStyle = new Style(Color.DeepSkyBlue1) }.Expand();
        table.AddColumn("[bold deepskyblue1]🏷️ EPC (Hex)[/]");
        table.AddColumn("[bold springgreen2]📡 Antenna[/]");
        table.AddColumn("[bold yellow1]📶 Peak RSSI[/]");
        table.AddColumn("[bold cyan1]🔢 Read Count[/]");
        table.AddColumn("[bold grey70]🕒 Last Seen[/]");

        try
        {
            await console.Live(table).AutoClear(false).Overflow(VerticalOverflow.Ellipsis).StartAsync(async context =>
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    table.Rows.Clear();
                    IReadOnlyList<TagStat> topTags = tagStats.Values.OrderByDescending(stat => stat.LastSeen).Take(15).ToArray();
                    long totalReads = tagStats.Values.Sum(stat => stat.ReadCount);
                    foreach (TagStat tag in topTags)
                    {
                        table.AddRow($"[bold white]{tag.Epc}[/]", $"[cyan1]{tag.AntennaId}[/]", $"[yellow1]{tag.PeakRssi} dBm[/]", $"[bold springgreen2]{tag.ReadCount:N0}[/]", $"[grey]{tag.LastSeen:HH:mm:ss.fff}[/]");
                    }
                    string keepaliveMarkup = lastKeepalive.HasValue ? $" | [grey]💓 Heartbeat:[/] [bold deeppink1]{lastKeepalive:HH:mm:ss}[/]" : "";
                    table.Title = new TableTitle($"[bold white on deepskyblue1] 🏷️ LIVE TAG MONITOR [/] [grey]Unique Tags:[/] [bold yellow]{tagStats.Count}[/] | [grey]Total Reads:[/] [bold springgreen2]{totalReads:N0}[/]{keepaliveMarkup}");
                    context.Refresh();
                    try { await Task.Delay(100, cancellationToken); }
                    catch (OperationCanceledException) { break; }
                }
            });
        }
        finally
        {
            if (inventorySession is not null)
            {
                try { await reportPump.ConfigureAwait(false); }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            }
            else
            {
                session.Reader.TagsReported -= reportHandler;
            }
            session.Reader.KeepaliveReceived -= keepaliveHandler;
            console.MarkupLine($"[bold cyan1]✔ Live tag monitor ended ({tagStats.Count} unique tags); inventory state was unchanged.[/]");
        }
    }

    private static async Task PumpSessionReportsAsync(
        InventorySession inventorySession,
        ConcurrentDictionary<string, TagStat> tagStats,
        CancellationToken cancellationToken)
    {
        await foreach (TagReport report in inventorySession.ReadReportsAsync(cancellationToken).ConfigureAwait(false))
        {
            ObserveTagReport(tagStats, report);
        }
    }

    private static void ObserveTagReport(ConcurrentDictionary<string, TagStat> tagStats, TagReport report)
    {
        string epc = Convert.ToHexString(report.ElectronicProductCode.Span);
        if (string.IsNullOrEmpty(epc))
        {
            return;
        }

        tagStats.AddOrUpdate(
            epc,
            key => new TagStat { Epc = key, AntennaId = report.AntennaId ?? 0, PeakRssi = report.PeakRssi ?? 0, ReadCount = 1, LastSeen = DateTime.Now },
            (_, existing) =>
            {
                existing.ReadCount++;
                existing.AntennaId = report.AntennaId ?? existing.AntennaId;
                existing.PeakRssi = report.PeakRssi ?? existing.PeakRssi;
                existing.LastSeen = DateTime.Now;
                return existing;
            });
    }

    private async Task MonitorKeepaliveTableAsync(CancellationToken cancellationToken)
    {
        console.MarkupLine("[bold deeppink1]💓 Monitoring live heartbeats (KEEPALIVE). Press Ctrl+C to return to the prompt; inventory keeps running.[/]");
        DateTime? lastKeepalive = null;
        int keepaliveCount = 0;
        
        EventHandler<EventArgs>? keepaliveHandler = (_, _) =>
        {
            lastKeepalive = DateTime.Now;
            Interlocked.Increment(ref keepaliveCount);
        };

        session.Reader!.KeepaliveReceived += keepaliveHandler;

        try
        {
            var table = new Table
            {
                Border = TableBorder.Rounded,
                BorderStyle = new Style(Color.DeepPink1),
                Title = new TableTitle("[bold white on deeppink1] 💓 KEEPALIVE STATUS [/]"),
                Expand = true
            };
            table.HideHeaders();
            table.AddColumn(new TableColumn("Label").RightAligned());
            table.AddColumn(new TableColumn("Value").LeftAligned());

            await console.Live(table).AutoClear(false).StartAsync(async context =>
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    table.Rows.Clear();
                    if (lastKeepalive.HasValue)
                    {
                        var timeSince = DateTime.Now - lastKeepalive.Value;
                        string status = timeSince.TotalSeconds < 15 ? "[springgreen2]🟢 Healthy[/]" : "[yellow]🟡 Delayed / Silent[/]";
                        
                        table.AddRow("[grey70]Status:[/]", status);
                        table.AddRow("[grey70]Last Heartbeat:[/]", $"[bold white]{lastKeepalive.Value:HH:mm:ss}[/] [grey]({timeSince.TotalSeconds:F1}s ago)[/]");
                        table.AddRow("[grey70]Total Received:[/]", $"[bold deeppink1]{keepaliveCount}[/]");
                    }
                    else
                    {
                        table.AddRow("[grey70]Status:[/]", "[yellow]Waiting for first heartbeat...[/]");
                    }
                    
                    context.Refresh();
                    try { await Task.Delay(100, cancellationToken); }
                    catch (OperationCanceledException) { break; }
                }
            });
        }
        finally
        {
            session.Reader.KeepaliveReceived -= keepaliveHandler;
            console.MarkupLine($"[bold cyan1]✔ Live keepalive monitor ended ({keepaliveCount} total received); inventory state was unchanged.[/]");
        }
    }

    private void RenderDeferredFrames(IReadOnlyList<CapturedFrame> deferredFrames)
    {
        if (deferredFrames.Count == 0)
        {
            return;
        }

        console.MarkupLine($"[grey]Rendering {deferredFrames.Count} non-tag LLRP frame(s) deferred while the Live tag table was active:[/]");
        lock (session.FrameRenderLock)
        {
            foreach (CapturedFrame frame in deferredFrames)
            {
                FrameRenderer.RenderObservedFrame(frame, console, includeHexDump: true);
                console.WriteLine();
            }
        }
    }
}
