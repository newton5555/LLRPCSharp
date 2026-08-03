using LlrpNet.Protocol.Impinj.Enumerations.V1_0_1;

namespace LlrpSdk.Extensions.Impinj;

/// <summary>
/// High-level writable Impinj reader configuration. This is a hand-written extension model, not a generated
/// LLRP parameter: <see cref="ImpinjReaderExtension"/> compiles it to the generated custom parameters.
/// </summary>
public sealed record ImpinjReaderConfiguration
{
    public const string ExtensionKey = "impinj.configuration";

    public ImpinjInventorySearchType? InventorySearchMode { get; init; }
    public ImpinjFixedFrequencySettings? FixedFrequency { get; init; }
    public ImpinjReducedPowerFrequencySettings? ReducedPowerFrequency { get; init; }
    public ImpinjLowDutyCycleSettings? LowDutyCycle { get; init; }
    public IReadOnlyList<ImpinjGpiDebounceSetting> GpiDebounce { get; init; } = [];
    public ImpinjLinkMonitorSettings? LinkMonitor { get; init; }
    public ImpinjReportBufferMode? ReportBufferMode { get; init; }
    public ImpinjAccessSpecSettings? AccessSpec { get; init; }
    public IReadOnlyList<ImpinjAdvancedGpoSetting> AdvancedGpos { get; init; } = [];
}

public sealed record ImpinjFixedFrequencySettings(
    ImpinjFixedFrequencyMode Mode,
    IReadOnlyList<ushort> ChannelList);

public sealed record ImpinjReducedPowerFrequencySettings(
    ImpinjReducedPowerMode Mode,
    IReadOnlyList<ushort> ChannelList);

public sealed record ImpinjLowDutyCycleSettings(
    ImpinjLowDutyCycleMode Mode,
    ushort EmptyFieldTimeoutMilliseconds,
    ushort FieldPingIntervalMilliseconds);

public sealed record ImpinjAdvancedGpoSetting(
    ushort GpoPortNumber,
    ImpinjAdvancedGPOMode Mode,
    uint PulseDurationMilliseconds);

/// <summary>Read-only Impinj reader facts returned by the reader and never sent in a settings apply request.</summary>
public sealed record ImpinjReaderFacts
{
    public const string ExtensionKey = "impinj.facts";
    public ImpinjRegulatoryRegion? RegulatoryRegion { get; init; }
    public short? TemperatureCelsius { get; init; }
}

/// <summary>Configures debounce for one Impinj GPI port.</summary>
public sealed record ImpinjGpiDebounceSetting(ushort GpiPortNumber, uint DebounceMilliseconds);

/// <summary>Configures Impinj LLRP keepalive acknowledgement monitoring.</summary>
public sealed record ImpinjLinkMonitorSettings(bool Enabled, ushort LinkDownThreshold);

/// <summary>Configures global Impinj AccessSpec execution behavior.</summary>
public sealed record ImpinjAccessSpecSettings(
    ushort? BlockWriteWordCount,
    ushort? OpSpecRetryCount,
    ImpinjAccessSpecOrderingMode? OrderingMode);
