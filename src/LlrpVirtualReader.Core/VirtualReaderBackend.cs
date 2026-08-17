using LlrpNet.Protocol.Parameters;
using V101Enumerations = LlrpNet.Protocol.Enumerations.V1_0_1;
using V101Parameters = LlrpNet.Protocol.Parameters.V1_0_1;

namespace LlrpVirtualReader;

/// <summary>
/// Defines the device-side behavior consumed by the LLRP service layer.
/// </summary>
/// <remarks>
/// The protocol dispatcher and standard handlers depend on this contract rather than on a fixed tag source.
/// A physical-device host can implement the same resource and inventory operations over a real RFID module;
/// <see cref="VirtualReaderDeviceBackend"/> is the deterministic in-memory implementation used here.
/// </remarks>
public interface ILlrpReaderDeviceBackend
{
    /// <summary>Gets the profile and policy values exposed by the backend.</summary>
    public VirtualReaderOptions Options { get; }

    /// <summary>Gets the inventory/tag operation implementation.</summary>
    public ILlrpReaderInventoryBackend Inventory { get; }

    public bool TryAddRoSpec(V101Parameters.ROSpec roSpec);
    public bool TryGetRoSpec(uint roSpecId, out V101Parameters.ROSpec? roSpec);
    public IReadOnlyList<V101Parameters.ROSpec> GetRoSpecs();
    public bool TryUpdateRoSpec(uint roSpecId, Func<V101Parameters.ROSpec, V101Parameters.ROSpec> update);
    public bool TryDeleteRoSpec(uint roSpecId, out bool deletedAll);
    public IReadOnlyList<uint> GetActiveRoSpecIds();
    public IReadOnlyList<ushort> GetInventoryAntennaIds(uint roSpecId);

    public bool TryAddAccessSpec(V101Parameters.AccessSpec accessSpec);
    public bool TryGetAccessSpec(uint accessSpecId, out V101Parameters.AccessSpec? accessSpec);
    public IReadOnlyList<V101Parameters.AccessSpec> GetAccessSpecs();
    public bool TryUpdateAccessSpec(uint accessSpecId, Func<V101Parameters.AccessSpec, V101Parameters.AccessSpec> update);
    public bool TryDeleteAccessSpec(uint accessSpecId);

    public IReadOnlyList<V101Parameters.AntennaConfiguration> GetAntennaConfigurations();
    public IReadOnlyList<V101Parameters.GPOWriteData> GetGpoWriteData();
    public V101Parameters.ReaderEventNotificationSpec? GetReaderEventNotificationSpec();
    public V101Parameters.ROReportSpec? GetRoReportSpec();
    public V101Parameters.AccessReportSpec? GetAccessReportSpec();
    public V101Parameters.KeepaliveSpec GetKeepaliveSpec();
    public V101Parameters.EventsAndReports? GetEventsAndReports();
    public void SetConfiguration(
        bool resetToFactoryDefault,
        IReadOnlyList<V101Parameters.AntennaConfiguration> antennaConfigurations,
        V101Parameters.ReaderEventNotificationSpec? readerEventNotificationSpec,
        V101Parameters.ROReportSpec? roReportSpec,
        V101Parameters.AccessReportSpec? accessReportSpec,
        V101Parameters.KeepaliveSpec? keepaliveSpec,
        IReadOnlyList<V101Parameters.GPOWriteData> gpoWriteData,
        V101Parameters.EventsAndReports? eventsAndReports);
}

/// <summary>Provides inventory observations and tag-memory operations to an LLRP device backend.</summary>
public interface ILlrpReaderInventoryBackend
{
    /// <summary>Observes one inventory round for the supplied ROSpec and antenna selection.</summary>
    public IReadOnlyList<VirtualTag> Observe(VirtualReaderInventoryRound round);

    /// <summary>Reads words from one observed tag memory bank.</summary>
    public bool TryReadWords(
        ReadOnlySpan<byte> electronicProductCode,
        byte memoryBank,
        int wordPointer,
        int wordCount,
        out IReadOnlyList<ushort> words);

    /// <summary>Writes words to one observed tag memory bank.</summary>
    public bool TryWriteWords(
        ReadOnlySpan<byte> electronicProductCode,
        byte memoryBank,
        int wordPointer,
        IReadOnlyList<ushort> words);

    /// <summary>Gets bytes used for C1G2 selection matching.</summary>
    public bool TryGetMemoryBytes(
        ReadOnlySpan<byte> electronicProductCode,
        byte memoryBank,
        out ReadOnlyMemory<byte> bytes);
}

/// <summary>Identifies one deterministic inventory round.</summary>
public sealed record VirtualReaderInventoryRound(
    uint RoSpecId,
    int Sequence,
    IReadOnlyList<ushort> AntennaIds);

/// <summary>Deterministic Virtual Reader implementation of the device backend contract.</summary>
public sealed class VirtualReaderDeviceBackend : ILlrpReaderDeviceBackend
{
    private readonly VirtualReaderDeviceState _state;

    /// <summary>Creates a virtual backend using the configured deterministic RF behavior.</summary>
    public VirtualReaderDeviceBackend(
        VirtualReaderOptions options,
        ILlrpReaderInventoryBackend? inventory = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _state = new VirtualReaderDeviceState(options);
        Inventory = inventory ?? new VirtualReaderInventoryBackend(_state.TagSource, options.RfSimulation);
    }

    /// <inheritdoc />
    public VirtualReaderOptions Options => _state.Options;

    /// <inheritdoc />
    public ILlrpReaderInventoryBackend Inventory { get; }

    public bool TryAddRoSpec(V101Parameters.ROSpec roSpec) => _state.TryAddRoSpec(roSpec);
    public bool TryGetRoSpec(uint roSpecId, out V101Parameters.ROSpec? roSpec) => _state.TryGetRoSpec(roSpecId, out roSpec);
    public IReadOnlyList<V101Parameters.ROSpec> GetRoSpecs() => _state.GetRoSpecs();
    public bool TryUpdateRoSpec(uint roSpecId, Func<V101Parameters.ROSpec, V101Parameters.ROSpec> update) =>
        _state.TryUpdateRoSpec(roSpecId, update);
    public bool TryDeleteRoSpec(uint roSpecId, out bool deletedAll) => _state.TryDeleteRoSpec(roSpecId, out deletedAll);
    public IReadOnlyList<uint> GetActiveRoSpecIds() => _state.GetActiveRoSpecIds();
    public IReadOnlyList<ushort> GetInventoryAntennaIds(uint roSpecId) => _state.GetInventoryAntennaIds(roSpecId);

    public bool TryAddAccessSpec(V101Parameters.AccessSpec accessSpec) => _state.TryAddAccessSpec(accessSpec);
    public bool TryGetAccessSpec(uint accessSpecId, out V101Parameters.AccessSpec? accessSpec) =>
        _state.TryGetAccessSpec(accessSpecId, out accessSpec);
    public IReadOnlyList<V101Parameters.AccessSpec> GetAccessSpecs() => _state.GetAccessSpecs();
    public bool TryUpdateAccessSpec(uint accessSpecId, Func<V101Parameters.AccessSpec, V101Parameters.AccessSpec> update) =>
        _state.TryUpdateAccessSpec(accessSpecId, update);
    public bool TryDeleteAccessSpec(uint accessSpecId) => _state.TryDeleteAccessSpec(accessSpecId);

    public IReadOnlyList<V101Parameters.AntennaConfiguration> GetAntennaConfigurations() =>
        _state.GetAntennaConfigurations();
    public IReadOnlyList<V101Parameters.GPOWriteData> GetGpoWriteData() => _state.GetGpoWriteData();
    public V101Parameters.ReaderEventNotificationSpec? GetReaderEventNotificationSpec() =>
        _state.GetReaderEventNotificationSpec();
    public V101Parameters.ROReportSpec? GetRoReportSpec() => _state.GetRoReportSpec();
    public V101Parameters.AccessReportSpec? GetAccessReportSpec() => _state.GetAccessReportSpec();
    public V101Parameters.KeepaliveSpec GetKeepaliveSpec() => _state.GetKeepaliveSpec();
    public V101Parameters.EventsAndReports? GetEventsAndReports() => _state.GetEventsAndReports();

    public void SetConfiguration(
        bool resetToFactoryDefault,
        IReadOnlyList<V101Parameters.AntennaConfiguration> antennaConfigurations,
        V101Parameters.ReaderEventNotificationSpec? readerEventNotificationSpec,
        V101Parameters.ROReportSpec? roReportSpec,
        V101Parameters.AccessReportSpec? accessReportSpec,
        V101Parameters.KeepaliveSpec? keepaliveSpec,
        IReadOnlyList<V101Parameters.GPOWriteData> gpoWriteData,
        V101Parameters.EventsAndReports? eventsAndReports) =>
        _state.SetConfiguration(
            resetToFactoryDefault,
            antennaConfigurations,
            readerEventNotificationSpec,
            roReportSpec,
            accessReportSpec,
            keepaliveSpec,
            gpoWriteData,
            eventsAndReports);
}
