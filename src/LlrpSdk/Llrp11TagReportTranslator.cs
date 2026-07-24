using LlrpNet.Protocol.Choices.V1_1;
using LlrpNet.Protocol.Messages.V1_1;
using LlrpNet.Protocol.Parameters.V1_1;

namespace LlrpSdk;

/// <summary>
/// Projects standard LLRP 1.1 access-report parameters into SDK tag observations.
/// </summary>
internal static class Llrp11TagReportTranslator
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
                tag.SpecIndex?.SpecIndex_2,
                tag.InventoryParameterSpecID?.InventoryParameterSpecID_2,
                tag.AntennaID?.AntennaID_2,
                tag.PeakRSSI?.PeakRSSI_2,
                tag.ChannelIndex?.ChannelIndex_2,
                GetTimestamp(
                    tag.FirstSeenTimestampUTC?.Microseconds,
                    tag.FirstSeenTimestampUptime?.Microseconds),
                GetTimestamp(
                    tag.LastSeenTimestampUTC?.Microseconds,
                    tag.LastSeenTimestampUptime?.Microseconds),
                tag.TagSeenCount?.TagCount,
                tag.AccessSpecID?.AccessSpecID_2);
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
                $"Unsupported LLRP 1.1 EPC parameter type {parameter.GetType().FullName}."),
        };
    }

    private static TagTimestamp? GetTimestamp(ulong? utcMicroseconds, ulong? uptimeMicroseconds)
    {
        return utcMicroseconds is null && uptimeMicroseconds is null
            ? null
            : new TagTimestamp(utcMicroseconds, uptimeMicroseconds);
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
