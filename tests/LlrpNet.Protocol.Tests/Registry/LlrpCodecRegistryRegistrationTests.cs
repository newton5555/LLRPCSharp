using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Messages;
using LlrpNet.Protocol.Registry;
using LlrpNet.Protocol.Tests.Codecs;

namespace LlrpNet.Protocol.Tests.Registry;

public sealed class LlrpCodecRegistryRegistrationTests
{
    [Fact]
    public void RegisterMessage_AllowsSameWireAndClrKeysInDifferentVersions()
    {
        var registry = new LlrpCodecRegistry();
        registry.RegisterMessage(LlrpProtocolVersion.Version101, 10, new TestMessageCodec());
        registry.RegisterMessage(LlrpProtocolVersion.Version11, 10, new TestMessageCodec(decodeAdjustment: 1));

        TestMessage version101 = Assert.IsType<TestMessage>(
            registry.DecodeMessage(CreateFrame(LlrpProtocolVersion.Version101, 10, 1, [0x20])));
        TestMessage version11 = Assert.IsType<TestMessage>(
            registry.DecodeMessage(CreateFrame(LlrpProtocolVersion.Version11, 10, 2, [0x20])));

        Assert.Equal((byte)0x20, version101.Value);
        Assert.Equal((byte)0x21, version11.Value);
    }

    [Fact]
    public void RegisterMessage_ConflictsFailWithoutPartiallyUpdatingOtherIndex()
    {
        var registry = new LlrpCodecRegistry();
        registry.RegisterMessage(LlrpProtocolVersion.Version101, 10, new TestMessageCodec());

        Assert.Throws<InvalidOperationException>(
            () => registry.RegisterMessage(
                LlrpProtocolVersion.Version101,
                10,
                new OtherTestMessageCodec()));

        registry.RegisterMessage(
            LlrpProtocolVersion.Version101,
            11,
            new OtherTestMessageCodec());

        Assert.Throws<InvalidOperationException>(
            () => registry.RegisterMessage(
                LlrpProtocolVersion.Version101,
                12,
                new TestMessageCodec()));

        ILlrpMessage typeTwelve = registry.DecodeMessage(
            CreateFrame(LlrpProtocolVersion.Version101, 12, 3, [0x30]));
        Assert.IsType<UnknownMessage>(typeTwelve);
    }

    [Fact]
    public void RegisterMessage_RejectsUnsupportedVersionAndWireType()
    {
        var registry = new LlrpCodecRegistry();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => registry.RegisterMessage((LlrpProtocolVersion)0, 1, new TestMessageCodec()));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => registry.RegisterMessage(LlrpProtocolVersion.Version101, 1024, new TestMessageCodec()));
    }

    private static byte[] CreateFrame(
        LlrpProtocolVersion version,
        ushort messageType,
        uint messageId,
        ReadOnlySpan<byte> payload)
    {
        var frame = new byte[LlrpMessageHeader.EncodedLength + payload.Length];
        new LlrpMessageHeader(version, messageType, (uint)frame.Length, messageId).Encode(frame);
        payload.CopyTo(frame.AsSpan(LlrpMessageHeader.EncodedLength));
        return frame;
    }
}
