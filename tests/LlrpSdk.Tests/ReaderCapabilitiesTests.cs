using LlrpNet.Protocol.Messages.V1_0_1;
using LlrpNet.Core.Protocol;
using LlrpSdk;

namespace LlrpSdk.Tests;

public sealed class ReaderCapabilitiesTests
{
    private static ReaderCapabilities CreateCapabilities(
        IReadOnlyList<RxSensitivityEntry>? rxSensitivities = null,
        short? maximumReceiveSensitivityDbm = null) =>
        new(
            maxNumberOfAntennas: 2,
            canSetAntennaProperties: true,
            hasUtcClockCapability: false,
            generalDeviceParameters: [],
            rawResponse: new ENABLE_EVENTS_AND_REPORTS(1),
            additionalParameters: [],
            rxSensitivities: rxSensitivities,
            maximumReceiveSensitivityDbm: maximumReceiveSensitivityDbm);

    [Fact]
    public void RxSensitivityEntry_ExposesRawOffsetInDbWithoutScaling()
    {
        // LLRP 1.0.1/1.1: ReceiveSensitivityValue is an integer dB offset relative to the
        // reader's maximum sensitivity (0 dB = most sensitive). It is NOT a dBm value and is
        // NOT scaled by 100.
        Assert.Equal(0, new RxSensitivityEntry(1, 0).ReceiveSensitivityDb);
        Assert.Equal(10, new RxSensitivityEntry(2, 10).ReceiveSensitivityDb);
        Assert.Equal(128, new RxSensitivityEntry(3, 128).ReceiveSensitivityDb);
    }

    [Fact]
    public void RxSensitivityEntry_KeepsWireValue()
    {
        var entry = new RxSensitivityEntry(7, 23);
        Assert.Equal((ushort)7, entry.Index);
        Assert.Equal((short)23, entry.ReceiveSensitivityValue);
    }

    [Fact]
    public void TxPowerEntry_StillScalesWireValueToDbm()
    {
        // TransmitPowerValue IS dBm * 100 per LLRP, so /100 stays valid there.
        Assert.Equal(30.0, new TxPowerEntry(1, 3000).TransmitPowerDbm);
    }

    [Fact]
    public void ReaderCapabilities_PassesThroughMaximumReceiveSensitivityDbm()
    {
        ReaderCapabilities capabilities = CreateCapabilities(maximumReceiveSensitivityDbm: -80);
        Assert.Equal((short)(-80), capabilities.MaximumReceiveSensitivityDbm);
    }

    [Fact]
    public void ReaderCapabilities_DefaultsMaximumReceiveSensitivityDbmToNull()
    {
        // LLRP 1.0.1 does not advertise MaximumReceiveSensitivity.
        Assert.Null(CreateCapabilities().MaximumReceiveSensitivityDbm);
    }

    [Fact]
    public void ReaderResourceLimits_UsesNullForUnknownAndZeroForExplicitUnsupported()
    {
        ReaderResourceLimits unknown = ReaderResourceLimits.Unknown;
        Assert.Null(unknown.MaxNumROSpecs);

        var explicitUnsupported = new ReaderResourceLimits { MaxNumROSpecs = 0 };
        Assert.Equal((uint)0, explicitUnsupported.MaxNumROSpecs);
        Assert.Null(explicitUnsupported.MaxNumAccessSpecs);
    }

    [Fact]
    public void ReaderDefaults_OmitInventoryWhenReaderAdvertisesNoRoSpecs()
    {
        var connectedCapabilities = new ReaderCapabilities(
            maxNumberOfAntennas: 1,
            canSetAntennaProperties: true,
            hasUtcClockCapability: true,
            generalDeviceParameters: [],
            rawResponse: new ENABLE_EVENTS_AND_REPORTS(1),
            additionalParameters: [],
            resourceLimits: new ReaderResourceLimits { MaxNumROSpecs = 0 });

        ReaderSettingsDefaults defaults = ReaderSettingsDefaults.CreateForReader(
            new ReaderSettingsDefaultContext(
                new ReaderIdentity(1, 2, "fw"),
                connectedCapabilities,
                LlrpProtocolVersion.Version101));

        Assert.Null(defaults.Settings.Inventory);
        Assert.Contains(defaults.Notes, note => note.Contains("MaxNumROSpecs=0", StringComparison.Ordinal));
    }
}
