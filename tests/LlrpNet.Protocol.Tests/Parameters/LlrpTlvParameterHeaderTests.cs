using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Parameters;

namespace LlrpNet.Protocol.Tests.Parameters;

public sealed class LlrpTlvParameterHeaderTests
{
    [Fact]
    public void Encode_UtcTimestampHeader_MatchesSpecificationBytes()
    {
        var header = new LlrpTlvParameterHeader(ParameterType: 128, ParameterLength: 12);
        Span<byte> destination = stackalloc byte[LlrpTlvParameterHeader.EncodedLength];

        header.Encode(destination);

        Assert.Equal(new byte[] { 0x00, 0x80, 0x00, 0x0C }, destination.ToArray());
    }

    [Fact]
    public void Decode_CustomTypeBoundary_IsSupported()
    {
        byte[] source = [0x03, 0xFF, 0x00, 0x04];

        LlrpTlvParameterHeader header = LlrpTlvParameterHeader.Decode(source);

        Assert.Equal((ushort)1023, header.ParameterType);
        Assert.Equal((ushort)4, header.ParameterLength);
    }

    [Fact]
    public void Decode_RejectsNonZeroReservedBits()
    {
        byte[] source = [0x04, 0x80, 0x00, 0x04];

        LlrpProtocolException exception = Assert.Throws<LlrpProtocolException>(
            () => LlrpTlvParameterHeader.Decode(source));

        Assert.Equal(LlrpProtocolErrorCode.InvalidParameterEncoding, exception.ErrorCode);
    }

    [Fact]
    public void Decode_RejectsTypeFromTvNumberSpace()
    {
        byte[] source = [0x00, 0x7F, 0x00, 0x04];

        LlrpProtocolException exception = Assert.Throws<LlrpProtocolException>(
            () => LlrpTlvParameterHeader.Decode(source));

        Assert.Equal(LlrpProtocolErrorCode.InvalidParameterType, exception.ErrorCode);
    }

    [Fact]
    public void Decode_RejectsLengthSmallerThanHeader()
    {
        byte[] source = [0x00, 0x80, 0x00, 0x03];

        LlrpProtocolException exception = Assert.Throws<LlrpProtocolException>(
            () => LlrpTlvParameterHeader.Decode(source));

        Assert.Equal(LlrpProtocolErrorCode.InvalidParameterLength, exception.ErrorCode);
    }
}

