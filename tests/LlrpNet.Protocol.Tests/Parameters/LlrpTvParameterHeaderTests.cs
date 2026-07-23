using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Parameters;

namespace LlrpNet.Protocol.Tests.Parameters;

public sealed class LlrpTvParameterHeaderTests
{
    [Fact]
    public void Encode_AntennaIdHeader_MatchesSpecificationByte()
    {
        var header = new LlrpTvParameterHeader(ParameterType: 1);
        Span<byte> destination = stackalloc byte[LlrpTvParameterHeader.EncodedLength];

        header.Encode(destination);

        Assert.Equal(0x81, destination[0]);
    }

    [Fact]
    public void Decode_MaximumType_IsSupported()
    {
        LlrpTvParameterHeader header = LlrpTvParameterHeader.Decode([0xFF]);

        Assert.Equal((byte)127, header.ParameterType);
    }

    [Fact]
    public void Decode_RejectsTlvMarker()
    {
        LlrpProtocolException exception = Assert.Throws<LlrpProtocolException>(
            () => LlrpTvParameterHeader.Decode([0x01]));

        Assert.Equal(LlrpProtocolErrorCode.InvalidParameterEncoding, exception.ErrorCode);
    }

    [Fact]
    public void Decode_RejectsReservedTypeZero()
    {
        LlrpProtocolException exception = Assert.Throws<LlrpProtocolException>(
            () => LlrpTvParameterHeader.Decode([0x80]));

        Assert.Equal(LlrpProtocolErrorCode.InvalidParameterType, exception.ErrorCode);
    }
}

