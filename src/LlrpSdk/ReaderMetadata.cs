namespace LlrpSdk;

using System.Collections.ObjectModel;
using LlrpNet.Protocol.Messages;
using LlrpNet.Protocol.Parameters;

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
        ushort maxNumberOfAntennas,
        bool canSetAntennaProperties,
        bool hasUtcClockCapability,
        IEnumerable<ILlrpParameter> generalDeviceParameters,
        ILlrpMessage rawResponse,
        IEnumerable<ILlrpParameter> additionalParameters)
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
    public ILlrpMessage RawResponse { get; }
}

/// <summary>Internal normalized metadata returned by a version-specific protocol adapter.</summary>
internal sealed record ReaderMetadataSnapshot(
    ReaderIdentity Identity,
    ReaderCapabilities Capabilities);
