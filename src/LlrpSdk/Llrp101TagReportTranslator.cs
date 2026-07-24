using LlrpNet.Protocol.Choices.V1_0_1;
using LlrpNet.Protocol.Messages.V1_0_1;
using LlrpNet.Protocol.Parameters.V1_0_1;

namespace LlrpSdk;

/// <summary>
/// Projects standard LLRP 1.0.1 access-report parameters into SDK tag observations.
/// </summary>
internal static class Llrp101TagReportTranslator
{
    public static IReadOnlyList<TagReport> Translate(RO_ACCESS_REPORT report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var reports = new TagReport[report.TagReportDataItems.Count];
        for (int index = 0; index < report.TagReportDataItems.Count; index++)
        {
            TagReportData tag = report.TagReportDataItems[index];
            reports[index] = new TagReport(
                GetElectronicProductCode(tag.EPCParameter),
                tag.ROSpecID?.ROSpecID_2,
                tag.AntennaID?.AntennaID_2,
                tag.PeakRSSI?.PeakRSSI_2,
                tag.ChannelIndex?.ChannelIndex_2,
                tag.TagSeenCount?.TagCount);
        }

        return reports;
    }

    private static ReadOnlyMemory<byte> GetElectronicProductCode(IEPCParameter parameter)
    {
        return parameter switch
        {
            EPC_96 epc96 => epc96.EPC.ToArray(),
            EPCData epcData => PackBits(epcData.EPC),
            _ => throw new NotSupportedException(
                $"Unsupported LLRP 1.0.1 EPC parameter type {parameter.GetType().FullName}."),
        };
    }

    private static byte[] PackBits(IReadOnlyList<bool> bits)
    {
        var packed = new byte[(bits.Count + 7) / 8];
        for (int index = 0; index < bits.Count; index++)
        {
            if (bits[index])
            {
                packed[index / 8] |= (byte)(1 << (7 - (index % 8)));
            }
        }

        return packed;
    }
}
