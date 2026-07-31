using LlrpReaderStudio.Core;
using LlrpSdk;

namespace LlrpReaderStudio.Tests;

public sealed class TagAggregateStoreTests
{
    [Fact]
    public void Add_MergesSameEpcAcrossReaders()
    {
        var store = new TagAggregateStore();
        var first = new ReaderProfile { Name = "Dock A", Host = "reader-a" };
        var second = new ReaderProfile { Name = "Dock B", Host = "reader-b" };

        store.Add(first, Report(3, 1, -42), DateTimeOffset.UnixEpoch);
        TagObservation result = store.Add(second, Report(2, 2, -38), DateTimeOffset.UnixEpoch.AddSeconds(1));

        Assert.Equal("E2003412", result.Epc);
        Assert.Equal(5, result.ReadCount);
        Assert.Equal(new[] { "Dock A", "Dock B" }, result.Readers);
        Assert.Equal(new ushort[] { 1, 2 }, result.Antennas);
        Assert.Equal((sbyte)-38, result.LastRssi);
    }

    private static TagReport Report(ushort count, ushort antenna, sbyte rssi) => new(
        Convert.FromHexString("E2003412"),
        14150,
        1,
        1,
        antenna,
        rssi,
        1,
        null,
        null,
        count,
        null);
}
