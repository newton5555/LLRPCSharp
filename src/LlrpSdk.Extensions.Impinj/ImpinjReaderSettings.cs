using LlrpSdk.Extensions.Impinj.Enumerations.V1_0_1;

namespace LlrpSdk.Extensions.Impinj;

/// <summary>Read-only projection of the Impinj-specific portion of a reader configuration response.</summary>
public sealed record ImpinjReaderSettings
{
    /// <summary>Gets the configured regulatory region, when reported by the reader.</summary>
    public ImpinjRegulatoryRegion? RegulatoryRegion { get; init; }

    /// <summary>Gets one debounce setting per reported GPI port.</summary>
    public IReadOnlyList<ImpinjGpiDebounceSetting> GpiDebounce { get; init; } = [];

    /// <summary>Gets the reader's internal temperature in degrees Celsius, when reported.</summary>
    public short? TemperatureCelsius { get; init; }

    /// <summary>Gets the LLRP link-monitor configuration, when reported.</summary>
    public ImpinjLinkMonitorSettings? LinkMonitor { get; init; }

    /// <summary>Gets the asynchronous report-buffer mode, when reported.</summary>
    public ImpinjReportBufferMode? ReportBufferMode { get; init; }

    /// <summary>Gets the global AccessSpec execution settings, when reported.</summary>
    public ImpinjAccessSpecSettings? AccessSpec { get; init; }
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
