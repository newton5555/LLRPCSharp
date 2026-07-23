using System.Buffers.Binary;
using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Codecs;
using LlrpNet.Protocol.Messages;
using LlrpNet.Protocol.Parameters;

namespace LlrpNet.Protocol.Tests.Codecs;

internal sealed record TestMessage(uint MessageId, byte Value) : ILlrpMessage;

internal sealed record OtherTestMessage(uint MessageId, byte Value) : ILlrpMessage;

internal sealed record TestParameter(ushort Value) : ILlrpParameter;

internal sealed record OtherTestParameter(byte Value) : ILlrpParameter;

internal sealed class TestMessageCodec(byte decodeAdjustment = 0) : LlrpMessageCodec<TestMessage>
{
    public override TestMessage Decode(LlrpMessageHeader header, ReadOnlySpan<byte> payload)
    {
        if (payload.Length != 1)
        {
            throw new InvalidDataException("The test message requires exactly one payload octet.");
        }

        return new TestMessage(header.MessageId, checked((byte)(payload[0] + decodeAdjustment)));
    }

    public override int GetEncodedPayloadLength(LlrpProtocolVersion version, TestMessage message)
    {
        return 1;
    }

    public override int Encode(
        LlrpProtocolVersion version,
        TestMessage message,
        Span<byte> destination)
    {
        destination[0] = message.Value;
        return 1;
    }
}

internal sealed class OtherTestMessageCodec : LlrpMessageCodec<OtherTestMessage>
{
    public override OtherTestMessage Decode(LlrpMessageHeader header, ReadOnlySpan<byte> payload)
    {
        if (payload.Length != 1)
        {
            throw new InvalidDataException("The other test message requires exactly one payload octet.");
        }

        return new OtherTestMessage(header.MessageId, payload[0]);
    }

    public override int GetEncodedPayloadLength(LlrpProtocolVersion version, OtherTestMessage message)
    {
        return 1;
    }

    public override int Encode(
        LlrpProtocolVersion version,
        OtherTestMessage message,
        Span<byte> destination)
    {
        destination[0] = message.Value;
        return 1;
    }
}

internal sealed class TestParameterCodec : LlrpParameterCodec<TestParameter>
{
    public override TestParameter Decode(LlrpProtocolVersion version, ReadOnlySpan<byte> payload)
    {
        if (payload.Length != sizeof(ushort))
        {
            throw new InvalidDataException("The test parameter requires exactly two payload octets.");
        }

        return new TestParameter(BinaryPrimitives.ReadUInt16BigEndian(payload));
    }

    public override int GetEncodedPayloadLength(
        LlrpProtocolVersion version,
        TestParameter parameter)
    {
        return sizeof(ushort);
    }

    public override int Encode(
        LlrpProtocolVersion version,
        TestParameter parameter,
        Span<byte> destination)
    {
        BinaryPrimitives.WriteUInt16BigEndian(destination, parameter.Value);
        return sizeof(ushort);
    }
}

internal sealed class OtherTestParameterCodec : LlrpParameterCodec<OtherTestParameter>
{
    public override OtherTestParameter Decode(
        LlrpProtocolVersion version,
        ReadOnlySpan<byte> payload)
    {
        if (payload.Length != 1)
        {
            throw new InvalidDataException("The other test parameter requires exactly one payload octet.");
        }

        return new OtherTestParameter(payload[0]);
    }

    public override int GetEncodedPayloadLength(
        LlrpProtocolVersion version,
        OtherTestParameter parameter)
    {
        return 1;
    }

    public override int Encode(
        LlrpProtocolVersion version,
        OtherTestParameter parameter,
        Span<byte> destination)
    {
        destination[0] = parameter.Value;
        return 1;
    }
}

internal sealed class ShortWritingMessageCodec : LlrpMessageCodec<TestMessage>
{
    public override TestMessage Decode(LlrpMessageHeader header, ReadOnlySpan<byte> payload)
    {
        return new TestMessage(header.MessageId, 0);
    }

    public override int GetEncodedPayloadLength(LlrpProtocolVersion version, TestMessage message)
    {
        return 1;
    }

    public override int Encode(
        LlrpProtocolVersion version,
        TestMessage message,
        Span<byte> destination)
    {
        return 0;
    }
}

internal sealed class WrongMessageIdMessageCodec : LlrpMessageCodec<TestMessage>
{
    public override TestMessage Decode(LlrpMessageHeader header, ReadOnlySpan<byte> payload)
    {
        if (payload.Length != 1)
        {
            throw new InvalidDataException("The test message requires exactly one payload octet.");
        }

        return new TestMessage(checked(header.MessageId + 1), payload[0]);
    }

    public override int GetEncodedPayloadLength(LlrpProtocolVersion version, TestMessage message)
    {
        return 1;
    }

    public override int Encode(
        LlrpProtocolVersion version,
        TestMessage message,
        Span<byte> destination)
    {
        destination[0] = message.Value;
        return 1;
    }
}

internal sealed class WrongLengthParameterCodec : LlrpParameterCodec<TestParameter>
{
    public override TestParameter Decode(LlrpProtocolVersion version, ReadOnlySpan<byte> payload)
    {
        return new TestParameter(0);
    }

    public override int GetEncodedPayloadLength(
        LlrpProtocolVersion version,
        TestParameter parameter)
    {
        return 1;
    }

    public override int Encode(
        LlrpProtocolVersion version,
        TestParameter parameter,
        Span<byte> destination)
    {
        destination[0] = 0;
        return 1;
    }
}
