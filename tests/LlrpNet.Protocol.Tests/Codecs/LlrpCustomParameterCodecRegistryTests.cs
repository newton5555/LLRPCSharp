using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Parameters;
using LlrpNet.Protocol.Registry;

namespace LlrpNet.Protocol.Tests.Codecs;

public sealed class LlrpCustomParameterCodecRegistryTests
{
    private const uint VendorOne = 0x01020304;
    private const uint VendorTwo = 0x11223344;
    private const uint SubtypeOne = 0xAABBCCDD;
    private const uint SubtypeTwo = 0x10203040;

    [Fact]
    public void TypedCustomParameter_EncodeAndDecode_WritesRegistryOwnedMetadata()
    {
        var registry = new LlrpCodecRegistry();
        registry.RegisterCustomParameter(
            LlrpProtocolVersion.Version101,
            VendorOne,
            SubtypeOne,
            new TestCustomParameterCodec());
        var parameter = new TestCustomParameter(0xCAFE);
        byte[] expected =
        [
            0x03, 0xFF, 0x00, 0x0E,
            0x01, 0x02, 0x03, 0x04,
            0xAA, 0xBB, 0xCC, 0xDD,
            0xCA, 0xFE,
        ];

        byte[] encoded = registry.EncodeParameter(LlrpProtocolVersion.Version101, parameter);
        LlrpParameterDecodeResult result = registry.DecodeParameter(
            LlrpProtocolVersion.Version101,
            expected);
        var decoded = Assert.IsType<TestCustomParameter>(result.Parameter);

        Assert.Equal(expected, encoded);
        Assert.Equal(expected.Length, result.BytesConsumed);
        Assert.Equal(parameter, decoded);
    }

    [Fact]
    public void UnknownCustomParameter_CopiesDataAndRoundTripsExactBytes()
    {
        var registry = new LlrpCodecRegistry();
        byte[] expected =
        [
            0x03, 0xFF, 0x00, 0x0E,
            0x01, 0x02, 0x03, 0x04,
            0xAA, 0xBB, 0xCC, 0xDD,
            0x10, 0x20,
        ];
        byte[] source = expected.ToArray();

        var raw = Assert.IsType<RawCustomParameter>(
            registry.DecodeParameter(LlrpProtocolVersion.Version101, source).Parameter);
        source[^2] = 0xFF;
        byte[] reencoded = registry.EncodeParameter(LlrpProtocolVersion.Version101, raw);

        Assert.Equal(new byte[] { 0x10, 0x20 }, raw.Data.ToArray());
        Assert.Equal(expected, reencoded);
    }

    [Fact]
    public void CustomParameterLookup_IsolatedByVendorAndSubtype()
    {
        var registry = new LlrpCodecRegistry();
        registry.RegisterCustomParameter(
            LlrpProtocolVersion.Version101,
            VendorOne,
            SubtypeOne,
            new TestCustomParameterCodec());

        RawCustomParameter differentVendor = DecodeRaw(registry, VendorTwo, SubtypeOne);
        RawCustomParameter differentSubtype = DecodeRaw(registry, VendorOne, SubtypeTwo);

        Assert.Equal(VendorTwo, differentVendor.VendorId);
        Assert.Equal(SubtypeOne, differentVendor.Subtype);
        Assert.Equal(VendorOne, differentSubtype.VendorId);
        Assert.Equal(SubtypeTwo, differentSubtype.Subtype);
    }

    [Fact]
    public void CustomParameterRegistration_IsolatedByVersionForWireAndClrKeys()
    {
        var registry = new LlrpCodecRegistry();
        registry.RegisterCustomParameter(
            LlrpProtocolVersion.Version101,
            VendorOne,
            SubtypeOne,
            new TestCustomParameterCodec());
        registry.RegisterCustomParameter(
            LlrpProtocolVersion.Version11,
            VendorTwo,
            SubtypeTwo,
            new TestCustomParameterCodec());
        var parameter = new TestCustomParameter(0x1234);

        byte[] version101 = registry.EncodeParameter(LlrpProtocolVersion.Version101, parameter);
        byte[] version11 = registry.EncodeParameter(LlrpProtocolVersion.Version11, parameter);

        Assert.Equal(
            new byte[] { 0x01, 0x02, 0x03, 0x04, 0xAA, 0xBB, 0xCC, 0xDD },
            version101.AsSpan(4, 8).ToArray());
        Assert.Equal(
            new byte[] { 0x11, 0x22, 0x33, 0x44, 0x10, 0x20, 0x30, 0x40 },
            version11.AsSpan(4, 8).ToArray());
        Assert.IsType<TestCustomParameter>(
            registry.DecodeParameter(LlrpProtocolVersion.Version101, version101).Parameter);
        Assert.IsType<TestCustomParameter>(
            registry.DecodeParameter(LlrpProtocolVersion.Version11, version11).Parameter);
    }

    [Fact]
    public void RegisterCustomParameter_ConflictsDoNotPartiallyUpdateOtherIndex()
    {
        var registry = new LlrpCodecRegistry();
        registry.RegisterCustomParameter(
            LlrpProtocolVersion.Version101,
            VendorOne,
            SubtypeOne,
            new TestCustomParameterCodec());

        Assert.Throws<InvalidOperationException>(
            () => registry.RegisterCustomParameter(
                LlrpProtocolVersion.Version101,
                VendorOne,
                SubtypeOne,
                new OtherCustomParameterCodec()));

        registry.RegisterCustomParameter(
            LlrpProtocolVersion.Version101,
            VendorOne,
            SubtypeTwo,
            new OtherCustomParameterCodec());

        Assert.Throws<InvalidOperationException>(
            () => registry.RegisterCustomParameter(
                LlrpProtocolVersion.Version101,
                VendorTwo,
                parameterSubtype: 3,
                new TestCustomParameterCodec()));

        RawCustomParameter unclaimedWireKey = DecodeRaw(registry, VendorTwo, parameterSubtype: 3);
        Assert.Equal(VendorTwo, unclaimedWireKey.VendorId);
    }

    [Fact]
    public void RegisterTlvParameter_RejectsCustomParameterWireType()
    {
        var registry = new LlrpCodecRegistry();

        Assert.Throws<ArgumentException>(
            () => registry.RegisterTlvParameter(
                LlrpProtocolVersion.Version101,
                RawCustomParameter.CustomParameterType,
                new TestParameterCodec()));
    }

    [Fact]
    public void StandardAndCustomParameterRegistrationsShareExactClrEncoderKey()
    {
        var standardFirst = new LlrpCodecRegistry();
        standardFirst.RegisterTlvParameter(
            LlrpProtocolVersion.Version101,
            parameterType: 200,
            new RegularTestCustomParameterCodec());

        Assert.Throws<InvalidOperationException>(
            () => standardFirst.RegisterCustomParameter(
                LlrpProtocolVersion.Version101,
                VendorOne,
                SubtypeOne,
                new TestCustomParameterCodec()));
        Assert.IsType<RawCustomParameter>(DecodeRaw(standardFirst, VendorOne, SubtypeOne));

        var customFirst = new LlrpCodecRegistry();
        customFirst.RegisterCustomParameter(
            LlrpProtocolVersion.Version101,
            VendorOne,
            SubtypeOne,
            new TestCustomParameterCodec());

        Assert.Throws<InvalidOperationException>(
            () => customFirst.RegisterTlvParameter(
                LlrpProtocolVersion.Version101,
                parameterType: 200,
                new RegularTestCustomParameterCodec()));
        byte[] unclaimedStandardParameter = customFirst.EncodeParameter(
            LlrpProtocolVersion.Version101,
            new UnknownParameter(LlrpProtocolVersion.Version101, 200, [0x12, 0x34]));
        Assert.IsType<UnknownParameter>(
            customFirst.DecodeParameter(
                LlrpProtocolVersion.Version101,
                unclaimedStandardParameter).Parameter);
    }

    [Fact]
    public void EncodeCustomParameter_RejectsCodecThatReportsWrongWrittenLength()
    {
        var registry = new LlrpCodecRegistry();
        registry.RegisterCustomParameter(
            LlrpProtocolVersion.Version101,
            VendorOne,
            SubtypeOne,
            new ShortWritingCustomParameterCodec());

        Assert.Throws<InvalidOperationException>(
            () => registry.EncodeParameter(
                LlrpProtocolVersion.Version101,
                new TestCustomParameter(1)));
    }

    private static RawCustomParameter DecodeRaw(
        LlrpCodecRegistry registry,
        uint vendorId,
        uint parameterSubtype)
    {
        byte[] encoded = registry.EncodeParameter(
            LlrpProtocolVersion.Version101,
            new RawCustomParameter(
                LlrpProtocolVersion.Version101,
                vendorId,
                parameterSubtype,
                [0x12, 0x34]));
        return Assert.IsType<RawCustomParameter>(
            registry.DecodeParameter(LlrpProtocolVersion.Version101, encoded).Parameter);
    }
}
