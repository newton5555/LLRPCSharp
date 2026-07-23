using System.Collections.ObjectModel;

namespace LlrpNet.Protocol.Parameters.V1_0_1;

/// <summary>
/// Describes the general device capabilities reported by an LLRP 1.0.1 reader.
/// </summary>
public sealed class GeneralDeviceCapabilities : ILlrpParameter
{
    /// <summary>
    /// The LLRP TLV parameter type.
    /// </summary>
    public const ushort ParameterType = 137;

    private readonly ReadOnlyCollection<ILlrpParameter> _parameters;

    /// <summary>
    /// Initializes a new instance of the <see cref="GeneralDeviceCapabilities"/> class.
    /// </summary>
    /// <param name="maxNumberOfAntennaSupported">The largest number of antennas supported by the reader.</param>
    /// <param name="canSetAntennaProperties">Whether antenna properties can be configured.</param>
    /// <param name="hasUtcClockCapability">Whether the reader has a UTC clock.</param>
    /// <param name="deviceManufacturerName">The IANA manufacturer identifier.</param>
    /// <param name="modelName">The manufacturer-defined model identifier.</param>
    /// <param name="readerFirmwareVersion">The reader firmware version.</param>
    /// <param name="parameters">
    /// Required and optional nested capability parameters in schema order.
    /// </param>
    public GeneralDeviceCapabilities(
        ushort maxNumberOfAntennaSupported,
        bool canSetAntennaProperties,
        bool hasUtcClockCapability,
        uint deviceManufacturerName,
        uint modelName,
        string readerFirmwareVersion,
        IEnumerable<ILlrpParameter>? parameters = null)
    {
        ArgumentNullException.ThrowIfNull(readerFirmwareVersion);
        ILlrpParameter[] parameterArray = parameters?.ToArray() ?? [];
        if (parameterArray.Any(static parameter => parameter is null))
        {
            throw new ArgumentException("A trailing parameter collection cannot contain null entries.", nameof(parameters));
        }

        MaxNumberOfAntennaSupported = maxNumberOfAntennaSupported;
        CanSetAntennaProperties = canSetAntennaProperties;
        HasUtcClockCapability = hasUtcClockCapability;
        DeviceManufacturerName = deviceManufacturerName;
        ModelName = modelName;
        ReaderFirmwareVersion = readerFirmwareVersion;
        _parameters = Array.AsReadOnly(parameterArray);
    }

    /// <summary>
    /// Gets the largest number of antennas supported by the reader.
    /// </summary>
    public ushort MaxNumberOfAntennaSupported { get; }

    /// <summary>
    /// Gets a value indicating whether antenna properties can be configured.
    /// </summary>
    public bool CanSetAntennaProperties { get; }

    /// <summary>
    /// Gets a value indicating whether the reader has a UTC clock.
    /// </summary>
    public bool HasUtcClockCapability { get; }

    /// <summary>
    /// Gets the IANA manufacturer identifier.
    /// </summary>
    public uint DeviceManufacturerName { get; }

    /// <summary>
    /// Gets the manufacturer-defined model identifier.
    /// </summary>
    public uint ModelName { get; }

    /// <summary>
    /// Gets the reader firmware version.
    /// </summary>
    public string ReaderFirmwareVersion { get; }

    /// <summary>
    /// Gets nested capability parameters in their supplied order.
    /// The LLRP 1.0.1 codec enforces the required schema order and cardinalities when encoding.
    /// </summary>
    public IReadOnlyList<ILlrpParameter> Parameters => _parameters;
}
