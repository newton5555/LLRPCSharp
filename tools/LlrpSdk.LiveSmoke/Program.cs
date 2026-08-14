using LlrpNet.Core.Diagnostics;
using LlrpSdk;
using LlrpSdk.Extensions.Impinj;
using LlrpSdk.Extensions.Zebra;
using V101Parameters = LlrpNet.Protocol.Parameters.V1_0_1;
using V11Parameters = LlrpNet.Protocol.Parameters.V1_1;

if (args.Length < 1)
{
    throw new ArgumentException(
        "Usage: dotnet run --project tools/LlrpSdk.LiveSmoke -- <host> [--inventory] [--impinj-serialized-tid] [--impinj-rf-phase-angle] [--impinj-peak-rssi] [--impinj-population-estimation] [--read <epc-hex>] [--apply-current-impinj --yes] [--clear-managed --yes]");
}

string host = args[0];
bool requestZebra = args.Contains("--zebra", StringComparer.Ordinal);
bool requestImpinjSerializedTid = args.Contains("--impinj-serialized-tid", StringComparer.Ordinal);
bool requestImpinjRfPhaseAngle = args.Contains("--impinj-rf-phase-angle", StringComparer.Ordinal);
bool requestImpinjPeakRssi = args.Contains("--impinj-peak-rssi", StringComparer.Ordinal);
bool requestImpinjPopulationEstimation = args.Contains("--impinj-population-estimation", StringComparer.Ordinal);
bool applyCurrentImpinj = args.Contains("--apply-current-impinj", StringComparer.Ordinal);
bool clearManaged = args.Contains("--clear-managed", StringComparer.Ordinal);
bool confirmed = args.Contains("--yes", StringComparer.Ordinal);
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

    if (argument is not "--inventory" and not "--zebra" and not "--impinj-serialized-tid" and not "--impinj-rf-phase-angle" and not "--impinj-peak-rssi" and not "--impinj-population-estimation" and not "--apply-current-impinj" and not "--clear-managed" and not "--yes")
    {
        throw new ArgumentException($"Unknown LiveSmoke option '{argument}'.");
    }
}

bool requestImpinjReportFields = requestImpinjSerializedTid || requestImpinjRfPhaseAngle || requestImpinjPeakRssi;
if (applyCurrentImpinj && !confirmed)
{
    throw new ArgumentException("--apply-current-impinj writes reader configuration and requires --yes.");
}
if (clearManaged && !confirmed)
{
    throw new ArgumentException("--clear-managed deletes the SDK-managed ROSpec and requires --yes.");
}
var frameJournal = new LlrpFrameJournal();

LlrpReaderBuilder builder = LlrpReader.CreateBuilder(host);
if (requestZebra)
{
    builder.UseZebra();
}
else
{
    builder.UseImpinj();
}

await using LlrpReader reader = builder
    .WithFrameObserver(frameJournal)
    .WithConnectTimeout(TimeSpan.FromSeconds(10))
    .WithRequestTimeout(TimeSpan.FromSeconds(10))
    .Build();

try
{
    await reader.ConnectAsync();
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Connect failed: {exception.Message}");
    foreach (LlrpNet.Core.Diagnostics.LlrpCapturedFrame frame in frameJournal.Snapshot())
    {
        Console.Error.WriteLine($"  {frame.Direction} {Convert.ToHexString(frame.FrameBytes)}");
    }
    DumpDecodeFailures(reader, frameJournal);
    throw;
}
ReaderSettingsSnapshot settingsSnapshot;
try
{
    settingsSnapshot = await reader.QuerySettingsAsync();
}
catch (Exception exception)
{
    Console.Error.WriteLine($"QuerySettings failed: {exception.Message}");
    foreach (LlrpNet.Core.Diagnostics.LlrpCapturedFrame frame in frameJournal.Snapshot())
    {
        Console.Error.WriteLine($"  {frame.Direction} {Convert.ToHexString(frame.FrameBytes)}");
    }
    DumpDecodeFailures(reader, frameJournal);
    throw;
}

static void DumpDecodeFailures(LlrpReader reader, LlrpNet.Core.Diagnostics.LlrpFrameJournal journal)
{
    foreach (LlrpNet.Core.Diagnostics.LlrpCapturedFrame frame in journal.Snapshot())
    {
        if (frame.Direction != LlrpNet.Core.Diagnostics.LlrpFrameDirection.Receive)
        {
            continue;
        }

        try
        {
            reader.Registry.DecodeMessage(frame.FrameBytes);
        }
        catch (Exception decodeException)
        {
            Console.Error.WriteLine($"  DECODE DIAGNOSTIC: {decodeException.Message}");
            Console.Error.WriteLine(decodeException.StackTrace);
        }
    }
}
ReaderConfiguration configuration = settingsSnapshot.Settings.Configuration;
IReadOnlyList<LlrpNet.Protocol.Parameters.ILlrpParameter> configuredRoSpecs =
    await reader.RoSpecs.GetAllAsync();

Console.WriteLine($"Connected: {reader.ConnectionId}");
Console.WriteLine($"Protocol: {reader.NegotiatedVersion}");
Console.WriteLine($"Identity: {reader.Identity?.ManufacturerId}/{reader.Identity?.ModelId} {reader.Identity?.FirmwareVersion}");
Console.WriteLine($"Extensions: {string.Join(", ", reader.Extensions.Select(static extension => extension.Id))}");
Console.WriteLine($"Capabilities: antennas={reader.Capabilities?.MaxNumberOfAntennas}, additional={reader.Capabilities?.AdditionalParameters.Count}");
Console.WriteLine($"Configuration: antennas={configuration.Antennas.Count}, gpi={configuration.Gpis.Count}, gpo={configuration.Gpos.Count}");
Console.WriteLine($"Managed inventory: {settingsSnapshot.ManagedRoSpec?.State.ToString() ?? "none"}");
Console.WriteLine($"ROSpecs: {string.Join(", ", configuredRoSpecs.Select(DescribeRoSpec))}");
if (configuration.Extensions.TryGetValue(ImpinjReaderConfiguration.ExtensionKey, out object? configurationValue) &&
    configurationValue is ImpinjReaderConfiguration impinjConfiguration)
{
    Console.WriteLine(
        $"Impinj configuration: gpiDebounce={impinjConfiguration.GpiDebounce.Count}, linkMonitor={impinjConfiguration.LinkMonitor}, " +
        $"reportBuffer={impinjConfiguration.ReportBufferMode}, accessSpec={impinjConfiguration.AccessSpec}");
}
if (configuration.Extensions.TryGetValue(ImpinjReaderFacts.ExtensionKey, out object? factsValue) &&
    factsValue is ImpinjReaderFacts facts)
{
    Console.WriteLine($"Impinj facts: region={facts.RegulatoryRegion}, temperature={facts.TemperatureCelsius}");
}
if (requestZebra && reader.Capabilities is { } zebraCapabilities)
{
    Console.WriteLine($"Additional capability parameters: {string.Join(", ", zebraCapabilities.AdditionalParameters.Select(static parameter => parameter.GetType().Name))}");
}
if (configuration.Extensions.TryGetValue(ZebraReaderConfiguration.ExtensionKey, out object? zebraConfigurationValue) &&
    zebraConfigurationValue is ZebraReaderConfiguration zebraConfiguration)
{
    Console.WriteLine(
        $"Zebra configuration: radioPower={zebraConfiguration.RadioPowerState?.ToString() ?? "n/a"} " +
        $"transmitDelay={zebraConfiguration.RadioTransmitDelay?.ToString() ?? "n/a"} " +
        $"autonomous={zebraConfiguration.AutonomousModeState?.ToString() ?? "n/a"} " +
        $"persistence={zebraConfiguration.SaveConfiguration?.ToString() ?? "n/a"}/{zebraConfiguration.SaveTagData?.ToString() ?? "n/a"}/{zebraConfiguration.SaveTagEventData?.ToString() ?? "n/a"} " +
        $"nxpQuiet={zebraConfiguration.EnableNxpSetAndResetQuietCommands?.ToString() ?? "n/a"}");
}

if (clearManaged)
{
    await reader.SynchronizeStateAsync();
    await reader.ClearManagedSettingsAsync();
    Console.WriteLine("SDK-managed inventory resources cleared.");
}

if (applyCurrentImpinj)
{
    if (!configuration.Extensions.TryGetValue(ImpinjReaderConfiguration.ExtensionKey, out object? beforeValue) ||
        beforeValue is not ImpinjReaderConfiguration before)
    {
        throw new InvalidOperationException("The connected reader did not return an Impinj high-level configuration to reapply.");
    }

    Console.WriteLine("Applying the current Impinj configuration values back to the reader...");
    await reader.ApplySettingsAsync(settingsSnapshot.Settings with { Inventory = null });
    ReaderSettingsSnapshot afterSnapshot = await reader.QuerySettingsAsync();
    if (!afterSnapshot.Settings.Configuration.Extensions.TryGetValue(ImpinjReaderConfiguration.ExtensionKey, out object? afterValue) ||
        afterValue is not ImpinjReaderConfiguration after || !Equivalent(before, after))
    {
        throw new InvalidOperationException("Impinj configuration readback differs from the values submitted to the reader.");
    }

    Console.WriteLine("Impinj ApplySettingsAsync and QuerySettingsAsync readback verified.");
}

if (args.Contains("--inventory", StringComparer.Ordinal) || requestImpinjReportFields || requestImpinjPopulationEstimation || readOptionIndex >= 0)
{
    if (settingsSnapshot.Settings.Inventory is not null)
    {
        throw new InvalidOperationException(
            "The reader already has an SDK-managed inventory configuration. " +
            "The smoke tool refuses to replace it; clear it explicitly or use a dedicated test reader.");
    }

    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    await reader.SynchronizeStateAsync(timeout.Token);
    var inventoryExtensions = new Dictionary<string, object?>();
    if (requestImpinjReportFields)
    {
        inventoryExtensions[ImpinjInventoryReportOptions.ExtensionKey] = new ImpinjInventoryReportOptions
        {
            IncludeSerializedTid = requestImpinjSerializedTid,
            IncludeRfPhaseAngle = requestImpinjRfPhaseAngle,
            IncludePeakRssi = requestImpinjPeakRssi,
        };
    }
    if (requestImpinjPopulationEstimation)
    {
        inventoryExtensions[ImpinjInventoryControlOptions.ExtensionKey] = new ImpinjInventoryControlOptions
        {
            EnableTagPopulationEstimation = true,
        };
    }
    var inventorySettings = new InventorySettings { Extensions = inventoryExtensions };
    InventorySession inventorySession;
    try
    {
        inventorySession = await reader.StartInventoryAsync(inventorySettings, timeout.Token);
    }
    catch
    {
        DumpRoSpecFrames(frameJournal);
        throw;
    }

    await using var inventorySessionLease = inventorySession;
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
        await reader.ClearManagedSettingsAsync();
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

static bool Equivalent(ImpinjReaderConfiguration left, ImpinjReaderConfiguration right)
{
    return left.GpiDebounce.SequenceEqual(right.GpiDebounce) &&
        left.LinkMonitor == right.LinkMonitor &&
        left.ReportBufferMode == right.ReportBufferMode &&
        left.AccessSpec == right.AccessSpec &&
        left.AdvancedGpos.SequenceEqual(right.AdvancedGpos);
}
