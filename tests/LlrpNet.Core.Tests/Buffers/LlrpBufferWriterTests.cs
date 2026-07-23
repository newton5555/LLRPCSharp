using LlrpNet.Core.Buffers;

namespace LlrpNet.Core.Tests.Buffers;

public sealed class LlrpBufferWriterTests
{
    [Fact]
    public void WritesUnsignedIntegersInNetworkByteOrder()
    {
        byte[] buffer = new byte[15];
        var writer = new LlrpBufferWriter(buffer);

        writer.WriteByte(0x7F);
        writer.WriteUInt16(0x1234);
        writer.WriteUInt32(0x89ABCDEF);
        writer.WriteUInt64(0x0123456789ABCDEF);

        byte[] expected =
        [
            0x7F,
            0x12, 0x34,
            0x89, 0xAB, 0xCD, 0xEF,
            0x01, 0x23, 0x45, 0x67, 0x89, 0xAB, 0xCD, 0xEF,
        ];
        Assert.Equal(expected, buffer);
        Assert.Equal(buffer.Length, writer.Position);
        Assert.Equal(0, writer.Remaining);
    }

    [Fact]
    public void WritesSignedIntegersInNetworkByteOrder()
    {
        byte[] buffer = new byte[15];
        var writer = new LlrpBufferWriter(buffer);

        writer.WriteSByte(sbyte.MinValue);
        writer.WriteInt16(-32767);
        writer.WriteInt32(-2147483647);
        writer.WriteInt64(-9223372036854775807);

        byte[] expected =
        [
            0x80,
            0x80, 0x01,
            0x80, 0x00, 0x00, 0x01,
            0x80, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01,
        ];
        Assert.Equal(expected, buffer);
    }

    [Fact]
    public void WriteBytesCopiesInputAndAdvancePreservesSkippedBytes()
    {
        byte[] buffer = [0xFF, 0xFF, 0xFF, 0xFF];
        var writer = new LlrpBufferWriter(buffer);

        writer.Advance(1);
        writer.WriteBytes([0x10, 0x20]);

        Assert.Equal(new byte[] { 0xFF, 0x10, 0x20, 0xFF }, buffer);
        Assert.Equal(3, writer.WrittenSpan.Length);
    }

    [Fact]
    public void InsufficientCapacityThrowsWithoutWritingOrAdvancing()
    {
        byte[] buffer = [0xAA, 0xBB];
        var writer = new LlrpBufferWriter(buffer);
        writer.WriteByte(0x01);

        Exception? exception = null;
        try
        {
            writer.WriteUInt16(0x0203);
        }
        catch (Exception caught)
        {
            exception = caught;
        }

        InvalidOperationException error = Assert.IsType<InvalidOperationException>(exception);
        Assert.Contains("only 1 remain", error.Message, StringComparison.Ordinal);
        Assert.Equal(1, writer.Position);
        Assert.Equal(new byte[] { 0x01, 0xBB }, buffer);
    }

    [Fact]
    public void NegativeAdvanceThrowsWithoutAdvancing()
    {
        byte[] buffer = new byte[1];
        var writer = new LlrpBufferWriter(buffer);

        Exception? exception = null;
        try
        {
            writer.Advance(-1);
        }
        catch (Exception caught)
        {
            exception = caught;
        }

        Assert.IsType<ArgumentOutOfRangeException>(exception);
        Assert.Equal(0, writer.Position);
    }
}
