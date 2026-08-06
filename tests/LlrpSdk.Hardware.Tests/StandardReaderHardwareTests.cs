using LlrpNet.Core.Protocol;
using LlrpSdk;

namespace LlrpSdk.Hardware.Tests;

/// <summary>
/// Standard-reader hardware tests: exercise the managed <see cref="LlrpReader"/> over the plain LLRP 1.0.1 protocol
/// path only — no vendor extensions (no <c>UseImpinj()</c>), forced LLRP 1.0.1. This is the acceptance path for
/// standards-only readers (see docs/acceptance/reader-interoperability.md "1.0.1 标准协议" row) and validates that
/// the SDK works without relying on any vendor extension.
/// </summary>
/// <remarks>
/// These tests require a real reader on the network and at least one tag in the field; non-destructive only.
/// </remarks>
public sealed class StandardReaderHardwareTests
{
    [Fact]
    public async Task StandardReader_ConnectInitializesAndReportsCapabilities()
    {
        if (HardwareTestEnvironment.SkipReason is { } skip)
        {
            return;
        }

        TargetReaderConfig config = HardwareTestEnvironment.Config.TargetReader;
        await using LlrpReader reader = CreateStandardReader(config);
        await reader.ConnectAsync();

        Assert.True(reader.IsConnected);
        Assert.Equal(LlrpProtocolVersion.Version101, reader.NegotiatedVersion);
        Assert.NotNull(reader.Identity);
        Assert.NotNull(reader.Capabilities);
        Assert.True(reader.Capabilities.MaxNumberOfAntennas > 0);
        Assert.NotNull(reader.Capabilities.TxPowers);
        Assert.NotNull(reader.Capabilities.RfModes);
    }

    [Fact]
    public async Task StandardReader_DefaultSettingsAndConfigurationQuery_Succeed()
    {
        if (HardwareTestEnvironment.SkipReason is { } skip)
        {
            return;
        }

        TargetReaderConfig config = HardwareTestEnvironment.Config.TargetReader;
        await using LlrpReader reader = CreateStandardReader(config);
        await reader.ConnectAsync();

        ReaderSettingsDefaults defaults = await reader.GetDefaultSettingsAsync();
        Assert.NotNull(defaults.Settings.Inventory);
        Assert.NotEmpty(defaults.Settings.Inventory!.AntennaIds);

        ReaderSettingsSnapshot snapshot = await reader.QuerySettingsAsync();
        Assert.NotNull(snapshot.Settings.Configuration);
        Assert.NotEmpty(snapshot.Settings.Configuration.Antennas);
    }

    [Fact]
    public async Task StandardReader_InventoryReportsTagsAndCleansUp()
    {
        if (HardwareTestEnvironment.SkipReason is { } skip)
        {
            return;
        }

        TargetReaderConfig config = HardwareTestEnvironment.Config.TargetReader;
        await using LlrpReader reader = CreateStandardReader(config);
        await reader.ConnectAsync();

        // Standard generic defaults carry no vendor extensions; antenna set comes from the test configuration.
        ReaderSettingsDefaults defaults = await reader.GetDefaultSettingsAsync();
        InventorySettings inventory = (defaults.Settings.Inventory ?? new InventorySettings())
            with
        { AntennaIds = config.Antennas.ToArray() };

        await using var session = await reader.StartInventoryAsync(inventory);
        var reports = new List<TagReport>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        try
        {
            try
            {
                await foreach (TagReport report in session.ReadReportsAsync(cts.Token))
                {
                    reports.Add(report);
                    if (reports.Count >= 5)
                    {
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Sampling window elapsed.
            }

            Assert.NotEmpty(reports); // Requires at least one tag in the field.
            Assert.All(reports, report =>
            {
                Assert.False(string.IsNullOrEmpty(report.EpcHex), "Standard report must carry a hex EPC.");
                Assert.True(report.AntennaId is > 0, "Standard report must carry an antenna id.");
            });
        }
        finally
        {
            await reader.ClearManagedSettingsAsync(); // Clean up even on failure so tests never poison the shared device.
        }

        Assert.Equal(ReaderOperationState.Idle, reader.OperationState);
    }

    /// <summary>Builds a reader without vendor extensions and with LLRP 1.0.1 forced.</summary>
    private static LlrpReader CreateStandardReader(TargetReaderConfig config)
    {
        return LlrpReader.CreateBuilder(config.Ip)
            .WithPort(config.Port)
            .WithProtocolVersionPolicy(LlrpProtocolVersionPolicy.Force101)
            .WithConnectTimeout(TimeSpan.FromSeconds(10))
            .WithRequestTimeout(TimeSpan.FromSeconds(10))
            .Build();
    }
}
