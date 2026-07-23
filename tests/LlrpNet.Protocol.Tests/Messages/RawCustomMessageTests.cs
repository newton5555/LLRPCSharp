using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Messages;
using LlrpNet.Protocol.Registry;

namespace LlrpNet.Protocol.Tests.Messages;

public sealed class RawCustomMessageTests
{
    [Fact]
    public void UnknownCustomMessage_DecodeThenEncode_PreservesExactBytesAndCopiesData()
    {
        var registry = new LlrpCodecRegistry();
        byte[] expected =
        [
            0x07, 0xFF,
            0x00, 0x00, 0x00, 0x11,
            0x01, 0x02, 0x03, 0x04,
            0x00, 0x00, 0x12, 0x34,
            0x56,
            0xAA, 0xBB,
        ];
        byte[] source = expected.ToArray();

        var decoded = Assert.IsType<RawCustomMessage>(registry.DecodeMessage(source));
        source[^2] = 0xFF;
        byte[] reencoded = registry.EncodeMessage(LlrpProtocolVersion.Version101, decoded);

        Assert.Equal(LlrpProtocolVersion.Version101, decoded.Version);
        Assert.Equal((uint)0x01020304, decoded.MessageId);
        Assert.Equal((uint)0x00001234, decoded.VendorId);
        Assert.Equal((byte)0x56, decoded.MessageSubtype);
        Assert.Equal(new byte[] { 0xAA, 0xBB }, decoded.Data.ToArray());
        Assert.Equal(expected, reencoded);
    }

    [Fact]
    public void DecodeCustomMessage_RejectsPayloadShorterThanMetadata()
    {
        var registry = new LlrpCodecRegistry();
        byte[] frame =
        [
            0x07, 0xFF,
            0x00, 0x00, 0x00, 0x0E,
            0x00, 0x00, 0x00, 0x01,
            0x01, 0x02, 0x03, 0x04,
        ];

        LlrpProtocolException exception = Assert.Throws<LlrpProtocolException>(
            () => registry.DecodeMessage(frame));

        Assert.Equal(LlrpProtocolErrorCode.InvalidMessageLength, exception.ErrorCode);
    }

    [Fact]
    public void UnknownMessage_RejectsReservedCustomWireType()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new UnknownMessage(
                LlrpProtocolVersion.Version101,
                RawCustomMessage.CustomMessageType,
                1,
                [0x00]));
    }

    [Fact]
    public void RawCustomMessage_RejectsVersionChangeOnEncode()
    {
        var registry = new LlrpCodecRegistry();
        var message = new RawCustomMessage(
            LlrpProtocolVersion.Version101,
            1,
            vendorId: 2,
            messageSubtype: 3,
            [0x04]);

        Assert.Throws<ArgumentException>(
            () => registry.EncodeMessage(LlrpProtocolVersion.Version11, message));
    }
}
