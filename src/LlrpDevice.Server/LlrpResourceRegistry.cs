using LlrpDevice.Abstractions;
using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Messages;
using V101Enumerations = LlrpNet.Protocol.Enumerations.V1_0_1;
using V101Messages = LlrpNet.Protocol.Messages.V1_0_1;
using V101Parameters = LlrpNet.Protocol.Parameters.V1_0_1;

namespace LlrpDevice.Server;

/// <summary>
/// Owns the protocol resource graph independently of the concrete device implementation.
/// </summary>
/// <remarks>
/// LLRP 1.0.1 is the canonical internal resource representation during this migration. It is
/// confined to the Server project and never appears in <see cref="ILlrpDevice"/> contracts.
/// </remarks>
internal sealed class LlrpResourceRegistry
{
    private readonly object _gate = new();
    private readonly ILlrpDevice _device;
    private readonly IReadOnlyList<LlrpNet.Protocol.Parameters.ILlrpParameter> _initialReaderConfigurationCustomItems;
    private readonly Dictionary<uint, V101Parameters.ROSpec> _roSpecs = [];
    private readonly Dictionary<uint, V101Parameters.AccessSpec> _accessSpecs = [];
    private readonly Dictionary<uint, RoSpecRuntime> _roSpecRuntime = [];
    private readonly Dictionary<ushort, bool> _gpiStates = [];
    private readonly Dictionary<ushort, bool> _antennaConnections = [];
    private IReadOnlyList<V101Parameters.AntennaConfiguration> _antennaConfigurations;
    private IReadOnlyList<V101Parameters.GPOWriteData> _gpoWriteData;
    private V101Parameters.ReaderEventNotificationSpec? _readerEventNotificationSpec;
    private V101Parameters.ROReportSpec? _roReportSpec;
    private V101Parameters.AccessReportSpec? _accessReportSpec;
    private V101Parameters.KeepaliveSpec _keepaliveSpec =
        new(V101Enumerations.KeepaliveTriggerType.Null, 0);
    private bool _keepaliveSpecConfigured;
    private V101Parameters.EventsAndReports? _eventsAndReports;
    private IReadOnlyList<LlrpNet.Protocol.Parameters.ILlrpParameter> _readerConfigurationCustomItems;
    private uint _configurationStateValue = 1;

    public LlrpResourceRegistry(
        ILlrpDevice device,
        IReadOnlyList<LlrpNet.Protocol.Parameters.ILlrpParameter>? initialReaderConfigurationCustomItems = null)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _initialReaderConfigurationCustomItems = initialReaderConfigurationCustomItems?.ToArray() ?? [];
        _readerConfigurationCustomItems = _initialReaderConfigurationCustomItems;
        _antennaConfigurations = device.Configuration.Antennas
            .Select(ToWireAntennaConfiguration)
            .ToArray();
        _gpoWriteData = device.Configuration.Gpos
            .Select(static gpo => new V101Parameters.GPOWriteData(gpo.PortNumber, gpo.State))
            .ToArray();
        for (ushort port = 1; port <= 4; port++)
        {
            _gpiStates[port] = false;
        }
        for (ushort antenna = 1; antenna <= device.Capabilities.MaxNumberOfAntennas; antenna++)
        {
            _antennaConnections[antenna] = true;
        }
    }

    public bool TryAddRoSpec(V101Parameters.ROSpec roSpec)
    {
        lock (_gate)
        {
            if (roSpec.ROSpecID == 0 || !_roSpecs.TryAdd(roSpec.ROSpecID, roSpec))
            {
                return false;
            }

            _roSpecRuntime[roSpec.ROSpecID] = new RoSpecRuntime();
            return true;
        }
    }

    public bool TryGetRoSpec(uint roSpecId, out V101Parameters.ROSpec? roSpec)
    {
        lock (_gate)
        {
            return _roSpecs.TryGetValue(roSpecId, out roSpec);
        }
    }

    public IReadOnlyList<V101Parameters.ROSpec> GetRoSpecs()
    {
        lock (_gate)
        {
            return _roSpecs.Values.OrderBy(static spec => spec.ROSpecID).ToArray();
        }
    }

    public IReadOnlyList<uint> GetRoSpecIds()
    {
        lock (_gate)
        {
            return _roSpecs.Keys.OrderBy(static id => id).ToArray();
        }
    }

    public bool TryUpdateRoSpec(uint roSpecId, Func<V101Parameters.ROSpec, V101Parameters.ROSpec> update)
    {
        ArgumentNullException.ThrowIfNull(update);
        lock (_gate)
        {
            if (!_roSpecs.TryGetValue(roSpecId, out V101Parameters.ROSpec? current))
            {
                return false;
            }

            _roSpecs[roSpecId] = update(current);
            return true;
        }
    }

    public bool TryDeleteRoSpec(uint roSpecId, out bool deletedAll)
    {
        lock (_gate)
        {
            if (roSpecId == 0)
            {
                _roSpecs.Clear();
                _accessSpecs.Clear();
                _roSpecRuntime.Clear();
                deletedAll = true;
                return true;
            }

            deletedAll = false;
            if (!_roSpecs.Remove(roSpecId))
            {
                return false;
            }

            _roSpecRuntime.Remove(roSpecId);

            foreach (uint accessSpecId in _accessSpecs.Values
                .Where(accessSpec => accessSpec.ROSpecID == roSpecId)
                .Select(static accessSpec => accessSpec.AccessSpecID)
                .ToArray())
            {
                _accessSpecs.Remove(accessSpecId);
            }

            return true;
        }
    }

    public bool TryAddAccessSpec(V101Parameters.AccessSpec accessSpec)
    {
        lock (_gate)
        {
            return accessSpec.AccessSpecID != 0 &&
                _roSpecs.ContainsKey(accessSpec.ROSpecID) &&
                _accessSpecs.TryAdd(accessSpec.AccessSpecID, accessSpec);
        }
    }

    public bool TryGetAccessSpec(uint accessSpecId, out V101Parameters.AccessSpec? accessSpec)
    {
        lock (_gate)
        {
            return _accessSpecs.TryGetValue(accessSpecId, out accessSpec);
        }
    }

    public IReadOnlyList<V101Parameters.AccessSpec> GetAccessSpecs()
    {
        lock (_gate)
        {
            return _accessSpecs.Values.OrderBy(static spec => spec.AccessSpecID).ToArray();
        }
    }

    public IReadOnlyList<uint> GetAccessSpecIds()
    {
        lock (_gate)
        {
            return _accessSpecs.Keys.OrderBy(static id => id).ToArray();
        }
    }

    public bool TryUpdateAccessSpec(uint accessSpecId, Func<V101Parameters.AccessSpec, V101Parameters.AccessSpec> update)
    {
        ArgumentNullException.ThrowIfNull(update);
        lock (_gate)
        {
            if (!_accessSpecs.TryGetValue(accessSpecId, out V101Parameters.AccessSpec? current))
            {
                return false;
            }

            _accessSpecs[accessSpecId] = update(current);
            return true;
        }
    }

    public bool TryDeleteAccessSpec(uint accessSpecId)
    {
        lock (_gate)
        {
            return accessSpecId == 0 ? ClearAccessSpecs() : _accessSpecs.Remove(accessSpecId);
        }
    }

    public IReadOnlyList<uint> GetActiveRoSpecIds()
    {
        lock (_gate)
        {
            return _roSpecs.Values
                .Where(static roSpec => roSpec.CurrentState == V101Enumerations.ROSpecState.Active)
                .Select(static roSpec => roSpec.ROSpecID)
                .ToArray();
        }
    }

    public bool IsAutomaticReportDeliveryEnabled(uint roSpecId)
    {
        lock (_gate)
        {
            return !_roSpecs.TryGetValue(roSpecId, out V101Parameters.ROSpec? roSpec) ||
                roSpec.ROReportSpec?.ROReportTrigger != V101Enumerations.ROReportTriggerType.None;
        }
    }

    public void MarkRoSpecEnabled(uint roSpecId)
    {
        lock (_gate)
        {
            if (!_roSpecRuntime.TryGetValue(roSpecId, out RoSpecRuntime? runtime))
            {
                return;
            }

            DateTimeOffset now = DateTimeOffset.UtcNow;
            runtime.EnabledAtUtc = now;
            runtime.StartedAtUtc = null;
            runtime.HasStarted = false;
            runtime.StartTriggerLatched = false;
            runtime.NextPeriodicStartAtUtc = GetPeriodicStartTime(_roSpecs[roSpecId], now);
        }
    }

    public void MarkRoSpecStarted(uint roSpecId)
    {
        lock (_gate)
        {
            if (_roSpecRuntime.TryGetValue(roSpecId, out RoSpecRuntime? runtime))
            {
                runtime.StartedAtUtc = DateTimeOffset.UtcNow;
                runtime.HasStarted = true;
                runtime.StartTriggerLatched = _roSpecs.TryGetValue(roSpecId, out V101Parameters.ROSpec? roSpec) &&
                    roSpec.ROBoundarySpec.ROSpecStartTrigger.ROSpecStartTriggerType == V101Enumerations.ROSpecStartTriggerType.GPI;
            }
        }
    }

    public void MarkRoSpecStopped(uint roSpecId)
    {
        lock (_gate)
        {
            if (_roSpecRuntime.TryGetValue(roSpecId, out RoSpecRuntime? runtime))
            {
                runtime.StartedAtUtc = null;
            }
        }
    }

    public void SetGpiState(ushort portNumber, bool state)
    {
        lock (_gate)
        {
            _gpiStates[portNumber] = state;
        }
    }

    public IReadOnlyList<V101Parameters.GPIPortCurrentState> GetGpiStates(ushort portNumber = 0)
    {
        lock (_gate)
        {
            return _gpiStates
                .Where(item => portNumber == 0 || item.Key == portNumber)
                .OrderBy(item => item.Key)
                .Select(item => new V101Parameters.GPIPortCurrentState(
                    item.Key,
                    true,
                    item.Value ? V101Enumerations.GPIPortState.High : V101Enumerations.GPIPortState.Low))
                .ToArray();
        }
    }

    public void SetAntennaConnection(ushort antennaId, bool connected)
    {
        lock (_gate)
        {
            _antennaConnections[antennaId] = connected;
        }
    }

    public bool IsAntennaConnected(ushort antennaId)
    {
        lock (_gate)
        {
            return !_antennaConnections.TryGetValue(antennaId, out bool connected) || connected;
        }
    }

    public IReadOnlyList<RoSpecTriggerTransition> ProcessRoSpecTriggers(DateTimeOffset now)
    {
        lock (_gate)
        {
            var transitions = new List<RoSpecTriggerTransition>();
            foreach ((uint roSpecId, V101Parameters.ROSpec roSpec) in _roSpecs.ToArray())
            {
                if (!_roSpecRuntime.TryGetValue(roSpecId, out RoSpecRuntime? runtime))
                {
                    continue;
                }

                if (roSpec.CurrentState == V101Enumerations.ROSpecState.Inactive &&
                    ShouldStart(roSpec, runtime, now))
                {
                    _roSpecs[roSpecId] = roSpec with { CurrentState = V101Enumerations.ROSpecState.Active };
                    runtime.StartedAtUtc = now;
                    runtime.HasStarted = true;
                    runtime.StartTriggerLatched = roSpec.ROBoundarySpec.ROSpecStartTrigger.ROSpecStartTriggerType ==
                        V101Enumerations.ROSpecStartTriggerType.GPI;
                    if (roSpec.ROBoundarySpec.ROSpecStartTrigger.ROSpecStartTriggerType == V101Enumerations.ROSpecStartTriggerType.Periodic)
                    {
                        uint period = roSpec.ROBoundarySpec.ROSpecStartTrigger.PeriodicTriggerValue?.Period ?? 0;
                        runtime.NextPeriodicStartAtUtc = period == 0 ? null : now.AddMilliseconds(period);
                    }

                    transitions.Add(new RoSpecTriggerTransition(roSpecId, Started: true));
                    continue;
                }

                if (roSpec.CurrentState == V101Enumerations.ROSpecState.Active &&
                    ShouldStop(roSpec, runtime, now))
                {
                    _roSpecs[roSpecId] = roSpec with { CurrentState = V101Enumerations.ROSpecState.Inactive };
                    runtime.StartedAtUtc = null;
                    transitions.Add(new RoSpecTriggerTransition(roSpecId, Started: false));
                }
            }

            return transitions;
        }
    }

    public IReadOnlyList<ushort> GetInventoryAntennaIds(uint roSpecId)
    {
        lock (_gate)
        {
            if (!_roSpecs.TryGetValue(roSpecId, out V101Parameters.ROSpec? roSpec))
            {
                return [];
            }

            return roSpec.SpecParameterItems
                .OfType<V101Parameters.AISpec>()
                .SelectMany(static spec => spec.AntennaIDs)
                .Distinct()
                .ToArray();
        }
    }

    public LlrpInventoryPlan BuildInventoryPlan(uint roSpecId)
    {
        lock (_gate)
        {
            if (!_roSpecs.TryGetValue(roSpecId, out V101Parameters.ROSpec? roSpec))
            {
                return new LlrpInventoryPlan { RoSpecId = roSpecId };
            }

            V101Parameters.AISpec? aiSpec = roSpec.SpecParameterItems
                .OfType<V101Parameters.AISpec>()
                .FirstOrDefault();
            V101Parameters.InventoryParameterSpec? inventorySpec = aiSpec?.InventoryParameterSpecItems.FirstOrDefault();
            V101Parameters.C1G2InventoryCommand? command = inventorySpec?.AntennaConfigurationItems
                .SelectMany(static item => item.AirProtocolInventoryCommandSettingsItems)
                .OfType<V101Parameters.C1G2InventoryCommand>()
                .FirstOrDefault();

            return new LlrpInventoryPlan
            {
                RoSpecId = roSpecId,
                AntennaIds = aiSpec?.AntennaIDs ?? [],
                AntennaConfigurations = inventorySpec?.AntennaConfigurationItems
                    .Select(static antenna => new LlrpInventoryAntennaConfiguration
                    {
                        AntennaId = antenna.AntennaID,
                        ReceiverSensitivityIndex = antenna.RFReceiver?.ReceiverSensitivity,
                        TransmitPowerIndex = antenna.RFTransmitter?.TransmitPower,
                        HopTableId = antenna.RFTransmitter?.HopTableID,
                        ChannelIndex = antenna.RFTransmitter?.ChannelIndex,
                    })
                    .ToArray() ?? [],
                InventoryParameterSpecId = inventorySpec?.InventoryParameterSpecID,
                Filters = command is null ? [] : command.C1G2FilterItems.Select(ToInventoryFilter).ToArray(),
                RfControl = command?.C1G2RFControl is { } rf ? new LlrpInventoryRfControl
                {
                    ModeIndex = rf.ModeIndex,
                    Tari = rf.Tari,
                } : null,
                Singulation = command?.C1G2SingulationControl is { } singulation ? new LlrpInventorySingulationControl
                {
                    Session = singulation.Session,
                    TagPopulation = singulation.TagPopulation,
                    TagTransitTime = singulation.TagTransitTime,
                    StateAware = command.TagInventoryStateAware || singulation.C1G2TagInventoryStateAwareSingulationAction is not null,
                    StateAwareSingulation = singulation.C1G2TagInventoryStateAwareSingulationAction is { } stateAware
                        ? new LlrpInventoryStateAwareSingulation
                        {
                            Target = stateAware.I == V101Enumerations.C1G2TagInventoryStateAwareI.State_A
                                ? LlrpInventorySingulationTarget.StateA
                                : LlrpInventorySingulationTarget.StateB,
                            SelectedFlag = stateAware.S == V101Enumerations.C1G2TagInventoryStateAwareS.SL
                                ? LlrpInventorySelectedFlag.Set
                                : LlrpInventorySelectedFlag.Clear,
                        }
                        : null,
                } : null,
            };
        }
    }

    public IReadOnlyList<V101Parameters.AntennaConfiguration> GetAntennaConfigurations()
    {
        lock (_gate)
        {
            return _antennaConfigurations.ToArray();
        }
    }

    public IReadOnlyList<V101Parameters.GPOWriteData> GetGpoWriteData()
    {
        lock (_gate)
        {
            return _gpoWriteData.ToArray();
        }
    }

    public V101Parameters.ReaderEventNotificationSpec? GetReaderEventNotificationSpec()
    {
        lock (_gate)
        {
            return _readerEventNotificationSpec;
        }
    }

    public V101Parameters.ROReportSpec? GetRoReportSpec()
    {
        lock (_gate)
        {
            return _roReportSpec;
        }
    }

    public V101Parameters.AccessReportSpec? GetAccessReportSpec()
    {
        lock (_gate)
        {
            return _accessReportSpec;
        }
    }

    public V101Parameters.KeepaliveSpec GetKeepaliveSpec()
    {
        lock (_gate)
        {
            return _keepaliveSpec;
        }
    }

    public bool IsKeepaliveSpecConfigured
    {
        get
        {
            lock (_gate)
            {
                return _keepaliveSpecConfigured;
            }
        }
    }

    public V101Parameters.EventsAndReports? GetEventsAndReports()
    {
        lock (_gate)
        {
            return _eventsAndReports;
        }
    }

    public IReadOnlyList<LlrpNet.Protocol.Parameters.ILlrpParameter> GetReaderConfigurationCustomItems()
    {
        lock (_gate)
        {
            return _readerConfigurationCustomItems.ToArray();
        }
    }

    public uint GetConfigurationStateValue()
    {
        lock (_gate)
        {
            return _configurationStateValue;
        }
    }

    public LlrpDeviceOperationResult SetConfiguration(
        bool resetToFactoryDefault,
        IReadOnlyList<V101Parameters.AntennaConfiguration> antennaConfigurations,
        V101Parameters.ReaderEventNotificationSpec? readerEventNotificationSpec,
        V101Parameters.ROReportSpec? roReportSpec,
        V101Parameters.AccessReportSpec? accessReportSpec,
        V101Parameters.KeepaliveSpec? keepaliveSpec,
        IReadOnlyList<V101Parameters.GPOWriteData> gpoWriteData,
        V101Parameters.EventsAndReports? eventsAndReports,
        IReadOnlyList<LlrpNet.Protocol.Parameters.ILlrpParameter> customItems)
    {
        ArgumentNullException.ThrowIfNull(antennaConfigurations);
        ArgumentNullException.ThrowIfNull(gpoWriteData);
        ArgumentNullException.ThrowIfNull(customItems);
        var update = new LlrpDeviceConfigurationUpdate
        {
            ResetToFactoryDefault = resetToFactoryDefault,
            Antennas = antennaConfigurations.Select(static antenna => new LlrpDeviceAntennaConfiguration
            {
                AntennaId = antenna.AntennaID,
                ReceiverSensitivityIndex = antenna.RFReceiver?.ReceiverSensitivity ?? 0,
                TransmitPowerIndex = antenna.RFTransmitter?.TransmitPower ?? 0,
                HopTableId = antenna.RFTransmitter?.HopTableID ?? 0,
                ChannelIndex = antenna.RFTransmitter?.ChannelIndex ?? 0,
            }).ToArray(),
            Gpos = gpoWriteData.Select(static gpo => new LlrpDeviceGpoState
            {
                PortNumber = gpo.GPOPortNumber,
                State = gpo.GPOData,
            }).ToArray(),
        };
        LlrpDeviceOperationResult result = _device.ApplyConfigurationAsync(update)
            .AsTask()
            .GetAwaiter()
            .GetResult();
        if (!result.Succeeded)
        {
            return result;
        }

        lock (_gate)
        {
            if (resetToFactoryDefault)
            {
                _roSpecs.Clear();
                _accessSpecs.Clear();
                _roSpecRuntime.Clear();
                _antennaConfigurations = _device.Configuration.Antennas
                    .Select(ToWireAntennaConfiguration)
                    .ToArray();
                _gpoWriteData = [new(1, false)];
                _readerEventNotificationSpec = null;
                _roReportSpec = null;
                _accessReportSpec = null;
                _keepaliveSpec = new(V101Enumerations.KeepaliveTriggerType.Null, 0);
                _keepaliveSpecConfigured = false;
                _eventsAndReports = null;
                _readerConfigurationCustomItems = _initialReaderConfigurationCustomItems;
                _configurationStateValue = unchecked(_configurationStateValue + 1);
                return result;
            }

            if (antennaConfigurations.Count > 0)
            {
                _antennaConfigurations = antennaConfigurations.ToArray();
            }

            if (gpoWriteData.Count > 0)
            {
                _gpoWriteData = gpoWriteData.ToArray();
            }

            _readerEventNotificationSpec = readerEventNotificationSpec ?? _readerEventNotificationSpec;
            _roReportSpec = roReportSpec ?? _roReportSpec;
            _accessReportSpec = accessReportSpec ?? _accessReportSpec;
            if (keepaliveSpec is not null)
            {
                _keepaliveSpec = keepaliveSpec;
                _keepaliveSpecConfigured = true;
            }
            _eventsAndReports = eventsAndReports ?? _eventsAndReports;
            if (customItems.Count > 0)
            {
                _readerConfigurationCustomItems = customItems.ToArray();
            }
            _configurationStateValue = unchecked(_configurationStateValue + 1);
        }

        return result;
    }

    private static V101Parameters.AntennaConfiguration ToWireAntennaConfiguration(
        LlrpDeviceAntennaConfiguration antenna) => new(
            antenna.AntennaId,
            new V101Parameters.RFReceiver(antenna.ReceiverSensitivityIndex),
            new V101Parameters.RFTransmitter(
                antenna.HopTableId,
                antenna.ChannelIndex,
                antenna.TransmitPowerIndex),
            []);

    public InventoryObservationBatch ObserveInventory(uint roSpecId, int sequence)
    {
        V101Parameters.ROSpec? roSpec;
        IReadOnlyList<ushort> antennas;
        lock (_gate)
        {
            if (!_roSpecs.TryGetValue(roSpecId, out roSpec) ||
                roSpec.CurrentState != V101Enumerations.ROSpecState.Active)
            {
                return new InventoryObservationBatch();
            }

            antennas = roSpec.SpecParameterItems
                .OfType<V101Parameters.AISpec>()
                .SelectMany(static spec => spec.AntennaIDs)
                .Distinct()
                .ToArray();
        }

        LlrpInventoryPlan plan = BuildInventoryPlan(roSpecId);
        IInventoryExecution execution = _device.StartInventoryAsync(plan)
            .AsTask()
            .GetAwaiter()
            .GetResult();
        try
        {
            return execution.ObserveAsync(new LlrpInventoryRound(roSpecId, sequence, antennas))
                .AsTask().GetAwaiter().GetResult();
        }
        finally
        {
            execution.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private bool ClearAccessSpecs()
    {
        _accessSpecs.Clear();
        return true;
    }

    private static LlrpInventoryFilter ToInventoryFilter(V101Parameters.C1G2Filter filter)
    {
        V101Parameters.C1G2TagInventoryMask mask = filter.C1G2TagInventoryMask;
        bool[] bits = mask.TagMask.ToArray();
        return new LlrpInventoryFilter
        {
            Selector = new LlrpTagSelector
            {
                MemoryBank = (LlrpTagMemoryBank)mask.MB,
                BitPointer = mask.Pointer,
                BitLength = checked((ushort)bits.Length),
                Mask = PackBits(Enumerable.Repeat(true, bits.Length).ToArray()),
                Data = PackBits(bits),
                Match = true,
            },
            MatchAction = filter.C1G2TagInventoryStateUnawareFilterAction is { } unaware
                ? MapFilterAction(unaware.Action, match: true)
                : LlrpInventoryFilterAction.DoNothing,
            NonMatchAction = filter.C1G2TagInventoryStateUnawareFilterAction is { } unaware2
                ? MapFilterAction(unaware2.Action, match: false)
                : LlrpInventoryFilterAction.DoNothing,
            StateTarget = filter.C1G2TagInventoryStateAwareFilterAction is { } aware
                ? MapStateTarget(aware.Target)
                : null,
            StateAction = filter.C1G2TagInventoryStateAwareFilterAction is { } aware2
                ? MapStateAction(aware2.Action)
                : null,
        };
    }

    private static LlrpInventoryFilterAction MapFilterAction(
        V101Enumerations.C1G2StateUnawareAction action,
        bool match)
    {
        return action switch
        {
            V101Enumerations.C1G2StateUnawareAction.Select_Unselect => match
                ? LlrpInventoryFilterAction.Select
                : LlrpInventoryFilterAction.Unselect,
            V101Enumerations.C1G2StateUnawareAction.Select_DoNothing => match
                ? LlrpInventoryFilterAction.Select
                : LlrpInventoryFilterAction.DoNothing,
            V101Enumerations.C1G2StateUnawareAction.DoNothing_Unselect => match
                ? LlrpInventoryFilterAction.DoNothing
                : LlrpInventoryFilterAction.Unselect,
            V101Enumerations.C1G2StateUnawareAction.Unselect_DoNothing => match
                ? LlrpInventoryFilterAction.Unselect
                : LlrpInventoryFilterAction.DoNothing,
            V101Enumerations.C1G2StateUnawareAction.Unselect_Select => match
                ? LlrpInventoryFilterAction.Unselect
                : LlrpInventoryFilterAction.Select,
            V101Enumerations.C1G2StateUnawareAction.DoNothing_Select => match
                ? LlrpInventoryFilterAction.DoNothing
                : LlrpInventoryFilterAction.Select,
            _ => LlrpInventoryFilterAction.DoNothing,
        };
    }

    private static LlrpInventoryStateTarget MapStateTarget(V101Enumerations.C1G2StateAwareTarget target) => target switch
    {
        V101Enumerations.C1G2StateAwareTarget.SL => LlrpInventoryStateTarget.SelectedFlag,
        V101Enumerations.C1G2StateAwareTarget.Inventoried_State_For_Session_S0 => LlrpInventoryStateTarget.Session0,
        V101Enumerations.C1G2StateAwareTarget.Inventoried_State_For_Session_S1 => LlrpInventoryStateTarget.Session1,
        V101Enumerations.C1G2StateAwareTarget.Inventoried_State_For_Session_S2 => LlrpInventoryStateTarget.Session2,
        V101Enumerations.C1G2StateAwareTarget.Inventoried_State_For_Session_S3 => LlrpInventoryStateTarget.Session3,
        _ => LlrpInventoryStateTarget.SelectedFlag,
    };

    private static LlrpInventoryStateAction MapStateAction(V101Enumerations.C1G2StateAwareAction action) => action switch
    {
        V101Enumerations.C1G2StateAwareAction.AssertSLOrA_DeassertSLOrB => LlrpInventoryStateAction.AssertStateAOrSelectedAndDeassertStateBOrUnselected,
        V101Enumerations.C1G2StateAwareAction.AssertSLOrA_Noop => LlrpInventoryStateAction.AssertStateAOrSelectedAndNoOperation,
        V101Enumerations.C1G2StateAwareAction.Noop_DeassertSLOrB => LlrpInventoryStateAction.NoOperationAndDeassertStateBOrUnselected,
        V101Enumerations.C1G2StateAwareAction.NegateSLOrABBA_Noop => LlrpInventoryStateAction.NegateStateOrSelectedAndNoOperation,
        V101Enumerations.C1G2StateAwareAction.DeassertSLOrB_AssertSLOrA => LlrpInventoryStateAction.DeassertStateBOrUnselectedAndAssertStateAOrSelected,
        V101Enumerations.C1G2StateAwareAction.DeassertSLOrB_Noop => LlrpInventoryStateAction.DeassertStateBOrUnselectedAndNoOperation,
        V101Enumerations.C1G2StateAwareAction.Noop_AssertSLOrA => LlrpInventoryStateAction.NoOperationAndAssertStateAOrSelected,
        _ => LlrpInventoryStateAction.NoOperationAndNegateStateOrSelected,
    };

    private static byte[] PackBits(IReadOnlyList<bool> bits)
    {
        var packed = new byte[(bits.Count + 7) / 8];
        for (int index = 0; index < bits.Count; index++)
        {
            if (bits[index])
            {
                packed[index / 8] |= (byte)(1 << (7 - index % 8));
            }
        }

        return packed;
    }

    private bool ShouldStart(V101Parameters.ROSpec roSpec, RoSpecRuntime runtime, DateTimeOffset now)
    {
        V101Parameters.ROSpecStartTrigger trigger = roSpec.ROBoundarySpec.ROSpecStartTrigger;
        if (trigger.ROSpecStartTriggerType == V101Enumerations.ROSpecStartTriggerType.GPI &&
            trigger.GPITriggerValue is { } startGpi &&
            _gpiStates.TryGetValue(startGpi.GPIPortNum, out bool gpiState) &&
            gpiState != startGpi.GPIEvent)
        {
            runtime.StartTriggerLatched = false;
        }

        return trigger.ROSpecStartTriggerType switch
        {
            V101Enumerations.ROSpecStartTriggerType.Immediate => !runtime.HasStarted,
            V101Enumerations.ROSpecStartTriggerType.Periodic =>
                runtime.NextPeriodicStartAtUtc is DateTimeOffset next && now >= next,
            V101Enumerations.ROSpecStartTriggerType.GPI =>
                trigger.GPITriggerValue is { } triggerGpi &&
                _gpiStates.TryGetValue(triggerGpi.GPIPortNum, out bool state) &&
                state == triggerGpi.GPIEvent &&
                !runtime.StartTriggerLatched,
            _ => false,
        };
    }

    private bool ShouldStop(V101Parameters.ROSpec roSpec, RoSpecRuntime runtime, DateTimeOffset now)
    {
        V101Parameters.ROSpecStopTrigger trigger = roSpec.ROBoundarySpec.ROSpecStopTrigger;
        if (runtime.StartedAtUtc is not DateTimeOffset startedAt)
        {
            return false;
        }

        return trigger.ROSpecStopTriggerType switch
        {
            V101Enumerations.ROSpecStopTriggerType.Duration =>
                now >= startedAt.AddMilliseconds(trigger.DurationTriggerValue),
            V101Enumerations.ROSpecStopTriggerType.GPI_With_Timeout =>
                trigger.GPITriggerValue is { } gpi &&
                ((_gpiStates.TryGetValue(gpi.GPIPortNum, out bool state) && state == gpi.GPIEvent) ||
                 (gpi.Timeout > 0 && now >= startedAt.AddMilliseconds(gpi.Timeout))),
            _ => false,
        };
    }

    private static DateTimeOffset? GetPeriodicStartTime(V101Parameters.ROSpec roSpec, DateTimeOffset enabledAt)
    {
        V101Parameters.ROSpecStartTrigger trigger = roSpec.ROBoundarySpec.ROSpecStartTrigger;
        if (trigger.ROSpecStartTriggerType != V101Enumerations.ROSpecStartTriggerType.Periodic ||
            trigger.PeriodicTriggerValue is not { } periodic)
        {
            return null;
        }

        if (periodic.UTCTimestamp is { } utc)
        {
            DateTimeOffset timestamp = DateTimeOffset.UnixEpoch.AddTicks(
                checked((long)utc.Microseconds * TimeSpan.TicksPerMicrosecond));
            return timestamp.AddMilliseconds(periodic.Offset);
        }

        return enabledAt.AddMilliseconds(periodic.Offset);
    }

    private sealed class RoSpecRuntime
    {
        public DateTimeOffset? EnabledAtUtc { get; set; }
        public DateTimeOffset? StartedAtUtc { get; set; }
        public DateTimeOffset? NextPeriodicStartAtUtc { get; set; }
        public bool HasStarted { get; set; }
        public bool StartTriggerLatched { get; set; }
    }
}

internal sealed record RoSpecTriggerTransition(uint RoSpecId, bool Started);

internal readonly record struct LlrpReportBufferResult(
    bool Overflowed,
    bool Warning,
    byte Percentage);

internal sealed class LlrpDeviceServerState
{
    private readonly object _reportGate = new();
    private readonly Queue<V101Messages.RO_ACCESS_REPORT> _reportBuffer = new();
    private readonly Queue<LlrpDeviceEvent> _heldEvents = new();
    private readonly Dictionary<uint, List<V101Parameters.TagReportData>> _pendingRoSpecReports = [];
    private int _hasSeenClient;
    private bool _holdEventsAndReports;

    public LlrpDeviceServerState(ILlrpDevice device, LlrpDeviceServerOptions options)
    {
        Device = device ?? throw new ArgumentNullException(nameof(device));
        Options = options ?? throw new ArgumentNullException(nameof(options));
        Resources = new LlrpResourceRegistry(device, options.InitialReaderConfigurationCustomItems);
        Inventory = new LlrpDeviceInventoryBridge(device);
    }

    public ILlrpDevice Device { get; }
    public LlrpDeviceServerOptions Options { get; }
    public LlrpResourceRegistry Resources { get; }
    public LlrpDeviceInventoryBridge Inventory { get; }

    public bool TryAddRoSpec(V101Parameters.ROSpec roSpec) => Resources.TryAddRoSpec(roSpec);
    public bool TryGetRoSpec(uint roSpecId, out V101Parameters.ROSpec? roSpec) => Resources.TryGetRoSpec(roSpecId, out roSpec);
    public IReadOnlyList<V101Parameters.ROSpec> GetRoSpecs() => Resources.GetRoSpecs();
    public IReadOnlyList<uint> GetRoSpecIds() => Resources.GetRoSpecIds();
    public bool TryUpdateRoSpec(uint roSpecId, Func<V101Parameters.ROSpec, V101Parameters.ROSpec> update) =>
        Resources.TryUpdateRoSpec(roSpecId, update);
    public bool TryDeleteRoSpec(uint roSpecId, out bool deletedAll) => Resources.TryDeleteRoSpec(roSpecId, out deletedAll);
    public IReadOnlyList<uint> GetActiveRoSpecIds() => Resources.GetActiveRoSpecIds();
    public bool IsAutomaticReportDeliveryEnabled(uint roSpecId) => Resources.IsAutomaticReportDeliveryEnabled(roSpecId);
    public IReadOnlyList<ushort> GetInventoryAntennaIds(uint roSpecId) => Resources.GetInventoryAntennaIds(roSpecId);
    public LlrpInventoryPlan BuildInventoryPlan(uint roSpecId) => Resources.BuildInventoryPlan(roSpecId);
    public void MarkRoSpecEnabled(uint roSpecId) => Resources.MarkRoSpecEnabled(roSpecId);
    public void MarkRoSpecStarted(uint roSpecId) => Resources.MarkRoSpecStarted(roSpecId);
    public void MarkRoSpecStopped(uint roSpecId) => Resources.MarkRoSpecStopped(roSpecId);
    public void SetGpiState(ushort portNumber, bool state) => Resources.SetGpiState(portNumber, state);
    public IReadOnlyList<V101Parameters.GPIPortCurrentState> GetGpiStates(ushort portNumber = 0) => Resources.GetGpiStates(portNumber);
    public void SetAntennaConnection(ushort antennaId, bool connected) => Resources.SetAntennaConnection(antennaId, connected);
    public bool IsAntennaConnected(ushort antennaId) => Resources.IsAntennaConnected(antennaId);
    public IReadOnlyList<RoSpecTriggerTransition> ProcessRoSpecTriggers(DateTimeOffset now) => Resources.ProcessRoSpecTriggers(now);
    public bool TryAddAccessSpec(V101Parameters.AccessSpec accessSpec) => Resources.TryAddAccessSpec(accessSpec);
    public bool TryGetAccessSpec(uint accessSpecId, out V101Parameters.AccessSpec? accessSpec) => Resources.TryGetAccessSpec(accessSpecId, out accessSpec);
    public IReadOnlyList<V101Parameters.AccessSpec> GetAccessSpecs() => Resources.GetAccessSpecs();
    public IReadOnlyList<uint> GetAccessSpecIds() => Resources.GetAccessSpecIds();
    public bool TryUpdateAccessSpec(uint accessSpecId, Func<V101Parameters.AccessSpec, V101Parameters.AccessSpec> update) =>
        Resources.TryUpdateAccessSpec(accessSpecId, update);
    public bool TryDeleteAccessSpec(uint accessSpecId) => Resources.TryDeleteAccessSpec(accessSpecId);
    public IReadOnlyList<V101Parameters.AntennaConfiguration> GetAntennaConfigurations() => Resources.GetAntennaConfigurations();
    public IReadOnlyList<V101Parameters.GPOWriteData> GetGpoWriteData() => Resources.GetGpoWriteData();
    public V101Parameters.ReaderEventNotificationSpec? GetReaderEventNotificationSpec() => Resources.GetReaderEventNotificationSpec();
    public V101Parameters.ROReportSpec? GetRoReportSpec() => Resources.GetRoReportSpec();
    public V101Parameters.AccessReportSpec? GetAccessReportSpec() => Resources.GetAccessReportSpec();
    public V101Parameters.KeepaliveSpec GetKeepaliveSpec() => Resources.GetKeepaliveSpec();
    public bool IsKeepaliveSpecConfigured => Resources.IsKeepaliveSpecConfigured;
    public V101Parameters.EventsAndReports? GetEventsAndReports() => Resources.GetEventsAndReports();
    public uint GetConfigurationStateValue() => Resources.GetConfigurationStateValue();
    public IReadOnlyList<LlrpNet.Protocol.Parameters.ILlrpParameter> GetReaderConfigurationCustomItems() =>
        Resources.GetReaderConfigurationCustomItems();
    public IReadOnlyList<LlrpNet.Protocol.Parameters.ILlrpParameter> GetReaderCapabilitiesCustomItems() =>
        Options.InitialReaderCapabilitiesCustomItems;

    public bool IsEventEnabled(V101Enumerations.NotificationEventType eventType) =>
        Resources.GetReaderEventNotificationSpec()?.EventNotificationStateItems
            .OfType<V101Parameters.EventNotificationState>()
            .Any(item => item.EventType == eventType && item.NotificationState) == true;

    public bool IsHoldingEventsAndReports
    {
        get
        {
            lock (_reportGate)
            {
                return _holdEventsAndReports;
            }
        }
    }

    public void ClientConnected()
    {
        lock (_reportGate)
        {
            bool holdConfigured = Resources.GetEventsAndReports()?.HoldEventsAndReportsUponReconnect == true;
            _holdEventsAndReports = holdConfigured && Volatile.Read(ref _hasSeenClient) != 0;
            Volatile.Write(ref _hasSeenClient, 1);
        }
    }

    public void ClientDisconnected()
    {
        lock (_reportGate)
        {
            _holdEventsAndReports = Resources.GetEventsAndReports()?.HoldEventsAndReportsUponReconnect == true;
        }
    }

    public void ReleaseHeldEventsAndReports()
    {
        lock (_reportGate)
        {
            _holdEventsAndReports = false;
        }
    }

    public void BufferHeldEvent(LlrpDeviceEvent deviceEvent)
    {
        ArgumentNullException.ThrowIfNull(deviceEvent);
        lock (_reportGate)
        {
            while (_heldEvents.Count >= Options.ReportBufferCapacity)
            {
                _heldEvents.Dequeue();
            }

            _heldEvents.Enqueue(deviceEvent);
        }
    }

    public IReadOnlyList<LlrpDeviceEvent> DrainHeldEvents()
    {
        lock (_reportGate)
        {
            var events = new List<LlrpDeviceEvent>(_heldEvents.Count);
            while (_heldEvents.Count > 0)
            {
                events.Add(_heldEvents.Dequeue());
            }

            return events;
        }
    }

    public void ClearRuntimeReports()
    {
        lock (_reportGate)
        {
            _reportBuffer.Clear();
            _pendingRoSpecReports.Clear();
            _heldEvents.Clear();
            _holdEventsAndReports = false;
        }
    }

    public LlrpReportBufferResult BufferReport(V101Messages.RO_ACCESS_REPORT report)
    {
        ArgumentNullException.ThrowIfNull(report);
        lock (_reportGate)
        {
            bool overflowed = false;
            while (_reportBuffer.Count >= Options.ReportBufferCapacity)
            {
                _reportBuffer.Dequeue();
                overflowed = true;
            }

            _reportBuffer.Enqueue(report);
            byte percentage = checked((byte)Math.Min(
                100,
                (int)Math.Ceiling(_reportBuffer.Count * 100d / Options.ReportBufferCapacity)));
            return new LlrpReportBufferResult(
                overflowed,
                percentage >= 80,
                percentage);
        }
    }

    public void AccumulateRoSpecReport(
        uint roSpecId,
        IReadOnlyList<V101Parameters.TagReportData> tagReports)
    {
        ArgumentNullException.ThrowIfNull(tagReports);
        lock (_reportGate)
        {
            if (!_pendingRoSpecReports.TryGetValue(roSpecId, out List<V101Parameters.TagReportData>? pending))
            {
                pending = [];
                _pendingRoSpecReports[roSpecId] = pending;
            }

            pending.AddRange(tagReports);
        }
    }

    public IReadOnlyList<V101Messages.RO_ACCESS_REPORT> TakeReadyRoSpecReports(
        uint roSpecId,
        ushort reportEvery,
        Func<uint> messageIdFactory)
    {
        ArgumentNullException.ThrowIfNull(messageIdFactory);
        if (reportEvery == 0)
        {
            return [];
        }

        lock (_reportGate)
        {
            if (!_pendingRoSpecReports.TryGetValue(roSpecId, out List<V101Parameters.TagReportData>? pending) ||
                pending.Count < reportEvery)
            {
                return [];
            }

            var reports = new List<V101Messages.RO_ACCESS_REPORT>(pending.Count / reportEvery);
            int readyCount = pending.Count - pending.Count % reportEvery;
            for (int offset = 0; offset < readyCount; offset += reportEvery)
            {
                reports.Add(new V101Messages.RO_ACCESS_REPORT(
                    messageIdFactory(),
                    pending.GetRange(offset, reportEvery),
                    [],
                    []));
            }

            pending.RemoveRange(0, readyCount);
            if (pending.Count == 0)
            {
                _pendingRoSpecReports.Remove(roSpecId);
            }

            return reports;
        }
    }

    public void ClearAccumulatedRoSpecReport(uint roSpecId)
    {
        lock (_reportGate)
        {
            _pendingRoSpecReports.Remove(roSpecId);
        }
    }

    public void ClearAllAccumulatedRoSpecReports()
    {
        lock (_reportGate)
        {
            _pendingRoSpecReports.Clear();
        }
    }

    public V101Messages.RO_ACCESS_REPORT? TakeAccumulatedRoSpecReport(uint roSpecId, uint messageId = 0)
    {
        lock (_reportGate)
        {
            if (!_pendingRoSpecReports.Remove(roSpecId, out List<V101Parameters.TagReportData>? pending) || pending.Count == 0)
            {
                return null;
            }

            return new V101Messages.RO_ACCESS_REPORT(messageId, pending, [], []);
        }
    }

    public V101Messages.RO_ACCESS_REPORT TakeBufferedReport(uint messageId)
    {
        lock (_reportGate)
        {
            return _reportBuffer.Count == 0
                ? new V101Messages.RO_ACCESS_REPORT(messageId, [], [], [])
                : _reportBuffer.Dequeue() with { MessageId = messageId };
        }
    }

    public IReadOnlyList<ILlrpMessage> DrainBufferedReports()
    {
        lock (_reportGate)
        {
            var reports = new List<ILlrpMessage>(_reportBuffer.Count);
            while (_reportBuffer.Count > 0)
            {
                reports.Add(_reportBuffer.Dequeue());
            }

            return reports;
        }
    }

    public int BufferedReportCount
    {
        get
        {
            lock (_reportGate)
            {
                return _reportBuffer.Count;
            }
        }
    }
    public LlrpDeviceOperationResult SetConfiguration(
        bool resetToFactoryDefault,
        IReadOnlyList<V101Parameters.AntennaConfiguration> antennaConfigurations,
        V101Parameters.ReaderEventNotificationSpec? readerEventNotificationSpec,
        V101Parameters.ROReportSpec? roReportSpec,
        V101Parameters.AccessReportSpec? accessReportSpec,
        V101Parameters.KeepaliveSpec? keepaliveSpec,
        IReadOnlyList<V101Parameters.GPOWriteData> gpoWriteData,
        V101Parameters.EventsAndReports? eventsAndReports,
        IReadOnlyList<LlrpNet.Protocol.Parameters.ILlrpParameter> customItems) =>
        Resources.SetConfiguration(
            resetToFactoryDefault,
            antennaConfigurations,
            readerEventNotificationSpec,
            roReportSpec,
            accessReportSpec,
            keepaliveSpec,
            gpoWriteData,
            eventsAndReports,
            customItems);
}

internal sealed record LlrpDeviceTag
{
    public required ReadOnlyMemory<byte> ElectronicProductCode { get; init; }
    public ReadOnlyMemory<byte> Tid { get; init; }
    public short PeakRssi { get; init; } = -42;
    public ushort AntennaId { get; init; } = 1;
    public ushort ChannelIndex { get; init; } = 1;
    public DateTimeOffset FirstSeenUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastSeenUtc { get; init; }
    public uint SeenCount { get; init; } = 1;
    public ushort? PcBits { get; init; }
    public ushort? Crc { get; init; }
}

internal sealed class LlrpDeviceInventoryBridge
{
    private readonly ILlrpDevice _device;

    public LlrpDeviceInventoryBridge(ILlrpDevice device) => _device = device;

    public IReadOnlyList<LlrpDeviceTag> Observe(LlrpInventoryPlan plan, LlrpInventoryRound round)
    {
        IInventoryExecution execution = _device.StartInventoryAsync(plan)
            .AsTask().GetAwaiter().GetResult();
        try
        {
            InventoryObservationBatch batch = execution.ObserveAsync(round)
                .AsTask().GetAwaiter().GetResult();
            return batch.Tags.Select(static tag => new LlrpDeviceTag
            {
                ElectronicProductCode = tag.ElectronicProductCode,
                Tid = tag.Tid,
                PeakRssi = tag.PeakRssi,
                AntennaId = tag.AntennaId,
                ChannelIndex = tag.ChannelIndex,
                FirstSeenUtc = tag.FirstSeenUtc,
                LastSeenUtc = tag.LastSeenUtc,
                SeenCount = tag.SeenCount,
                PcBits = tag.PcBits,
                Crc = tag.Crc,
            }).ToArray();
        }
        finally
        {
            execution.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    public IReadOnlyList<LlrpDeviceTag> Observe(LlrpInventoryRound round) => Observe(
        new LlrpInventoryPlan
        {
            RoSpecId = round.RoSpecId,
            AntennaIds = round.AntennaIds,
        },
        round);

    public bool TryReadWords(
        ReadOnlySpan<byte> epc,
        byte memoryBank,
        int wordPointer,
        int wordCount,
        out IReadOnlyList<ushort> words)
    {
        LlrpTagAccessResult? result = Execute(
            epc,
            new LlrpTagAccessOperation
            {
                OperationId = 1,
                Kind = LlrpTagAccessOperationKind.Read,
                MemoryBank = (LlrpTagMemoryBank)memoryBank,
                WordPointer = checked((ushort)wordPointer),
                WordCount = checked((ushort)wordCount),
            });
        LlrpTagAccessOperationResult? operation = result?.Operations.SingleOrDefault();
        words = operation?.ReadData ?? [];
        return operation?.Result == LlrpTagAccessResultCode.Success;
    }

    public bool TryWriteWords(
        ReadOnlySpan<byte> epc,
        byte memoryBank,
        int wordPointer,
        IReadOnlyList<ushort> words)
    {
        LlrpTagAccessResult? result = Execute(
            epc,
            new LlrpTagAccessOperation
            {
                OperationId = 1,
                Kind = LlrpTagAccessOperationKind.Write,
                MemoryBank = (LlrpTagMemoryBank)memoryBank,
                WordPointer = checked((ushort)wordPointer),
                WordCount = checked((ushort)words.Count),
                WriteData = words,
            });
        return result?.Operations.SingleOrDefault()?.Result == LlrpTagAccessResultCode.Success;
    }

    public bool TryGetMemoryBytes(ReadOnlySpan<byte> epc, byte memoryBank, out ReadOnlyMemory<byte> bytes)
    {
        if (memoryBank == 1)
        {
            var memoryBytes = new byte[4 + epc.Length];
            epc.CopyTo(memoryBytes.AsSpan(4));
            bytes = memoryBytes;
            return true;
        }

        if (memoryBank == 0)
        {
            bytes = new byte[16];
            return true;
        }

        if (memoryBank == 2)
        {
            TagObservation? tag = FindTag(epc);
            if (tag is not null && !tag.Tid.IsEmpty)
            {
                bytes = tag.Tid;
                return true;
            }

            bytes = ReadOnlyMemory<byte>.Empty;
            return false;
        }

        if (memoryBank == 3)
        {
            var words = new List<ushort>();
            for (int pointer = 0; pointer < 256; pointer++)
            {
                if (!TryReadWords(epc, memoryBank, pointer, 1, out IReadOnlyList<ushort> one) || one.Count != 1)
                {
                    break;
                }

                words.Add(one[0]);
            }

            if (words.Count == 0)
            {
                bytes = ReadOnlyMemory<byte>.Empty;
                return false;
            }

            bytes = WordsToBytes(words);
            return true;
        }

        bytes = ReadOnlyMemory<byte>.Empty;
        return false;
    }

    private TagObservation? FindTag(ReadOnlySpan<byte> epc)
    {
        byte[] expected = epc.ToArray();
        IInventoryExecution execution = _device.StartInventoryAsync(new LlrpInventoryPlan { RoSpecId = 0 })
            .AsTask().GetAwaiter().GetResult();
        try
        {
            InventoryObservationBatch batch = execution.ObserveAsync(new LlrpInventoryRound(0, 0, []))
                .AsTask().GetAwaiter().GetResult();
            return batch.Tags.FirstOrDefault(tag => tag.ElectronicProductCode.Span.SequenceEqual(expected));
        }
        finally
        {
            execution.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static byte[] WordsToBytes(IReadOnlyList<ushort> words)
    {
        var bytes = new byte[words.Count * 2];
        for (int index = 0; index < words.Count; index++)
        {
            bytes[index * 2] = (byte)(words[index] >> 8);
            bytes[index * 2 + 1] = (byte)words[index];
        }

        return bytes;
    }

    private LlrpTagAccessResult? Execute(ReadOnlySpan<byte> epc, LlrpTagAccessOperation operation)
    {
        var selector = new LlrpTagSelector
        {
            MemoryBank = LlrpTagMemoryBank.ElectronicProductCode,
            BitPointer = 32,
            BitLength = checked((ushort)(epc.Length * 8)),
            Mask = epc.ToArray(),
            Data = epc.ToArray(),
            Match = true,
        };
        return _device.ExecuteTagAccessAsync(new LlrpTagAccessRequest
        {
            AccessSpecId = 0,
            RoSpecId = 0,
            Selector = selector,
            Operations = [operation],
        }).AsTask().GetAwaiter().GetResult().FirstOrDefault();
    }
}
