namespace LlrpSdk;

using System.Collections.ObjectModel;
using LlrpNet.Protocol.Messages.V1_0_1;
using LlrpNet.Protocol.Parameters;
using LlrpNet.Protocol.Parameters.V1_0_1;

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
        FirmwareVersion = firmwareVersion;
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
        GeneralDeviceCapabilities generalDeviceCapabilities,
        GetReaderCapabilitiesResponse rawResponse)
    {
        ArgumentNullException.ThrowIfNull(generalDeviceCapabilities);
        ArgumentNullException.ThrowIfNull(rawResponse);

        MaxNumberOfAntennas = generalDeviceCapabilities.MaxNumberOfAntennaSupported;
        CanSetAntennaProperties = generalDeviceCapabilities.CanSetAntennaProperties;
        HasUtcClockCapability = generalDeviceCapabilities.HasUTCClockCapability;
        ILlrpParameter[] generalDeviceParams =
        [
            .. generalDeviceCapabilities.ReceiveSensitivityTableEntryItems,
            .. generalDeviceCapabilities.PerAntennaReceiveSensitivityRangeItems,
            generalDeviceCapabilities.GPIOCapabilities,
            .. generalDeviceCapabilities.PerAntennaAirProtocolItems,
        ];
        GeneralDeviceParameters = Array.AsReadOnly(generalDeviceParams);
        RawResponse = rawResponse;
        _additionalParameters = Array.AsReadOnly(
            rawResponse.CustomItems
                .Where(parameter => !ReferenceEquals(parameter, generalDeviceCapabilities))
                .ToArray());
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
    public GetReaderCapabilitiesResponse RawResponse { get; }
}
