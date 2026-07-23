using LlrpNet.Core.Buffers;

namespace LlrpNet.Core.Tests.Buffers;

public sealed class LlrpBitReaderTests
{
    [Fact]
    public void ReadBitUsesMostSignificantBitFirstOrder()
    {
        var reader = new LlrpBitReader([0b1010_0001]);

        bool[] expected = [true, false, true, false, false, false, false, true];
        foreach (bool value in expected)
        {
            Assert.Equal(value, reader.ReadBit());
        }

        Assert.True(reader.End);
        Assert.Equal(8, reader.BitPosition);
    }

    [Fact]
    public void ReadBitsCrossesByteBoundariesInMsbFirstOrder()
    {
        var reader = new LlrpBitReader([0b1100_1010, 0b0111_0100]);

        Assert.Equal(0b110ul, reader.ReadBits(3));
        Assert.Equal(0b010100111ul, reader.ReadBits(9));
        Assert.Equal(0b0100ul, reader.ReadBits(4));
        Assert.Equal(0, reader.RemainingBits);
    }

    [Fact]
    public void ReadBitsSupportsA64BitField()
    {
        var reader = new LlrpBitReader([0x01, 0x23, 0x45, 0x67, 0x89, 0xAB, 0xCD, 0xEF]);

        Assert.Equal(0x0123456789ABCDEFul, reader.ReadBits(64));
    }

    [Fact]
    public void BitOffsetStartsInsideAByte()
    {
        var reader = new LlrpBitReader([0b1110_0101], 3);

        Assert.Equal(0b00101ul, reader.ReadBits(5));
        Assert.True(reader.End);
    }

    [Fact]
    public void TruncatedReadThrowsWithoutAdvancing()
    {
        var reader = new LlrpBitReader([0b1010_0000], 5);

        Exception? exception = null;
        try
        {
            reader.ReadBits(4);
        }
        catch (Exception caught)
        {
            exception = caught;
        }

        EndOfStreamException error = Assert.IsType<EndOfStreamException>(exception);
        Assert.Contains("only 3 remain", error.Message, StringComparison.Ordinal);
        Assert.Equal(5, reader.BitPosition);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(65)]
    public void InvalidBitCountThrows(int bitCount)
    {
        var reader = new LlrpBitReader([0x00]);

        Exception? exception = null;
        try
        {
            reader.ReadBits(bitCount);
        }
        catch (Exception caught)
        {
            exception = caught;
        }

        Assert.IsType<ArgumentOutOfRangeException>(exception);
        Assert.Equal(0, reader.BitPosition);
    }
}
