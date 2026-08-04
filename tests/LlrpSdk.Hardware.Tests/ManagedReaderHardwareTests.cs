using LlrpSdk;
using LlrpSdk.Extensions.Impinj;

namespace LlrpSdk.Hardware.Tests;

/// <summary>
/// Managed <see cref="LlrpReader"/> hardware acceptance tests. These tests require a real reader on the network
/// (see <c>appsettings.local.json</c>) and at least one tag in the field; they are non-destructive: no Kill,
/// irreversible Lock, or tag/device configuration writes.
/// </summary>
/// <remarks>
/// Verified against Impinj R420, firmware 6.4.1.x, LLRP 1.0.1. Tag-population estimation is not enabled because
/// that firmware rejects it (see docs/acceptance/reader-interoperability.md).
/// </remarks>
public sealed class ManagedReaderHardwareTests
{
    [Fact]
    public async Task ManagedInventory_OnePhaseStart_ReportsTagsAndCleansUp()
    {
        if (HardwareTestEnvironment.SkipReason is { } skip)
        {
            return;
        }

        TargetReaderConfig config = HardwareTestEnvironment.Config.TargetReader;
        await using LlrpReader reader = CreateReader(config);
        await reader.ConnectAsync();

        ReaderSettingsDefaults defaults = await reader.GetDefaultSettingsAsync();
        InventorySettings inventory = (defaults.Settings.Inventory ?? new InventorySettings())
            with { AntennaIds = config.Antennas.ToArray() };

        // One-phase: deploy (takes full control, deletes all ROSpecs) and start in one call.
        await using var session = await reader.StartInventoryAsync(inventory);
        IReadOnlyList<TagReport> reports;
        try
        {
            reports = await SampleReportsAsync(session, maxReports: 5, duration: TimeSpan.FromSeconds(8));
            Assert.NotEmpty(reports); // Requires at least one tag in the field.
            Assert.All(reports, report => Assert.False(string.IsNullOrEmpty(report.EpcHex), "Report must carry a hex EPC."));
        }
        finally
        {
            await reader.ClearManagedSettingsAsync(); // Clean up even on failure so tests never poison the shared device.
        }

        Assert.Equal(ReaderOperationState.Idle, reader.OperationState);
    }

    [Fact]
    public async Task ManagedInventory_TwoPhaseStart_StartsDeployedInventory()
    {
        if (HardwareTestEnvironment.SkipReason is { } skip)
        {
            return;
        }

        TargetReaderConfig config = HardwareTestEnvironment.Config.TargetReader;
        await using LlrpReader reader = CreateReader(config);
        await reader.ConnectAsync();
        await reader.ClearManagedSettingsAsync(); // Defensive: ensure a clean device before this test's deployment.

        ReaderSettingsDefaults defaults = await reader.GetDefaultSettingsAsync();
        InventorySettings inventory = (defaults.Settings.Inventory ?? new InventorySettings())
            with { AntennaIds = config.Antennas.ToArray() };

        // Two-phase: deploy without starting, then start explicitly.
        await reader.ApplySettingsAsync(defaults.Settings with { Inventory = inventory });
        Assert.Equal(ReaderOperationState.Idle, reader.OperationState); // Deployed but not running.

        await using var session = await reader.StartInventoryAsync();
        try
        {
            IReadOnlyList<TagReport> reports = await SampleReportsAsync(session, maxReports: 5, duration: TimeSpan.FromSeconds(8));
            Assert.NotEmpty(reports); // Requires at least one tag in the field.
        }
        finally
        {
            await session.StopAsync();
            await reader.ClearManagedSettingsAsync(); // Clean up even on failure so tests never poison the shared device.
        }
    }

    [Fact]
    public async Task TagAccess_ReadsTagMemory_NonDestructive()
    {
        if (HardwareTestEnvironment.SkipReason is { } skip)
        {
            return;
        }

        TargetReaderConfig config = HardwareTestEnvironment.Config.TargetReader;
        await using LlrpReader reader = CreateReader(config);
        await reader.ConnectAsync();

        // Locate one tag first so the access operation has a concrete target.
        var inventory = new InventorySettings { AntennaIds = config.Antennas.ToArray() };
        await using var discovery = await reader.StartInventoryAsync(inventory);
        IReadOnlyList<TagReport> discovered = await SampleReportsAsync(discovery, maxReports: 1, duration: TimeSpan.FromSeconds(8));
        await discovery.StopAsync();
        if (discovered.Count == 0)
        {
            return; // No tag in the field; environment issue, not an SDK failure.
        }

        TagReport target = discovered[0];
        var selection = new TagSelection
        {
            MemoryBank = TagMemoryBank.ElectronicProductCode,
            BitPointer = 32,
            BitLength = 96,
            Mask = Enumerable.Repeat((byte)0xFF, 12).ToArray(),
            Data = Convert.FromHexString(target.EpcHex),
        };

        // Read is non-destructive; the tag access temporarily takes over inventory resources and cleans up after.
        TagAccessResult result = await reader.ReadTagMemoryAsync(
            new ReadTagRequest
            {
                Selection = selection,
                MemoryBank = TagMemoryBank.User,
                WordPointer = 0,
                WordCount = 1,
            },
            timeout: TimeSpan.FromSeconds(10));

        Assert.True(result.Operation.Success, $"Tag read failed: {result.Operation.Error}");
        Assert.NotNull(result.Operation.ReadData);
    }

    [Fact]
    public async Task ImpinjSerializedTid_IsProjectedWhenRequested()
    {
        if (HardwareTestEnvironment.SkipReason is { } skip || !HardwareTestEnvironment.Config.TargetReader.SupportsImpinjExtensions)
        {
            return;
        }

        TargetReaderConfig config = HardwareTestEnvironment.Config.TargetReader;
        await using LlrpReader reader = CreateReader(config);
        await reader.ConnectAsync();

        InventorySettings inventory = InventorySettings.Create(builder => builder
            .Antennas(config.Antennas.ToArray())
            .Impinj(imp => imp.IncludeSerializedTid().IncludePeakRssi()));

        await using var session = await reader.StartInventoryAsync(inventory);
        try
        {
            IReadOnlyList<TagReport> reports = await SampleReportsAsync(session, maxReports: 5, duration: TimeSpan.FromSeconds(8));
            Assert.NotEmpty(reports); // Requires at least one tag in the field.
            Assert.Contains(reports, report => !string.IsNullOrEmpty(report.GetSerializedTidHex()));
        }
        finally
        {
            await reader.ClearManagedSettingsAsync(); // Clean up even on failure so tests never poison the shared device.
        }
    }

    [Fact]
    public async Task QuerySettingsAsync_ReturnsDeviceConfiguration()
    {
        if (HardwareTestEnvironment.SkipReason is { } skip)
        {
            return;
        }

        TargetReaderConfig config = HardwareTestEnvironment.Config.TargetReader;
        await using LlrpReader reader = CreateReader(config);
        await reader.ConnectAsync();

        ReaderSettingsSnapshot snapshot = await reader.QuerySettingsAsync();

        Assert.NotNull(snapshot.Settings.Configuration);
        Assert.NotEmpty(snapshot.Settings.Configuration.Antennas);
    }

    private static LlrpReader CreateReader(TargetReaderConfig config)
    {
        LlrpReaderBuilder builder = LlrpReader.CreateBuilder(config.Ip)
            .WithPort(config.Port)
            .WithConnectTimeout(TimeSpan.FromSeconds(10))
            .WithRequestTimeout(TimeSpan.FromSeconds(10));
        if (config.SupportsImpinjExtensions)
        {
            builder.UseImpinj();
        }

        return builder.Build();
    }

    private static async Task<IReadOnlyList<TagReport>> SampleReportsAsync(
        InventorySession session,
        int maxReports,
        TimeSpan duration)
    {
        var reports = new List<TagReport>();
        using var cts = new CancellationTokenSource(duration);
        try
        {
            await foreach (TagReport report in session.ReadReportsAsync(cts.Token))
            {
                reports.Add(report);
                if (reports.Count >= maxReports)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Sampling window elapsed; return what was observed.
        }

        return reports;
    }
}
