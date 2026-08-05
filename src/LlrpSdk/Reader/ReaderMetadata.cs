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
/// <remarks>
/// Per LLRP 1.0.1/1.1, <see cref="ReceiveSensitivityValue"/> is the receive sensitivity expressed as
/// an integer dB offset relative to the reader's maximum sensitivity: 0 dB means the most sensitive
/// setting, and larger values are proportionally less sensitive. It is <b>not</b> an absolute dBm
/// value and it is <b>not</b> scaled by 100.
/// To compute an absolute sensitivity in dBm, add the offset to
/// <see cref="ReaderCapabilities.MaximumReceiveSensitivityDbm"/> (LLRP 1.1 only); LLRP 1.0.1 does
/// not advertise an absolute maximum, so the offset table is all that is available.
/// </remarks>
public sealed record RxSensitivityEntry(ushort Index, short ReceiveSensitivityValue)
{
    /// <summary>Gets the receive sensitivity as an integer dB offset relative to the reader's maximum sensitivity (0 = most sensitive).</summary>
    public int ReceiveSensitivityDb => ReceiveSensitivityValue;
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
        bool canDoTagInventoryStateAwareSingulation = false,
        short? maximumReceiveSensitivityDbm = null)
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
        MaximumReceiveSensitivityDbm = maximumReceiveSensitivityDbm;
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
    /// Gets the reader's maximum receive sensitivity in absolute dBm — the raw LLRP 1.1
    /// <c>MaximumReceiveSensitivity.MaximumSensitivityValue</c> wire value, or
    /// <see langword="null"/> on LLRP 1.0.1, which does not advertise this parameter.
    /// The value is an integer dBm, not scaled by 100. Combine it with each
    /// <see cref="RxSensitivityEntry.ReceiveSensitivityValue"/> dB offset to obtain an absolute
    /// sensitivity in dBm: <c>MaximumReceiveSensitivityDbm + offset</c>.
    /// </summary>
    public short? MaximumReceiveSensitivityDbm { get; }

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
