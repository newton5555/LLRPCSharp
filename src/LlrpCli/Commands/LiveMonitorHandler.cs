using System.Collections.Concurrent;
using Spectre.Console;
using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Messages;
using LlrpSdk;

namespace LlrpCli.Commands;

/// <summary>
/// Owns temporary raw-frame and aggregated tag-table monitor scopes.
/// </summary>
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
            await MonitorFramesAsync(seconds, cancellationToken);
            return;
        }

        await MonitorTagTableAsync(seconds, cancellationToken);
    }

    private async Task MonitorFramesAsync(int seconds, CancellationToken cancellationToken)
    {
        console.MarkupLine($"[bold springgreen2]📡 Listening to passive LLRP frame logs for {seconds} seconds...[/]");
        console.MarkupLine("[grey]Raw Frame Mode: printing raw RX/TX frame trees and hex dumps.[/]");
        console.WriteLine();

        session.IsMonitoringTable = false;
        session.IsMonitoring = true;
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(seconds), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Expected if user cancels.
        }
        finally
        {
            session.IsMonitoring = false;
            console.MarkupLine("[bold cyan1]✔ Passive frame monitoring ended.[/]");
        }
    }

    private async Task MonitorTagTableAsync(int seconds, CancellationToken cancellationToken)
    {
        console.MarkupLine($"[bold springgreen2]📊 Streaming live tag statistics table for {seconds} seconds...[/]");
        console.MarkupLine("[grey]Live Table Mode: aggregated unique EPC tag counts, RSSI, and antennas.[/]");
        var tagStats = new ConcurrentDictionary<string, TagStat>();

        session.MonitorFrameCallback = frame =>
        {
            try
            {
                ILlrpMessage message = session.Reader!.Registry.DecodeMessage(frame.Bytes);
                foreach (TagReport report in session.Reader.TranslateTagReports(message))
                {
                    string epc = Convert.ToHexString(report.ElectronicProductCode.Span);
                    if (string.IsNullOrEmpty(epc))
                    {
                        continue;
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
            }
            catch
            {
                // Non-report messages are ignored in the tag table.
            }
        };

        session.IsMonitoringTable = true;
        session.IsMonitoring = false;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(seconds));

        var table = new Table { Border = TableBorder.Rounded, BorderStyle = new Style(Color.DeepSkyBlue1) };
        table.AddColumn("[bold deepskyblue1]🏷️ EPC (Hex)[/]");
        table.AddColumn("[bold springgreen2]📡 Antenna[/]");
        table.AddColumn("[bold yellow1]📶 Peak RSSI[/]");
        table.AddColumn("[bold cyan1]🔢 Read Count[/]");
        table.AddColumn("[bold grey70]🕒 Last Seen[/]");

        try
        {
            await console.Live(table).AutoClear(false).Overflow(VerticalOverflow.Ellipsis).StartAsync(async context =>
            {
                while (!cts.IsCancellationRequested)
                {
                    table.Rows.Clear();
                    var topTags = tagStats.Values.OrderByDescending(stat => stat.LastSeen).Take(15).ToList();
                    long totalReads = tagStats.Values.Sum(stat => stat.ReadCount);
                    foreach (TagStat tag in topTags)
                    {
                        table.AddRow($"[bold white]{tag.Epc}[/]", $"[cyan1]{tag.AntennaId}[/]", $"[yellow1]{tag.PeakRssi} dBm[/]", $"[bold springgreen2]{tag.ReadCount:N0}[/]", $"[grey]{tag.LastSeen:HH:mm:ss.fff}[/]");
                    }

                    table.Title = new TableTitle($"[bold white on deepskyblue1] 🏷️ LIVE TAG MONITOR [/] [grey]Unique Tags:[/] [bold yellow]{tagStats.Count}[/] | [grey]Total Reads:[/] [bold springgreen2]{totalReads:N0}[/]");
                    context.Refresh();
                    try { await Task.Delay(100, cts.Token); }
                    catch (OperationCanceledException) { break; }
                }
            });
        }
        catch (OperationCanceledException)
        {
            // Expected when canceled.
        }
        finally
        {
            session.IsMonitoringTable = false;
            session.MonitorFrameCallback = null;
            console.MarkupLine($"[bold cyan1]✔ Live tag summary ended. Total Unique Tags: {tagStats.Count}[/]");
        }
    }
}
