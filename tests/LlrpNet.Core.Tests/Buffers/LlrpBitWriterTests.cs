using LlrpNet.Core.Buffers;

namespace LlrpNet.Core.Tests.Buffers;

public sealed class LlrpBitWriterTests
{
    [Fact]
    public void WriteBitsUsesMsbFirstOrderAcrossByteBoundaries()
    {
        byte[] buffer = new byte[2];
        var writer = new LlrpBitWriter(buffer);

        writer.WriteBits(0b110, 3);
        writer.WriteBits(0b010100111, 9);
        writer.WriteBits(0b0100, 4);

        Assert.Equal(new byte[] { 0b1100_1010, 0b0111_0100 }, buffer);
        Assert.Equal(16, writer.BitPosition);
        Assert.Equal(2, writer.BytesWritten);
        Assert.Equal(2, writer.WrittenSpan.Length);
    }

    [Fact]
    public void WriteBitSetsAndClearsIndividualBits()
    {
        byte[] buffer = [0xFF];
        var writer = new LlrpBitWriter(buffer);

        writer.WriteBit(false);
        writer.WriteBit(true);
        writer.WriteBit(false);

        Assert.Equal(0b0101_1111, buffer[0]);
    }

    [Fact]
    public void BitOffsetPreservesBitsOutsideTheWrittenField()
    {
        byte[] buffer = [0xFF];
        var writer = new LlrpBitWriter(buffer, 2);

        writer.WriteBits(0, 3);

        Assert.Equal(0b1100_0111, buffer[0]);
        Assert.Equal(5, writer.BitPosition);
    }

    [Fact]
    public void WriteBitsSupportsA64BitField()
    {
        byte[] buffer = new byte[8];
        var writer = new LlrpBitWriter(buffer);

        writer.WriteBits(0x0123456789ABCDEF, 64);

        Assert.Equal(new byte[] { 0x01, 0x23, 0x45, 0x67, 0x89, 0xAB, 0xCD, 0xEF }, buffer);
    }

    [Fact]
    public void InsufficientCapacityThrowsWithoutWritingOrAdvancing()
    {
        byte[] buffer = [0x00];
        var writer = new LlrpBitWriter(buffer);
        writer.WriteBits(0b1010, 4);

        Exception? exception = null;
        try
        {
            writer.WriteBits(0b1_1111, 5);
        }
        catch (Exception caught)
        {
            exception = caught;
        }

        InvalidOperationException error = Assert.IsType<InvalidOperationException>(exception);
        Assert.Contains("only 4 remain", error.Message, StringComparison.Ordinal);
        Assert.Equal(4, writer.BitPosition);
        Assert.Equal(0b1010_0000, buffer[0]);
    }

    [Fact]
    public void ValueTooWideForFieldThrowsWithoutWritingOrAdvancing()
    {
        byte[] buffer = [0xFF];
        var writer = new LlrpBitWriter(buffer);

        Exception? exception = null;
        try
        {
            writer.WriteBits(0b1000, 3);
        }
        catch (Exception caught)
        {
            exception = caught;
        }

        Assert.IsType<ArgumentOutOfRangeException>(exception);
        Assert.Equal(0, writer.BitPosition);
        Assert.Equal(0xFF, buffer[0]);
    }
}
