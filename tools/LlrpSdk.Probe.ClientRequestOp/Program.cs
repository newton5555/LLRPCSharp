using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Messages;
using LlrpNet.Protocol.Parameters;
using LlrpSdk;
using V101Choices = LlrpNet.Protocol.Choices.V1_0_1;
using V101Enumerations = LlrpNet.Protocol.Enumerations.V1_0_1;
using V101Messages = LlrpNet.Protocol.Messages.V1_0_1;
using V101Parameters = LlrpNet.Protocol.Parameters.V1_0_1;

// Probes whether an Impinj R430 actually emits CLIENT_REQUEST_OP when an AccessSpec
// with a ClientRequestOpSpec is enabled while an inventory ROSpec is running.

const uint AccessSpecId = 55502;
const int ListenSeconds = 20;
// LlrpReader.ManagedInventoryRoSpecId is internal; the SDK-managed inventory ROSpec always uses 14150.
const uint ManagedRoSpecId = 14150;

if (args.Length < 1)
{
    Console.Error.WriteLine("Usage: LlrpSdk.Probe.ClientRequestOp <host>");
    return 1;
}

string host = args[0];
var options = new LlrpReaderBuilder(host).BuildOptions();
await using var reader = new LlrpReader(options);
await reader.ConnectAsync();
Console.WriteLine($"Connected: protocol={reader.NegotiatedVersion}");

// 0) Read the LLRP capability gate for client-requested access from the SDK capabilities model.
if (reader.Capabilities is { } caps)
{
    Console.WriteLine(
        $"ReaderCapabilities: SupportsClientRequestOpSpec={caps.SupportsClientRequestOpSpec} " +
        $"CanDoRfSurvey={caps.CanDoRfSurvey} IsTagAccessAvailable={caps.IsTagAccessAvailable}");
}
if (reader.Capabilities?.RawResponse is V101Messages.GET_READER_CAPABILITIES_RESPONSE capabilityResponse &&
    capabilityResponse.LLRPCapabilities is { } llrpCaps)
{
    Console.WriteLine(
        $"LLRPCapabilities: SupportsEventAndReportHolding={llrpCaps.SupportsEventAndReportHolding} " +
        $"CanReportBufferFillWarning={llrpCaps.CanReportBufferFillWarning}");
}

// 1) Deploy a managed inventory ROSpec through the SDK (SDK compiles and owns it).
using var inventoryTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
await reader.SynchronizeStateAsync(inventoryTimeout.Token);
await using var inventorySession = await reader.StartInventoryAsync(new InventorySettings(), inventoryTimeout.Token);
Console.WriteLine("[1] Managed inventory ROSpec deployed and started.");

bool sawRequest = false;
bool sawReadResult = false;
try
{
    // 2) Deploy an AccessSpec whose AccessCommand is ClientRequestOpSpec (no concrete operation).
    var target = new V101Parameters.C1G2TargetTag(
        MB: 0,
        Match: false,
        Pointer: 0,
        TagMask: Array.Empty<bool>(),
        TagData: Array.Empty<bool>());
    var command = new V101Parameters.AccessCommand(
        new V101Parameters.C1G2TagSpec([target]),
        new ILlrpParameter[] { new V101Parameters.ClientRequestOpSpec(1) },
        []);
    var accessSpec = new V101Parameters.AccessSpec(
        AccessSpecId,
        AntennaID: 0,
        V101Enumerations.AirProtocols.EPCGlobalClass1Gen2,
        V101Enumerations.AccessSpecState.Disabled,
        ManagedRoSpecId,
        new V101Parameters.AccessSpecStopTrigger(V101Enumerations.AccessSpecStopTriggerType.Operation_Count, 1),
        command,
        new V101Parameters.AccessReportSpec(V101Enumerations.AccessReportTriggerType.End_Of_AccessSpec),
        []);
    await Transact<V101Messages.ADD_ACCESSSPEC_RESPONSE>(
        reader,
        new V101Messages.ADD_ACCESSSPEC(reader.Protocol.NextMessageId(), accessSpec));
    await Transact<V101Messages.ENABLE_ACCESSSPEC_RESPONSE>(
        reader,
        new V101Messages.ENABLE_ACCESSSPEC(reader.Protocol.NextMessageId(), AccessSpecId));
    Console.WriteLine($"[2] ClientRequestOpSpec AccessSpec {AccessSpecId} enabled. Waiting up to {ListenSeconds}s for CLIENT_REQUEST_OP...");

    // 3) Watch the raw message stream for CLIENT_REQUEST_OP / reports / errors.
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(ListenSeconds));
    try
    {
        await foreach (ILlrpMessage message in reader.ReadMessagesAsync(timeout.Token))
        {
            switch (message)
            {
                case V101Messages.CLIENT_REQUEST_OP request:
                    sawRequest = true;
                    Console.WriteLine("== CLIENT_REQUEST_OP received ==");
                    PrintTagReportData(request.TagReportData);
                    var epc = (V101Parameters.EPCData)request.TagReportData.EPCParameter;
                    var response = new V101Messages.CLIENT_REQUEST_OP_RESPONSE(
                        reader.Protocol.NextMessageId(),
                        new V101Parameters.ClientRequestResponse(
                            AccessSpecId,
                            epc,
                            new V101Choices.IAirProtocolOpSpec[]
                            {
                                new V101Parameters.C1G2Read(OpSpecID: 1, AccessPassword: 0, MB: 3, WordPointer: 0, WordCount: 1),
                            }));
                    await reader.Protocol.SendAsync(response);
                    Console.WriteLine("== CLIENT_REQUEST_OP_RESPONSE sent (C1G2Read user-memory word 0) ==");
                    break;

                case V101Messages.RO_ACCESS_REPORT report:
                    foreach (V101Parameters.TagReportData data in report.TagReportDataItems)
                    {
                        PrintTagReportData(data);
                        foreach (ILlrpParameter item in data.AccessCommandOpSpecResultItems)
                        {
                            if (item is V101Parameters.C1G2ReadOpSpecResult readResult)
                            {
                                sawReadResult = true;
                                Console.WriteLine(
                                    $"    C1G2ReadOpSpecResult: status={readResult.Result} opSpecId={readResult.OpSpecID} data={ToHexWords(readResult.ReadData)}");
                            }
                            else
                            {
                                Console.WriteLine($"    OpSpecResult: {item.GetType().Name}");
                            }
                        }
                    }
                    break;

                case V101Messages.ERROR_MESSAGE error:
                    Console.WriteLine($"ERROR_MESSAGE: status={error.LLRPStatus.StatusCode} description={error.LLRPStatus.ErrorDescription}");
                    break;
            }
        }
    }
    catch (OperationCanceledException)
    {
        Console.WriteLine($"[3] Timeout: no CLIENT_REQUEST_OP within {ListenSeconds}s.");
    }

    Console.WriteLine();
    Console.WriteLine(sawRequest
        ? "RESULT: R430 SUPPORTS CLIENT_REQUEST_OP (emitted the request and accepted a typed response)."
        : "RESULT: NOT OBSERVED - R430 did not emit CLIENT_REQUEST_OP within the listen window.");
    if (sawReadResult)
    {
        Console.WriteLine("RESULT: The requested C1G2Read was executed and reported via RO_ACCESS_REPORT.");
    }
}
finally
{
    // 4) Remove only the AccessSpec we deployed, then stop and clear the SDK-managed inventory
    // resources exactly like the LiveSmoke tool does (session disposal alone does not delete them).
    await Quiet(async () => await Transact<V101Messages.DISABLE_ACCESSSPEC_RESPONSE>(
        reader,
        new V101Messages.DISABLE_ACCESSSPEC(reader.Protocol.NextMessageId(), AccessSpecId)));
    await Quiet(async () => await Transact<V101Messages.DELETE_ACCESSSPEC_RESPONSE>(
        reader,
        new V101Messages.DELETE_ACCESSSPEC(reader.Protocol.NextMessageId(), AccessSpecId)));
    await Quiet(async () => await reader.StopAsync());
    await Quiet(async () => await reader.SynchronizeStateAsync());
    await Quiet(async () => await reader.ClearManagedSettingsAsync());
    Console.WriteLine("[4] Cleanup complete.");
}

return 0;

static async Task Transact<TResponse>(LlrpReader reader, ILlrpMessage request)
    where TResponse : class, ILlrpMessage
{
    TResponse response = await reader.Protocol.TransactAsync<TResponse>(request);
    var status = response.GetType().GetProperty("LLRPStatus")?.GetValue(response);
    Console.WriteLine($"    {request.GetType().Name} -> {response.GetType().Name} status={status}");
}

static async Task Quiet(Func<Task> action)
{
    try
    {
        await action();
    }
    catch (Exception exception)
    {
        Console.WriteLine($"    cleanup warning: {exception.Message}");
    }
}

static void PrintTagReportData(V101Parameters.TagReportData data)
{
    string epc = data.EPCParameter is V101Parameters.EPCData epcData
        ? ToHexBits(epcData.EPC)
        : data.EPCParameter.GetType().Name;
    Console.WriteLine(
        $"    EPC={epc} antenna={data.AntennaID?.AntennaID_2} rssi={data.PeakRSSI?.PeakRSSI_2} " +
        $"roSpecId={data.ROSpecID?.ROSpecID_2} accessSpecId={data.AccessSpecID?.AccessSpecID_2}");
}

static string ToHexBits(IEnumerable<bool> bits)
{
    bool[] array = bits.ToArray();
    var bytes = new byte[(array.Length + 7) / 8];
    for (int index = 0; index < array.Length; index++)
    {
        if (array[index])
        {
            bytes[index / 8] |= (byte)(0x80 >> (index % 8));
        }
    }

    return Convert.ToHexString(bytes);
}

static string ToHexWords(IReadOnlyList<ushort> words)
{
    var bytes = new byte[words.Count * 2];
    for (int index = 0; index < words.Count; index++)
    {
        bytes[index * 2] = (byte)(words[index] >> 8);
        bytes[index * 2 + 1] = (byte)words[index];
    }

    return Convert.ToHexString(bytes);
}
