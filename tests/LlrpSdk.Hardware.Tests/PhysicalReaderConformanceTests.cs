using LlrpSdk;
using LlrpSdk.Extensions.Impinj;

namespace LlrpSdk.Hardware.Tests;

public sealed class PhysicalReaderConformanceTests
{
    [Fact]
    public async Task PhysicalReader_ConnectAndCapabilitiesConformance_Succeeds()
    {
        if (HardwareTestEnvironment.SkipReason is { } skip)
        {
            return; // Skip when hardware test is disabled
        }

        TargetReaderConfig readerInfo = HardwareTestEnvironment.Config.TargetReader;

        LlrpReaderBuilder builder = LlrpReader.CreateBuilder(readerInfo.Ip)
            .WithPort(readerInfo.Port)
            .WithConnectTimeout(TimeSpan.FromSeconds(5));

        if (readerInfo.SupportsImpinjExtensions)
        {
            builder.UseImpinj();
        }

        await using LlrpReader reader = builder.Build();
        await reader.ConnectAsync();

        Assert.True(reader.IsConnected, "Reader should be in connected state.");
        Assert.NotNull(reader.Capabilities);
        Assert.NotNull(reader.Identity);

        // Verify LLRP Conformance: Device capabilities must report valid antenna and power table
        Assert.True(reader.Capabilities.MaxNumberOfAntennas > 0, "Reader must report at least 1 antenna.");
        Assert.NotNull(reader.Capabilities.TxPowers);
        Assert.NotNull(reader.Capabilities.RfModes);
    }

    [Fact]
    public async Task PhysicalReader_InventorySessionLifecycle_Succeeds()
    {
        if (HardwareTestEnvironment.SkipReason is { } skip)
        {
            return;
        }

        TargetReaderConfig readerInfo = HardwareTestEnvironment.Config.TargetReader;

        LlrpReaderBuilder builder = LlrpReader.CreateBuilder(readerInfo.Ip)
            .WithPort(readerInfo.Port);

        if (readerInfo.SupportsImpinjExtensions)
        {
            builder.UseImpinj();
        }

        await using LlrpReader reader = builder.Build();
        await reader.ConnectAsync();

        // 1. Fetch defaults and apply
        ReaderSettingsDefaults defaults = await reader.GetDefaultSettingsAsync();
        Assert.NotNull(defaults.Settings);

        ReaderSettings settings = defaults.Settings.Edit(b => b
            .Inventory(inv => inv
                .Antennas(readerInfo.Antennas.ToArray())
                .ReportEveryTag()));

        await reader.ApplySettingsAsync(settings);

        // 2. Start inventory and sample tag reports for 3 seconds
        await using InventorySession session = await reader.StartInventoryAsync();
        Assert.NotNull(session);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var reports = new List<TagReport>();

        try
        {
            await foreach (TagReport report in session.ReadReportsAsync(cts.Token))
            {
                reports.Add(report);
                if (reports.Count >= 10)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected timeout when sampling
        }

        await session.StopAsync();
        await reader.ClearManagedSettingsAsync(); // Clean up even on failure so tests never poison the shared device.
    }
}
