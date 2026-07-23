using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Messages;
using LlrpNet.Protocol.Registry;

namespace LlrpNet.Protocol.Tests.Codecs;

public sealed class LlrpCustomMessageCodecRegistryTests
{
    private const uint VendorOne = 0x01020304;
    private const uint VendorTwo = 0x11223344;

    [Fact]
    public void TypedCustomMessage_EncodeAndDecode_WritesRegistryOwnedMetadata()
    {
        var registry = new LlrpCodecRegistry();
        registry.RegisterCustomMessage(
            LlrpProtocolVersion.Version101,
            VendorOne,
            messageSubtype: 0x56,
            new TestCustomMessageCodec());
        var message = new TestCustomMessage(0xAABBCCDD, 0xCAFE);
        byte[] expected =
        [
            0x07, 0xFF,
            0x00, 0x00, 0x00, 0x11,
            0xAA, 0xBB, 0xCC, 0xDD,
            0x01, 0x02, 0x03, 0x04,
            0x56,
            0xCA, 0xFE,
        ];

        byte[] encoded = registry.EncodeMessage(LlrpProtocolVersion.Version101, message);
        var decoded = Assert.IsType<TestCustomMessage>(registry.DecodeMessage(expected));

        Assert.Equal(expected, encoded);
        Assert.Equal(message, decoded);
    }

    [Fact]
    public void CustomMessageLookup_IsolatedByVendorAndSubtype()
    {
        var registry = new LlrpCodecRegistry();
        registry.RegisterCustomMessage(
            LlrpProtocolVersion.Version101,
            VendorOne,
            messageSubtype: 1,
            new TestCustomMessageCodec());

        RawCustomMessage differentVendor = DecodeRaw(registry, VendorTwo, messageSubtype: 1);
        RawCustomMessage differentSubtype = DecodeRaw(registry, VendorOne, messageSubtype: 2);

        Assert.Equal(VendorTwo, differentVendor.VendorId);
        Assert.Equal((byte)1, differentVendor.MessageSubtype);
        Assert.Equal(VendorOne, differentSubtype.VendorId);
        Assert.Equal((byte)2, differentSubtype.MessageSubtype);
    }

    [Fact]
    public void CustomMessageRegistration_IsolatedByVersionForWireAndClrKeys()
    {
        var registry = new LlrpCodecRegistry();
        registry.RegisterCustomMessage(
            LlrpProtocolVersion.Version101,
            VendorOne,
            messageSubtype: 1,
            new TestCustomMessageCodec());
        registry.RegisterCustomMessage(
            LlrpProtocolVersion.Version11,
            VendorTwo,
            messageSubtype: 2,
            new TestCustomMessageCodec());
        var message = new TestCustomMessage(5, 0x1234);

        byte[] version101 = registry.EncodeMessage(LlrpProtocolVersion.Version101, message);
        byte[] version11 = registry.EncodeMessage(LlrpProtocolVersion.Version11, message);

        Assert.Equal(new byte[] { 0x01, 0x02, 0x03, 0x04, 0x01 }, version101.AsSpan(10, 5).ToArray());
        Assert.Equal(new byte[] { 0x11, 0x22, 0x33, 0x44, 0x02 }, version11.AsSpan(10, 5).ToArray());
        Assert.IsType<TestCustomMessage>(registry.DecodeMessage(version101));
        Assert.IsType<TestCustomMessage>(registry.DecodeMessage(version11));
    }

    [Fact]
    public void RegisterCustomMessage_ConflictsDoNotPartiallyUpdateOtherIndex()
    {
        var registry = new LlrpCodecRegistry();
        registry.RegisterCustomMessage(
            LlrpProtocolVersion.Version101,
            VendorOne,
            messageSubtype: 1,
            new TestCustomMessageCodec());

        Assert.Throws<InvalidOperationException>(
            () => registry.RegisterCustomMessage(
                LlrpProtocolVersion.Version101,
                VendorOne,
                messageSubtype: 1,
                new OtherCustomMessageCodec()));

        registry.RegisterCustomMessage(
            LlrpProtocolVersion.Version101,
            VendorOne,
            messageSubtype: 2,
            new OtherCustomMessageCodec());

        Assert.Throws<InvalidOperationException>(
            () => registry.RegisterCustomMessage(
                LlrpProtocolVersion.Version101,
                VendorTwo,
                messageSubtype: 3,
                new TestCustomMessageCodec()));

        RawCustomMessage unclaimedWireKey = DecodeRaw(registry, VendorTwo, messageSubtype: 3);
        Assert.Equal(VendorTwo, unclaimedWireKey.VendorId);
    }

    [Fact]
    public void RegisterMessage_RejectsCustomMessageWireType()
    {
        var registry = new LlrpCodecRegistry();

        Assert.Throws<ArgumentException>(
            () => registry.RegisterMessage(
                LlrpProtocolVersion.Version101,
                RawCustomMessage.CustomMessageType,
                new TestMessageCodec()));
    }

    [Fact]
    public void StandardAndCustomMessageRegistrationsShareExactClrEncoderKey()
    {
        var standardFirst = new LlrpCodecRegistry();
        standardFirst.RegisterMessage(
            LlrpProtocolVersion.Version101,
            messageType: 500,
            new RegularTestCustomMessageCodec());

        Assert.Throws<InvalidOperationException>(
            () => standardFirst.RegisterCustomMessage(
                LlrpProtocolVersion.Version101,
                VendorOne,
                messageSubtype: 1,
                new TestCustomMessageCodec()));
        Assert.IsType<RawCustomMessage>(DecodeRaw(standardFirst, VendorOne, messageSubtype: 1));

        var customFirst = new LlrpCodecRegistry();
        customFirst.RegisterCustomMessage(
            LlrpProtocolVersion.Version101,
            VendorOne,
            messageSubtype: 1,
            new TestCustomMessageCodec());

        Assert.Throws<InvalidOperationException>(
            () => customFirst.RegisterMessage(
                LlrpProtocolVersion.Version101,
                messageType: 500,
                new RegularTestCustomMessageCodec()));
        byte[] unclaimedStandardFrame = customFirst.EncodeMessage(
            LlrpProtocolVersion.Version101,
            new UnknownMessage(LlrpProtocolVersion.Version101, 500, 1, [0x12, 0x34]));
        Assert.IsType<UnknownMessage>(customFirst.DecodeMessage(unclaimedStandardFrame));
    }

    [Fact]
    public void EncodeCustomMessage_RejectsCodecThatReportsWrongWrittenLength()
    {
        var registry = new LlrpCodecRegistry();
        registry.RegisterCustomMessage(
            LlrpProtocolVersion.Version101,
            VendorOne,
            messageSubtype: 1,
            new ShortWritingCustomMessageCodec());

        Assert.Throws<InvalidOperationException>(
            () => registry.EncodeMessage(
                LlrpProtocolVersion.Version101,
                new TestCustomMessage(1, 2)));
    }

    [Fact]
    public void DecodeCustomMessage_RejectsCodecThatChangesWireMessageId()
    {
        const uint wireMessageId = 0x01020304;
        const uint decodedMessageId = wireMessageId + 1;
        var registry = new LlrpCodecRegistry();
        registry.RegisterCustomMessage(
            LlrpProtocolVersion.Version101,
            VendorOne,
            messageSubtype: 1,
            new WrongMessageIdCustomMessageCodec());
        byte[] frame = registry.EncodeMessage(
            LlrpProtocolVersion.Version101,
            new TestCustomMessage(wireMessageId, 0xCAFE));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => registry.DecodeMessage(frame));

        Assert.Contains(nameof(WrongMessageIdCustomMessageCodec), exception.Message);
        Assert.Contains(wireMessageId.ToString(), exception.Message);
        Assert.Contains(decodedMessageId.ToString(), exception.Message);
    }

    private static RawCustomMessage DecodeRaw(
        LlrpCodecRegistry registry,
        uint vendorId,
        byte messageSubtype)
    {
        byte[] frame = registry.EncodeMessage(
            LlrpProtocolVersion.Version101,
            new RawCustomMessage(
                LlrpProtocolVersion.Version101,
                messageId: 1,
                vendorId,
                messageSubtype,
                [0x12, 0x34]));
        return Assert.IsType<RawCustomMessage>(registry.DecodeMessage(frame));
    }
}
