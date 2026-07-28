namespace LlrpSdk;

using System.Collections.ObjectModel;
using LlrpNet.Protocol.Messages;
using LlrpNet.Protocol.Parameters;

/// <summary>
/// Represents an antenna transmit power level table entry.
/// </summary>
public sealed record TxPowerEntry(ushort Index, short TransmitPowerValue)
{
    /// <summary>Gets the transmit power formatted in dBm (TransmitPowerValue / 100.0).</summary>
    public double TransmitPowerDbm => TransmitPowerValue / 100.0;
}

/// <summary>
/// Represents a receive sensitivity table entry.
/// </summary>
public sealed record RxSensitivityEntry(ushort Index, short ReceiveSensitivityValue)
{
    /// <summary>Gets the receive sensitivity formatted in dBm (ReceiveSensitivityValue / 100.0).</summary>
    public double ReceiveSensitivityDbm => ReceiveSensitivityValue / 100.0;
}

/// <summary>
/// Represents a frequency hop table entry.
/// </summary>
public sealed record FrequencyHopTableEntry(byte HopTableId, IReadOnlyList<uint> Frequencies);

/// <summary>
/// Represents a C1G2 UHF RF mode table entry.
/// </summary>
public sealed record C1G2RfModeEntry(
    uint ModeIdentifier,
    string DrValue,
    bool EpchagtcConformance,
    byte MValue,
    string ForwardLinkModulation,
    string SpectralMaskIndicator,
    uint BdrValue,
    uint PieValue,
    uint MinTariValue,
    uint MaxTariValue,
    uint StepTariValue);

/// <summary>
/// Represents immutable identity information queried from a connected reader.
/// </summary>
public sealed class ReaderIdentity
{
    internal ReaderIdentity(
        uint manufacturerId,
        uint modelId,
        string firmwareVersion)
    {
        ManufacturerId = manufacturerId;
        ModelId = modelId;
        FirmwareVersion = firmwareVersion?.TrimEnd('\0') ?? string.Empty;
    }

    /// <summary>
    /// Gets the IANA manufacturer identifier reported by GeneralDeviceCapabilities.
    /// </summary>
    public uint ManufacturerId { get; }

    /// <summary>
    /// Gets the manufacturer-defined model identifier.
    /// </summary>
    public uint ModelId { get; }

    /// <summary>
    /// Gets the reader firmware version.
    /// </summary>
    public string FirmwareVersion { get; }
}

/// <summary>
/// Represents immutable normalized capabilities queried from a connected reader.
/// </summary>
public sealed class ReaderCapabilities
{
    private readonly ReadOnlyCollection<ILlrpParameter> _additionalParameters;

    internal ReaderCapabilities(
        ushort maxNumberOfAntennas,
        bool canSetAntennaProperties,
        bool hasUtcClockCapability,
        IEnumerable<ILlrpParameter> generalDeviceParameters,
        ILlrpMessage rawResponse,
        IEnumerable<ILlrpParameter> additionalParameters,
        IEnumerable<TxPowerEntry>? txPowers = null,
        IEnumerable<RxSensitivityEntry>? rxSensitivities = null,
        IEnumerable<uint>? txFrequencies = null,
        IEnumerable<FrequencyHopTableEntry>? hopTables = null,
        IEnumerable<C1G2RfModeEntry>? rfModes = null,
        bool isTagAccessAvailable = true,
        bool isMultiwordBlockWriteAvailable = false,
        bool isMultiwordBlockEraseAvailable = false,
        bool canDoTagInventoryStateAwareSingulation = false)
    {
        ArgumentNullException.ThrowIfNull(generalDeviceParameters);
        ArgumentNullException.ThrowIfNull(rawResponse);
        ArgumentNullException.ThrowIfNull(additionalParameters);

        MaxNumberOfAntennas = maxNumberOfAntennas;
        CanSetAntennaProperties = canSetAntennaProperties;
        HasUtcClockCapability = hasUtcClockCapability;
        GeneralDeviceParameters = Array.AsReadOnly(generalDeviceParameters.ToArray());
        RawResponse = rawResponse;
        _additionalParameters = Array.AsReadOnly(additionalParameters.ToArray());

        TxPowers = Array.AsReadOnly((txPowers ?? Array.Empty<TxPowerEntry>()).ToArray());
        RxSensitivities = Array.AsReadOnly((rxSensitivities ?? Array.Empty<RxSensitivityEntry>()).ToArray());
        TxFrequencies = Array.AsReadOnly((txFrequencies ?? Array.Empty<uint>()).ToArray());
        HopTables = Array.AsReadOnly((hopTables ?? Array.Empty<FrequencyHopTableEntry>()).ToArray());
        RfModes = Array.AsReadOnly((rfModes ?? Array.Empty<C1G2RfModeEntry>()).ToArray());

        IsTagAccessAvailable = isTagAccessAvailable;
        IsMultiwordBlockWriteAvailable = isMultiwordBlockWriteAvailable;
        IsMultiwordBlockEraseAvailable = isMultiwordBlockEraseAvailable;
        CanDoTagInventoryStateAwareSingulation = canDoTagInventoryStateAwareSingulation;
    }

    /// <summary>
    /// Gets the maximum number of antennas reported by the reader.
    /// </summary>
    public ushort MaxNumberOfAntennas { get; }

    /// <summary>
    /// Gets a value indicating whether antenna properties can be configured.
    /// </summary>
    public bool CanSetAntennaProperties { get; }

    /// <summary>
    /// Gets a value indicating whether the reader has a UTC clock.
    /// </summary>
    public bool HasUtcClockCapability { get; }

    /// <summary>
    /// Gets the transmit power table entries reported by the reader.
    /// </summary>
    public IReadOnlyList<TxPowerEntry> TxPowers { get; }

    /// <summary>
    /// Gets the receive sensitivity table entries reported by the reader.
    /// </summary>
    public IReadOnlyList<RxSensitivityEntry> RxSensitivities { get; }

    /// <summary>
    /// Gets the list of transmit frequencies (in kHz) reported by the reader.
    /// </summary>
    public IReadOnlyList<uint> TxFrequencies { get; }

    /// <summary>
    /// Gets the frequency hop tables reported by the reader.
    /// </summary>
    public IReadOnlyList<FrequencyHopTableEntry> HopTables { get; }

    /// <summary>
    /// Gets the C1G2 RF mode table entries supported by the reader.
    /// </summary>
    public IReadOnlyList<C1G2RfModeEntry> RfModes { get; }

    /// <summary>
    /// Gets a value indicating whether C1G2 tag access (AccessSpec) is available.
    /// </summary>
    public bool IsTagAccessAvailable { get; }

    /// <summary>
    /// Gets a value indicating whether C1G2 Multiword BlockWrite is supported.
    /// </summary>
    public bool IsMultiwordBlockWriteAvailable { get; }

    /// <summary>
    /// Gets a value indicating whether C1G2 BlockErase is supported.
    /// </summary>
    public bool IsMultiwordBlockEraseAvailable { get; }

    /// <summary>
    /// Gets a value indicating whether inventory state-aware singulation is supported.
    /// </summary>
    public bool CanDoTagInventoryStateAwareSingulation { get; }

    /// <summary>
    /// Gets unnormalized parameters nested inside GeneralDeviceCapabilities in wire order.
    /// </summary>
    public IReadOnlyList<ILlrpParameter> GeneralDeviceParameters { get; }

    /// <summary>
    /// Gets top-level capability parameters other than the mapped GeneralDeviceCapabilities parameter.
    /// </summary>
    public IReadOnlyList<ILlrpParameter> AdditionalParameters => _additionalParameters;

    /// <summary>
    /// Gets the immutable decoded response retained for forward-compatible access to all capability data.
    /// </summary>
    public ILlrpMessage RawResponse { get; }
}

/// <summary>Internal normalized metadata returned by a version-specific protocol adapter.</summary>
internal sealed record ReaderMetadataSnapshot(
    ReaderIdentity Identity,
    ReaderCapabilities Capabilities);
