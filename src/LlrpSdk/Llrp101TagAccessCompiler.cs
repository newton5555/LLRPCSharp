using LlrpNet.Protocol.Choices.V1_0_1;
using LlrpNet.Protocol.Enumerations.V1_0_1;
using LlrpNet.Protocol.Parameters;
using LlrpNet.Protocol.Parameters.V1_0_1;

namespace LlrpSdk;

internal static class Llrp101TagAccessCompiler
{
    public static AccessSpec Compile(uint accessSpecId, uint roSpecId, TagAccessRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(request);
        ILlrpParameter opSpec = request switch
        {
            ReadTagRequest read => new C1G2Read(1, read.AccessPassword, (byte)read.MemoryBank, read.WordPointer, read.WordCount),
            WriteTagRequest write => new C1G2Write(1, write.AccessPassword, (byte)write.MemoryBank, write.WordPointer, write.WriteData),
            _ => throw new NotSupportedException($"Unsupported tag access request type {request.GetType().FullName}.")
        };
        var target = new C1G2TargetTag(
            (byte)request.Selection.MemoryBank,
            request.Selection.Match,
            request.Selection.BitPointer,
            ToBits(request.Selection.Mask.Span, request.Selection.BitLength),
            ToBits(request.Selection.Data.Span, request.Selection.BitLength));
        var command = new AccessCommand(new C1G2TagSpec([target]), [opSpec], []);
        return new AccessSpec(
            accessSpecId,
            request.AntennaId,
            AirProtocols.EPCGlobalClass1Gen2,
            AccessSpecState.Disabled,
            roSpecId,
            new AccessSpecStopTrigger(AccessSpecStopTriggerType.Operation_Count, 1),
            command,
            new AccessReportSpec(AccessReportTriggerType.End_Of_AccessSpec),
            []);
    }

    private static void Validate(TagAccessRequest request)
    {
        TagSelection selection = request.Selection ?? throw new ArgumentException("A tag selection is required.", nameof(request));
        if (selection.BitLength == 0 || selection.BitLength > checked(selection.Mask.Length * 8) || selection.BitLength > checked(selection.Data.Length * 8))
        {
            throw new ArgumentException("Selection bit length must be positive and fit both mask and data.", nameof(request));
        }
        if (request is ReadTagRequest { WordCount: 0 })
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Read word count must be positive.");
        }
        if (request is WriteTagRequest { WriteData.Count: 0 })
        {
            throw new ArgumentException("Write data must contain at least one word.", nameof(request));
        }
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
