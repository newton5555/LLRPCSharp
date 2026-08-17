using V101Enumerations = LlrpNet.Protocol.Enumerations.V1_0_1;
using V101Parameters = LlrpNet.Protocol.Parameters.V1_0_1;

namespace LlrpVirtualReader;

/// <summary>
/// Thread-safe device state owned by one virtual reader host.
/// </summary>
/// <remarks>
/// The state stores the canonical 1.0.1 wire model. The 1.1 profile translates its shared standard messages
/// through the LlrpNet registry before reaching this store, so the host never keeps two divergent resource graphs.
/// </remarks>
internal sealed class VirtualReaderDeviceState
{
    private readonly object _gate = new();
    private readonly Dictionary<uint, V101Parameters.ROSpec> _roSpecs = [];
    private readonly Dictionary<uint, V101Parameters.AccessSpec> _accessSpecs = [];
    private IReadOnlyList<V101Parameters.AntennaConfiguration> _antennaConfigurations = [];
    private IReadOnlyList<V101Parameters.GPOWriteData> _gpoWriteData = [new(1, false)];
    private V101Parameters.ReaderEventNotificationSpec? _readerEventNotificationSpec;
    private V101Parameters.ROReportSpec? _roReportSpec;
    private V101Parameters.AccessReportSpec? _accessReportSpec;
    private V101Parameters.KeepaliveSpec _keepaliveSpec =
        new(V101Enumerations.KeepaliveTriggerType.Null, 0);
    private V101Parameters.EventsAndReports? _eventsAndReports;

    public VirtualReaderDeviceState(VirtualReaderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Options = options;
        TagSource = options.NormalizeLegacyTagSource();
        _antennaConfigurations = options.AntennaConfigurations
            .Select(static antenna => new V101Parameters.AntennaConfiguration(
                antenna.AntennaId,
                new V101Parameters.RFReceiver(antenna.ReceiverSensitivityIndex),
                new V101Parameters.RFTransmitter(
                    antenna.HopTableId,
                    antenna.ChannelIndex,
                    antenna.TransmitPowerIndex),
                []))
            .ToArray();
        _gpoWriteData = options.GpoStates
            .Select(static gpo => new V101Parameters.GPOWriteData(gpo.PortNumber, gpo.State))
            .ToArray();
    }

    public VirtualReaderOptions Options { get; }

    public IVirtualTagSource TagSource { get; }

    public bool TryAddRoSpec(V101Parameters.ROSpec roSpec)
    {
        lock (_gate)
        {
            return roSpec.ROSpecID != 0 && _roSpecs.TryAdd(roSpec.ROSpecID, roSpec);
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
                deletedAll = true;
                return true;
            }

            deletedAll = false;
            if (!_roSpecs.Remove(roSpecId))
            {
                return false;
            }

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

    public V101Parameters.EventsAndReports? GetEventsAndReports()
    {
        lock (_gate)
        {
            return _eventsAndReports;
        }
    }

    public void SetConfiguration(
        bool resetToFactoryDefault,
        IReadOnlyList<V101Parameters.AntennaConfiguration> antennaConfigurations,
        V101Parameters.ReaderEventNotificationSpec? readerEventNotificationSpec,
        V101Parameters.ROReportSpec? roReportSpec,
        V101Parameters.AccessReportSpec? accessReportSpec,
        V101Parameters.KeepaliveSpec? keepaliveSpec,
        IReadOnlyList<V101Parameters.GPOWriteData> gpoWriteData,
        V101Parameters.EventsAndReports? eventsAndReports)
    {
        ArgumentNullException.ThrowIfNull(antennaConfigurations);
        ArgumentNullException.ThrowIfNull(gpoWriteData);
        lock (_gate)
        {
            if (resetToFactoryDefault)
            {
                _antennaConfigurations = [];
                _gpoWriteData = [new(1, false)];
                _readerEventNotificationSpec = null;
                _roReportSpec = null;
                _accessReportSpec = null;
                _keepaliveSpec = new(V101Enumerations.KeepaliveTriggerType.Null, 0);
                _eventsAndReports = null;
                return;
            }

            if (antennaConfigurations.Count > 0)
            {
                _antennaConfigurations = antennaConfigurations.ToArray();
            }

            if (gpoWriteData.Count > 0)
            {
                _gpoWriteData = gpoWriteData.ToArray();
            }

            if (readerEventNotificationSpec is not null)
            {
                _readerEventNotificationSpec = readerEventNotificationSpec;
            }

            if (roReportSpec is not null)
            {
                _roReportSpec = roReportSpec;
            }

            if (accessReportSpec is not null)
            {
                _accessReportSpec = accessReportSpec;
            }

            if (keepaliveSpec is not null)
            {
                _keepaliveSpec = keepaliveSpec;
            }

            if (eventsAndReports is not null)
            {
                _eventsAndReports = eventsAndReports;
            }
        }
    }

    private bool ClearAccessSpecs()
    {
        _accessSpecs.Clear();
        return true;
    }
}
