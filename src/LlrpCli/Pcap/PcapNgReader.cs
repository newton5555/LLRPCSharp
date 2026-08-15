using System.Buffers.Binary;

namespace LlrpCli.Pcap;

/// <summary>A single TCP segment extracted from a pcapng capture.</summary>
public sealed record PcapTcpSegment(
    uint Timestamp,
    string SrcIp,
    uint SrcPort,
    string DstIp,
    uint DstPort,
    byte[] Payload);

/// <summary>
/// Minimal pcapng reader that extracts TCP segments from Ethernet/IPv4 captures.
/// Handles Section Header, Interface Description, and Enhanced Packet blocks.
/// </summary>
public static class PcapNgReader
{
    private const uint SectionHeaderBlockType = 0x0A0D0D0A;
    private const uint InterfaceDescriptionBlockType = 1;
    private const uint EnhancedPacketBlockType = 6;

    /// <summary>Parses a pcapng file and returns every TCP segment with a non-empty payload.</summary>
    public static IReadOnlyList<PcapTcpSegment> ReadTcpSegments(ReadOnlySpan<byte> bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes.ToArray());
        var segments = new List<PcapTcpSegment>();
        int offset = 0;

        while (offset + 12 <= bytes.Length)
        {
            uint blockType = BinaryPrimitives.ReadUInt32LittleEndian(bytes[offset..]);
            int blockLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes[(offset + 4)..]));
            if (blockLength < 12 || offset + blockLength > bytes.Length)
            {
                break;
            }

            if (blockType == EnhancedPacketBlockType)
            {
                TryParseEnhancedPacket(bytes, offset, blockLength, segments);
            }

            offset += blockLength;
        }

        return segments;
    }

    private static void TryParseEnhancedPacket(
        ReadOnlySpan<byte> bytes,
        int blockStart,
        int blockLength,
        List<PcapTcpSegment> segments)
    {
        // EPB body: interfaceId(4) tsHigh(4) tsLow(4) capturedLen(4) originalLen(4) packetData...
        if (blockLength < 28)
        {
            return;
        }

        int capturedLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes[(blockStart + 20)..]));
        int packetStart = blockStart + 28;
        if (capturedLength <= 0 || packetStart + capturedLength > blockStart + blockLength)
        {
            return;
        }

        ReadOnlySpan<byte> packet = bytes.Slice(packetStart, capturedLength);
        TryParseEthernetTcp(packet, segments);
    }

    private static void TryParseEthernetTcp(ReadOnlySpan<byte> packet, List<PcapTcpSegment> segments)
    {
        if (packet.Length < 14)
        {
            return;
        }

        ushort etherType = BinaryPrimitives.ReadUInt16BigEndian(packet[12..]);
        int ipOffset = 14;
        if (etherType == 0x8100) // 802.1Q VLAN
        {
            ipOffset = 18;
        }

        if (packet.Length < ipOffset + 20)
        {
            return;
        }

        // IPv4: version/ihl(1) ... protocol(9) srcIp(12-15) dstIp(16-19)
        int version = packet[ipOffset] >> 4;
        if (version != 4)
        {
            return;
        }

        int ipHeaderLength = (packet[ipOffset] & 0x0F) * 4;
        byte protocol = packet[ipOffset + 9];
        if (protocol != 6) // TCP only
        {
            return;
        }

        string srcIp = $"{packet[ipOffset + 12]}.{packet[ipOffset + 13]}.{packet[ipOffset + 14]}.{packet[ipOffset + 15]}";
        string dstIp = $"{packet[ipOffset + 16]}.{packet[ipOffset + 17]}.{packet[ipOffset + 18]}.{packet[ipOffset + 19]}";

        int tcpOffset = ipOffset + ipHeaderLength;
        if (packet.Length < tcpOffset + 20)
        {
            return;
        }

        uint srcPort = BinaryPrimitives.ReadUInt16BigEndian(packet[tcpOffset..]);
        uint dstPort = BinaryPrimitives.ReadUInt16BigEndian(packet[(tcpOffset + 2)..]);
        int tcpHeaderLength = (packet[tcpOffset + 12] >> 4) * 4;
        int payloadStart = tcpOffset + tcpHeaderLength;
        if (payloadStart >= packet.Length)
        {
            return;
        }

        byte[] payload = packet[payloadStart..].ToArray();
        if (payload.Length == 0)
        {
            return;
        }

        segments.Add(new PcapTcpSegment(
            Timestamp: 0,
            srcIp,
            srcPort,
            dstIp,
            dstPort,
            payload));
    }
}
