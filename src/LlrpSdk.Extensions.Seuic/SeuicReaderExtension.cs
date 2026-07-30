using LlrpNet.Core.Protocol;

namespace LlrpSdk.Extensions.Seuic;

/// <summary>Provides standard LLRP inventory compatibility defaults for known Seuic readers.</summary>
public sealed class SeuicReaderExtension :
    IReaderExtension,
    IReaderSettingsDefaultsContributor
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

    /// <inheritdoc />
    public ReaderSettingsDefaults? GetDefaultSettings(ReaderSettingsDefaultContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        IReadOnlyList<InventoryAntennaConfiguration>? antennaConfigurations = ResolveAntennaConfigurations(context.Capabilities);
        if (antennaConfigurations is null)
        {
            return null;
        }
        return new ReaderSettingsDefaults
        {
            ProfileId = "seuic.uf40.llrp-1.0.1",
            Source = ReaderSettingsDefaultSource.ReaderProfile,
            Notes =
            [
                "Resolved all installed antennas and explicit standard AISpec RF values from this reader's capabilities.",
                "Transmit power uses the highest advertised index; receive sensitivity prefers index 1 and otherwise the lowest advertised index."
            ],
            Settings = new ReaderSettings
            {
                Inventory = new InventorySettings
                {
                    AntennaIds = antennaConfigurations.Select(static configuration => configuration.AntennaId).ToArray(),
                    AntennaConfigurations = antennaConfigurations
                }
            }
        };
    }

    private static IReadOnlyList<InventoryAntennaConfiguration>? ResolveAntennaConfigurations(ReaderCapabilities capabilities)
    {
        ushort[] antennas = Enumerable.Range(1, capabilities.MaxNumberOfAntennas)
            .Select(static antenna => (ushort)antenna)
            .ToArray();
        TxPowerEntry? maximumTxPower = capabilities.TxPowers.OrderBy(static entry => entry.Index).LastOrDefault();
        RxSensitivityEntry? defaultRxSensitivity = capabilities.RxSensitivities.FirstOrDefault(static entry => entry.Index == 1)
            ?? capabilities.RxSensitivities.OrderBy(static entry => entry.Index).FirstOrDefault();

        return antennas.Length == 0 || maximumTxPower is null || defaultRxSensitivity is null
            ? null
            : antennas.Select(antennaId => new InventoryAntennaConfiguration
            {
                AntennaId = antennaId,
                ReceiverSensitivityIndex = defaultRxSensitivity.Index,
                TransmitPowerIndex = maximumTxPower.Index,
                HopTableId = 1,
                ChannelIndex = 1,
            }).ToArray();
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
