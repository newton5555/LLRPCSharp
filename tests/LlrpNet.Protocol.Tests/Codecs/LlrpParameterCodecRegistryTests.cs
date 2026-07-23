using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Parameters;
using LlrpNet.Protocol.Registry;

namespace LlrpNet.Protocol.Tests.Codecs;

public sealed class LlrpParameterCodecRegistryTests
{
    [Fact]
    public void UnknownTlv_DecodeThenEncode_PreservesDeclaredBytesAndBoundary()
    {
        var registry = new LlrpCodecRegistry();
        byte[] source = [0x00, 0xC8, 0x00, 0x06, 0xAA, 0xBB, 0xFE, 0xED];

        LlrpParameterDecodeResult result = registry.DecodeParameter(
            LlrpProtocolVersion.Version101,
            source);
        var parameter = Assert.IsType<UnknownParameter>(result.Parameter);
        source[4] = 0xFF;
        byte[] encoded = registry.EncodeParameter(LlrpProtocolVersion.Version101, parameter);

        Assert.Equal(6, result.BytesConsumed);
        Assert.Equal((ushort)200, parameter.ParameterType);
        Assert.Equal(new byte[] { 0xAA, 0xBB }, parameter.Data.ToArray());
        Assert.Equal(new byte[] { 0x00, 0xC8, 0x00, 0x06, 0xAA, 0xBB }, encoded);
    }

    [Fact]
    public void DecodeParameter_RejectsTruncatedTlvValue()
    {
        var registry = new LlrpCodecRegistry();
        byte[] source = [0x00, 0xC8, 0x00, 0x07, 0xAA, 0xBB];

        LlrpProtocolException exception = Assert.Throws<LlrpProtocolException>(
            () => registry.DecodeParameter(LlrpProtocolVersion.Version101, source));

        Assert.Equal(LlrpProtocolErrorCode.TruncatedData, exception.ErrorCode);
    }

    [Fact]
    public void DecodeParameter_UnknownTvFailsWithVersionAndType()
    {
        var registry = new LlrpCodecRegistry();

        UnknownTvParameterException exception = Assert.Throws<UnknownTvParameterException>(
            () => registry.DecodeParameter(LlrpProtocolVersion.Version11, [0x85, 0x01, 0x02]));

        Assert.Equal(LlrpProtocolVersion.Version11, exception.Version);
        Assert.Equal((byte)5, exception.ParameterType);
    }

    [Fact]
    public void RegisteredTv_DecodesFixedLengthAndLeavesFollowingBytes()
    {
        var registry = new LlrpCodecRegistry();
        registry.RegisterTvParameter(
            LlrpProtocolVersion.Version101,
            parameterType: 5,
            encodedLength: 3,
            new TestParameterCodec());

        LlrpParameterDecodeResult result = registry.DecodeParameter(
            LlrpProtocolVersion.Version101,
            [0x85, 0x12, 0x34, 0xFE, 0xED]);
        var parameter = Assert.IsType<TestParameter>(result.Parameter);

        Assert.Equal(3, result.BytesConsumed);
        Assert.Equal((ushort)0x1234, parameter.Value);
        Assert.Equal(
            new byte[] { 0x85, 0x12, 0x34 },
            registry.EncodeParameter(LlrpProtocolVersion.Version101, parameter));
    }

    [Fact]
    public void RegisteredTlv_UsesVersionAndClrTypeForRoundTrip()
    {
        var registry = new LlrpCodecRegistry();
        registry.RegisterTlvParameter(
            LlrpProtocolVersion.Version20,
            parameterType: 200,
            new TestParameterCodec());
        var parameter = new TestParameter(0xCAFE);

        byte[] encoded = registry.EncodeParameter(LlrpProtocolVersion.Version20, parameter);
        LlrpParameterDecodeResult result = registry.DecodeParameter(
            LlrpProtocolVersion.Version20,
            encoded);

        Assert.Equal(new byte[] { 0x00, 0xC8, 0x00, 0x06, 0xCA, 0xFE }, encoded);
        Assert.Equal(parameter, Assert.IsType<TestParameter>(result.Parameter));
        Assert.Equal(encoded.Length, result.BytesConsumed);
    }

    [Fact]
    public void RawCustom_DecodeThenEncode_PreservesMetadataAndData()
    {
        var registry = new LlrpCodecRegistry();
        byte[] source =
        [
            0x03, 0xFF, 0x00, 0x0F,
            0x01, 0x02, 0x03, 0x04,
            0xAA, 0xBB, 0xCC, 0xDD,
            0x10, 0x20, 0x30,
            0xFE,
        ];

        LlrpParameterDecodeResult result = registry.DecodeParameter(
            LlrpProtocolVersion.Version101,
            source);
        var parameter = Assert.IsType<RawCustomParameter>(result.Parameter);
        byte[] encoded = registry.EncodeParameter(LlrpProtocolVersion.Version101, parameter);

        Assert.Equal(15, result.BytesConsumed);
        Assert.Equal((uint)0x01020304, parameter.VendorId);
        Assert.Equal((uint)0xAABBCCDD, parameter.Subtype);
        Assert.Equal(new byte[] { 0x10, 0x20, 0x30 }, parameter.Data.ToArray());
        Assert.Equal(source.AsSpan(0, 15).ToArray(), encoded);
    }

    [Fact]
    public void DecodeParameter_RejectsCustomValueShorterThanVendorMetadata()
    {
        var registry = new LlrpCodecRegistry();
        byte[] source =
        [
            0x03, 0xFF, 0x00, 0x0B,
            0x01, 0x02, 0x03, 0x04,
            0xAA, 0xBB, 0xCC,
        ];

        LlrpProtocolException exception = Assert.Throws<LlrpProtocolException>(
            () => registry.DecodeParameter(LlrpProtocolVersion.Version101, source));

        Assert.Equal(LlrpProtocolErrorCode.InvalidParameterLength, exception.ErrorCode);
    }

    [Fact]
    public void RegisterParameter_ConflictsFailWithoutPartiallyUpdatingOtherIndex()
    {
        var registry = new LlrpCodecRegistry();
        registry.RegisterTlvParameter(
            LlrpProtocolVersion.Version101,
            200,
            new TestParameterCodec());

        Assert.Throws<InvalidOperationException>(
            () => registry.RegisterTlvParameter(
                LlrpProtocolVersion.Version101,
                200,
                new OtherTestParameterCodec()));

        registry.RegisterTlvParameter(
            LlrpProtocolVersion.Version101,
            201,
            new OtherTestParameterCodec());

        Assert.Throws<InvalidOperationException>(
            () => registry.RegisterTlvParameter(
                LlrpProtocolVersion.Version101,
                202,
                new TestParameterCodec()));

        LlrpParameterDecodeResult type202 = registry.DecodeParameter(
            LlrpProtocolVersion.Version101,
            [0x00, 0xCA, 0x00, 0x04]);
        Assert.IsType<UnknownParameter>(type202.Parameter);
    }

    [Fact]
    public void EncodeParameter_RejectsTvCodecWhoseLengthIsNotFixedRegistration()
    {
        var registry = new LlrpCodecRegistry();
        registry.RegisterTvParameter(
            LlrpProtocolVersion.Version101,
            parameterType: 5,
            encodedLength: 3,
            new WrongLengthParameterCodec());

        Assert.Throws<InvalidOperationException>(
            () => registry.EncodeParameter(
                LlrpProtocolVersion.Version101,
                new TestParameter(1)));
    }

    [Fact]
    public void EncodeParameter_RejectsRawVersionChange()
    {
        var registry = new LlrpCodecRegistry();
        var parameter = new UnknownParameter(LlrpProtocolVersion.Version101, 200, [0x01]);

        Assert.Throws<ArgumentException>(
            () => registry.EncodeParameter(LlrpProtocolVersion.Version11, parameter));
    }

    [Fact]
    public void RawParameters_RejectValuesThatCannotFitTlvLengthField()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new UnknownParameter(
                LlrpProtocolVersion.Version101,
                200,
                new byte[ushort.MaxValue - 3]));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RawCustomParameter(
                LlrpProtocolVersion.Version101,
                1,
                2,
                new byte[ushort.MaxValue - 11]));
    }
}
