using LlrpNet.Core.Diagnostics;
using LlrpSdk;
using LlrpSdk.Extensions.Impinj;
using V101Parameters = LlrpNet.Protocol.Parameters.V1_0_1;
using V11Parameters = LlrpNet.Protocol.Parameters.V1_1;

if (args.Length < 1)
{
    throw new ArgumentException(
        "Usage: dotnet run --project tools/LlrpSdk.LiveSmoke -- <host> [--inventory] [--impinj-serialized-tid] [--impinj-rf-phase-angle] [--impinj-peak-rssi] [--read <epc-hex>]");
}

string host = args[0];
bool requestImpinjSerializedTid = args.Contains("--impinj-serialized-tid", StringComparer.Ordinal);
bool requestImpinjRfPhaseAngle = args.Contains("--impinj-rf-phase-angle", StringComparer.Ordinal);
bool requestImpinjPeakRssi = args.Contains("--impinj-peak-rssi", StringComparer.Ordinal);
int readOptionIndex = Array.IndexOf(args, "--read");
for (int index = 1; index < args.Length; index++)
{
    string argument = args[index];
    if (argument == "--read")
    {
        if (++index >= args.Length || !args[index].All(static character => Uri.IsHexDigit(character)))
        {
            throw new ArgumentException("--read requires an EPC hex value.");
        }

        continue;
    }

    if (argument is not "--inventory" and not "--impinj-serialized-tid" and not "--impinj-rf-phase-angle" and not "--impinj-peak-rssi")
    {
        throw new ArgumentException($"Unknown LiveSmoke option '{argument}'.");
    }
}

bool requestImpinjReportFields = requestImpinjSerializedTid || requestImpinjRfPhaseAngle || requestImpinjPeakRssi;
var frameJournal = new LlrpFrameJournal();

await using LlrpReader reader = LlrpReader.CreateBuilder(host)
    .UseImpinj()
    .WithFrameObserver(frameJournal)
    .WithConnectTimeout(TimeSpan.FromSeconds(10))
    .WithRequestTimeout(TimeSpan.FromSeconds(10))
    .Build();

await reader.ConnectAsync();
ReaderConfiguration defaults = reader.GetDefaultConfiguration();
ReaderConfiguration configuration = await reader.QueryConfigurationAsync();
IReadOnlyList<LlrpNet.Protocol.Parameters.ILlrpParameter> configuredRoSpecs =
    await reader.RoSpecs.GetAllAsync();

Console.WriteLine($"Connected: {reader.ConnectionId}");
Console.WriteLine($"Protocol: {reader.NegotiatedVersion}");
Console.WriteLine($"Identity: {reader.Identity?.ManufacturerId}/{reader.Identity?.ModelId} {reader.Identity?.FirmwareVersion}");
Console.WriteLine($"Extensions: {string.Join(", ", reader.Extensions.Select(static extension => extension.Id))}");
Console.WriteLine($"Capabilities: antennas={reader.Capabilities?.MaxNumberOfAntennas}, additional={reader.Capabilities?.AdditionalParameters.Count}");
Console.WriteLine($"Defaults: keepalive={defaults.Keepalive.TriggerType}/{defaults.Keepalive.IntervalMs}ms, antennas={defaults.Antennas.Count}, gpo={defaults.Gpos.Count}");
Console.WriteLine($"Configuration: antennas={configuration.Antennas.Count}, gpi={configuration.Gpis.Count}, gpo={configuration.Gpos.Count}");
Console.WriteLine($"ROSpecs: {string.Join(", ", configuredRoSpecs.Select(DescribeRoSpec))}");
if (configuration.Extensions.TryGetValue("impinj.readerSettings", out object? extensionValue) &&
    extensionValue is ImpinjReaderSettings impinjSettings)
{
    Console.WriteLine(
        $"Impinj settings: region={impinjSettings.RegulatoryRegion}, temperature={impinjSettings.TemperatureCelsius}, " +
        $"gpiDebounce={impinjSettings.GpiDebounce.Count}, linkMonitor={impinjSettings.LinkMonitor}, " +
        $"reportBuffer={impinjSettings.ReportBufferMode}, accessSpec={impinjSettings.AccessSpec}");
}

if (args.Length >= 2)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    await reader.SynchronizeStateAsync(timeout.Token);
    uint smokeRoSpecId = (uint)Random.Shared.Next(1_500_000_000, 2_000_000_000);
    var inventorySettings = new ReaderSettings
    {
        RoSpecId = smokeRoSpecId,
        Extensions = requestImpinjReportFields
            ? new Dictionary<string, object?>
            {
                [ImpinjInventoryReportOptions.ExtensionKey] = new ImpinjInventoryReportOptions
                {
                    IncludeSerializedTid = requestImpinjSerializedTid,
                    IncludeRfPhaseAngle = requestImpinjRfPhaseAngle,
                    IncludePeakRssi = requestImpinjPeakRssi,
                }
            }
            : new Dictionary<string, object?>()
    };
    try
    {
        await reader.StartAsync(inventorySettings, timeout.Token);
    }
    catch
    {
        DumpRoSpecFrames(frameJournal);
        throw;
    }
    try
    {
        bool observedTag = false;
        try
        {
            await foreach (TagReport report in reader.ReadTagReportsAsync(timeout.Token))
            {
                observedTag = true;
                Console.WriteLine($"Tag: {Convert.ToHexString(report.ElectronicProductCode.Span)} antenna={report.AntennaId} rssi={report.PeakRssi}");
                if (requestImpinjReportFields)
                {
                    Console.WriteLine($"Impinj report: {DescribeImpinjReportExtensions(report)}");
                }
                break;
            }
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            Console.Error.WriteLine("No tag report was received within 10 seconds; tag access was skipped.");
            Environment.ExitCode = 1;
        }

        if (readOptionIndex >= 0 && observedTag)
        {
            if (readOptionIndex + 1 >= args.Length)
            {
                throw new ArgumentException("--read requires an EPC hex value.");
            }
            byte[] epc = Convert.FromHexString(args[readOptionIndex + 1]);
            TagAccessResult result = await reader.ReadTagMemoryAsync(new ReadTagRequest
            {
                Selection = new TagSelection
                {
                    MemoryBank = TagMemoryBank.ElectronicProductCode,
                    BitPointer = 32,
                    BitLength = checked((ushort)(epc.Length * 8)),
                    Mask = Enumerable.Repeat((byte)0xFF, epc.Length).ToArray(),
                    Data = epc,
                },
                MemoryBank = TagMemoryBank.User,
                WordPointer = 0,
                WordCount = 1,
            }, TimeSpan.FromSeconds(10), timeout.Token);
            Console.WriteLine($"Read result: success={result.Operation.Success} data={Convert.ToHexString(result.Operation.ReadData.SelectMany(BitConverter.GetBytes).Reverse().ToArray())} error={result.Operation.Error}");
        }
    }
    finally
    {
        await reader.StopAsync();
    }
}

static void DumpRoSpecFrames(LlrpFrameJournal frameJournal)
{
    foreach (LlrpCapturedFrame frame in frameJournal.Snapshot()
        .Where(static frame => GetMessageType(frame.FrameBytes) is 20 or 30))
    {
        Console.Error.WriteLine(
            $"{frame.Direction} {GetMessageType(frame.FrameBytes)} ({frame.FrameBytes.Length} bytes): {Convert.ToHexString(frame.FrameBytes)}");
    }
}

static ushort GetMessageType(byte[] frameBytes)
{
    return frameBytes.Length >= 2
        ? (ushort)(((frameBytes[0] & 0x03) << 8) | frameBytes[1])
        : ushort.MaxValue;
}

static string DescribeRoSpec(LlrpNet.Protocol.Parameters.ILlrpParameter roSpec)
{
    return roSpec switch
    {
        V101Parameters.ROSpec v101 => $"{v101.ROSpecID}/{v101.CurrentState}",
        V11Parameters.ROSpec v11 => $"{v11.ROSpecID}/{v11.CurrentState}",
        _ => roSpec.GetType().Name,
    };
}

static string DescribeImpinjReportExtensions(TagReport report)
{
    if (report.Extensions is null)
    {
        return "(none)";
    }

    string[] values = report.Extensions
        .Where(static pair => pair.Key.StartsWith("impinj.", StringComparison.Ordinal))
        .Select(static pair => $"{pair.Key}={FormatExtensionValue(pair.Value)}")
        .ToArray();
    return values.Length == 0 ? "(none)" : string.Join(", ", values);
}

static string FormatExtensionValue(object? value)
{
    return value switch
    {
        IReadOnlyList<ushort> words => string.Concat(words.Select(static word => word.ToString("X4"))),
        _ => value?.ToString() ?? "(null)",
    };
}
