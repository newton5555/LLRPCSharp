using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Parameters;
using LlrpNet.Protocol.Parameters.V1_0_1;
using LlrpNet.Protocol.Registry;
using LlrpNet.Protocol.Registry.V1_0_1;

namespace LlrpNet.Protocol.Tests.Parameters.V1_0_1;

public sealed class LlrpStatusTests
{
    [Fact]
    public void EncodeAndDecode_SuccessWithEmptyDescription_MatchesNormativeBytes()
    {
        LlrpCodecRegistry registry = CreateRegistry();
        byte[] expected = [0x01, 0x1F, 0x00, 0x08, 0x00, 0x00, 0x00, 0x00];
        var parameter = new LlrpStatus(LlrpStatusCode.MSuccess);

        byte[] encoded = registry.EncodeParameter(LlrpProtocolVersion.Version101, parameter);
        LlrpParameterDecodeResult result = registry.DecodeParameter(
            LlrpProtocolVersion.Version101,
            expected);
        var decoded = Assert.IsType<LlrpStatus>(result.Parameter);

        Assert.Equal(expected, encoded);
        Assert.Equal(expected.Length, result.BytesConsumed);
        Assert.Equal(LlrpStatusCode.MSuccess, decoded.StatusCode);
        Assert.Equal(string.Empty, decoded.ErrorDescription);
        Assert.Empty(decoded.ErrorParameters);
    }

    [Fact]
    public void Utf8v_LengthCountsEncodedOctets_NotCharacters()
    {
        LlrpCodecRegistry registry = CreateRegistry();
        byte[] expected =
        [
            0x01, 0x1F, 0x00, 0x0B,
            0x00, 0x64,
            0x00, 0x03,
            0xE9, 0x94, 0x99,
        ];
        var parameter = new LlrpStatus(LlrpStatusCode.MParameterError, "错");

        byte[] encoded = registry.EncodeParameter(LlrpProtocolVersion.Version101, parameter);
        var decoded = Assert.IsType<LlrpStatus>(registry.DecodeParameter(
            LlrpProtocolVersion.Version101,
            expected).Parameter);

        Assert.Equal(expected, encoded);
        Assert.Equal("错", decoded.ErrorDescription);
    }

    [Fact]
    public void FieldErrorAndParameterError_RoundTripInWireOrder()
    {
        LlrpCodecRegistry registry = CreateRegistry();
        byte[] expected =
        [
            0x01, 0x1F, 0x00, 0x16,
            0x00, 0x64,
            0x00, 0x03, 0x62, 0x61, 0x64,
            0x01, 0x20, 0x00, 0x05, 0xAB,
            0x01, 0x21, 0x00, 0x06, 0xCD, 0xEF,
        ];

        var decoded = Assert.IsType<LlrpStatus>(registry.DecodeParameter(
            LlrpProtocolVersion.Version101,
            expected).Parameter);
        byte[] reencoded = registry.EncodeParameter(LlrpProtocolVersion.Version101, decoded);

        Assert.Equal(expected, reencoded);
        Assert.Equal(LlrpStatusCode.MParameterError, decoded.StatusCode);
        Assert.Equal("bad", decoded.ErrorDescription);
        Assert.Equal(2, decoded.ErrorParameters.Count);
        Assert.Equal((ushort)288, Assert.IsType<UnknownParameter>(decoded.ErrorParameters[0]).ParameterType);
        Assert.Equal((ushort)289, Assert.IsType<UnknownParameter>(decoded.ErrorParameters[1]).ParameterType);
    }

    [Fact]
    public void Decode_RejectsTruncatedUtf8vValue()
    {
        LlrpCodecRegistry registry = CreateRegistry();
        byte[] encoded = [0x01, 0x1F, 0x00, 0x09, 0x00, 0x00, 0x00, 0x03, 0x61];

        LlrpProtocolException exception = Assert.Throws<LlrpProtocolException>(
            () => registry.DecodeParameter(LlrpProtocolVersion.Version101, encoded));

        Assert.Equal(LlrpProtocolErrorCode.TruncatedData, exception.ErrorCode);
    }

    [Fact]
    public void Decode_RejectsMalformedUtf8()
    {
        LlrpCodecRegistry registry = CreateRegistry();
        byte[] encoded = [0x01, 0x1F, 0x00, 0x0A, 0x00, 0x00, 0x00, 0x02, 0xC3, 0x28];

        LlrpProtocolException exception = Assert.Throws<LlrpProtocolException>(
            () => registry.DecodeParameter(LlrpProtocolVersion.Version101, encoded));

        Assert.Equal(LlrpProtocolErrorCode.InvalidParameterEncoding, exception.ErrorCode);
    }

    [Fact]
    public void Decode_RejectsUndefinedStatusCode()
    {
        LlrpCodecRegistry registry = CreateRegistry();
        byte[] encoded = [0x01, 0x1F, 0x00, 0x08, 0x00, 0x63, 0x00, 0x00];

        LlrpProtocolException exception = Assert.Throws<LlrpProtocolException>(
            () => registry.DecodeParameter(LlrpProtocolVersion.Version101, encoded));

        Assert.Equal(LlrpProtocolErrorCode.InvalidParameterEncoding, exception.ErrorCode);
    }

    [Fact]
    public void Decode_RejectsUnexpectedNestedParameterType()
    {
        LlrpCodecRegistry registry = CreateRegistry();
        byte[] encoded =
        [
            0x01, 0x1F, 0x00, 0x0C,
            0x00, 0x64, 0x00, 0x00,
            0x01, 0x22, 0x00, 0x04,
        ];

        LlrpProtocolException exception = Assert.Throws<LlrpProtocolException>(
            () => registry.DecodeParameter(LlrpProtocolVersion.Version101, encoded));

        Assert.Equal(LlrpProtocolErrorCode.InvalidParameterEncoding, exception.ErrorCode);
    }

    [Fact]
    public void Constructor_RejectsUndefinedStatusCode()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new LlrpStatus((LlrpStatusCode)99));
    }

    private static LlrpCodecRegistry CreateRegistry()
    {
        var registry = new LlrpCodecRegistry();
        Llrp101StandardModule.Register(registry);
        return registry;
    }
}
