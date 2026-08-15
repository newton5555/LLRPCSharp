using LlrpNet.Core.Diagnostics;
using LlrpNet.Core.Protocol;

namespace LlrpCli.Pcap;

/// <summary>A reassembled complete LLRP message frame from a TCP stream.</summary>
public sealed record LlrpCapturedMessage(
    string SrcIp,
    uint SrcPort,
    string DstIp,
    uint DstPort,
    byte[] Frame,
    LlrpFrameDirection Direction);

/// <summary>
/// Reassembles TCP segments into complete LLRP frames by buffering per-connection bytes and
/// slicing on the LLRP message header length field. Frames that cannot be completed (split across
/// missing segments) are skipped.
/// </summary>
public static class LlrpStreamReassembler
{
    public static IReadOnlyList<LlrpCapturedMessage> Reassemble(IEnumerable<PcapTcpSegment> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);
        var messages = new List<LlrpCapturedMessage>();
        var buffers = new Dictionary<string, (MemoryStream Stream, string SrcIp, uint SrcPort, string DstIp, uint DstPort)>();

        foreach (PcapTcpSegment segment in segments)
        {
            string key = GetStreamKey(segment);
            if (!buffers.TryGetValue(key, out var entry))
            {
                entry = (new MemoryStream(), segment.SrcIp, segment.SrcPort, segment.DstIp, segment.DstPort);
                buffers[key] = entry;
            }

            entry.Stream.Write(segment.Payload, 0, segment.Payload.Length);
            DrainCompleteFrames(entry.Stream, entry.SrcIp, entry.SrcPort, entry.DstIp, entry.DstPort, messages);
        }

        return messages;
    }

    private static string GetStreamKey(PcapTcpSegment segment)
    {
        // Key on the unordered endpoint pair so both directions share one buffer;
        // direction is recovered from the 5084 source port.
        string a = $"{segment.SrcIp}:{segment.SrcPort}";
        string b = $"{segment.DstIp}:{segment.DstPort}";
        return string.CompareOrdinal(a, b) < 0 ? $"{a}|{b}" : $"{b}|{a}";
    }

    private static void DrainCompleteFrames(
        MemoryStream stream,
        string srcIp,
        uint srcPort,
        string dstIp,
        uint dstPort,
        List<LlrpCapturedMessage> messages)
    {
        byte[] buffer = stream.GetBuffer();
        int count = checked((int)stream.Length);
        int offset = 0;

        while (count - offset >= LlrpMessageHeader.EncodedLength)
        {
            LlrpMessageHeader header;
            try
            {
                header = LlrpMessageHeader.Decode(buffer.AsSpan(offset, LlrpMessageHeader.EncodedLength));
            }
            catch
            {
                // Not enough bytes for a valid header; keep buffered and wait for more.
                break;
            }

            int messageLength = checked((int)header.MessageLength);
            if (messageLength < LlrpMessageHeader.EncodedLength)
            {
                break;
            }

            if (count - offset < messageLength)
            {
                // Incomplete frame; keep the remaining bytes buffered.
                break;
            }

            byte[] frame = new byte[messageLength];
            Array.Copy(buffer, offset, frame, 0, messageLength);
            LlrpFrameDirection direction = srcPort == 5084 ? LlrpFrameDirection.Receive : LlrpFrameDirection.Transmit;
            messages.Add(new LlrpCapturedMessage(srcIp, srcPort, dstIp, dstPort, frame, direction));
            offset += messageLength;
        }

        if (offset > 0)
        {
            // Compact the remaining partial data to the front of the stream.
            byte[] remaining = new byte[count - offset];
            Array.Copy(buffer, offset, remaining, 0, remaining.Length);
            stream.SetLength(0);
            stream.Write(remaining, 0, remaining.Length);
        }
    }
}
