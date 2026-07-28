using LlrpNet.Core.Protocol;
using LlrpSdk.Extensions;

namespace LlrpSdk.Extensions.Seuic;

/// <summary>Provides standard LLRP inventory compatibility defaults for known Seuic readers.</summary>
public sealed class SeuicReaderExtension : IReaderExtension, IInventoryProfileContributor
{
    public const uint ManufacturerId = 57690;
    public const uint Uf40ModelId = 40;
    public static SeuicReaderExtension Instance { get; } = new();

    public string Id => "seuic-reader-llrp-1.0.1";
    public string? MutualExclusionGroup => "reader-vendor";

    public bool Matches(ReaderExtensionMatchContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.ManufacturerId == ManufacturerId &&
            context.ModelId == Uf40ModelId &&
            context.ProtocolVersion == LlrpProtocolVersion.Version101;
    }

    public InventoryCompilationDefaults? GetCompilationDefaults(InventoryContributionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ReaderCapabilities capabilities = context.Capabilities;
        ushort[] antennas = Enumerable.Range(1, capabilities.MaxNumberOfAntennas)
            .Select(static antenna => (ushort)antenna)
            .ToArray();
        TxPowerEntry? maximumTxPower = capabilities.TxPowers.OrderBy(static entry => entry.Index).LastOrDefault();
        RxSensitivityEntry? defaultRxSensitivity = capabilities.RxSensitivities.FirstOrDefault(static entry => entry.Index == 1)
            ?? capabilities.RxSensitivities.OrderBy(static entry => entry.Index).FirstOrDefault();

        return antennas.Length == 0 || maximumTxPower is null || defaultRxSensitivity is null
            ? null
            : new InventoryCompilationDefaults(antennas, defaultRxSensitivity.Index, maximumTxPower.Index, 1, 1);
    }
}

/// <summary>Registers the Seuic standard-inventory compatibility extension.</summary>
public static class SeuicLlrpReaderBuilderExtensions
{
    public static LlrpReaderBuilder UseSeuic(this LlrpReaderBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.UseReaderExtension(SeuicReaderExtension.Instance);
    }
}
