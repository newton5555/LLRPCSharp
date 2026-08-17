using LlrpDevice.Abstractions;
using LlrpDevice.Server;
using LlrpDevice.Virtual;
using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Messages;
using LlrpNet.Protocol.Registry;

namespace LlrpVirtualReader;

internal static class LegacyDeviceOptionMapper
{
    public static VirtualDeviceOptions BuildVirtualDeviceOptions(VirtualReaderOptions options)
    {
        IVirtualTagSource source = options.NormalizeLegacyTagSource();
        return new VirtualDeviceOptions
        {
            Identity = new LlrpDeviceIdentity
            {
                ReaderId = options.ReaderId,
                Name = options.ReaderName,
                ManufacturerId = options.Capabilities.ManufacturerId,
                ModelId = options.Capabilities.ModelId,
                FirmwareVersion = options.Capabilities.FirmwareVersion,
            },
            Capabilities = new LlrpDeviceCapabilities
            {
                MaxNumberOfAntennas = options.Capabilities.MaxNumberOfAntennas,
                CanSetAntennaProperties = options.Capabilities.CanSetAntennaProperties,
                HasUtcClockCapability = options.Capabilities.HasUtcClockCapability,
            },
            Configuration = new LlrpDeviceConfiguration
            {
                Antennas = options.AntennaConfigurations.Select(static antenna => new LlrpDeviceAntennaConfiguration
                {
                    AntennaId = antenna.AntennaId,
                    ReceiverSensitivityIndex = antenna.ReceiverSensitivityIndex,
                    TransmitPowerIndex = antenna.TransmitPowerIndex,
                    HopTableId = antenna.HopTableId,
                    ChannelIndex = antenna.ChannelIndex,
                }).ToArray(),
                Gpos = options.GpoStates.Select(static gpo => new LlrpDeviceGpoState
                {
                    PortNumber = gpo.PortNumber,
                    State = gpo.State,
                }).ToArray(),
            },
            Tags = source.GetTags().Select(static tag => new VirtualTagDefinition
            {
                ElectronicProductCode = tag.ElectronicProductCode,
                Tid = tag.Tid,
                PeakRssi = tag.PeakRssi,
                AntennaId = tag.AntennaId,
                ChannelIndex = tag.ChannelIndex,
                UserMemory = tag.UserMemory,
            }).ToArray(),
            RfSimulation = new VirtualRfSimulationOptions
            {
                Scenario = options.RfSimulation.Scenario switch
                {
                    VirtualReaderRfScenario.MovingTags => VirtualRfScenario.MovingTags,
                    VirtualReaderRfScenario.Noisy => VirtualRfScenario.Noisy,
                    _ => VirtualRfScenario.Static,
                },
                RandomSeed = options.RfSimulation.RandomSeed,
                DetectionProbability = options.RfSimulation.DetectionProbability,
                PresenceCycleRounds = options.RfSimulation.PresenceCycleRounds,
                RssiJitterDb = options.RfSimulation.RssiJitterDb,
                MaxTagsPerRound = options.RfSimulation.MaxTagsPerRound,
            },
        };
    }

    public static LlrpDeviceServerOptions BuildServerOptions(
        VirtualReaderHostOptions hostOptions,
        IReadOnlyList<ILlrpDeviceProtocolModule> modules)
    {
        VirtualReaderOptions options = hostOptions.ReaderOptions;
        return new LlrpDeviceServerOptions
        {
            ListenAddress = hostOptions.ListenAddress,
            Port = hostOptions.Port,
            ProtocolVersion = options.ProtocolVersion,
            MaximumClientConnections = options.MaximumClientConnections,
            ConnectionLimitPolicy = options.ConnectionLimitPolicy switch
            {
                VirtualReaderConnectionLimitPolicy.ReplaceExisting => LlrpDeviceConnectionLimitPolicy.ReplaceExisting,
                _ => LlrpDeviceConnectionLimitPolicy.RejectAdditional,
            },
            IdleTimeout = options.IdleTimeout,
            FrameAssemblyTimeout = options.FrameAssemblyTimeout,
            MaximumFrameLength = options.MaximumFrameLength,
            UseTcpKeepAlive = options.UseTcpKeepAlive,
            KeepAliveInterval = options.KeepAliveInterval,
            Reports = new LlrpDeviceReportOptions
            {
                ReportInterval = options.Reports.ReportInterval,
                ReportCount = options.Reports.ReportCount,
                Repeat = options.Reports.Repeat,
            },
            UnknownVendorParameterBehavior = options.UnknownVendorParameterBehavior switch
            {
                VirtualReaderUnknownVendorParameterBehavior.Reject => LlrpUnknownVendorParameterBehavior.Reject,
                _ => LlrpUnknownVendorParameterBehavior.PreserveAndIgnore,
            },
            UseStrictStandardInventoryProfile = options.UseStrictStandardInventoryProfile,
            DropResponseForMessageTypes = options.DropResponseForMessageTypes,
            ErrorResponseForMessageTypes = options.ErrorResponseForMessageTypes.ToDictionary(
                static pair => pair.Key,
                static pair => new LlrpDeviceServerErrorResponse(pair.Value.StatusCode, pair.Value.Description)),
            CloseConnectionAfterRequestMessageTypes = options.CloseConnectionAfterRequestMessageTypes,
            TruncateResponseForMessageTypes = options.TruncateResponseForMessageTypes,
            LoggerFactory = hostOptions.LoggerFactory,
            FrameObserver = hostOptions.FrameObserver,
            ProtocolModules = modules,
        };
    }
}

internal sealed class LegacyLlrpDeviceAdapter : ILlrpDevice
{
    private readonly ILlrpReaderDeviceBackend _backend;
    private readonly VirtualReaderOptions _options;
    private int _disposed;

    public LegacyLlrpDeviceAdapter(ILlrpReaderDeviceBackend backend, VirtualReaderOptions options)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        Identity = new LlrpDeviceIdentity
        {
            ReaderId = options.ReaderId,
            Name = options.ReaderName,
            ManufacturerId = options.Capabilities.ManufacturerId,
            ModelId = options.Capabilities.ModelId,
            FirmwareVersion = options.Capabilities.FirmwareVersion,
        };
    }

    public LlrpDeviceIdentity Identity { get; }

    public LlrpDeviceCapabilities Capabilities => new()
    {
        MaxNumberOfAntennas = _options.Capabilities.MaxNumberOfAntennas,
        CanSetAntennaProperties = _options.Capabilities.CanSetAntennaProperties,
        HasUtcClockCapability = _options.Capabilities.HasUtcClockCapability,
        SupportsTagAccess = true,
        SupportsBlockWrite = false,
        SupportsBlockErase = false,
    };

    public LlrpDeviceConfiguration Configuration => new()
    {
        Antennas = _backend.GetAntennaConfigurations().Select(static antenna => new LlrpDeviceAntennaConfiguration
        {
            AntennaId = antenna.AntennaID,
            ReceiverSensitivityIndex = antenna.RFReceiver?.ReceiverSensitivity ?? 0,
            TransmitPowerIndex = antenna.RFTransmitter?.TransmitPower ?? 0,
            HopTableId = antenna.RFTransmitter?.HopTableID ?? 0,
            ChannelIndex = antenna.RFTransmitter?.ChannelIndex ?? 0,
        }).ToArray(),
        Gpos = _backend.GetGpoWriteData().Select(static gpo => new LlrpDeviceGpoState
        {
            PortNumber = gpo.GPOPortNumber,
            State = gpo.GPOData,
        }).ToArray(),
    };

    public event EventHandler<LlrpDeviceEvent>? EventRaised;

    public ValueTask<LlrpDeviceOperationResult> ApplyConfigurationAsync(
        LlrpDeviceConfigurationUpdate update,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var antennas = update.Antennas.Select(static antenna => new LlrpNet.Protocol.Parameters.V1_0_1.AntennaConfiguration(
            antenna.AntennaId,
            new LlrpNet.Protocol.Parameters.V1_0_1.RFReceiver(antenna.ReceiverSensitivityIndex),
            new LlrpNet.Protocol.Parameters.V1_0_1.RFTransmitter(
                antenna.HopTableId,
                antenna.ChannelIndex,
                antenna.TransmitPowerIndex),
            [])).ToArray();
        var gpos = update.Gpos.Select(static gpo => new LlrpNet.Protocol.Parameters.V1_0_1.GPOWriteData(
            gpo.PortNumber,
            gpo.State)).ToArray();
        _backend.SetConfiguration(update.ResetToFactoryDefault, antennas, null, null, null, null, gpos, null);
        EventRaised?.Invoke(this, new LlrpDeviceEvent { Name = "configuration.changed" });
        return ValueTask.FromResult(LlrpDeviceOperationResult.Success());
    }

    public ValueTask<IInventoryExecution> StartInventoryAsync(
        LlrpInventoryPlan plan,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<IInventoryExecution>(new LegacyInventoryExecution(_backend.Inventory, plan));
    }

    public ValueTask<IReadOnlyList<LlrpTagAccessResult>> ExecuteTagAccessAsync(
        LlrpTagAccessRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        var results = new List<LlrpTagAccessResult>();
        foreach (VirtualTag tag in _backend.Inventory.Observe(
            new VirtualReaderInventoryRound(request.RoSpecId, 0, [])))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Matches(request.Selector, tag))
            {
                continue;
            }

            results.Add(new LlrpTagAccessResult
            {
                Tag = new TagObservation
                {
                    ElectronicProductCode = tag.ElectronicProductCode,
                    Tid = tag.Tid,
                    PeakRssi = tag.PeakRssi,
                    AntennaId = tag.AntennaId,
                    ChannelIndex = tag.ChannelIndex,
                },
                Operations = request.Operations.Select(operation => ExecuteOperation(tag, operation)).ToArray(),
            });
        }

        return ValueTask.FromResult<IReadOnlyList<LlrpTagAccessResult>>(results);
    }

    public ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _disposed, 1);
        return ValueTask.CompletedTask;
    }

    private LlrpTagAccessOperationResult ExecuteOperation(VirtualTag tag, LlrpTagAccessOperation operation)
    {
        LlrpTagAccessResultCode result;
        IReadOnlyList<ushort> readData = [];
        ushort wordsWritten = 0;
        switch (operation.Kind)
        {
            case LlrpTagAccessOperationKind.Read:
                bool read = _backend.Inventory.TryReadWords(
                    tag.ElectronicProductCode.Span,
                    (byte)operation.MemoryBank,
                    operation.WordPointer,
                    operation.WordCount,
                    out readData);
                result = read && readData.Count == operation.WordCount
                    ? LlrpTagAccessResultCode.Success
                    : LlrpTagAccessResultCode.MemoryOverrun;
                break;
            case LlrpTagAccessOperationKind.Write:
            case LlrpTagAccessOperationKind.BlockWrite:
                bool written = _backend.Inventory.TryWriteWords(
                    tag.ElectronicProductCode.Span,
                    (byte)operation.MemoryBank,
                    operation.WordPointer,
                    operation.WriteData);
                result = written ? LlrpTagAccessResultCode.Success : LlrpTagAccessResultCode.Locked;
                wordsWritten = written ? checked((ushort)operation.WriteData.Count) : (ushort)0;
                break;
            default:
                result = LlrpTagAccessResultCode.UnsupportedOperation;
                break;
        }

        return new LlrpTagAccessOperationResult
        {
            OperationId = operation.OperationId,
            Result = result,
            ReadData = readData,
            WordsWritten = wordsWritten,
            Error = result == LlrpTagAccessResultCode.Success ? null : result.ToString(),
        };
    }

    private bool Matches(LlrpTagSelector selector, VirtualTag tag)
    {
        if (!_backend.Inventory.TryGetMemoryBytes(
            tag.ElectronicProductCode.Span,
            (byte)selector.MemoryBank,
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

    private sealed class LegacyInventoryExecution : IInventoryExecution
    {
        private readonly ILlrpReaderInventoryBackend _inventory;
        private int _stopped;

        public LegacyInventoryExecution(ILlrpReaderInventoryBackend inventory, LlrpInventoryPlan plan)
        {
            _inventory = inventory;
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

            IReadOnlyList<VirtualTag> tags = _inventory.Observe(
                new VirtualReaderInventoryRound(round.RoSpecId, round.Sequence, round.AntennaIds));
            return ValueTask.FromResult(new InventoryObservationBatch
            {
                Tags = tags.Select(static tag => new TagObservation
                {
                    ElectronicProductCode = tag.ElectronicProductCode,
                    Tid = tag.Tid,
                    PeakRssi = tag.PeakRssi,
                    AntennaId = tag.AntennaId,
                    ChannelIndex = tag.ChannelIndex,
                }).ToArray(),
            });
        }

        public ValueTask StopAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Exchange(ref _stopped, 1);
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync() => StopAsync();
    }
}

internal sealed class LegacyProtocolModuleAdapter : ILlrpDeviceProtocolModule
{
    private readonly IVirtualReaderProtocolModule _module;
    private readonly VirtualReaderHost _host;
    private readonly ILlrpReaderDeviceBackend _backend;

    public LegacyProtocolModuleAdapter(
        IVirtualReaderProtocolModule module,
        VirtualReaderHost host,
        ILlrpReaderDeviceBackend backend)
    {
        _module = module;
        _host = host;
        _backend = backend;
    }

    public string Id => _module.Id;
    public IReadOnlySet<LlrpProtocolVersion> SupportedVersions => _module.SupportedVersions;

    public void RegisterCodecs(LlrpCodecRegistry registry) => _module.RegisterCodecs(registry);

    public void RegisterHandlers(LlrpDeviceHandlerRegistry registry)
    {
        var oldRegistry = new VirtualReaderHandlerRegistry();
        _module.RegisterHandlers(oldRegistry);
        foreach (IVirtualReaderMessageHandler handler in oldRegistry.Handlers)
        {
            registry.Add(new LegacyMessageHandlerAdapter(handler, _host, _backend));
        }
    }

    private sealed class LegacyMessageHandlerAdapter : ILlrpDeviceMessageHandler
    {
        private readonly IVirtualReaderMessageHandler _handler;
        private readonly VirtualReaderHost _host;
        private readonly ILlrpReaderDeviceBackend _backend;

        public LegacyMessageHandlerAdapter(
            IVirtualReaderMessageHandler handler,
            VirtualReaderHost host,
            ILlrpReaderDeviceBackend backend)
        {
            _handler = handler;
            _host = host;
            _backend = backend;
        }

        public string Name => _handler.Name;

        public bool CanHandle(LlrpProtocolVersion version, ILlrpMessage message) =>
            _handler.CanHandle(version, message);

        public async ValueTask<LlrpDeviceDispatchResult> HandleAsync(
            LlrpDeviceRequestContext context,
            ILlrpMessage message,
            CancellationToken cancellationToken)
        {
            var oldContext = new VirtualReaderRequestContext(
                _host,
                _backend,
                context.ConnectionId,
                context.Version,
                context.MessageId);
            VirtualReaderDispatchResult result = await _handler
                .HandleAsync(oldContext, message, cancellationToken)
                .ConfigureAwait(false);
            return new LlrpDeviceDispatchResult(
                result.Response,
                result.AdditionalMessages,
                result.CloseConnection,
                result.ResponseVersion,
                result.NextProtocolVersion);
        }
    }
}
