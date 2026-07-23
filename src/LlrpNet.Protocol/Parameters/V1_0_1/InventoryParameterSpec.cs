using System.Collections.ObjectModel;

namespace LlrpNet.Protocol.Parameters.V1_0_1;

/// <summary>
/// Defines an air-protocol inventory operation for one LLRP 1.0.1 AISpec.
/// </summary>
public sealed class InventoryParameterSpec : ILlrpParameter
{
    /// <summary>The LLRP TLV parameter type.</summary>
    public const ushort ParameterType = 186;

    private readonly ReadOnlyCollection<ILlrpParameter> _antennaConfigurations;
    private readonly ReadOnlyCollection<ILlrpParameter> _customParameters;

    /// <summary>
    /// Initializes an inventory parameter specification.
    /// </summary>
    /// <param name="inventoryParameterSpecId">The inventory specification identifier.</param>
    /// <param name="protocolId">The air protocol identifier.</param>
    /// <param name="antennaConfigurations">Optional AntennaConfiguration parameters (type 222).</param>
    /// <param name="customParameters">Optional trailing Custom parameters.</param>
    public InventoryParameterSpec(
        ushort inventoryParameterSpecId,
        AirProtocolId protocolId,
        IEnumerable<ILlrpParameter>? antennaConfigurations = null,
        IEnumerable<ILlrpParameter>? customParameters = null)
    {
        if (!IsDefined(protocolId))
        {
            throw new ArgumentOutOfRangeException(
                nameof(protocolId),
                protocolId,
                "The air protocol identifier must be defined by LLRP 1.0.1.");
        }

        ILlrpParameter[] antennaArray = antennaConfigurations?.ToArray() ?? [];
        if (antennaArray.Any(static parameter => parameter is null))
        {
            throw new ArgumentException(
                "An antenna configuration collection cannot contain null entries.",
                nameof(antennaConfigurations));
        }

        ILlrpParameter[] customArray = customParameters?.ToArray() ?? [];
        if (customArray.Any(static parameter => parameter is null))
        {
            throw new ArgumentException(
                "An inventory Custom collection cannot contain null entries.",
                nameof(customParameters));
        }

        InventoryParameterSpecId = inventoryParameterSpecId;
        ProtocolId = protocolId;
        _antennaConfigurations = Array.AsReadOnly(antennaArray);
        _customParameters = Array.AsReadOnly(customArray);
    }

    /// <summary>Gets the inventory specification identifier.</summary>
    public ushort InventoryParameterSpecId { get; }

    /// <summary>Gets the air protocol identifier.</summary>
    public AirProtocolId ProtocolId { get; }

    /// <summary>Gets AntennaConfiguration parameters in wire order.</summary>
    public IReadOnlyList<ILlrpParameter> AntennaConfigurations => _antennaConfigurations;

    /// <summary>Gets trailing Custom parameters in wire order.</summary>
    public IReadOnlyList<ILlrpParameter> CustomParameters => _customParameters;

    internal static bool IsDefined(AirProtocolId value)
    {
        return value is >= AirProtocolId.Unspecified and <= AirProtocolId.EpcGlobalClass1Gen2;
    }
}
