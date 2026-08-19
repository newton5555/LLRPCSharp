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
        ReaderSettingsDefaults generic = ReaderSettingsDefaults.CreateForReader(context);
        if (generic.Settings.Inventory is null)
        {
            return generic with
            {
                ProfileId = "seuic.uf40.llrp-1.0.1",
                Source = ReaderSettingsDefaultSource.ReaderProfile,
                Notes = generic.Notes.Append("Seuic UF40 profile could not resolve an inventory because the reader advertised no antennas.").ToArray(),
            };
        }

        return new ReaderSettingsDefaults
        {
            ProfileId = "seuic.uf40.llrp-1.0.1",
            Source = ReaderSettingsDefaultSource.ReaderProfile,
            Notes = generic.Notes.Concat(
            [
                "Resolved all installed antennas and explicit standard AISpec RF values from this reader's capabilities.",
                "Transmit power uses the highest advertised power value; receive sensitivity uses the first advertised index."
            ]).ToArray(),
            Settings = generic.Settings,
        };
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
