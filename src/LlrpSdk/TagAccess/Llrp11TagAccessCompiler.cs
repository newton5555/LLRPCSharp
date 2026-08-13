using LlrpNet.Protocol.Choices.V1_1;
using LlrpNet.Protocol.Enumerations.V1_1;
using LlrpNet.Protocol.Parameters;
using LlrpNet.Protocol.Parameters.V1_1;
using V11Enumerations = LlrpNet.Protocol.Enumerations.V1_1;

namespace LlrpSdk;

internal static class Llrp11TagAccessCompiler
{
    public static AccessSpec Compile(uint accessSpecId, uint roSpecId, TagAccessRequest request, bool useBlockWrite = false)
    {
        ArgumentNullException.ThrowIfNull(request);
        return CompileSequence(accessSpecId, roSpecId, [request], useBlockWrite);
    }

    public static AccessSpec CompileSequence(
        uint accessSpecId,
        uint roSpecId,
        IReadOnlyList<TagAccessRequest> requests,
        bool useBlockWrite = false)
    {
        ArgumentNullException.ThrowIfNull(requests);
        if (requests.Count == 0)
        {
            throw new ArgumentException("A tag access sequence requires at least one operation.", nameof(requests));
        }

        TagAccessRequest first = requests[0] ?? throw new ArgumentException("A tag access sequence cannot contain null operations.", nameof(requests));
        Validate(first);
        var opSpecs = new ILlrpParameter[requests.Count];
        for (int index = 0; index < requests.Count; index++)
        {
            TagAccessRequest request = requests[index] ?? throw new ArgumentException("A tag access sequence cannot contain null operations.", nameof(requests));
            Validate(request);
            if (!HasSameTarget(first, request))
            {
                throw new ArgumentException("Every tag access sequence operation must use the same selection and antenna.", nameof(requests));
            }
            opSpecs[index] = CompileOperation(request, checked((ushort)(index + 1)), useBlockWrite);
        }

        TagSelection selection = first.Selection;
        var target = new C1G2TargetTag((byte)selection.MemoryBank, selection.Match, selection.BitPointer, ToBits(selection.Mask.Span, selection.BitLength), ToBits(selection.Data.Span, selection.BitLength));
        var command = new AccessCommand(new C1G2TagSpec([target]), opSpecs, []);
        return new AccessSpec(accessSpecId, first.AntennaId, V11Enumerations.AirProtocols.EPCGlobalClass1Gen2, V11Enumerations.AccessSpecState.Disabled, roSpecId,
            new AccessSpecStopTrigger(V11Enumerations.AccessSpecStopTriggerType.Operation_Count, 1), command,
            new AccessReportSpec(V11Enumerations.AccessReportTriggerType.End_Of_AccessSpec), []);
    }

    private static ILlrpParameter CompileOperation(TagAccessRequest request, ushort opSpecId, bool useBlockWrite)
    {
        uint accessPassword = TagAccessPassword.ParseHex(request.AccessPassword, nameof(request.AccessPassword));
        return request switch
        {
            ReadTagRequest read => new C1G2Read(opSpecId, accessPassword, (byte)read.MemoryBank, read.WordPointer, read.WordCount),
            WriteTagRequest write when useBlockWrite && write.WriteData.Count > 1 => new C1G2BlockWrite(opSpecId, accessPassword, (byte)write.MemoryBank, write.WordPointer, write.WriteData),
            WriteTagRequest write => new C1G2Write(opSpecId, accessPassword, (byte)write.MemoryBank, write.WordPointer, write.WriteData),
            LockTagRequest lockReq => CompileLock(lockReq, opSpecId, accessPassword),
            KillTagRequest killReq => new C1G2Kill(opSpecId, TagAccessPassword.ParseHex(killReq.KillPassword, nameof(killReq.KillPassword))),
            BlockEraseTagRequest eraseReq => new C1G2BlockErase(opSpecId, accessPassword, (byte)eraseReq.MemoryBank, eraseReq.WordPointer, eraseReq.WordCount),
            _ => throw new NotSupportedException($"Unsupported tag access request type {request.GetType().FullName}.")
        };
    }

    private static void Validate(TagAccessRequest request)
    {
        TagSelection selection = request.Selection ?? throw new ArgumentException("A tag selection is required.", nameof(request));
        if (selection.BitLength > selection.Mask.Length * 8 || selection.BitLength > selection.Data.Length * 8)
        {
            throw new ArgumentException("Selection bit length must fit both mask and data.", nameof(request));
        }
        if (request is ReadTagRequest { WordCount: 0 })
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Read word count must be positive.");
        }
        if (request is WriteTagRequest { WriteData.Count: 0 })
        {
            throw new ArgumentException("Write data must contain at least one word.", nameof(request));
        }
        if (request is BlockEraseTagRequest { WordCount: 0 })
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Block erase word count must be positive.");
        }
    }

    private static bool HasSameTarget(TagAccessRequest expected, TagAccessRequest actual)
    {
        TagSelection left = expected.Selection;
        TagSelection right = actual.Selection;
        return expected.AntennaId == actual.AntennaId && left.MemoryBank == right.MemoryBank && left.BitPointer == right.BitPointer &&
            left.BitLength == right.BitLength && left.Match == right.Match && left.Mask.Span.SequenceEqual(right.Mask.Span) && left.Data.Span.SequenceEqual(right.Data.Span);
    }

    private static C1G2Lock CompileLock(LockTagRequest lockReq, ushort opSpecId, uint accessPassword)
    {
        var payloads = new List<C1G2LockPayload>();
        AddPayloadIfSet(payloads, V11Enumerations.C1G2LockDataField.Kill_Password, lockReq.KillPasswordLockMode);
        AddPayloadIfSet(payloads, V11Enumerations.C1G2LockDataField.Access_Password, lockReq.AccessPasswordLockMode);
        AddPayloadIfSet(payloads, V11Enumerations.C1G2LockDataField.EPC_Memory, lockReq.EpcMemoryLockMode);
        AddPayloadIfSet(payloads, V11Enumerations.C1G2LockDataField.TID_Memory, lockReq.TidMemoryLockMode);
        AddPayloadIfSet(payloads, V11Enumerations.C1G2LockDataField.User_Memory, lockReq.UserMemoryLockMode);

        if (payloads.Count == 0)
        {
            throw new ArgumentException("LockTagRequest must specify at least one lock mode change.", nameof(lockReq));
        }

        return new C1G2Lock(opSpecId, accessPassword, payloads);
    }

    private static void AddPayloadIfSet(List<C1G2LockPayload> payloads, V11Enumerations.C1G2LockDataField field, TagLockMode mode)
    {
        if (mode == TagLockMode.NoChange)
        {
            return;
        }
        V11Enumerations.C1G2LockPrivilege privilege = mode switch
        {
            TagLockMode.Accessible => V11Enumerations.C1G2LockPrivilege.Read_Write,
            TagLockMode.AlwaysAccessible => V11Enumerations.C1G2LockPrivilege.Perma_Unlock,
            TagLockMode.SecuredWrite => V11Enumerations.C1G2LockPrivilege.Unlock,
            TagLockMode.AlwaysNotWritable => V11Enumerations.C1G2LockPrivilege.Perma_Lock,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
        };
        payloads.Add(new C1G2LockPayload(privilege, field));
    }

    private static IReadOnlyList<bool> ToBits(ReadOnlySpan<byte> bytes, ushort bitLength)
    {
        // LLRP masks are bit vectors; a zero bit length uses every packed bit (standard semantics: array length == bit count).
        int length = bitLength == 0 ? checked(bytes.Length * 8) : bitLength;
        var bits = new bool[length];
        for (int i = 0; i < length; i++)
        {
            bits[i] = (bytes[i / 8] & (1 << (7 - (i % 8)))) != 0;
        }
        return bits;
    }
}
