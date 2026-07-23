using LlrpNet.Core.Protocol;

namespace LlrpNet.Core.Tests.Protocol;

public sealed class LlrpMessageHeaderTests
{
    [Fact]
    public void Encode_UsesNetworkOrderAndExpectedBitLayout()
    {
        var header = new LlrpMessageHeader(
            LlrpProtocolVersion.Version101,
            MessageType: 1,
            MessageLength: 11,
            MessageId: 0x01020304);
        Span<byte> destination = stackalloc byte[LlrpMessageHeader.EncodedLength];

        header.Encode(destination);

        Assert.Equal(
            [0x04, 0x01, 0x00, 0x00, 0x00, 0x0B, 0x01, 0x02, 0x03, 0x04],
            destination.ToArray());
    }

    [Theory]
    [InlineData(LlrpProtocolVersion.Version101, 0x04)]
    [InlineData(LlrpProtocolVersion.Version11, 0x08)]
    [InlineData(LlrpProtocolVersion.Version20, 0x0C)]
    public void Decode_RecognizesSupportedVersionValues(LlrpProtocolVersion version, byte firstOctet)
    {
        byte[] bytes = [firstOctet, 0x3E, 0, 0, 0, 10, 0, 0, 0, 7];

        LlrpMessageHeader header = LlrpMessageHeader.Decode(bytes);

        Assert.Equal(version, header.Version);
        Assert.Equal((ushort)62, header.MessageType);
        Assert.Equal((uint)10, header.MessageLength);
        Assert.Equal((uint)7, header.MessageId);
    }

    [Fact]
    public void Decode_RejectsNonZeroReservedBits()
    {
        byte[] bytes = [0x24, 0x01, 0, 0, 0, 10, 0, 0, 0, 1];

        LlrpProtocolException exception = Assert.Throws<LlrpProtocolException>(
            () => LlrpMessageHeader.Decode(bytes));

        Assert.Equal(LlrpProtocolErrorCode.InvalidReservedBits, exception.ErrorCode);
    }

    [Theory]
    [InlineData(0x00)]
    [InlineData(0x10)]
    public void Decode_RejectsUnsupportedVersion(byte firstOctet)
    {
        byte[] bytes = [firstOctet, 0x01, 0, 0, 0, 10, 0, 0, 0, 1];

        LlrpProtocolException exception = Assert.Throws<LlrpProtocolException>(
            () => LlrpMessageHeader.Decode(bytes));

        Assert.Equal(LlrpProtocolErrorCode.UnsupportedVersion, exception.ErrorCode);
    }

    [Fact]
    public void Decode_RejectsLengthSmallerThanHeader()
    {
        byte[] bytes = [0x04, 0x01, 0, 0, 0, 9, 0, 0, 0, 1];

        LlrpProtocolException exception = Assert.Throws<LlrpProtocolException>(
            () => LlrpMessageHeader.Decode(bytes));

        Assert.Equal(LlrpProtocolErrorCode.InvalidMessageLength, exception.ErrorCode);
    }

    [Fact]
    public void Decode_RejectsTruncatedHeader()
    {
        byte[] bytes = new byte[LlrpMessageHeader.EncodedLength - 1];

        LlrpProtocolException exception = Assert.Throws<LlrpProtocolException>(
            () => LlrpMessageHeader.Decode(bytes));

        Assert.Equal(LlrpProtocolErrorCode.TruncatedData, exception.ErrorCode);
    }

    [Fact]
    public void Encode_RejectsMessageTypeOutsideTenBits()
    {
        var header = new LlrpMessageHeader(
            LlrpProtocolVersion.Version101,
            MessageType: 1024,
            MessageLength: 10,
            MessageId: 1);
        byte[] destination = new byte[LlrpMessageHeader.EncodedLength];

        LlrpProtocolException exception = Assert.Throws<LlrpProtocolException>(
            () => header.Encode(destination));

        Assert.Equal(LlrpProtocolErrorCode.InvalidMessageType, exception.ErrorCode);
    }
}

