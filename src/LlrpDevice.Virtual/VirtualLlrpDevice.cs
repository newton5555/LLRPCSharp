using LlrpDevice.Abstractions;

namespace LlrpDevice.Virtual;

/// <summary>Deterministic virtual implementation of the generic LLRP device contract.</summary>
public sealed class VirtualLlrpDevice : ILlrpDevice
{
    private readonly object _configurationGate = new();
    private readonly VirtualDeviceOptions _options;
    private readonly VirtualTagStore _tags;
    private LlrpDeviceConfiguration _configuration;
    private int _disposed;

    public VirtualLlrpDevice(
        VirtualDeviceOptions? options = null,
        IVirtualInventoryDataSource? inventoryDataSource = null)
    {
        _options = options ?? new VirtualDeviceOptions();
        _options.Validate();
        _configuration = _options.Configuration;
        IVirtualInventoryDataSource source = inventoryDataSource
            ?? new InMemoryVirtualInventoryDataSource("device-options", _options.Tags);
        _tags = new VirtualTagStore(source.Tags);
    }

    public LlrpDeviceIdentity Identity => _options.Identity;

    public LlrpDeviceCapabilities Capabilities => _options.Capabilities;

    public LlrpDeviceConfiguration Configuration
    {
        get
        {
            lock (_configurationGate)
            {
                return _configuration with
                {
                    Antennas = _configuration.Antennas.ToArray(),
                    Gpos = _configuration.Gpos.ToArray(),
                };
            }
        }
    }

    public event EventHandler<LlrpDeviceEvent>? EventRaised;

    /// <summary>Changes a virtual GPI input and publishes the corresponding device event.</summary>
    public void SetGpiState(ushort portNumber, bool state)
    {
        if (portNumber == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(portNumber));
        }

        Publish(new LlrpDeviceEvent
        {
            Name = "gpi.changed",
            GpiPortNumber = portNumber,
            GpiState = state,
        });
    }

    /// <summary>Changes a virtual antenna connection state and publishes the corresponding device event.</summary>
    public void SetAntennaConnection(ushort antennaId, bool connected)
    {
        if (antennaId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(antennaId));
        }

        Publish(new LlrpDeviceEvent
        {
            Name = "antenna.changed",
            AntennaId = antennaId,
            AntennaConnected = connected,
        });
    }

    /// <summary>Publishes a deterministic reader-exception event for event-path testing.</summary>
    public void RaiseReaderException(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        Publish(new LlrpDeviceEvent
        {
            Name = "reader.exception",
            Detail = message,
            Error = new LlrpDeviceError { Code = "virtual.reader.exception", Message = message },
        });
    }

    /// <summary>Requests a graceful standard CLOSE_CONNECTION from the hosting Server.</summary>
    public void RequestCloseConnection() => Publish(new LlrpDeviceEvent { Name = "connection.close" });

    public ValueTask<LlrpDeviceOperationResult> ApplyConfigurationAsync(
        LlrpDeviceConfigurationUpdate update,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_configurationGate)
        {
            _configuration = update.ResetToFactoryDefault
                ? new LlrpDeviceConfiguration
                {
                    Antennas = _options.Configuration.Antennas.ToArray(),
                    Gpos = _options.Configuration.Gpos.ToArray(),
                }
                : _configuration with
                {
                    Antennas = update.Antennas.Count > 0 ? update.Antennas.ToArray() : _configuration.Antennas,
                    Gpos = update.Gpos.Count > 0 ? update.Gpos.ToArray() : _configuration.Gpos,
                };
        }

        Publish(new LlrpDeviceEvent { Name = "configuration.changed" });
        return ValueTask.FromResult(LlrpDeviceOperationResult.Success());
    }

    public ValueTask<IInventoryExecution> StartInventoryAsync(
        LlrpInventoryPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<IInventoryExecution>(new VirtualInventoryExecution(_tags, _options.RfSimulation, plan));
    }

    public ValueTask<IReadOnlyList<LlrpTagAccessResult>> ExecuteTagAccessAsync(
        LlrpTagAccessRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Selector);
        ArgumentNullException.ThrowIfNull(request.Operations);
        cancellationToken.ThrowIfCancellationRequested();

        var results = new List<LlrpTagAccessResult>();
        foreach (TagObservation tag in _tags.Snapshot())
        {
            if (!Matches(request.Selector, tag))
            {
                continue;
            }

            var operationResults = new List<LlrpTagAccessOperationResult>(request.Operations.Count);
            foreach (LlrpTagAccessOperation operation in request.Operations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                operationResults.Add(ExecuteOperation(tag, operation));
            }

            results.Add(new LlrpTagAccessResult
            {
                Tag = tag,
                Operations = operationResults,
            });
        }

        return ValueTask.FromResult<IReadOnlyList<LlrpTagAccessResult>>(results);
    }

    public ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _disposed, 1);
        return ValueTask.CompletedTask;
    }

    private LlrpTagAccessOperationResult ExecuteOperation(TagObservation tag, LlrpTagAccessOperation operation)
    {
        LlrpTagAccessResultCode result;
        IReadOnlyList<ushort> readData = [];
        ushort wordsWritten = 0;
        string? error = null;
        switch (operation.Kind)
        {
            case LlrpTagAccessOperationKind.Read:
                readData = _tags.ReadWords(
                    tag.ElectronicProductCode.Span,
                    operation.MemoryBank,
                    operation.WordPointer,
                    operation.WordCount);
                result = readData.Count == operation.WordCount
                    ? LlrpTagAccessResultCode.Success
                    : LlrpTagAccessResultCode.MemoryOverrun;
                break;
            case LlrpTagAccessOperationKind.Write:
            case LlrpTagAccessOperationKind.BlockWrite:
                if (_tags.WriteWords(
                    tag.ElectronicProductCode.Span,
                    operation.MemoryBank,
                    operation.WordPointer,
                    operation.WriteData))
                {
                    result = LlrpTagAccessResultCode.Success;
                    wordsWritten = checked((ushort)operation.WriteData.Count);
                }
                else
                {
                    result = LlrpTagAccessResultCode.Locked;
                }

                break;
            case LlrpTagAccessOperationKind.Lock:
                result = _tags.Lock(
                    tag.ElectronicProductCode.Span,
                    operation.LockRequests,
                    operation.AccessPassword)
                    ? LlrpTagAccessResultCode.Success
                    : LlrpTagAccessResultCode.IncorrectPassword;
                break;
            case LlrpTagAccessOperationKind.Kill:
                result = _tags.Kill(tag.ElectronicProductCode.Span, operation.KillPassword)
                    ? LlrpTagAccessResultCode.Success
                    : LlrpTagAccessResultCode.IncorrectPassword;
                break;
            case LlrpTagAccessOperationKind.BlockErase:
                result = _tags.BlockErase(
                    tag.ElectronicProductCode.Span,
                    operation.MemoryBank,
                    operation.WordPointer,
                    operation.WordCount)
                    ? LlrpTagAccessResultCode.Success
                    : LlrpTagAccessResultCode.MemoryOverrun;
                break;
            default:
                result = LlrpTagAccessResultCode.UnsupportedOperation;
                error = $"Unsupported operation kind {operation.Kind}.";
                break;
        }

        if (result != LlrpTagAccessResultCode.Success && error is null)
        {
            error = result.ToString();
        }

        return new LlrpTagAccessOperationResult
        {
            OperationId = operation.OperationId,
            Result = result,
            ReadData = readData,
            WordsWritten = wordsWritten,
            Error = error,
        };
    }

    private bool Matches(LlrpTagSelector selector, TagObservation tag)
    {
        if (!_tags.TryGetMemoryBytes(
                tag.ElectronicProductCode.Span,
                selector.MemoryBank,
                out ReadOnlyMemory<byte> memory))
        {
            return false;
        }

        int bitLength = selector.BitLength == 0
            ? Math.Min(selector.Mask.Length, selector.Data.Length) * 8
            : selector.BitLength;
        if (selector.BitPointer + bitLength > memory.Length * 8 ||
            bitLength > selector.Mask.Length * 8 ||
            bitLength > selector.Data.Length * 8)
        {
            return false;
        }

        for (int index = 0; index < bitLength; index++)
        {
            bool maskBit = ReadBit(selector.Mask.Span, index);
            if (maskBit && ReadBit(memory.Span, selector.BitPointer + index) != ReadBit(selector.Data.Span, index))
            {
                return !selector.Match;
            }
        }

        return selector.Match;
    }

    private static bool ReadBit(ReadOnlySpan<byte> bytes, int bit) =>
        (bytes[bit / 8] & (1 << (7 - bit % 8))) != 0;

    private void Publish(string name) => Publish(new LlrpDeviceEvent { Name = name });

    private void Publish(LlrpDeviceEvent deviceEvent)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        try
        {
            EventRaised?.Invoke(this, deviceEvent);
        }
        catch
        {
            // Device observers must not change the outcome of a device operation.
        }
    }
}

/// <summary>Runs deterministic inventory observations for one virtual ROSpec execution.</summary>
public sealed class VirtualInventoryExecution : IInventoryExecution
{
    private readonly VirtualTagStore _tags;
    private readonly VirtualRfSimulationOptions _options;
    private int _stopped;

    internal VirtualInventoryExecution(
        VirtualTagStore tags,
        VirtualRfSimulationOptions options,
        LlrpInventoryPlan plan)
    {
        _tags = tags;
        _options = options;
        Plan = plan;
    }

    public LlrpInventoryPlan Plan { get; }

    public ValueTask<InventoryObservationBatch> ObserveAsync(
        LlrpInventoryRound round,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Volatile.Read(ref _stopped) != 0)
        {
            return ValueTask.FromResult(new InventoryObservationBatch());
        }

        ushort[] antennas = round.AntennaIds.Count == 0 || round.AntennaIds.Contains((ushort)0)
            ? []
            : round.AntennaIds.Distinct().ToArray();
        var observations = new List<TagObservation>();
        IReadOnlyList<TagObservation> tags = _tags.Snapshot();
        for (int index = 0; index < tags.Count; index++)
        {
            TagObservation tag = tags[index];
            if (antennas.Length > 0 && !antennas.Contains(tag.AntennaId))
            {
                continue;
            }

            if (_options.Scenario == VirtualRfScenario.MovingTags && !IsPresent(index, round.Sequence))
            {
                continue;
            }

            if (_options.Scenario == VirtualRfScenario.Noisy && !PassesDetection(index, round.Sequence))
            {
                continue;
            }

            if (_options.Scenario == VirtualRfScenario.Noisy && _options.RssiJitterDb > 0)
            {
                int jitter = StableRange(index, round.Sequence, _options.RssiJitterDb * 2 + 1) - _options.RssiJitterDb;
                tag = tag with { PeakRssi = checked((short)(tag.PeakRssi + jitter)) };
            }

            if (!PassesInventoryFilters(tag, Plan))
            {
                continue;
            }

            if (!_tags.PassesStateAwareSingulation(tag.ElectronicProductCode.Span, Plan.Singulation))
            {
                continue;
            }

            tag = _tags.MarkSeen(tag.ElectronicProductCode.Span, round.StartedAtUtc) ?? tag;
            observations.Add(tag);
        }

        int maxTagsPerRound = Plan.MaxTagsPerRound is int requestedMax && requestedMax > 0
            ? Math.Min(requestedMax, _options.MaxTagsPerRound > 0 ? _options.MaxTagsPerRound : requestedMax)
            : _options.MaxTagsPerRound;
        if (maxTagsPerRound > 0 && observations.Count > maxTagsPerRound)
        {
            observations.RemoveRange(maxTagsPerRound, observations.Count - maxTagsPerRound);
        }

        return ValueTask.FromResult(new InventoryObservationBatch { Tags = observations });
    }

    public ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Exchange(ref _stopped, 1);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    private bool IsPresent(int tagIndex, int sequence)
    {
        int phase = Math.Abs(sequence / _options.PresenceCycleRounds + tagIndex) % 2;
        return phase == 0;
    }

    private bool PassesDetection(int tagIndex, int sequence)
    {
        if (_options.DetectionProbability >= 1)
        {
            return true;
        }

        if (_options.DetectionProbability <= 0)
        {
            return false;
        }

        return StableRange(tagIndex, sequence, 10_000) < _options.DetectionProbability * 10_000;
    }

    private int StableRange(int tagIndex, int sequence, int exclusiveMax)
    {
        uint value = unchecked((uint)_options.RandomSeed);
        value = Mix(value, unchecked((uint)tagIndex));
        value = Mix(value, unchecked((uint)sequence));
        return (int)(value % (uint)exclusiveMax);
    }

    private static uint Mix(uint value, uint input)
    {
        value ^= input + 0x9E3779B9u + (value << 6) + (value >> 2);
        value ^= value >> 16;
        value *= 0x7FEB352Du;
        value ^= value >> 15;
        value *= 0x846CA68Bu;
        return value ^ (value >> 16);
    }

    private bool PassesInventoryFilters(TagObservation tag, LlrpInventoryPlan plan)
    {
        bool selected = true;
        foreach (LlrpInventoryFilter filter in plan.Filters)
        {
            bool match = Matches(filter.Selector, tag);
            _tags.ApplyStateAwareFilter(tag.ElectronicProductCode.Span, filter, match);
            if (filter.StateAction is not null)
            {
                continue;
            }

            LlrpInventoryFilterAction action = match ? filter.MatchAction : filter.NonMatchAction;
            selected = action switch
            {
                LlrpInventoryFilterAction.Select => true,
                LlrpInventoryFilterAction.Unselect => false,
                _ => selected,
            };
        }

        return selected;
    }

    private bool Matches(LlrpTagSelector selector, TagObservation tag)
    {
        if (!_tags.TryGetMemoryBytes(
            tag.ElectronicProductCode.Span,
            selector.MemoryBank,
            out ReadOnlyMemory<byte> memory))
        {
            return false;
        }

        int bitLength = selector.BitLength == 0
            ? Math.Min(selector.Mask.Length, selector.Data.Length) * 8
            : selector.BitLength;
        if (bitLength <= 0 || selector.BitPointer + bitLength > memory.Length * 8 ||
            bitLength > selector.Mask.Length * 8 || bitLength > selector.Data.Length * 8)
        {
            return false;
        }

        for (int index = 0; index < bitLength; index++)
        {
            bool maskBit = ReadBit(selector.Mask.Span, index);
            if (maskBit && ReadBit(memory.Span, selector.BitPointer + index) != ReadBit(selector.Data.Span, index))
            {
                return !selector.Match;
            }
        }

        return selector.Match;
    }

    private static bool ReadBit(ReadOnlySpan<byte> bytes, int bit) =>
        (bytes[bit / 8] & (1 << (7 - bit % 8))) != 0;
}
