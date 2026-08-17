using LlrpVirtualReader;

namespace LlrpVirtualReader.Core.Tests;

public sealed class VirtualReaderBackendTests
{
    [Fact]
    public void Static_backend_preserves_the_default_tag_and_antenna_selection()
    {
        var backend = new VirtualReaderInventoryBackend(
            FixedVirtualTagSource.CreateDefault(),
            new VirtualReaderRfSimulationOptions { Scenario = VirtualReaderRfScenario.Static });

        IReadOnlyList<VirtualTag> observations = backend.Observe(
            new VirtualReaderInventoryRound(14150, 0, [1]));

        VirtualTag tag = Assert.Single(observations);
        Assert.Equal("E28011710000020D056E9BEE", Convert.ToHexString(tag.ElectronicProductCode.Span));
        Assert.Equal((ushort)1, tag.AntennaId);
    }

    [Fact]
    public void Moving_tags_are_repeatable_and_have_presence_windows()
    {
        var source = new FixedVirtualTagSource(
        [
            new VirtualTag { ElectronicProductCode = Convert.FromHexString("E28000000000000000000001") },
            new VirtualTag { ElectronicProductCode = Convert.FromHexString("E28000000000000000000002") },
        ]);
        var options = new VirtualReaderRfSimulationOptions
        {
            Scenario = VirtualReaderRfScenario.MovingTags,
            PresenceCycleRounds = 2,
        };
        var first = new VirtualReaderInventoryBackend(source, options);
        var second = new VirtualReaderInventoryBackend(source, options);

        for (int round = 0; round < 8; round++)
        {
            IReadOnlyList<VirtualTag> left = first.Observe(new VirtualReaderInventoryRound(1, round, []));
            IReadOnlyList<VirtualTag> right = second.Observe(new VirtualReaderInventoryRound(1, round, []));
            Assert.Equal(
                left.Select(static tag => Convert.ToHexString(tag.ElectronicProductCode.Span)),
                right.Select(static tag => Convert.ToHexString(tag.ElectronicProductCode.Span)));
        }

        string firstRound = string.Join(
            ",",
            first.Observe(new VirtualReaderInventoryRound(1, 0, []))
                .Select(static tag => Convert.ToHexString(tag.ElectronicProductCode.Span)));
        string laterRound = string.Join(
            ",",
            first.Observe(new VirtualReaderInventoryRound(1, 2, []))
                .Select(static tag => Convert.ToHexString(tag.ElectronicProductCode.Span)));
        Assert.NotEqual(firstRound, laterRound);
    }

    [Fact]
    public void Noisy_backend_is_seeded_and_applies_detection_and_rssi_rules()
    {
        var source = new FixedVirtualTagSource(
        [
            new VirtualTag
            {
                ElectronicProductCode = Convert.FromHexString("E28000000000000000000001"),
                PeakRssi = -40,
            },
        ]);
        var options = new VirtualReaderRfSimulationOptions
        {
            Scenario = VirtualReaderRfScenario.Noisy,
            RandomSeed = 7,
            DetectionProbability = 1,
            RssiJitterDb = 3,
        };
        var first = new VirtualReaderInventoryBackend(source, options);
        var second = new VirtualReaderInventoryBackend(source, options);

        IReadOnlyList<VirtualTag> left = first.Observe(new VirtualReaderInventoryRound(1, 4, []));
        IReadOnlyList<VirtualTag> right = second.Observe(new VirtualReaderInventoryRound(1, 4, []));

        VirtualTag leftTag = Assert.Single(left);
        VirtualTag rightTag = Assert.Single(right);
        Assert.Equal(
            Convert.ToHexString(leftTag.ElectronicProductCode.Span),
            Convert.ToHexString(rightTag.ElectronicProductCode.Span));
        Assert.Equal(leftTag.PeakRssi, rightTag.PeakRssi);
        Assert.InRange(leftTag.PeakRssi, (short)-43, (short)-37);
    }

    [Fact]
    public void Virtual_backend_implements_the_device_behavior_contract()
    {
        var backend = new VirtualReaderDeviceBackend(new VirtualReaderOptions());

        Assert.IsAssignableFrom<ILlrpReaderDeviceBackend>(backend);
        Assert.NotNull(backend.Inventory);
        Assert.Equal(LlrpNet.Core.Protocol.LlrpProtocolVersion.Version101, backend.Options.ProtocolVersion);
    }

    [Fact]
    public async Task Host_uses_the_explicit_device_backend_factory()
    {
        bool factoryCalled = false;
        await using var host = new VirtualReaderHost(
            new VirtualReaderHostOptions
            {
                ReaderOptions = new VirtualReaderOptions(),
                DeviceBackendFactory = options =>
                {
                    factoryCalled = true;
                    return new VirtualReaderDeviceBackend(options);
                },
            });

        Assert.True(factoryCalled);
    }
}
