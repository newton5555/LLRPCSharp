using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Messages;
using LlrpNet.Protocol.Registry;

namespace LlrpNet.Protocol.Tests.Codecs;

public sealed class LlrpMessageCodecRegistryTests
{
    [Fact]
    public void EncodeMessage_UsesClrTypeRegistrationAndWritesCompleteFrame()
    {
        var registry = new LlrpCodecRegistry();
        registry.RegisterMessage(LlrpProtocolVersion.Version101, 42, new TestMessageCodec());
        var message = new TestMessage(MessageId: 0x01020304, Value: 0xA5);

        byte[] frame = registry.EncodeMessage(LlrpProtocolVersion.Version101, message);

        LlrpMessageHeader header = LlrpMessageHeader.Decode(frame);
        Assert.Equal(LlrpProtocolVersion.Version101, header.Version);
        Assert.Equal((ushort)42, header.MessageType);
        Assert.Equal((uint)11, header.MessageLength);
        Assert.Equal((uint)0x01020304, header.MessageId);
        Assert.Equal((byte)0xA5, frame[LlrpMessageHeader.EncodedLength]);
    }

    [Fact]
    public void UnknownMessage_DecodeThenEncode_PreservesExactWireBytes()
    {
        var registry = new LlrpCodecRegistry();
        var original = new UnknownMessage(
            LlrpProtocolVersion.Version11,
            messageType: 900,
            messageId: 19,
            [0x00, 0xA1, 0xFF, 0x02]);
        byte[] frame = registry.EncodeMessage(LlrpProtocolVersion.Version11, original);

        var decoded = Assert.IsType<UnknownMessage>(registry.DecodeMessage(frame));
        byte[] reencoded = registry.EncodeMessage(LlrpProtocolVersion.Version11, decoded);

        Assert.Equal(frame, reencoded);
        Assert.Equal(new byte[] { 0x00, 0xA1, 0xFF, 0x02 }, decoded.Payload.ToArray());
    }

    [Fact]
    public void DecodeMessage_CopiesUnknownPayloadFromInputBuffer()
    {
        var registry = new LlrpCodecRegistry();
        byte[] frame = registry.EncodeMessage(
            LlrpProtocolVersion.Version101,
            new UnknownMessage(LlrpProtocolVersion.Version101, 800, 4, [0x12, 0x34]));

        var decoded = Assert.IsType<UnknownMessage>(registry.DecodeMessage(frame));
        frame[LlrpMessageHeader.EncodedLength] = 0xFF;

        Assert.Equal(new byte[] { 0x12, 0x34 }, decoded.Payload.ToArray());
    }

    [Fact]
    public void DecodeMessage_RejectsTruncatedPayloadDeclaredByHeader()
    {
        var registry = new LlrpCodecRegistry();
        var frame = new byte[11];
        new LlrpMessageHeader(
            LlrpProtocolVersion.Version101,
            MessageType: 800,
            MessageLength: 12,
            MessageId: 1).Encode(frame);

        LlrpProtocolException exception = Assert.Throws<LlrpProtocolException>(
            () => registry.DecodeMessage(frame));

        Assert.Equal(LlrpProtocolErrorCode.TruncatedData, exception.ErrorCode);
    }

    [Fact]
    public void DecodeMessage_RejectsTrailingBytesBeyondDeclaredFrame()
    {
        var registry = new LlrpCodecRegistry();
        var frame = new byte[12];
        new LlrpMessageHeader(
            LlrpProtocolVersion.Version101,
            MessageType: 800,
            MessageLength: 11,
            MessageId: 1).Encode(frame);

        LlrpProtocolException exception = Assert.Throws<LlrpProtocolException>(
            () => registry.DecodeMessage(frame));

        Assert.Equal(LlrpProtocolErrorCode.InvalidMessageLength, exception.ErrorCode);
    }

    [Fact]
    public void EncodeMessage_RejectsRawMessageVersionChange()
    {
        var registry = new LlrpCodecRegistry();
        var message = new UnknownMessage(LlrpProtocolVersion.Version101, 800, 1, [0x01]);

        Assert.Throws<ArgumentException>(
            () => registry.EncodeMessage(LlrpProtocolVersion.Version11, message));
    }

    [Fact]
    public void EncodeMessage_RejectsCodecThatReportsWrongWrittenLength()
    {
        var registry = new LlrpCodecRegistry();
        registry.RegisterMessage(
            LlrpProtocolVersion.Version101,
            42,
            new ShortWritingMessageCodec());

        Assert.Throws<InvalidOperationException>(
            () => registry.EncodeMessage(
                LlrpProtocolVersion.Version101,
                new TestMessage(1, 2)));
    }

    [Fact]
    public void DecodeMessage_RejectsCodecThatChangesWireMessageId()
    {
        const uint wireMessageId = 0x01020304;
        const uint decodedMessageId = wireMessageId + 1;
        var registry = new LlrpCodecRegistry();
        registry.RegisterMessage(
            LlrpProtocolVersion.Version101,
            42,
            new WrongMessageIdMessageCodec());
        byte[] frame = registry.EncodeMessage(
            LlrpProtocolVersion.Version101,
            new TestMessage(wireMessageId, 0xA5));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => registry.DecodeMessage(frame));

        Assert.Contains(nameof(WrongMessageIdMessageCodec), exception.Message);
        Assert.Contains(wireMessageId.ToString(), exception.Message);
        Assert.Contains(decodedMessageId.ToString(), exception.Message);
    }

    [Fact]
    public void EncodeMessage_RejectsUnregisteredClrType()
    {
        var registry = new LlrpCodecRegistry();

        Assert.Throws<NotSupportedException>(
            () => registry.EncodeMessage(
                LlrpProtocolVersion.Version101,
                new TestMessage(1, 2)));
    }
}
