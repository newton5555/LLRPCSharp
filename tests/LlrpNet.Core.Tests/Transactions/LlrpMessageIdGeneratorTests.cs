using System.Collections.Concurrent;
using LlrpNet.Core.Transactions;

namespace LlrpNet.Core.Tests.Transactions;

public sealed class LlrpMessageIdGeneratorTests
{
    [Fact]
    public void Next_StartsAtOneAndIncrements()
    {
        var generator = new LlrpMessageIdGenerator();

        Assert.Equal((uint)1, generator.Next());
        Assert.Equal((uint)2, generator.Next());
    }

    [Fact]
    public void Next_CrossesSignedIntegerBoundary()
    {
        var generator = new LlrpMessageIdGenerator(int.MaxValue - 1u);

        Assert.Equal((uint)int.MaxValue, generator.Next());
        Assert.Equal(0x80000000u, generator.Next());
        Assert.Equal(0x80000001u, generator.Next());
    }

    [Fact]
    public void Next_AfterUnsignedWrapSkipsZero()
    {
        var generator = new LlrpMessageIdGenerator(uint.MaxValue - 1);

        Assert.Equal(uint.MaxValue, generator.Next());
        Assert.Equal((uint)1, generator.Next());
    }

    [Fact]
    public void Next_WhenCalledConcurrentlyReturnsDistinctNonZeroIdentifiers()
    {
        const int count = 20_000;
        var generator = new LlrpMessageIdGenerator();
        var identifiers = new ConcurrentBag<uint>();

        Parallel.For(0, count, _ => identifiers.Add(generator.Next()));

        Assert.Equal(count, identifiers.Count);
        Assert.DoesNotContain((uint)0, identifiers);
        Assert.Equal(count, identifiers.Distinct().Count());
    }
}
