using LlrpSdk.Extensions.Impinj.Enumerations.V1_0_1;
using LlrpSdk.Extensions.Impinj.Parameters.V1_0_1;
using LlrpSdk.Extensions;
using LlrpNet.Protocol.Parameters;

namespace LlrpSdk.Extensions.Impinj;

/// <summary>Configures Impinj inventory-command behavior not represented by standard LLRP settings.</summary>
public sealed record ImpinjInventoryControlOptions
{
    public const string ExtensionKey = "impinj.inventoryControl";

    /// <summary>
    /// Gets whether the reader derives the initial population estimate from preceding inventory rounds.
    /// </summary>
    public bool? EnableTagPopulationEstimation { get; init; }
}

/// <summary>Compiles validated inventory controls to Impinj custom parameters.</summary>
public static class ImpinjInventoryControlConfigurator
{
    public static IReadOnlyList<ILlrpParameter> BuildCustomItems(
        ReaderExtensionMatchContext reader,
        ImpinjInventoryControlOptions options)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(options);
        if (options.EnableTagPopulationEstimation is not { } enabled)
        {
            return [];
        }

        ImpinjInventoryCapabilities capabilities = ImpinjInventoryCapabilityCatalog.Get(reader);
        if (!capabilities.SupportsTagPopulationEstimation)
        {
            throw new NotSupportedException(
                $"Impinj tag population estimation is unavailable: {capabilities.Reason}");
        }

        return
        [
            new ImpinjEnableTagPopulationEstimationAlgorithm(
                enabled ? ImpinjTagPopulationEstimationMode.Enabled : ImpinjTagPopulationEstimationMode.Disabled,
                [])
        ];
    }
}
