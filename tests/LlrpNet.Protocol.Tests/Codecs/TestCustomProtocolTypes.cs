using System.Buffers.Binary;
using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Codecs;
using LlrpNet.Protocol.Messages;
using LlrpNet.Protocol.Parameters;

namespace LlrpNet.Protocol.Tests.Codecs;

internal sealed record TestCustomMessage(uint MessageId, ushort Value) : ILlrpMessage;

internal sealed record OtherCustomMessage(uint MessageId, byte Value) : ILlrpMessage;

internal sealed record TestCustomParameter(ushort Value) : ILlrpParameter;

internal sealed record OtherCustomParameter(byte Value) : ILlrpParameter;

internal sealed class TestCustomMessageCodec : LlrpCustomMessageCodec<TestCustomMessage>
{
    public override TestCustomMessage Decode(
        LlrpProtocolVersion version,
        uint messageId,
        ReadOnlySpan<byte> data)
    {
        if (data.Length != sizeof(ushort))
        {
            throw new InvalidDataException("The test custom message requires exactly two Data octets.");
        }

        return new TestCustomMessage(messageId, BinaryPrimitives.ReadUInt16BigEndian(data));
    }

    public override int GetEncodedDataLength(
        LlrpProtocolVersion version,
        TestCustomMessage message)
    {
        return sizeof(ushort);
    }

    public override int EncodeData(
        LlrpProtocolVersion version,
        TestCustomMessage message,
        Span<byte> destination)
    {
        BinaryPrimitives.WriteUInt16BigEndian(destination, message.Value);
        return sizeof(ushort);
    }
}

internal sealed class OtherCustomMessageCodec : LlrpCustomMessageCodec<OtherCustomMessage>
{
    public override OtherCustomMessage Decode(
        LlrpProtocolVersion version,
        uint messageId,
        ReadOnlySpan<byte> data)
    {
        if (data.Length != 1)
        {
            throw new InvalidDataException("The other custom message requires exactly one Data octet.");
        }

        return new OtherCustomMessage(messageId, data[0]);
    }

    public override int GetEncodedDataLength(
        LlrpProtocolVersion version,
        OtherCustomMessage message)
    {
        return 1;
    }

    public override int EncodeData(
        LlrpProtocolVersion version,
        OtherCustomMessage message,
        Span<byte> destination)
    {
        destination[0] = message.Value;
        return 1;
    }
}

internal sealed class ShortWritingCustomMessageCodec : LlrpCustomMessageCodec<TestCustomMessage>
{
    public override TestCustomMessage Decode(
        LlrpProtocolVersion version,
        uint messageId,
        ReadOnlySpan<byte> data)
    {
        return new TestCustomMessage(messageId, 0);
    }

    public override int GetEncodedDataLength(
        LlrpProtocolVersion version,
        TestCustomMessage message)
    {
        return 2;
    }

    public override int EncodeData(
        LlrpProtocolVersion version,
        TestCustomMessage message,
        Span<byte> destination)
    {
        destination[0] = 0;
        return 1;
    }
}

internal sealed class WrongMessageIdCustomMessageCodec : LlrpCustomMessageCodec<TestCustomMessage>
{
    public override TestCustomMessage Decode(
        LlrpProtocolVersion version,
        uint messageId,
        ReadOnlySpan<byte> data)
    {
        if (data.Length != sizeof(ushort))
        {
            throw new InvalidDataException("The test custom message requires exactly two Data octets.");
        }

        return new TestCustomMessage(
            checked(messageId + 1),
            BinaryPrimitives.ReadUInt16BigEndian(data));
    }

    public override int GetEncodedDataLength(
        LlrpProtocolVersion version,
        TestCustomMessage message)
    {
        return sizeof(ushort);
    }

    public override int EncodeData(
        LlrpProtocolVersion version,
        TestCustomMessage message,
        Span<byte> destination)
    {
        BinaryPrimitives.WriteUInt16BigEndian(destination, message.Value);
        return sizeof(ushort);
    }
}

internal sealed class RegularTestCustomMessageCodec : LlrpMessageCodec<TestCustomMessage>
{
    public override TestCustomMessage Decode(
        LlrpMessageHeader header,
        ReadOnlySpan<byte> payload)
    {
        if (payload.Length != sizeof(ushort))
        {
            throw new InvalidDataException("The regular test message requires exactly two payload octets.");
        }

        return new TestCustomMessage(header.MessageId, BinaryPrimitives.ReadUInt16BigEndian(payload));
    }

    public override int GetEncodedPayloadLength(
        LlrpProtocolVersion version,
        TestCustomMessage message)
    {
        return sizeof(ushort);
    }

    public override int Encode(
        LlrpProtocolVersion version,
        TestCustomMessage message,
        Span<byte> destination)
    {
        BinaryPrimitives.WriteUInt16BigEndian(destination, message.Value);
        return sizeof(ushort);
    }
}

internal sealed class TestCustomParameterCodec : LlrpCustomParameterCodec<TestCustomParameter>
{
    public override TestCustomParameter Decode(
        LlrpProtocolVersion version,
        ReadOnlySpan<byte> data)
    {
        if (data.Length != sizeof(ushort))
        {
            throw new InvalidDataException("The test custom parameter requires exactly two Data octets.");
        }

        return new TestCustomParameter(BinaryPrimitives.ReadUInt16BigEndian(data));
    }

    public override int GetEncodedDataLength(
        LlrpProtocolVersion version,
        TestCustomParameter parameter)
    {
        return sizeof(ushort);
    }

    public override int EncodeData(
        LlrpProtocolVersion version,
        TestCustomParameter parameter,
        Span<byte> destination)
    {
        BinaryPrimitives.WriteUInt16BigEndian(destination, parameter.Value);
        return sizeof(ushort);
    }
}

internal sealed class OtherCustomParameterCodec : LlrpCustomParameterCodec<OtherCustomParameter>
{
    public override OtherCustomParameter Decode(
        LlrpProtocolVersion version,
        ReadOnlySpan<byte> data)
    {
        if (data.Length != 1)
        {
            throw new InvalidDataException("The other custom parameter requires exactly one Data octet.");
        }

        return new OtherCustomParameter(data[0]);
    }

    public override int GetEncodedDataLength(
        LlrpProtocolVersion version,
        OtherCustomParameter parameter)
    {
        return 1;
    }

    public override int EncodeData(
        LlrpProtocolVersion version,
        OtherCustomParameter parameter,
        Span<byte> destination)
    {
        destination[0] = parameter.Value;
        return 1;
    }
}

internal sealed class ShortWritingCustomParameterCodec : LlrpCustomParameterCodec<TestCustomParameter>
{
    public override TestCustomParameter Decode(
        LlrpProtocolVersion version,
        ReadOnlySpan<byte> data)
    {
        return new TestCustomParameter(0);
    }

    public override int GetEncodedDataLength(
        LlrpProtocolVersion version,
        TestCustomParameter parameter)
    {
        return 2;
    }

    public override int EncodeData(
        LlrpProtocolVersion version,
        TestCustomParameter parameter,
        Span<byte> destination)
    {
        destination[0] = 0;
        return 1;
    }
}

internal sealed class RegularTestCustomParameterCodec : LlrpParameterCodec<TestCustomParameter>
{
    public override TestCustomParameter Decode(
        LlrpProtocolVersion version,
        ReadOnlySpan<byte> payload)
    {
        if (payload.Length != sizeof(ushort))
        {
            throw new InvalidDataException("The regular test parameter requires exactly two payload octets.");
        }

        return new TestCustomParameter(BinaryPrimitives.ReadUInt16BigEndian(payload));
    }

    public override int GetEncodedPayloadLength(
        LlrpProtocolVersion version,
        TestCustomParameter parameter)
    {
        return sizeof(ushort);
    }

    public override int Encode(
        LlrpProtocolVersion version,
        TestCustomParameter parameter,
        Span<byte> destination)
    {
        BinaryPrimitives.WriteUInt16BigEndian(destination, parameter.Value);
        return sizeof(ushort);
    }
}
