using LlrpNet.Protocol.Choices.V1_1;
using LlrpNet.Protocol.Messages.V1_1;
using LlrpNet.Protocol.Parameters;
using LlrpNet.Protocol.Parameters.V1_1;

namespace LlrpSdk;

/// <summary>
/// Projects standard LLRP 1.1 access-report parameters into SDK tag observations.
/// </summary>
internal static class Llrp11TagReportTranslator
{
    public static IReadOnlyList<TranslatedTagReport> Translate(RO_ACCESS_REPORT report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var reports = new TranslatedTagReport[report.TagReportDataItems.Count];
        for (int index = 0; index < report.TagReportDataItems.Count; index++)
        {
            TagReportData tag = report.TagReportDataItems[index];
            var translated = new TagReport(
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
                tag.AccessSpecID?.AccessSpecID_2,
                TranslateAccessResults(tag.AccessCommandOpSpecResultItems));
            reports[index] = new TranslatedTagReport(translated, tag.CustomItems);
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

    private static IReadOnlyList<TagAccessOperationResult> TranslateAccessResults(
        IReadOnlyList<ILlrpParameter> items)
    {
        if (items.Count == 0)
        {
            return [];
        }

        var results = new List<TagAccessOperationResult>(items.Count);
        foreach (ILlrpParameter item in items)
        {
            switch (item)
            {
                case C1G2ReadOpSpecResult read:
                    results.Add(new TagAccessOperationResult(
                        read.OpSpecID,
                        read.Result == global::LlrpNet.Protocol.Enumerations.V1_1.C1G2ReadResultType.Success,
                        read.ReadData,
                        null,
                        read.Result == global::LlrpNet.Protocol.Enumerations.V1_1.C1G2ReadResultType.Success
                            ? null
                            : read.Result.ToString()));
                    break;
                case C1G2WriteOpSpecResult write:
                    results.Add(new TagAccessOperationResult(
                        write.OpSpecID,
                        write.Result == global::LlrpNet.Protocol.Enumerations.V1_1.C1G2WriteResultType.Success,
                        [],
                        write.NumWordsWritten,
                        write.Result == global::LlrpNet.Protocol.Enumerations.V1_1.C1G2WriteResultType.Success
                            ? null
                            : write.Result.ToString()));
                    break;
                case C1G2BlockWriteOpSpecResult blockWrite:
                    results.Add(new TagAccessOperationResult(
                        blockWrite.OpSpecID,
                        blockWrite.Result == global::LlrpNet.Protocol.Enumerations.V1_1.C1G2BlockWriteResultType.Success,
                        [],
                        blockWrite.NumWordsWritten,
                        blockWrite.Result == global::LlrpNet.Protocol.Enumerations.V1_1.C1G2BlockWriteResultType.Success
                            ? null
                            : blockWrite.Result.ToString()));
                    break;
                case C1G2LockOpSpecResult lockResult:
                    AddStatusOnlyResult(results, lockResult.OpSpecID, lockResult.Result, global::LlrpNet.Protocol.Enumerations.V1_1.C1G2LockResultType.Success);
                    break;
                case C1G2KillOpSpecResult kill:
                    AddStatusOnlyResult(results, kill.OpSpecID, kill.Result, global::LlrpNet.Protocol.Enumerations.V1_1.C1G2KillResultType.Success);
                    break;
                case C1G2BlockEraseOpSpecResult erase:
                    AddStatusOnlyResult(results, erase.OpSpecID, erase.Result, global::LlrpNet.Protocol.Enumerations.V1_1.C1G2BlockEraseResultType.Success);
                    break;
            }
        }

        return results;
    }

    private static void AddStatusOnlyResult<T>(
        ICollection<TagAccessOperationResult> results,
        ushort opSpecId,
        T result,
        T success)
        where T : struct, Enum
    {
        bool isSuccess = EqualityComparer<T>.Default.Equals(result, success);
        results.Add(new TagAccessOperationResult(opSpecId, isSuccess, [], null, isSuccess ? null : result.ToString()));
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
