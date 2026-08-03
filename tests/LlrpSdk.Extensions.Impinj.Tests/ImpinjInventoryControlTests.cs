using LlrpNet.Core.Protocol;
using LlrpSdk.Extensions;
using LlrpSdk.Extensions.Impinj.Enumerations.V1_0_1;
using LlrpSdk.Extensions.Impinj.Parameters.V1_0_1;

namespace LlrpSdk.Extensions.Impinj.Tests;

public sealed class ImpinjInventoryControlTests
{
    private static readonly ReaderExtensionMatchContext UnknownImpinjReader = new(
        ImpinjReaderExtension.ManufacturerId,
        9_999_999,
        "10.3.0.240",
        LlrpProtocolVersion.Version101);

    [Fact]
    public void BuildCustomItems_CompilesAllRepresentedControlsWithExplicitOverride()
    {
        var options = new ImpinjInventoryControlOptions
        {
            EnableTagPopulationEstimation = true,
            TagFilterVerificationMode = ImpinjTagFilterVerificationMode.Passive,
            TruncatedReply = new ImpinjTruncatedReplyOptions(true, 6, 32, "ABCD"),
            Gen2XInventory = new ImpinjGen2XInventoryOptions(
                ImpinjGen2XCR.ID16,
                ImpinjGen2XID.Part,
                ImpinjGen2XProtection.CRC5),
            Gen2XTagSelection = new ImpinjGen2XTagSelectionOptions("A1B2C3", 96, true),
            EndpointIcVerificationMode = ImpinjEndpointICVerificationMode.Enabled,
            RampUpPowerBoostMode = ImpinjRampUpPowerBoostMode.Auto,
            AllowUnverifiedFeatures = true,
        };

        IReadOnlyList<LlrpNet.Protocol.Parameters.ILlrpParameter> items =
            ImpinjInventoryControlConfigurator.BuildCustomItems(UnknownImpinjReader, options);

        Assert.Contains(items, static item => item is ImpinjEnableTagPopulationEstimationAlgorithm);
        Assert.Contains(items, static item => item is ImpinjTagFilterVerificationConfiguration);
        var truncated = Assert.IsType<ImpinjTruncatedReplyConfiguration>(
            Assert.Single(items, static item => item is ImpinjTruncatedReplyConfiguration));
        Assert.Equal("ABCD", BitsToHex(truncated.TagMask));
        var selection = Assert.IsType<ImpinjGen2XTagSelectionConfig>(
            Assert.Single(items, static item => item is ImpinjGen2XTagSelectionConfig));
        Assert.Equal("A1B2C3", BitsToHex(selection.AppID));
        var epcLength = Assert.IsType<ImpinjGen2XTagSelectionEpcLength>(Assert.Single(selection.CustomItems));
        Assert.Equal((ushort)96, epcLength.EpcLengthInBits);
        Assert.True(epcLength.TBit);
        Assert.Contains(items, static item => item is ImpinjEndpointICVerificationConfig);
        Assert.Contains(items, static item => item is ImpinjRampUpPowerBoost);
    }

    [Fact]
    public void BuildCustomItems_RejectsTruncatedMaskCombinedWithStandardFilters()
    {
        var options = new ImpinjInventoryControlOptions
        {
            TruncatedReply = new ImpinjTruncatedReplyOptions(false, 6, 32, "AB"),
            AllowUnverifiedFeatures = true,
        };

        ArgumentException error = Assert.Throws<ArgumentException>(() =>
            ImpinjInventoryControlConfigurator.BuildCustomItems(UnknownImpinjReader, options, standardFilterCount: 1));

        Assert.Contains("standard inventory filters", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ContributeQuery_RestoresAllKnownInventoryControlsLosslessly()
    {
        var identity = new ReaderIdentity(
            ImpinjReaderExtension.ManufacturerId,
            9_999_999,
            "10.3.0.240");
        var commandItems = ImpinjInventoryControlConfigurator.BuildCustomItems(
            UnknownImpinjReader,
            new ImpinjInventoryControlOptions
            {
                TagFilterVerificationMode = ImpinjTagFilterVerificationMode.Active,
                TruncatedReply = new ImpinjTruncatedReplyOptions(true, 8, 48, "CAFE"),
                Gen2XInventory = new ImpinjGen2XInventoryOptions(
                    ImpinjGen2XCR.Stored_CRC,
                    ImpinjGen2XID.Part,
                    ImpinjGen2XProtection.CRC5_Plus),
                Gen2XTagSelection = new ImpinjGen2XTagSelectionOptions("010203", 128, false),
                EndpointIcVerificationMode = ImpinjEndpointICVerificationMode.Enabled,
                RampUpPowerBoostMode = ImpinjRampUpPowerBoostMode.On,
                AllowUnverifiedFeatures = true,
            });
        var context = new InventorySettingsContributionContext(
            identity,
            null!,
            LlrpProtocolVersion.Version101,
            [],
            commandItems);
        var extensions = new InventorySettingsExtensionBuilder();

        ImpinjReaderExtension.Instance.ContributeQuery(context, extensions);

        var restored = Assert.IsType<ImpinjInventoryControlOptions>(
            extensions.Build()[ImpinjInventoryControlOptions.ExtensionKey]);
        Assert.Equal(ImpinjTagFilterVerificationMode.Active, restored.TagFilterVerificationMode);
        Assert.Equal("CAFE", restored.TruncatedReply?.TagMaskHex);
        Assert.Equal(ImpinjGen2XID.Part, restored.Gen2XInventory?.Id);
        Assert.Equal("010203", restored.Gen2XTagSelection?.ApplicationIdHex);
        Assert.Equal((ushort)128, restored.Gen2XTagSelection?.EpcLengthInBits);
        Assert.Equal(ImpinjEndpointICVerificationMode.Enabled, restored.EndpointIcVerificationMode);
        Assert.Equal(ImpinjRampUpPowerBoostMode.On, restored.RampUpPowerBoostMode);
        Assert.True(restored.AllowUnverifiedFeatures);
    }

    [Fact]
    public void ReaderSettingsSerializer_RoundTripsInventoryControls()
    {
        var settings = new ReaderSettings
        {
            Inventory = new InventorySettings
            {
                Extensions = new Dictionary<string, object?>
                {
                    [ImpinjInventoryControlOptions.ExtensionKey] = new ImpinjInventoryControlOptions
                    {
                        TagFilterVerificationMode = ImpinjTagFilterVerificationMode.Passive,
                        Gen2XTagSelection = new ImpinjGen2XTagSelectionOptions("ABCD"),
                        AllowUnverifiedFeatures = true,
                    },
                },
            },
        };

        string json = ReaderSettingsSerializer.SerializeToJson(settings, [ImpinjReaderExtension.Instance]);
        ReaderSettings restored = ReaderSettingsSerializer.DeserializeFromJson(json, [ImpinjReaderExtension.Instance]);

        var controls = Assert.IsType<ImpinjInventoryControlOptions>(
            restored.Inventory!.Extensions[ImpinjInventoryControlOptions.ExtensionKey]);
        Assert.Equal(ImpinjTagFilterVerificationMode.Passive, controls.TagFilterVerificationMode);
        Assert.Equal("ABCD", controls.Gen2XTagSelection?.ApplicationIdHex);
        Assert.True(controls.AllowUnverifiedFeatures);
    }

    private static string BitsToHex(IReadOnlyList<bool> bits)
    {
        const string digits = "0123456789ABCDEF";
        var result = new char[bits.Count / 4];
        for (int index = 0; index < result.Length; index++)
        {
            int nibble = 0;
            for (int bit = 0; bit < 4; bit++)
            {
                if (bits[(index * 4) + bit])
                {
                    nibble |= 1 << (3 - bit);
                }
            }
            result[index] = digits[nibble];
        }
        return new string(result);
    }
}
