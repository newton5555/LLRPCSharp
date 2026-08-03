using LlrpReaderStudio.Core;

namespace LlrpReaderStudio.Tests;

public sealed class HexCodecTests
{
    [Fact]
    public void ParseBytes_AllowsCommonSeparators()
    {
        Assert.Equal(new byte[] { 0xE2, 0x00, 0x34 }, HexCodec.ParseBytes("E2:00-34"));
    }

    [Fact]
    public void Words_RoundTrip()
    {
        ushort[] words = HexCodec.ParseWords("1234 ABCD 0001");

        Assert.Equal(new ushort[] { 0x1234, 0xABCD, 0x0001 }, words);
        Assert.Equal("1234ABCD0001", HexCodec.FormatWords(words));
    }

    [Theory]
    [InlineData("A")]
    [InlineData("123")]
    public void ParseBytes_RejectsHalfBytes(string value)
    {
        Assert.Throws<FormatException>(() => HexCodec.ParseBytes(value));
    }
}
