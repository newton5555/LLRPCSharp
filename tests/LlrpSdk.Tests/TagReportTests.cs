using LlrpNet.Protocol.Choices.V1_0_1;
using LlrpNet.Protocol.Parameters;
using LlrpNet.Protocol.Parameters.V1_0_1;
using V101 = LlrpNet.Protocol.Messages.V1_0_1;

namespace LlrpSdk.Tests;

public sealed class TagReportTests
{
    [Fact]
    public void EpcHex_FromBytes_ReturnsUppercaseHex()
    {
        var report = new TagReport(
            new byte[] { 0xE2, 0x80, 0x11, 0x91, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01 },
            null, null, null, null, null, null, null, null, null, null);

        Assert.Equal("E28011910000000000000001", report.EpcHex);
    }

    [Fact]
    public void EpcHex_Empty_ReturnsEmptyString()
    {
        var report = new TagReport(
            ReadOnlyMemory<byte>.Empty,
            null, null, null, null, null, null, null, null, null, null);

        Assert.Equal(string.Empty, report.EpcHex);
    }

    [Fact]
    public void Translate_Epc96_SetsExactBitLength()
    {
        var report = new V101.RO_ACCESS_REPORT(
            1,
            TagReportDataItems: [CreateTagReportData(new EPC_96(new byte[] { 0xE2, 0x80, 0x11, 0x91, 0, 0, 0, 0, 0, 0, 0, 1 }))],
            RFSurveyReportDataItems: [],
            CustomItems: []);

        TagReport tag = Assert.Single(Llrp101TagReportTranslator.Translate(report)).Report;

        Assert.Equal(96, tag.EpcBitLength);
        Assert.Equal("E28011910000000000000001", tag.EpcHex);
        Assert.Equal("E28011910000000000000001", Convert.ToHexString(tag.ElectronicProductCode.Span));
    }

    [Fact]
    public void Translate_EpcData_NonByteAligned_PreservesExactBitLength()
    {
        // 100 significant bits (12.5 bytes): last byte carries 4 padding bits.
        byte[] source = [0xE2, 0x80, 0x11, 0x91, 0, 0, 0, 0, 0, 0, 0, 1, 0];
        var report = new V101.RO_ACCESS_REPORT(
            1,
            TagReportDataItems: [CreateTagReportData(new EPCData(BuildBits(100, source)))],
            RFSurveyReportDataItems: [],
            CustomItems: []);

        TagReport tag = Assert.Single(Llrp101TagReportTranslator.Translate(report)).Report;

        Assert.Equal(100, tag.EpcBitLength);
        Assert.Equal(13, tag.ElectronicProductCode.Length);
        Assert.Equal("E2801191000000000000000100", tag.EpcHex);
    }

    private static TagReportData CreateTagReportData(IEPCParameter epc) => new(
        epc,
        ROSpecID: null,
        SpecIndex: null,
        InventoryParameterSpecID: null,
        AntennaID: null,
        PeakRSSI: null,
        ChannelIndex: null,
        FirstSeenTimestampUTC: null,
        FirstSeenTimestampUptime: null,
        LastSeenTimestampUTC: null,
        LastSeenTimestampUptime: null,
        TagSeenCount: null,
        AirProtocolTagDataItems: [],
        AccessSpecID: null,
        AccessCommandOpSpecResultItems: [],
        CustomItems: []);

    private static IReadOnlyList<bool> BuildBits(int bitLength, byte[] bytes)
    {
        var bits = new bool[bitLength];
        for (int index = 0; index < bitLength; index++)
        {
            bits[index] = (bytes[index / 8] & (1 << (7 - (index % 8)))) != 0;
        }

        return bits;
    }
}
