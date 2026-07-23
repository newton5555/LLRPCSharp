using System.Buffers;
using LlrpNet.Core.Frames;
using LlrpNet.Core.Protocol;

namespace LlrpNet.Core.Tests.Frames;

public sealed class LlrpFrameDecoderTests
{
    private readonly LlrpFrameDecoder decoder = new();

    [Fact]
    public void TryReadFrame_WithPartialHeader_ReturnsFalseWithoutConsuming()
    {
        var buffer = new ReadOnlySequence<byte>(new byte[] { 0x04, 0x3E, 0, 0, 0 });
        SequencePosition originalStart = buffer.Start;

        bool result = decoder.TryReadFrame(ref buffer, out LlrpFrame frame);

        Assert.False(result);
        Assert.Equal(default, frame);
        Assert.Equal(originalStart, buffer.Start);
        Assert.Equal(5, buffer.Length);
    }

    [Fact]
    public void TryReadFrame_WithPartialPayload_ReturnsFalseWithoutConsuming()
    {
        byte[] bytes = CreateFrame(messageType: 1, messageId: 7, payload: [0xAA, 0xBB]);
        var buffer = new ReadOnlySequence<byte>(bytes.AsMemory(0, bytes.Length - 1));

        bool result = decoder.TryReadFrame(ref buffer, out _);

        Assert.False(result);
        Assert.Equal(bytes.Length - 1, buffer.Length);
    }

    [Fact]
    public void TryReadFrame_WithCoalescedFrames_ReturnsOneFrameAtATime()
    {
        byte[] first = CreateFrame(messageType: 62, messageId: 1, payload: []);
        byte[] second = CreateFrame(messageType: 1, messageId: 2, payload: [0x00]);
        var combined = new byte[first.Length + second.Length];
        first.CopyTo(combined, 0);
        second.CopyTo(combined, first.Length);
        var buffer = new ReadOnlySequence<byte>(combined);

        Assert.True(decoder.TryReadFrame(ref buffer, out LlrpFrame firstFrame));
        Assert.Equal((ushort)62, firstFrame.Header.MessageType);
        Assert.Equal((uint)1, firstFrame.Header.MessageId);
        Assert.Equal(first, firstFrame.Bytes.ToArray());
        Assert.Equal(second.Length, buffer.Length);

        Assert.True(decoder.TryReadFrame(ref buffer, out LlrpFrame secondFrame));
        Assert.Equal((ushort)1, secondFrame.Header.MessageType);
        Assert.Equal(new byte[] { 0x00 }, secondFrame.Payload.ToArray());
        Assert.True(buffer.IsEmpty);
    }

    [Fact]
    public void TryReadFrame_WithMultiSegmentInput_ReturnsCompleteFrame()
    {
        byte[] bytes = CreateFrame(messageType: 62, messageId: 0x11223344, payload: [1, 2, 3]);
        ReadOnlySequence<byte> buffer = SequenceFactory.Create(
            bytes.AsMemory(0, 3),
            bytes.AsMemory(3, 4),
            bytes.AsMemory(7));

        bool result = decoder.TryReadFrame(ref buffer, out LlrpFrame frame);

        Assert.True(result);
        Assert.Equal(bytes, frame.Bytes.ToArray());
        Assert.True(buffer.IsEmpty);
    }

    [Fact]
    public void TryReadFrame_RejectsFrameAboveConfiguredLimit()
    {
        var limitedDecoder = new LlrpFrameDecoder(maximumFrameLength: 10);
        byte[] bytes = CreateFrame(messageType: 1, messageId: 1, payload: [0x00]);
        var buffer = new ReadOnlySequence<byte>(bytes);

        LlrpProtocolException exception = Assert.Throws<LlrpProtocolException>(
            () => limitedDecoder.TryReadFrame(ref buffer, out _));

        Assert.Equal(LlrpProtocolErrorCode.FrameTooLarge, exception.ErrorCode);
        Assert.Equal(bytes.Length, buffer.Length);
    }

    private static byte[] CreateFrame(ushort messageType, uint messageId, byte[] payload)
    {
        var bytes = new byte[LlrpMessageHeader.EncodedLength + payload.Length];
        var header = new LlrpMessageHeader(
            LlrpProtocolVersion.Version101,
            messageType,
            (uint)bytes.Length,
            messageId);
        header.Encode(bytes);
        payload.CopyTo(bytes, LlrpMessageHeader.EncodedLength);
        return bytes;
    }

    private static class SequenceFactory
    {
        public static ReadOnlySequence<byte> Create(params ReadOnlyMemory<byte>[] segments)
        {
            if (segments.Length == 0)
            {
                return ReadOnlySequence<byte>.Empty;
            }

            Segment first = new(segments[0]);
            Segment last = first;
            for (int index = 1; index < segments.Length; index++)
            {
                last = last.Append(segments[index]);
            }

            return new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
        }

        private sealed class Segment : ReadOnlySequenceSegment<byte>
        {
            public Segment(ReadOnlyMemory<byte> memory)
            {
                Memory = memory;
            }

            public Segment Append(ReadOnlyMemory<byte> memory)
            {
                var segment = new Segment(memory)
                {
                    RunningIndex = RunningIndex + Memory.Length,
                };
                Next = segment;
                return segment;
            }
        }
    }
}

