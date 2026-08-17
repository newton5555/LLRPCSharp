using LlrpVirtualReader;

namespace LlrpVirtualReader.Core.Tests;

public sealed class VirtualReaderOptionsTests
{
    [Fact]
    public void Host_rejects_invalid_reader_configuration_before_binding()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new VirtualReaderHost(
            new VirtualReaderHostOptions
            {
                ReaderOptions = new VirtualReaderOptions { MaximumClientConnections = 0 },
            }));

        Assert.Throws<ArgumentOutOfRangeException>(() => new VirtualReaderHost(
            new VirtualReaderHostOptions
            {
                ReaderOptions = new VirtualReaderOptions
                {
                    Reports = new VirtualReaderReportOptions { ReportCount = -1 },
                },
            }));

        Assert.Throws<ArgumentOutOfRangeException>(() => new VirtualReaderHost(
            new VirtualReaderHostOptions
            {
                ReaderOptions = new VirtualReaderOptions { FrameAssemblyTimeout = TimeSpan.Zero },
            }));

        Assert.Throws<ArgumentOutOfRangeException>(() => new VirtualReaderHost(
            new VirtualReaderHostOptions
            {
                ReaderOptions = new VirtualReaderOptions { MaximumFrameLength = 9 },
            }));

        Assert.Throws<ArgumentOutOfRangeException>(() => new VirtualReaderHost(
            new VirtualReaderHostOptions
            {
                ReaderOptions = new VirtualReaderOptions
                {
                    Capabilities = new VirtualReaderCapabilities { MaxNumberOfAntennas = 0 },
                },
            }));

        Assert.Throws<ArgumentException>(() => new VirtualReaderHost(
            new VirtualReaderHostOptions
            {
                ReaderOptions = new VirtualReaderOptions
                {
                    AntennaConfigurations = [new VirtualReaderAntennaConfiguration { AntennaId = 0 }],
                },
            }));
    }

    [Fact]
    public void Fixed_tag_source_is_deterministic_and_supports_mutable_user_memory()
    {
        FixedVirtualTagSource source = FixedVirtualTagSource.CreateDefault();
        VirtualTag tag = Assert.Single(source.GetTags());
        byte[] epc = tag.ElectronicProductCode.ToArray();

        Assert.Equal("E28011710000020D056E9BEE", Convert.ToHexString(epc));
        Assert.True(source.TryReadWords(epc, 3, 0, 2, out IReadOnlyList<ushort> before));
        Assert.Equal(new ushort[] { 0, 0 }, before);

        Assert.True(source.TryWriteWords(epc, 3, 1, [0xABCD, 0x1234]));
        Assert.True(source.TryReadWords(epc, 3, 1, 2, out IReadOnlyList<ushort> after));
        Assert.Equal(new ushort[] { 0xABCD, 0x1234 }, after);
    }
}
