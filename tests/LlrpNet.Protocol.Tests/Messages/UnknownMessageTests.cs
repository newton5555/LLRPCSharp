using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Messages;

namespace LlrpNet.Protocol.Tests.Messages;

public sealed class UnknownMessageTests
{
    [Fact]
    public void Constructor_CopiesPayloadAndPreservesWireIdentity()
    {
        byte[] source = [0x10, 0x20, 0x30];
        var message = new UnknownMessage(
            LlrpProtocolVersion.Version101,
            messageType: 777,
            messageId: 1234,
            source);

        source[0] = 0xFF;

        Assert.Equal(LlrpProtocolVersion.Version101, message.Version);
        Assert.Equal((ushort)777, message.MessageType);
        Assert.Equal((uint)1234, message.MessageId);
        Assert.Equal(new byte[] { 0x10, 0x20, 0x30 }, message.Payload.ToArray());
    }

    [Fact]
    public void Constructor_RejectsUnsupportedVersion()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new UnknownMessage((LlrpProtocolVersion)0, 1, 1, []));
    }

    [Fact]
    public void Constructor_RejectsMessageTypeOutsideTenBits()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new UnknownMessage(LlrpProtocolVersion.Version101, 1024, 1, []));
    }

    [Fact]
    public void Constructor_RejectsCustomMessageWireType()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new UnknownMessage(
                LlrpProtocolVersion.Version101,
                RawCustomMessage.CustomMessageType,
                messageId: 1,
                payload: []));
    }
}
