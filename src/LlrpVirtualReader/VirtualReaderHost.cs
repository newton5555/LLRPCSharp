using System.Net;
using System.Net.Sockets;
using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Enumerations.V1_0_1;
using LlrpNet.Protocol.Messages;
using LlrpNet.Protocol.Messages.V1_0_1;
using LlrpNet.Protocol.Parameters;
using LlrpNet.Protocol.Parameters.V1_0_1;
using LlrpNet.Protocol.Registry;
using LlrpNet.Protocol.Registry.V1_0_1;
using V101Messages = LlrpNet.Protocol.Messages.V1_0_1;

namespace LlrpVirtualReader;

/// <summary>Small stateful LLRP 1.0.1 TCP server for SDK and integration development.</summary>
public sealed class VirtualReaderHost : IAsyncDisposable
{
    private readonly TcpListener listener;
    private readonly LlrpCodecRegistry registry = new();
    private readonly Dictionary<uint, ROSpec> roSpecs = [];
    private readonly HashSet<uint> enabledRoSpecs = [];
    private readonly Dictionary<uint, AccessSpec> accessSpecs = [];
    private readonly HashSet<uint> enabledAccessSpecs = [];
    private readonly object configurationGate = new();
    private IReadOnlyList<AntennaConfiguration> antennaConfigurations = [];
    private IReadOnlyList<GPOWriteData> gpoWriteData = [new GPOWriteData(1, false)];
    private ReaderEventNotificationSpec? readerEventNotificationSpec;
    private KeepaliveSpec? keepaliveSpec = new(KeepaliveTriggerType.Null, 0);
    private readonly CancellationTokenSource cancellation = new();
    private readonly byte[] tagEpc;
    private ushort[] tagUserMemory;
    private readonly HashSet<ushort> droppedResponseMessageTypes;
    private readonly Dictionary<ushort, VirtualReaderErrorResponse> errorResponseMessageTypes;
    private readonly HashSet<ushort> closeConnectionRequestMessageTypes;
    private readonly HashSet<ushort> truncateResponseMessageTypes;
    private int nextAsyncMessageId;
    private Task? acceptLoop;

    public VirtualReaderHost(int port = 0, VirtualReaderOptions? options = null)
    {
        options ??= new VirtualReaderOptions();
        if (options.ElectronicProductCode.Length != 12)
        {
            throw new ArgumentException("The virtual tag EPC must be exactly 96 bits.", nameof(options));
        }
        ArgumentNullException.ThrowIfNull(options.UserMemory);
        ArgumentNullException.ThrowIfNull(options.DropResponseForMessageTypes);
        ArgumentNullException.ThrowIfNull(options.ErrorResponseForMessageTypes);
        ArgumentNullException.ThrowIfNull(options.CloseConnectionAfterRequestMessageTypes);
        ArgumentNullException.ThrowIfNull(options.TruncateResponseForMessageTypes);

        listener = new TcpListener(IPAddress.Loopback, port);
        tagEpc = options.ElectronicProductCode.ToArray();
        tagUserMemory = options.UserMemory.ToArray();
        droppedResponseMessageTypes = options.DropResponseForMessageTypes.ToHashSet();
        errorResponseMessageTypes = options.ErrorResponseForMessageTypes.ToDictionary();
        closeConnectionRequestMessageTypes = options.CloseConnectionAfterRequestMessageTypes.ToHashSet();
        truncateResponseMessageTypes = options.TruncateResponseForMessageTypes.ToHashSet();
        Llrp101StandardModule.Register(registry);
    }

    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

    public void Start()
    {
        if (acceptLoop is not null)
        {
            throw new InvalidOperationException("The virtual reader is already running.");
        }
        listener.Start();
        acceptLoop = AcceptAsync(cancellation.Token);
    }

    public async ValueTask DisposeAsync()
    {
        cancellation.Cancel();
        listener.Stop();
        if (acceptLoop is not null)
        {
            await acceptLoop.ConfigureAwait(false);
        }
        cancellation.Dispose();
    }

    private async Task AcceptAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                TcpClient client = await listener.AcceptTcpClientAsync(token).ConfigureAwait(false);
                _ = ServeAsync(client, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (SocketException) when (token.IsCancellationRequested) { }
    }

    private async Task ServeAsync(TcpClient client, CancellationToken token)
    {
        using (client)
        await using (NetworkStream stream = client.GetStream())
        {
            var headerBuffer = new byte[LlrpMessageHeader.EncodedLength];
            while (!token.IsCancellationRequested && await ReadExactAsync(stream, headerBuffer, token).ConfigureAwait(false))
            {
                LlrpMessageHeader header = LlrpMessageHeader.Decode(headerBuffer);
                byte[] frame = new byte[checked((int)header.MessageLength)];
                headerBuffer.CopyTo(frame, 0);
                if (!await ReadExactAsync(stream, frame.AsMemory(headerBuffer.Length), token).ConfigureAwait(false))
                {
                    return;
                }
                ILlrpMessage request = registry.DecodeMessage(frame);
                if (ShouldCloseConnection(header.MessageType))
                {
                    return;
                }
                VirtualResponse dispatched = errorResponseMessageTypes.TryGetValue(header.MessageType, out VirtualReaderErrorResponse? fault)
                    ? new(new V101Messages.ERROR_MESSAGE(header.MessageId, new LLRPStatus(fault.StatusCode, fault.Description, null, null)), [])
                    : request switch
                    {
                        V101Messages.GET_READER_CAPABILITIES => new(Capabilities(header.MessageId), []),
                        GET_READER_CONFIG get => new(GetReaderConfig(get), []),
                        SET_READER_CONFIG set => new(SetReaderConfig(set), []),
                        ADD_ROSPEC add => new(AddRoSpec(add), []),
                        GET_ROSPECS get => new(GetRoSpecs(get), []),
                        DELETE_ROSPEC delete => new(DeleteRoSpec(delete), []),
                        ENABLE_ROSPEC enable => EnableRoSpecWithReports(enable),
                        DISABLE_ROSPEC disable => new(DisableRoSpec(disable), []),
                        START_ROSPEC start => StartRoSpecWithReports(start),
                        STOP_ROSPEC stop => new(StopRoSpec(stop), []),
                        ADD_ACCESSSPEC add => new(AddAccessSpec(add), []),
                        GET_ACCESSSPECS get => new(GetAccessSpecs(get), []),
                        DELETE_ACCESSSPEC delete => new(DeleteAccessSpec(delete), []),
                        ENABLE_ACCESSSPEC enable => EnableAccessSpecWithReport(enable),
                        DISABLE_ACCESSSPEC disable => new(DisableAccessSpec(disable), []),
                        _ => new(new V101Messages.ERROR_MESSAGE(header.MessageId, new LLRPStatus(StatusCode.M_UnsupportedMessage, "Virtual reader does not implement this request.", null, null)), []),
                    };
                if (droppedResponseMessageTypes.Contains(header.MessageType))
                {
                    continue;
                }
                byte[] responseFrame = registry.EncodeMessage(LlrpProtocolVersion.Version101, dispatched.Response);
                if (truncateResponseMessageTypes.Contains(header.MessageType))
                {
                    await stream.WriteAsync(responseFrame.AsMemory(0, responseFrame.Length - 1), token).ConfigureAwait(false);
                    return;
                }
                await stream.WriteAsync(responseFrame, token).ConfigureAwait(false);
                foreach (ILlrpMessage report in dispatched.Reports)
                {
                    byte[] reportFrame = registry.EncodeMessage(LlrpProtocolVersion.Version101, report);
                    await stream.WriteAsync(reportFrame, token).ConfigureAwait(false);
                }
            }
        }
    }

    private bool ShouldCloseConnection(ushort messageType)
    {
        lock (closeConnectionRequestMessageTypes)
        {
            return closeConnectionRequestMessageTypes.Remove(messageType);
        }
    }

    private static V101Messages.GET_READER_CAPABILITIES_RESPONSE Capabilities(uint messageId) => new(
        messageId,
        new LLRPStatus(StatusCode.M_Success, string.Empty, null, null),
        new GeneralDeviceCapabilities(4, true, true, 0, 0, "virtual-reader", [new ReceiveSensitivityTableEntry(1, 0)], [], new GPIOCapabilities(0, 0), [new PerAntennaAirProtocol(1, [AirProtocols.Unspecified])]),
        null, null, null, []);

    private GET_READER_CONFIG_RESPONSE GetReaderConfig(GET_READER_CONFIG request)
    {
        lock (configurationGate)
        {
            IReadOnlyList<AntennaProperties> properties = Enumerable.Range(1, 4)
                .Select(static id => new AntennaProperties(true, checked((ushort)id), 0))
                .ToArray();
            IReadOnlyList<GPIPortCurrentState> gpis = Enumerable.Range(1, 4)
                .Select(static id => new GPIPortCurrentState(checked((ushort)id), true, GPIPortState.Low))
                .ToArray();
            return new GET_READER_CONFIG_RESPONSE(
                request.MessageId,
                Status(StatusCode.M_Success, string.Empty),
                Identification: null,
                AntennaPropertiesItems: properties,
                AntennaConfigurationItems: antennaConfigurations,
                ReaderEventNotificationSpec: readerEventNotificationSpec,
                ROReportSpec: null,
                AccessReportSpec: null,
                LLRPConfigurationStateValue: null,
                KeepaliveSpec: keepaliveSpec,
                GPIPortCurrentStateItems: gpis,
                GPOWriteDataItems: gpoWriteData,
                EventsAndReports: null,
                CustomItems: []);
        }
    }

    private SET_READER_CONFIG_RESPONSE SetReaderConfig(SET_READER_CONFIG request)
    {
        lock (configurationGate)
        {
            if (request.ResetToFactoryDefault)
            {
                antennaConfigurations = [];
                gpoWriteData = [new GPOWriteData(1, false)];
                readerEventNotificationSpec = null;
                keepaliveSpec = new KeepaliveSpec(KeepaliveTriggerType.Null, 0);
            }
            else
            {
                if (request.KeepaliveSpec is not null)
                {
                    keepaliveSpec = request.KeepaliveSpec;
                }
                if (request.AntennaConfigurationItems.Count > 0)
                {
                    antennaConfigurations = request.AntennaConfigurationItems.ToArray();
                }
                if (request.GPOWriteDataItems.Count > 0)
                {
                    gpoWriteData = request.GPOWriteDataItems.ToArray();
                }
                if (request.ReaderEventNotificationSpec is not null)
                {
                    readerEventNotificationSpec = request.ReaderEventNotificationSpec;
                }
            }
        }

        return new SET_READER_CONFIG_RESPONSE(request.MessageId, Status(StatusCode.M_Success, string.Empty));
    }

    private ADD_ROSPEC_RESPONSE AddRoSpec(ADD_ROSPEC request)
    {
        lock (roSpecs)
        {
            if (!roSpecs.TryAdd(request.ROSpec.ROSpecID, request.ROSpec))
            {
                return new ADD_ROSPEC_RESPONSE(request.MessageId, Status(StatusCode.M_ParameterError, "ROSpec already exists."));
            }
        }

        return new ADD_ROSPEC_RESPONSE(request.MessageId, Status(StatusCode.M_Success, string.Empty));
    }

    private GET_ROSPECS_RESPONSE GetRoSpecs(GET_ROSPECS request)
    {
        ROSpec[] items;
        lock (roSpecs)
        {
            items = roSpecs.Values.OrderBy(static item => item.ROSpecID).ToArray();
        }

        return new GET_ROSPECS_RESPONSE(request.MessageId, Status(StatusCode.M_Success, string.Empty), items);
    }

    private DELETE_ROSPEC_RESPONSE DeleteRoSpec(DELETE_ROSPEC request)
    {
        lock (roSpecs)
        {
            if (request.ROSpecID == 0)
            {
                roSpecs.Clear();
                enabledRoSpecs.Clear();
                accessSpecs.Clear();
                enabledAccessSpecs.Clear();
                return new DELETE_ROSPEC_RESPONSE(request.MessageId, Status(StatusCode.M_Success, string.Empty));
            }

            if (!roSpecs.Remove(request.ROSpecID))
            {
                return new DELETE_ROSPEC_RESPONSE(request.MessageId, MissingRoSpec(request.ROSpecID));
            }

            enabledRoSpecs.Remove(request.ROSpecID);
            foreach (uint accessSpecId in accessSpecs.Values
                .Where(accessSpec => accessSpec.ROSpecID == request.ROSpecID)
                .Select(static accessSpec => accessSpec.AccessSpecID)
                .ToArray())
            {
                accessSpecs.Remove(accessSpecId);
                enabledAccessSpecs.Remove(accessSpecId);
            }
        }

        return new DELETE_ROSPEC_RESPONSE(request.MessageId, Status(StatusCode.M_Success, string.Empty));
    }

    private ENABLE_ROSPEC_RESPONSE EnableRoSpec(ENABLE_ROSPEC request)
    {
        lock (roSpecs)
        {
            if (!roSpecs.TryGetValue(request.ROSpecID, out ROSpec? roSpec))
            {
                return new ENABLE_ROSPEC_RESPONSE(request.MessageId, MissingRoSpec(request.ROSpecID));
            }

            bool immediate = roSpec.ROBoundarySpec.ROSpecStartTrigger.ROSpecStartTriggerType == ROSpecStartTriggerType.Immediate;
            roSpecs[request.ROSpecID] = roSpec with { CurrentState = immediate ? ROSpecState.Active : ROSpecState.Inactive };
            enabledRoSpecs.Add(request.ROSpecID);
        }

        return new ENABLE_ROSPEC_RESPONSE(request.MessageId, Status(StatusCode.M_Success, string.Empty));
    }

    private VirtualResponse EnableRoSpecWithReports(ENABLE_ROSPEC request)
    {
        ENABLE_ROSPEC_RESPONSE response = EnableRoSpec(request);
        return response.LLRPStatus.StatusCode == StatusCode.M_Success
            ? new(response, BuildInventoryReports(request.ROSpecID))
            : new(response, []);
    }

    private DISABLE_ROSPEC_RESPONSE DisableRoSpec(DISABLE_ROSPEC request)
    {
        lock (roSpecs)
        {
            if (!roSpecs.TryGetValue(request.ROSpecID, out ROSpec? roSpec))
            {
                return new DISABLE_ROSPEC_RESPONSE(request.MessageId, MissingRoSpec(request.ROSpecID));
            }

            roSpecs[request.ROSpecID] = roSpec with { CurrentState = ROSpecState.Disabled };
            enabledRoSpecs.Remove(request.ROSpecID);
        }

        return new DISABLE_ROSPEC_RESPONSE(request.MessageId, Status(StatusCode.M_Success, string.Empty));
    }

    private START_ROSPEC_RESPONSE StartRoSpec(START_ROSPEC request)
    {
        lock (roSpecs)
        {
            if (!roSpecs.TryGetValue(request.ROSpecID, out ROSpec? roSpec))
            {
                return new START_ROSPEC_RESPONSE(request.MessageId, MissingRoSpec(request.ROSpecID));
            }

            if (!enabledRoSpecs.Contains(request.ROSpecID))
            {
                return new START_ROSPEC_RESPONSE(
                    request.MessageId,
                    Status(StatusCode.M_ParameterError, "ROSpec must be enabled before it can be started."));
            }

            roSpecs[request.ROSpecID] = roSpec with { CurrentState = ROSpecState.Active };
        }

        return new START_ROSPEC_RESPONSE(request.MessageId, Status(StatusCode.M_Success, string.Empty));
    }

    private VirtualResponse StartRoSpecWithReports(START_ROSPEC request)
    {
        START_ROSPEC_RESPONSE response = StartRoSpec(request);
        return response.LLRPStatus.StatusCode == StatusCode.M_Success
            ? new(response, BuildInventoryReports(request.ROSpecID))
            : new(response, []);
    }

    private STOP_ROSPEC_RESPONSE StopRoSpec(STOP_ROSPEC request)
    {
        lock (roSpecs)
        {
            if (!roSpecs.TryGetValue(request.ROSpecID, out ROSpec? roSpec))
            {
                return new STOP_ROSPEC_RESPONSE(request.MessageId, MissingRoSpec(request.ROSpecID));
            }

            roSpecs[request.ROSpecID] = roSpec with
            {
                CurrentState = enabledRoSpecs.Contains(request.ROSpecID)
                    ? ROSpecState.Inactive
                    : ROSpecState.Disabled,
            };
        }

        return new STOP_ROSPEC_RESPONSE(request.MessageId, Status(StatusCode.M_Success, string.Empty));
    }

    private ADD_ACCESSSPEC_RESPONSE AddAccessSpec(ADD_ACCESSSPEC request)
    {
        lock (accessSpecs)
        {
            if (!roSpecs.ContainsKey(request.AccessSpec.ROSpecID))
            {
                return new ADD_ACCESSSPEC_RESPONSE(request.MessageId, MissingRoSpec(request.AccessSpec.ROSpecID));
            }

            if (!accessSpecs.TryAdd(request.AccessSpec.AccessSpecID, request.AccessSpec))
            {
                return new ADD_ACCESSSPEC_RESPONSE(request.MessageId, Status(StatusCode.M_ParameterError, "AccessSpec already exists."));
            }
        }

        return new ADD_ACCESSSPEC_RESPONSE(request.MessageId, Status(StatusCode.M_Success, string.Empty));
    }

    private GET_ACCESSSPECS_RESPONSE GetAccessSpecs(GET_ACCESSSPECS request)
    {
        AccessSpec[] items;
        lock (accessSpecs)
        {
            items = accessSpecs.Values.OrderBy(static item => item.AccessSpecID).ToArray();
        }

        return new GET_ACCESSSPECS_RESPONSE(request.MessageId, Status(StatusCode.M_Success, string.Empty), items);
    }

    private DELETE_ACCESSSPEC_RESPONSE DeleteAccessSpec(DELETE_ACCESSSPEC request)
    {
        lock (accessSpecs)
        {
            if (request.AccessSpecID == 0)
            {
                accessSpecs.Clear();
                enabledAccessSpecs.Clear();
                return new DELETE_ACCESSSPEC_RESPONSE(request.MessageId, Status(StatusCode.M_Success, string.Empty));
            }

            if (!accessSpecs.Remove(request.AccessSpecID))
            {
                return new DELETE_ACCESSSPEC_RESPONSE(request.MessageId, MissingAccessSpec(request.AccessSpecID));
            }

            enabledAccessSpecs.Remove(request.AccessSpecID);
        }

        return new DELETE_ACCESSSPEC_RESPONSE(request.MessageId, Status(StatusCode.M_Success, string.Empty));
    }

    private DISABLE_ACCESSSPEC_RESPONSE DisableAccessSpec(DISABLE_ACCESSSPEC request)
    {
        lock (accessSpecs)
        {
            if (!accessSpecs.TryGetValue(request.AccessSpecID, out AccessSpec? accessSpec))
            {
                return new DISABLE_ACCESSSPEC_RESPONSE(request.MessageId, MissingAccessSpec(request.AccessSpecID));
            }

            accessSpecs[request.AccessSpecID] = accessSpec with { CurrentState = AccessSpecState.Disabled };
            enabledAccessSpecs.Remove(request.AccessSpecID);
        }

        return new DISABLE_ACCESSSPEC_RESPONSE(request.MessageId, Status(StatusCode.M_Success, string.Empty));
    }

    private VirtualResponse EnableAccessSpecWithReport(ENABLE_ACCESSSPEC request)
    {
        ENABLE_ACCESSSPEC_RESPONSE response;
        AccessSpec? enabled = null;
        lock (accessSpecs)
        {
            if (!accessSpecs.TryGetValue(request.AccessSpecID, out AccessSpec? accessSpec))
            {
                response = new ENABLE_ACCESSSPEC_RESPONSE(request.MessageId, MissingAccessSpec(request.AccessSpecID));
            }
            else if (!enabledRoSpecs.Contains(accessSpec.ROSpecID))
            {
                response = new ENABLE_ACCESSSPEC_RESPONSE(request.MessageId, Status(StatusCode.M_ParameterError, "Associated ROSpec is not enabled."));
            }
            else
            {
                enabled = accessSpec with { CurrentState = AccessSpecState.Active };
                accessSpecs[request.AccessSpecID] = enabled;
                enabledAccessSpecs.Add(request.AccessSpecID);
                response = new ENABLE_ACCESSSPEC_RESPONSE(request.MessageId, Status(StatusCode.M_Success, string.Empty));
            }
        }

        return enabled is null ? new(response, []) : new(response, [BuildAccessReport(enabled)]);
    }

    private IReadOnlyList<ILlrpMessage> BuildInventoryReports(uint roSpecId)
    {
        ROSpec? roSpec;
        lock (roSpecs)
        {
            roSpecs.TryGetValue(roSpecId, out roSpec);
        }

        return roSpec?.CurrentState == ROSpecState.Active
            ? [new RO_ACCESS_REPORT(NextAsyncMessageId(), [BuildTagReport(roSpecId, null, [])], [], [])]
            : [];
    }

    private RO_ACCESS_REPORT BuildAccessReport(AccessSpec accessSpec)
    {
        bool selected = MatchesTag(accessSpec.AccessCommand.AirProtocolTagSpec);
        IReadOnlyList<ILlrpParameter> results = accessSpec.AccessCommand.AccessCommandOpSpecItems
            .Select(operation => BuildOperationResult(operation, selected))
            .ToArray();
        return new RO_ACCESS_REPORT(
            NextAsyncMessageId(),
            [BuildTagReport(accessSpec.ROSpecID, accessSpec.AccessSpecID, results)],
            [],
            []);
    }

    private ILlrpParameter BuildOperationResult(ILlrpParameter operation, bool selected)
    {
        if (!selected)
        {
            return operation switch
            {
                C1G2Read read => new C1G2ReadOpSpecResult(C1G2ReadResultType.No_Response_From_Tag, read.OpSpecID, []),
                C1G2Write write => new C1G2WriteOpSpecResult(C1G2WriteResultType.No_Response_From_Tag, write.OpSpecID, 0),
                _ => throw new NotSupportedException($"Virtual reader does not implement access operation {operation.GetType().Name}."),
            };
        }

        return operation switch
        {
            C1G2Read read => Read(read),
            C1G2Write write => Write(write),
            _ => throw new NotSupportedException($"Virtual reader does not implement access operation {operation.GetType().Name}."),
        };
    }

    private C1G2ReadOpSpecResult Read(C1G2Read operation)
    {
        ushort[] memory = GetMemory(operation.MB);
        int start = operation.WordPointer;
        int count = operation.WordCount;
        return start < 0 || start + count > memory.Length
            ? new C1G2ReadOpSpecResult(C1G2ReadResultType.Nonspecific_Tag_Error, operation.OpSpecID, [])
            : new C1G2ReadOpSpecResult(C1G2ReadResultType.Success, operation.OpSpecID, memory.Skip(start).Take(count).ToArray());
    }

    private C1G2WriteOpSpecResult Write(C1G2Write operation)
    {
        if (operation.MB != 3)
        {
            return new C1G2WriteOpSpecResult(C1G2WriteResultType.Tag_Memory_Locked_Error, operation.OpSpecID, 0);
        }

        int start = operation.WordPointer;
        if (start < 0 || start + operation.WriteData.Count > tagUserMemory.Length)
        {
            return new C1G2WriteOpSpecResult(C1G2WriteResultType.Tag_Memory_Overrun_Error, operation.OpSpecID, 0);
        }

        for (int index = 0; index < operation.WriteData.Count; index++)
        {
            tagUserMemory[start + index] = operation.WriteData[index];
        }
        return new C1G2WriteOpSpecResult(C1G2WriteResultType.Success, operation.OpSpecID, checked((ushort)operation.WriteData.Count));
    }

    private bool MatchesTag(global::LlrpNet.Protocol.Choices.V1_0_1.IAirProtocolTagSpec tagSpec)
    {
        if (tagSpec is not C1G2TagSpec c1g2)
        {
            return false;
        }

        foreach (C1G2TargetTag target in c1g2.C1G2TargetTagItems)
        {
            bool match = MatchesTarget(target);
            if (match != target.Match)
            {
                return false;
            }
        }
        return true;
    }

    private bool MatchesTarget(C1G2TargetTag target)
    {
        bool[] memoryBits = ToBits(GetMemoryBytes(target.MB));
        if (target.Pointer + target.TagMask.Count > memoryBits.Length || target.TagMask.Count != target.TagData.Count)
        {
            return false;
        }

        for (int index = 0; index < target.TagMask.Count; index++)
        {
            if (target.TagMask[index] && memoryBits[target.Pointer + index] != target.TagData[index])
            {
                return false;
            }
        }
        return true;
    }

    private ushort[] GetMemory(byte memoryBank) => memoryBank switch
    {
        1 => [0, 0, .. BytesToWords(tagEpc)],
        3 => tagUserMemory,
        _ => [],
    };

    private byte[] GetMemoryBytes(byte memoryBank) => memoryBank switch
    {
        1 => [0, 0, 0, 0, .. tagEpc],
        3 => WordsToBytes(tagUserMemory),
        _ => [],
    };

    private static ushort[] BytesToWords(ReadOnlySpan<byte> bytes)
    {
        var words = new ushort[bytes.Length / 2];
        for (int index = 0; index < words.Length; index++)
        {
            words[index] = (ushort)((bytes[index * 2] << 8) | bytes[(index * 2) + 1]);
        }
        return words;
    }

    private static byte[] WordsToBytes(ReadOnlySpan<ushort> words)
    {
        var bytes = new byte[words.Length * 2];
        for (int index = 0; index < words.Length; index++)
        {
            bytes[index * 2] = (byte)(words[index] >> 8);
            bytes[(index * 2) + 1] = (byte)words[index];
        }
        return bytes;
    }

    private static bool[] ToBits(ReadOnlySpan<byte> bytes)
    {
        var bits = new bool[bytes.Length * 8];
        for (int index = 0; index < bits.Length; index++)
        {
            bits[index] = (bytes[index / 8] & (1 << (7 - (index % 8)))) != 0;
        }
        return bits;
    }

    private TagReportData BuildTagReport(
        uint roSpecId,
        uint? accessSpecId,
        IReadOnlyList<ILlrpParameter> results) =>
        new(
            new EPC_96(tagEpc),
            new ROSpecID(roSpecId),
            null,
            new InventoryParameterSpecID(1),
            new AntennaID(1),
            new PeakRSSI(-42),
            null,
            null,
            null,
            null,
            null,
            new TagSeenCount(1),
            [],
            accessSpecId is uint id ? new AccessSpecID(id) : null,
            results,
            []);

    private uint NextAsyncMessageId() => unchecked((uint)Interlocked.Increment(ref nextAsyncMessageId));

    private static LLRPStatus MissingRoSpec(uint roSpecId) =>
        Status(StatusCode.M_ParameterError, $"ROSpec {roSpecId} does not exist.");

    private static LLRPStatus MissingAccessSpec(uint accessSpecId) =>
        Status(StatusCode.M_ParameterError, $"AccessSpec {accessSpecId} does not exist.");

    private static LLRPStatus Status(StatusCode code, string description) => new(code, description, null, null);

    private static async Task<bool> ReadExactAsync(NetworkStream stream, Memory<byte> buffer, CancellationToken token)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer[offset..], token).ConfigureAwait(false);
            if (read == 0)
            {
                return false;
            }
            offset += read;
        }
        return true;
    }

    private sealed record VirtualResponse(ILlrpMessage Response, IReadOnlyList<ILlrpMessage> Reports);
}
