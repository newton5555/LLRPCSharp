using LlrpNet.Core.Buffers;

namespace LlrpNet.Core.Tests.Buffers;

public sealed class LlrpBufferReaderTests
{
    [Fact]
    public void ReadsUnsignedIntegersInNetworkByteOrder()
    {
        byte[] buffer =
        [
            0x7F,
            0x12, 0x34,
            0x89, 0xAB, 0xCD, 0xEF,
            0x01, 0x23, 0x45, 0x67, 0x89, 0xAB, 0xCD, 0xEF,
        ];
        var reader = new LlrpBufferReader(buffer);

        Assert.Equal(0x7F, reader.ReadByte());
        Assert.Equal(0x1234, reader.ReadUInt16());
        Assert.Equal(0x89ABCDEFu, reader.ReadUInt32());
        Assert.Equal(0x0123456789ABCDEFul, reader.ReadUInt64());
        Assert.True(reader.End);
        Assert.Equal(buffer.Length, reader.Position);
        Assert.Equal(0, reader.Remaining);
    }

    [Fact]
    public void ReadsSignedIntegersInNetworkByteOrder()
    {
        byte[] buffer =
        [
            0x80,
            0x80, 0x01,
            0x80, 0x00, 0x00, 0x01,
            0x80, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01,
        ];
        var reader = new LlrpBufferReader(buffer);

        Assert.Equal(sbyte.MinValue, reader.ReadSByte());
        Assert.Equal(-32767, reader.ReadInt16());
        Assert.Equal(-2147483647, reader.ReadInt32());
        Assert.Equal(-9223372036854775807, reader.ReadInt64());
    }

    [Fact]
    public void ReadBytesReturnsAViewAndAdvanceSkipsBytes()
    {
        byte[] buffer = [0x10, 0x20, 0x30, 0x40];
        var reader = new LlrpBufferReader(buffer);

        reader.Advance(1);
        ReadOnlySpan<byte> value = reader.ReadBytes(2);
        buffer[1] = 0xA0;

        Assert.Equal(0xA0, value[0]);
        Assert.Equal(0x30, value[1]);
        Assert.Equal(3, reader.Position);
        Assert.Equal(0x40, reader.ReadByte());
    }

    [Fact]
    public void TruncatedReadThrowsWithoutAdvancing()
    {
        var reader = new LlrpBufferReader([0x01, 0x02, 0x03]);

        Exception? exception = null;
        try
        {
            reader.ReadUInt32();
        }
        catch (Exception caught)
        {
            exception = caught;
        }

        EndOfStreamException error = Assert.IsType<EndOfStreamException>(exception);
        Assert.Contains("requested 4 byte(s)", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, reader.Position);
        Assert.Equal(3, reader.Remaining);
    }

    [Fact]
    public void NegativeByteCountThrowsWithoutAdvancing()
    {
        var reader = new LlrpBufferReader([0x01]);

        Exception? exception = null;
        try
        {
            reader.ReadBytes(-1);
        }
        catch (Exception caught)
        {
            exception = caught;
        }

        Assert.IsType<ArgumentOutOfRangeException>(exception);
        Assert.Equal(0, reader.Position);
    }
}
