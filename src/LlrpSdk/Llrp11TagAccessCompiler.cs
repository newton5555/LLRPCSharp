using LlrpNet.Protocol.Choices.V1_1;
using LlrpNet.Protocol.Enumerations.V1_1;
using LlrpNet.Protocol.Parameters;
using LlrpNet.Protocol.Parameters.V1_1;
using V11Enumerations = LlrpNet.Protocol.Enumerations.V1_1;

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
            LockTagRequest lockReq => CompileLock(lockReq),
            KillTagRequest killReq => new C1G2Kill(1, killReq.KillPassword),
            BlockEraseTagRequest eraseReq when eraseReq.WordCount > 0 => new C1G2BlockErase(1, eraseReq.AccessPassword, (byte)eraseReq.MemoryBank, eraseReq.WordPointer, eraseReq.WordCount),
            ReadTagRequest => throw new ArgumentOutOfRangeException(nameof(request), "Read word count must be positive."),
            WriteTagRequest => throw new ArgumentException("Write data must contain at least one word.", nameof(request)),
            BlockEraseTagRequest => throw new ArgumentOutOfRangeException(nameof(request), "Block erase word count must be positive."),
            _ => throw new NotSupportedException($"Unsupported tag access request type {request.GetType().FullName}.")
        };
        TagSelection selection = request.Selection ?? throw new ArgumentException("A tag selection is required.", nameof(request));
        if (selection.BitLength == 0 || selection.BitLength > selection.Mask.Length * 8 || selection.BitLength > selection.Data.Length * 8)
        {
            throw new ArgumentException("Selection bit length must be positive and fit both mask and data.", nameof(request));
        }
        var target = new C1G2TargetTag((byte)selection.MemoryBank, selection.Match, selection.BitPointer, ToBits(selection.Mask.Span, selection.BitLength), ToBits(selection.Data.Span, selection.BitLength));
        var command = new AccessCommand(new C1G2TagSpec([target]), [opSpec], []);
        return new AccessSpec(accessSpecId, request.AntennaId, V11Enumerations.AirProtocols.EPCGlobalClass1Gen2, V11Enumerations.AccessSpecState.Disabled, roSpecId,
            new AccessSpecStopTrigger(V11Enumerations.AccessSpecStopTriggerType.Operation_Count, 1), command,
            new AccessReportSpec(V11Enumerations.AccessReportTriggerType.End_Of_AccessSpec), []);
    }

    private static C1G2Lock CompileLock(LockTagRequest lockReq)
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

        return new C1G2Lock(1, lockReq.AccessPassword, payloads);
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
        var bits = new bool[bitLength];
        for (int i = 0; i < bitLength; i++)
        {
            bits[i] = (bytes[i / 8] & (1 << (7 - (i % 8)))) != 0;
        }
        return bits;
    }
}
