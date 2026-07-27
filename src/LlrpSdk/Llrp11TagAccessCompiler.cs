using LlrpNet.Protocol.Choices.V1_1;
using LlrpNet.Protocol.Enumerations.V1_1;
using LlrpNet.Protocol.Parameters;
using LlrpNet.Protocol.Parameters.V1_1;

namespace LlrpSdk;

internal static class Llrp11TagAccessCompiler
{
    public static AccessSpec Compile(uint accessSpecId, uint roSpecId, TagAccessRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ILlrpParameter opSpec = request switch
        {
            ReadTagRequest read when read.WordCount > 0 => new C1G2Read(1, read.AccessPassword, (byte)read.MemoryBank, read.WordPointer, read.WordCount),
            WriteTagRequest write when write.WriteData is { Count: > 0 } => new C1G2Write(1, write.AccessPassword, (byte)write.MemoryBank, write.WordPointer, write.WriteData),
            ReadTagRequest => throw new ArgumentOutOfRangeException(nameof(request), "Read word count must be positive."),
            WriteTagRequest => throw new ArgumentException("Write data must contain at least one word.", nameof(request)),
            _ => throw new NotSupportedException($"Unsupported tag access request type {request.GetType().FullName}.")
        };
        TagSelection selection = request.Selection ?? throw new ArgumentException("A tag selection is required.", nameof(request));
        if (selection.BitLength == 0 || selection.BitLength > selection.Mask.Length * 8 || selection.BitLength > selection.Data.Length * 8)
        {
            throw new ArgumentException("Selection bit length must be positive and fit both mask and data.", nameof(request));
        }
        var target = new C1G2TargetTag((byte)selection.MemoryBank, selection.Match, selection.BitPointer, ToBits(selection.Mask.Span, selection.BitLength), ToBits(selection.Data.Span, selection.BitLength));
        var command = new AccessCommand(new C1G2TagSpec([target]), [opSpec], []);
        return new AccessSpec(accessSpecId, request.AntennaId, global::LlrpNet.Protocol.Enumerations.V1_1.AirProtocols.EPCGlobalClass1Gen2, global::LlrpNet.Protocol.Enumerations.V1_1.AccessSpecState.Disabled, roSpecId,
            new AccessSpecStopTrigger(global::LlrpNet.Protocol.Enumerations.V1_1.AccessSpecStopTriggerType.Operation_Count, 1), command,
            new AccessReportSpec(global::LlrpNet.Protocol.Enumerations.V1_1.AccessReportTriggerType.End_Of_AccessSpec), []);
    }

    private static IReadOnlyList<bool> ToBits(ReadOnlySpan<byte> bytes, ushort bitLength)
    {
        var bits = new bool[bitLength];
        for (int i = 0; i < bitLength; i++)
        {
            bits[i] = (bytes[i / 8] & (1 << (7 - (i % 8)))) != 0;
        }
        return bits;
    }
}
