using LlrpNet.Protocol.Impinj.Enumerations.V1_0_1;
using LlrpNet.Protocol.Impinj.Parameters.V1_0_1;
using LlrpSdk.Extensions;
using LlrpNet.Protocol.Parameters;

namespace LlrpSdk.Extensions.Impinj;

/// <summary>Configures Impinj inventory-command behavior not represented by standard LLRP settings.</summary>
public sealed record ImpinjInventoryControlOptions
{
    public const string ExtensionKey = "impinj.inventoryControl";

    /// <summary>
    /// Gets the fixed-frequency channel selection applied by the C1G2 inventory command.
    /// </summary>
    public ImpinjFixedFrequencySettings? FixedFrequency { get; init; }

    /// <summary>
    /// Gets the reduced-power channel list applied by the C1G2 inventory command.
    /// </summary>
    public ImpinjReducedPowerFrequencySettings? ReducedPowerFrequency { get; init; }

    /// <summary>
    /// Gets the low duty-cycle behavior applied by the C1G2 inventory command.
    /// </summary>
    public ImpinjLowDutyCycleSettings? LowDutyCycle { get; init; }

    /// <summary>
    /// Gets the inventory search mode used by the C1G2 inventory command.
    /// </summary>
    public ImpinjInventorySearchType? InventorySearchMode { get; init; }

    /// <summary>
    /// Gets whether the reader derives the initial population estimate from preceding inventory rounds.
    /// </summary>
    public bool? EnableTagPopulationEstimation { get; init; }

    /// <summary>Gets the reader-side verification mode used for standard Gen2 select filters.</summary>
    public ImpinjTagFilterVerificationMode? TagFilterVerificationMode { get; init; }

    /// <summary>Gets the truncated EPC-reply configuration, or <see langword="null"/> to leave it disabled.</summary>
    public ImpinjTruncatedReplyOptions? TruncatedReply { get; init; }

    /// <summary>Gets the Gen2X inventory reply configuration.</summary>
    public ImpinjGen2XInventoryOptions? Gen2XInventory { get; init; }

    /// <summary>Gets the Gen2X application-ID selection configuration.</summary>
    public ImpinjGen2XTagSelectionOptions? Gen2XTagSelection { get; init; }

    /// <summary>Gets the Endpoint IC verification behavior.</summary>
    public ImpinjEndpointICVerificationMode? EndpointIcVerificationMode { get; init; }

    /// <summary>Gets the Gen2v3 ramp-up power boost behavior.</summary>
    public ImpinjRampUpPowerBoostMode? RampUpPowerBoostMode { get; init; }

    /// <summary>
    /// Gets whether an explicitly requested feature may be sent when the SDK has no verified capability profile for
    /// the connected model and firmware. The reader remains authoritative and may reject the ROSpec.
    /// </summary>
    public bool AllowUnverifiedFeatures { get; init; }
}

/// <summary>Controls Impinj truncated EPC replies.</summary>
/// <param name="Gen2v2TagsOnly">Whether tags that do not support Gen2v2 must be ignored.</param>
/// <param name="EpcLengthWords">Expected EPC length in 16-bit words.</param>
/// <param name="BitPointer">Starting bit in EPC memory used by the truncating select.</param>
/// <param name="TagMaskHex">Optional hexadecimal mask; a non-empty value cannot be combined with standard filters.</param>
public sealed record ImpinjTruncatedReplyOptions(
    bool Gen2v2TagsOnly,
    byte EpcLengthWords,
    ushort BitPointer,
    string TagMaskHex = "");

/// <summary>Controls the data returned by an Impinj Gen2X inventory reply.</summary>
public sealed record ImpinjGen2XInventoryOptions(
    ImpinjGen2XCR Cr,
    ImpinjGen2XID Id,
    ImpinjGen2XProtection Protection);

/// <summary>Selects Gen2X tags by their 8-, 16-, or 24-bit application identifier.</summary>
/// <param name="ApplicationIdHex">Two, four, or six hexadecimal characters.</param>
/// <param name="EpcLengthInBits">Required when Gen2X inventory uses the <c>Part</c> ID mode.</param>
/// <param name="TBit">Required T-bit state when <paramref name="EpcLengthInBits"/> is present.</param>
public sealed record ImpinjGen2XTagSelectionOptions(
    string ApplicationIdHex,
    ushort? EpcLengthInBits = null,
    bool TBit = false);

/// <summary>Compiles validated inventory controls to Impinj custom parameters.</summary>
public static class ImpinjInventoryControlConfigurator
{
    public static IReadOnlyList<ILlrpParameter> BuildCustomItems(
        ReaderExtensionMatchContext reader,
        ImpinjInventoryControlOptions options,
        int standardFilterCount = 0)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfNegative(standardFilterCount);

        bool hasControls = options.FixedFrequency is not null ||
            options.ReducedPowerFrequency is not null ||
            options.LowDutyCycle is not null ||
            options.InventorySearchMode is not null ||
            options.EnableTagPopulationEstimation is not null ||
            options.TagFilterVerificationMode is not null ||
            options.TruncatedReply is not null ||
            options.Gen2XInventory is not null ||
            options.Gen2XTagSelection is not null ||
            options.EndpointIcVerificationMode is not null ||
            options.RampUpPowerBoostMode is not null;
        if (!hasControls)
        {
            return [];
        }

        if (reader.ManufacturerId != ImpinjReaderExtension.ManufacturerId ||
            reader.ProtocolVersion != LlrpNet.Core.Protocol.LlrpProtocolVersion.Version101)
        {
            throw new NotSupportedException("Impinj inventory controls require an Impinj reader using LLRP 1.0.1.");
        }

        ImpinjInventoryCapabilities capabilities = ImpinjInventoryCapabilityCatalog.Get(reader);
        EnsureSupported(
            options.EnableTagPopulationEstimation is not null,
            capabilities.SupportsTagPopulationEstimation,
            "tag population estimation",
            options.AllowUnverifiedFeatures,
            capabilities.Reason);
        EnsureSupported(
            options.TagFilterVerificationMode is not null,
            capabilities.SupportsTagFilterVerification,
            "tag-filter verification",
            options.AllowUnverifiedFeatures,
            capabilities.Reason);
        EnsureSupported(
            options.TruncatedReply is not null,
            capabilities.SupportsTruncatedReply,
            "truncated reply",
            options.AllowUnverifiedFeatures,
            capabilities.Reason);
        EnsureSupported(
            options.Gen2XInventory is not null || options.Gen2XTagSelection is not null,
            capabilities.SupportsGen2X,
            "Gen2X inventory",
            options.AllowUnverifiedFeatures,
            capabilities.Reason);
        EnsureSupported(
            options.EndpointIcVerificationMode is not null,
            capabilities.SupportsEndpointIcVerification,
            "Endpoint IC verification",
            options.AllowUnverifiedFeatures,
            capabilities.Reason);
        EnsureSupported(
            options.RampUpPowerBoostMode is not null,
            capabilities.SupportsRampUpPowerBoost,
            "ramp-up power boost",
            options.AllowUnverifiedFeatures,
            capabilities.Reason);

        var items = new List<ILlrpParameter>();
        if (options.FixedFrequency is { } fixedFrequency)
        {
            items.Add(new ImpinjFixedFrequencyList(fixedFrequency.Mode, fixedFrequency.ChannelList, []));
        }
        if (options.ReducedPowerFrequency is { } reducedPower)
        {
            items.Add(new ImpinjReducedPowerFrequencyList(reducedPower.Mode, reducedPower.ChannelList, []));
        }
        if (options.LowDutyCycle is { } lowDutyCycle)
        {
            items.Add(new ImpinjLowDutyCycle(
                lowDutyCycle.Mode,
                lowDutyCycle.EmptyFieldTimeoutMilliseconds,
                lowDutyCycle.FieldPingIntervalMilliseconds,
                []));
        }
        if (options.InventorySearchMode is { } searchMode)
        {
            items.Add(new ImpinjInventorySearchMode(searchMode, []));
        }
        if (options.EnableTagPopulationEstimation is { } enabled)
        {
            items.Add(new ImpinjEnableTagPopulationEstimationAlgorithm(
                enabled ? ImpinjTagPopulationEstimationMode.Enabled : ImpinjTagPopulationEstimationMode.Disabled,
                []));
        }
        if (options.TagFilterVerificationMode is { } verificationMode)
        {
            items.Add(new ImpinjTagFilterVerificationConfiguration(verificationMode, []));
        }
        if (options.TruncatedReply is { } truncated)
        {
            if (truncated.EpcLengthWords == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(options), "Truncated reply EPC length must be at least one word.");
            }
            string mask = truncated.TagMaskHex?.Trim() ?? string.Empty;
            if (mask.Length != 0 && standardFilterCount != 0)
            {
                throw new ArgumentException(
                    "Impinj truncated reply cannot use a non-empty tag mask together with standard inventory filters.",
                    nameof(options));
            }
            items.Add(new ImpinjTruncatedReplyConfiguration(
                truncated.Gen2v2TagsOnly,
                truncated.EpcLengthWords,
                truncated.BitPointer,
                ImpinjBitEncoding.FromHex(mask, nameof(ImpinjTruncatedReplyOptions.TagMaskHex)),
                []));
        }
        if (options.Gen2XInventory is { } gen2X)
        {
            items.Add(new ImpinjGen2XInventoryConfig(gen2X.Cr, gen2X.Id, gen2X.Protection, []));
        }
        if (options.Gen2XTagSelection is { } selection)
        {
            string appId = selection.ApplicationIdHex?.Trim() ?? string.Empty;
            if (appId.Length is not (2 or 4 or 6))
            {
                throw new ArgumentException("A Gen2X application ID must contain 2, 4, or 6 hexadecimal characters.", nameof(options));
            }
            if (options.Gen2XInventory?.Id == ImpinjGen2XID.Part && selection.EpcLengthInBits is null)
            {
                throw new ArgumentException("Gen2X Part ID mode requires an EPC length.", nameof(options));
            }

            IReadOnlyList<ILlrpParameter> nested = selection.EpcLengthInBits is { } epcLength
                ? [new ImpinjGen2XTagSelectionEpcLength(epcLength, selection.TBit, [])]
                : [];
            items.Add(new ImpinjGen2XTagSelectionConfig(
                ImpinjBitEncoding.FromHex(appId, nameof(ImpinjGen2XTagSelectionOptions.ApplicationIdHex)),
                nested));
        }
        if (options.EndpointIcVerificationMode is { } endpointMode)
        {
            items.Add(new ImpinjEndpointICVerificationConfig(endpointMode, []));
        }
        if (options.RampUpPowerBoostMode is { } boostMode)
        {
            items.Add(new ImpinjRampUpPowerBoost(boostMode));
        }

        return items;
    }

    private static void EnsureSupported(
        bool requested,
        bool supported,
        string feature,
        bool allowUnverified,
        string reason)
    {
        if (requested && !supported && !allowUnverified)
        {
            throw new NotSupportedException(
                $"Impinj {feature} is unavailable or unverified: {reason} " +
                $"Set {nameof(ImpinjInventoryControlOptions.AllowUnverifiedFeatures)} to true to let the reader decide.");
        }
    }
}

internal static class ImpinjBitEncoding
{
    public static IReadOnlyList<bool> FromHex(string value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value);
        var bits = new bool[checked(value.Length * 4)];
        for (int index = 0; index < value.Length; index++)
        {
            int nibble = value[index] switch
            {
                >= '0' and <= '9' => value[index] - '0',
                >= 'a' and <= 'f' => value[index] - 'a' + 10,
                >= 'A' and <= 'F' => value[index] - 'A' + 10,
                _ => throw new ArgumentException("The value must contain hexadecimal characters only.", parameterName),
            };
            for (int bit = 0; bit < 4; bit++)
            {
                bits[(index * 4) + bit] = (nibble & (1 << (3 - bit))) != 0;
            }
        }
        return bits;
    }

    public static string ToHex(IReadOnlyList<bool> bits, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(bits);
        if (bits.Count % 4 != 0)
        {
            throw new InvalidOperationException(
                $"Impinj parameter '{parameterName}' contains {bits.Count} bits and cannot be represented losslessly as hexadecimal text.");
        }

        const string digits = "0123456789ABCDEF";
        var characters = new char[bits.Count / 4];
        for (int index = 0; index < characters.Length; index++)
        {
            int nibble = 0;
            for (int bit = 0; bit < 4; bit++)
            {
                if (bits[(index * 4) + bit])
                {
                    nibble |= 1 << (3 - bit);
                }
            }
            characters[index] = digits[nibble];
        }
        return new string(characters);
    }
}
