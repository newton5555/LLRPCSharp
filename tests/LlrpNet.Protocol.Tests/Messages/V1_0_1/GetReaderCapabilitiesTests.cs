using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Messages;
using LlrpNet.Protocol.Messages.V1_0_1;
using LlrpNet.Protocol.Parameters;
using LlrpNet.Protocol.Registry;
using LlrpNet.Protocol.Registry.V1_0_1;
using LlrpNet.Protocol.Tests.Codecs;

namespace LlrpNet.Protocol.Tests.Messages.V1_0_1;

public sealed class GetReaderCapabilitiesTests
{
    [Fact]
    public void EncodeAndDecode_AllCapabilities_MatchesNormativeBytes()
    {
        LlrpCodecRegistry registry = CreateRegistry();
        byte[] expected = [0x04, 0x01, 0x00, 0x00, 0x00, 0x0B, 0x01, 0x02, 0x03, 0x04, 0x00];
        var message = new GetReaderCapabilities(
            0x01020304,
            GetReaderCapabilitiesRequestedData.All);

        byte[] encoded = registry.EncodeMessage(LlrpProtocolVersion.Version101, message);
        var decoded = Assert.IsType<GetReaderCapabilities>(registry.DecodeMessage(expected));

        Assert.Equal(expected, encoded);
        Assert.Equal((uint)0x01020304, decoded.MessageId);
        Assert.Equal(GetReaderCapabilitiesRequestedData.All, decoded.RequestedData);
        Assert.Empty(decoded.Parameters);
    }

    [Fact]
    public void Decode_RejectsMissingRequestedData()
    {
        LlrpCodecRegistry registry = CreateRegistry();
        byte[] frame = [0x04, 0x01, 0x00, 0x00, 0x00, 0x0A, 0x01, 0x02, 0x03, 0x04];

        LlrpProtocolException exception = Assert.Throws<LlrpProtocolException>(
            () => registry.DecodeMessage(frame));

        Assert.Equal(LlrpProtocolErrorCode.InvalidMessageLength, exception.ErrorCode);
    }

    [Fact]
    public void Decode_RejectsUndefinedRequestedData()
    {
        LlrpCodecRegistry registry = CreateRegistry();
        byte[] frame = [0x04, 0x01, 0x00, 0x00, 0x00, 0x0B, 0x01, 0x02, 0x03, 0x04, 0x05];

        LlrpProtocolException exception = Assert.Throws<LlrpProtocolException>(
            () => registry.DecodeMessage(frame));

        Assert.Equal(LlrpProtocolErrorCode.InvalidParameterEncoding, exception.ErrorCode);
    }

    [Fact]
    public void Constructor_RejectsUndefinedRequestedData()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new GetReaderCapabilities(
                1,
                (GetReaderCapabilitiesRequestedData)5));
    }

    [Fact]
    public void CustomParameters_RoundTripWithoutLosingBytes()
    {
        LlrpCodecRegistry registry = CreateRegistry();
        var message = new GetReaderCapabilities(
            messageId: 7,
            GetReaderCapabilitiesRequestedData.GeneralDeviceCapabilities,
            [
                new RawCustomParameter(
                    LlrpProtocolVersion.Version101,
                    vendorId: 0x01020304,
                    subtype: 0xAABBCCDD,
                    [0x10, 0x20]),
            ]);

        byte[] encoded = registry.EncodeMessage(LlrpProtocolVersion.Version101, message);
        var decoded = Assert.IsType<GetReaderCapabilities>(registry.DecodeMessage(encoded));
        byte[] reencoded = registry.EncodeMessage(LlrpProtocolVersion.Version101, decoded);

        Assert.Equal(encoded, reencoded);
        var custom = Assert.IsType<RawCustomParameter>(Assert.Single(decoded.Parameters));
        Assert.Equal((uint)0x01020304, custom.VendorId);
        Assert.Equal((uint)0xAABBCCDD, custom.Subtype);
        Assert.Equal(new byte[] { 0x10, 0x20 }, custom.Data.ToArray());
    }

    [Fact]
    public void RegisteredCustomTrailingParameter_UsesExistingParameterRegistry()
    {
        var registry = new LlrpCodecRegistry();
        registry.RegisterCustomParameter(
            LlrpProtocolVersion.Version101,
            vendorId: 0x01020304,
            parameterSubtype: 7,
            new TestCustomParameterCodec());
        Llrp101StandardModule.Register(registry);
        var message = new GetReaderCapabilities(
            messageId: 9,
            GetReaderCapabilitiesRequestedData.LlrpCapabilities,
            [new TestCustomParameter(0xCAFE)]);

        byte[] encoded = registry.EncodeMessage(LlrpProtocolVersion.Version101, message);
        var decoded = Assert.IsType<GetReaderCapabilities>(registry.DecodeMessage(encoded));

        Assert.Equal(
            new TestCustomParameter(0xCAFE),
            Assert.IsType<TestCustomParameter>(Assert.Single(decoded.Parameters)));
    }

    [Fact]
    public void Encode_RejectsNonCustomTrailingParameter()
    {
        LlrpCodecRegistry registry = CreateRegistry();
        var message = new GetReaderCapabilities(
            messageId: 1,
            GetReaderCapabilitiesRequestedData.All,
            [new UnknownParameter(LlrpProtocolVersion.Version101, 200, [])]);

        Assert.Throws<ArgumentException>(
            () => registry.EncodeMessage(LlrpProtocolVersion.Version101, message));
    }

    [Fact]
    public void Decode_RejectsNonCustomTrailingParameter()
    {
        LlrpCodecRegistry registry = CreateRegistry();
        byte[] frame = registry.EncodeMessage(
            LlrpProtocolVersion.Version101,
            new UnknownMessage(
                LlrpProtocolVersion.Version101,
                GetReaderCapabilities.MessageType,
                messageId: 1,
                [
                    (byte)GetReaderCapabilitiesRequestedData.All,
                    0x00, 0xC8, 0x00, 0x04,
                ]));

        LlrpProtocolException exception = Assert.Throws<LlrpProtocolException>(
            () => registry.DecodeMessage(frame));

        Assert.Equal(LlrpProtocolErrorCode.InvalidParameterEncoding, exception.ErrorCode);
    }

    private static LlrpCodecRegistry CreateRegistry()
    {
        var registry = new LlrpCodecRegistry();
        Llrp101StandardModule.Register(registry);
        return registry;
    }
}
