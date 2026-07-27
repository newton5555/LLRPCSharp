using LlrpSdk;
using LlrpVirtualReader;
using LlrpNet.Protocol.Messages.V1_0_1;
using LlrpNet.Protocol.Enumerations.V1_0_1;

namespace Interop.Tests;

public sealed class VirtualReaderSdkInteropTests
{
    [Fact]
    public async Task GetDefaultConfiguration_UsesSafeBaselineWithoutQueryingReaderConfiguration()
    {
        await using var host = new VirtualReaderHost(
            options: new VirtualReaderOptions
            {
                DropResponseForMessageTypes = new HashSet<ushort> { GET_READER_CONFIG.MessageType }
            });
        host.Start();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using LlrpReader reader = LlrpReader.CreateBuilder("127.0.0.1")
            .WithPort(host.Port)
            .WithConnectTimeout(TimeSpan.FromSeconds(2))
            .WithRequestTimeout(TimeSpan.FromMilliseconds(100))
            .WithProtocolVersionPolicy(LlrpProtocolVersionPolicy.Force101)
            .Build();

        await reader.ConnectAsync(timeout.Token);
        ReaderConfigurationDefaultsResult result = reader.GetDefaultConfigurationResult();
        ReaderConfiguration defaults = result.Configuration;

        Assert.Equal(LlrpSdk.KeepaliveTriggerType.None, defaults.Keepalive.TriggerType);
        Assert.Empty(defaults.Antennas);
        Assert.Empty(defaults.Gpos);
        Assert.True(result.IsGenericFallback);
        await Assert.ThrowsAsync<TimeoutException>(() => reader.QuerySettingsAsync(timeout.Token));
    }

    [Fact]
    public async Task GetDefaultConfiguration_SelectsMostSpecificRegisteredProfile()
    {
        await using var host = new VirtualReaderHost();
        host.Start();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using LlrpReader reader = LlrpReader.CreateBuilder("127.0.0.1")
            .WithPort(host.Port)
            .WithConnectTimeout(TimeSpan.FromSeconds(2))
            .WithRequestTimeout(TimeSpan.FromSeconds(2))
            .WithProtocolVersionPolicy(LlrpProtocolVersionPolicy.Force101)
            .UseConfigurationDefaultsProvider(new TestDefaultsProvider("vendor", 10, 1000))
            .UseConfigurationDefaultsProvider(new TestDefaultsProvider("model", 20, 2000))
            .Build();

        await reader.ConnectAsync(timeout.Token);
        ReaderConfigurationDefaultsResult result = reader.GetDefaultConfigurationResult();
        ReaderConfiguration defaults = result.Configuration;

        Assert.Equal(LlrpSdk.KeepaliveTriggerType.Periodic, defaults.Keepalive.TriggerType);
        Assert.Equal(2000U, defaults.Keepalive.IntervalMs);
        Assert.Equal("model", result.ProviderId);
        Assert.Equal("model.profile", result.ProfileId);
    }

    [Fact]
    public async Task GetDefaultConfiguration_RejectsAmbiguousProfilePriority()
    {
        await using var host = new VirtualReaderHost();
        host.Start();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using LlrpReader reader = LlrpReader.CreateBuilder("127.0.0.1")
            .WithPort(host.Port)
            .WithConnectTimeout(TimeSpan.FromSeconds(2))
            .WithRequestTimeout(TimeSpan.FromSeconds(2))
            .WithProtocolVersionPolicy(LlrpProtocolVersionPolicy.Force101)
            .UseConfigurationDefaultsProvider(new TestDefaultsProvider("one", 10, 1000))
            .UseConfigurationDefaultsProvider(new TestDefaultsProvider("two", 10, 2000))
            .Build();

        await reader.ConnectAsync(timeout.Token);
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(reader.GetDefaultConfiguration);

        Assert.Contains("one", exception.Message, StringComparison.Ordinal);
        Assert.Contains("two", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DroppedAddRoSpecResponse_ProducesSdkRequestTimeout()
    {
        await using var host = new VirtualReaderHost(
            options: new VirtualReaderOptions
            {
                DropResponseForMessageTypes = new HashSet<ushort> { ADD_ROSPEC.MessageType }
            });
        host.Start();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using LlrpReader reader = LlrpReader.CreateBuilder("127.0.0.1")
            .WithPort(host.Port)
            .WithConnectTimeout(TimeSpan.FromSeconds(2))
            .WithRequestTimeout(TimeSpan.FromMilliseconds(100))
            .WithProtocolVersionPolicy(LlrpProtocolVersionPolicy.Force101)
            .Build();

        await reader.ConnectAsync(timeout.Token);
        await Assert.ThrowsAsync<TimeoutException>(
            () => reader.StartAsync(new ReaderSettings { RoSpecId = 992 }, timeout.Token));
    }

    [Fact]
    public async Task InjectedAddRoSpecError_ProducesSdkOperationException()
    {
        await using var host = new VirtualReaderHost(
            options: new VirtualReaderOptions
            {
                ErrorResponseForMessageTypes = new Dictionary<ushort, VirtualReaderErrorResponse>
                {
                    [ADD_ROSPEC.MessageType] = new(StatusCode.M_ParameterError, "Injected ADD_ROSPEC failure.")
                }
            });
        host.Start();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using LlrpReader reader = LlrpReader.CreateBuilder("127.0.0.1")
            .WithPort(host.Port)
            .WithConnectTimeout(TimeSpan.FromSeconds(2))
            .WithRequestTimeout(TimeSpan.FromSeconds(2))
            .WithProtocolVersionPolicy(LlrpProtocolVersionPolicy.Force101)
            .Build();

        await reader.ConnectAsync(timeout.Token);
        LlrpReaderOperationException exception = await Assert.ThrowsAsync<LlrpReaderOperationException>(
            () => reader.StartAsync(new ReaderSettings { RoSpecId = 993 }, timeout.Token));

        Assert.Equal((ushort)StatusCode.M_ParameterError, exception.StatusCode);
        Assert.Equal("Injected ADD_ROSPEC failure.", exception.ErrorDescription);
    }

    [Fact]
    public async Task ConnectionClose_TriggersAutomaticReconnectWithoutRestoringInventory()
    {
        await using var host = new VirtualReaderHost(
            options: new VirtualReaderOptions
            {
                CloseConnectionAfterRequestMessageTypes = new HashSet<ushort> { GET_ROSPECS.MessageType }
            });
        host.Start();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using LlrpReader reader = LlrpReader.CreateBuilder("127.0.0.1")
            .WithPort(host.Port)
            .WithConnectTimeout(TimeSpan.FromSeconds(2))
            .WithRequestTimeout(TimeSpan.FromSeconds(2))
            .WithProtocolVersionPolicy(LlrpProtocolVersionPolicy.Force101)
            .WithAutomaticReconnect(new LlrpAutomaticReconnectOptions(
                maximumAttempts: 3,
                initialDelay: TimeSpan.FromMilliseconds(20),
                maximumDelay: TimeSpan.FromMilliseconds(50)))
            .Build();

        var reconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool faulted = false;
        reader.ConnectionChanged += (_, args) =>
        {
            if (args.CurrentState == ReaderConnectionState.Faulted)
            {
                faulted = true;
            }
            else if (faulted && args.CurrentState == ReaderConnectionState.Ready)
            {
                reconnected.TrySetResult();
            }
        };

        await reader.ConnectAsync(timeout.Token);
        await Assert.ThrowsAnyAsync<Exception>(() => reader.RoSpecs.GetAllAsync(timeout.Token));
        await reconnected.Task.WaitAsync(timeout.Token);

        Assert.Equal(ReaderConnectionState.Ready, reader.ConnectionState);
        Assert.Null(reader.CurrentSettings);
    }

    [Fact]
    public async Task QueryAndApplySettings_RoundTripAgainstVirtualReader()
    {
        await using var host = new VirtualReaderHost();
        host.Start();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using LlrpReader reader = LlrpReader.CreateBuilder("127.0.0.1")
            .WithPort(host.Port)
            .WithConnectTimeout(TimeSpan.FromSeconds(2))
            .WithRequestTimeout(TimeSpan.FromSeconds(2))
            .WithProtocolVersionPolicy(LlrpProtocolVersionPolicy.Force101)
            .Build();

        await reader.ConnectAsync(timeout.Token);
        ReaderConfiguration initial = await reader.QuerySettingsAsync(timeout.Token);
        Assert.Equal(4, initial.Antennas.Count);
        Assert.Equal(LlrpSdk.KeepaliveTriggerType.None, initial.Keepalive.TriggerType);
        Assert.False(Assert.Single(initial.Gpos).GpoData);

        await reader.ApplySettingsAsync(new ReaderConfiguration
        {
            Keepalive = new KeepaliveConfiguration
            {
                TriggerType = LlrpSdk.KeepaliveTriggerType.Periodic,
                IntervalMs = 1500
            },
            Gpos = [new GpoConfiguration { GpoPortNumber = 1, GpoData = true }]
        }, timeout.Token);

        await reader.SynchronizeStateAsync(timeout.Token);
        ReaderConfiguration updated = await reader.QuerySettingsAsync(timeout.Token);
        Assert.Equal(LlrpSdk.KeepaliveTriggerType.Periodic, updated.Keepalive.TriggerType);
        Assert.Equal(1500U, updated.Keepalive.IntervalMs);
        Assert.True(Assert.Single(updated.Gpos).GpoData);
    }

    [Fact]
    public async Task ResolveAndApplyConfigurationPatch_PreservesUnchangedVirtualReaderSettings()
    {
        await using var host = new VirtualReaderHost();
        host.Start();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using LlrpReader reader = LlrpReader.CreateBuilder("127.0.0.1")
            .WithPort(host.Port)
            .WithConnectTimeout(TimeSpan.FromSeconds(2))
            .WithRequestTimeout(TimeSpan.FromSeconds(2))
            .WithProtocolVersionPolicy(LlrpProtocolVersionPolicy.Force101)
            .Build();

        await reader.ConnectAsync(timeout.Token);
        var patch = new ReaderConfigurationPatch
        {
            Keepalive = new KeepaliveConfiguration
            {
                TriggerType = LlrpSdk.KeepaliveTriggerType.Periodic,
                IntervalMs = 2300
            }
        };

        ReaderConfiguration preview = await reader.ResolveConfigurationPatchAsync(patch, timeout.Token);
        Assert.Equal(2300U, preview.Keepalive.IntervalMs);
        Assert.Equal(4, preview.Antennas.Count);

        ReaderConfiguration beforeApply = await reader.QuerySettingsAsync(timeout.Token);
        Assert.Equal(LlrpSdk.KeepaliveTriggerType.None, beforeApply.Keepalive.TriggerType);

        await reader.ApplyConfigurationPatchAsync(patch, timeout.Token);
        await reader.SynchronizeStateAsync(timeout.Token);
        ReaderConfiguration afterApply = await reader.QuerySettingsAsync(timeout.Token);
        Assert.Equal(LlrpSdk.KeepaliveTriggerType.Periodic, afterApply.Keepalive.TriggerType);
        Assert.Equal(2300U, afterApply.Keepalive.IntervalMs);
        Assert.Equal(4, afterApply.Antennas.Count);
    }

    [Fact]
    public async Task QuerySettings_DoesNotRequireStateSynchronizationBeforeManagedInventory()
    {
        await using var host = new VirtualReaderHost();
        host.Start();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using LlrpReader reader = LlrpReader.CreateBuilder("127.0.0.1")
            .WithPort(host.Port)
            .WithConnectTimeout(TimeSpan.FromSeconds(2))
            .WithRequestTimeout(TimeSpan.FromSeconds(2))
            .WithProtocolVersionPolicy(LlrpProtocolVersionPolicy.Force101)
            .Build();

        await reader.ConnectAsync(timeout.Token);
        _ = await reader.QuerySettingsAsync(timeout.Token);
        await reader.StartAsync(new ReaderSettings { RoSpecId = 994 }, timeout.Token);
        try
        {
            Assert.Equal(ReaderOperationState.Inventorying, reader.OperationState);
        }
        finally
        {
            await reader.StopAsync(timeout.Token);
        }
    }

    [Fact]
    public async Task ManagedInventoryAndReadAccess_CompleteAgainstVirtualReader()
    {
        await using var host = new VirtualReaderHost();
        host.Start();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using LlrpReader reader = LlrpReader.CreateBuilder("127.0.0.1")
            .WithPort(host.Port)
            .WithConnectTimeout(TimeSpan.FromSeconds(5))
            .WithRequestTimeout(TimeSpan.FromSeconds(5))
            .WithProtocolVersionPolicy(LlrpProtocolVersionPolicy.Force101)
            .Build();

        await reader.ConnectAsync(timeout.Token);
        await reader.StartAsync(new ReaderSettings { RoSpecId = 991 }, timeout.Token);
        try
        {
            await using IAsyncEnumerator<TagReport> reports = reader.ReadTagReportsAsync(timeout.Token)
                .GetAsyncEnumerator(timeout.Token);
            Assert.True(await reports.MoveNextAsync());
            TagReport inventoryReport = reports.Current;
            Assert.Equal("E28011710000020D056E9BEE", Convert.ToHexString(inventoryReport.ElectronicProductCode.Span));
            Assert.Equal(991U, inventoryReport.RoSpecId);

            TagAccessResult result = await reader.ReadTagMemoryAsync(new ReadTagRequest
            {
                Selection = new TagSelection
                {
                    MemoryBank = TagMemoryBank.ElectronicProductCode,
                    BitPointer = 32,
                    BitLength = 96,
                    Mask = Enumerable.Repeat((byte)0xFF, 12).ToArray(),
                    Data = Convert.FromHexString("E28011710000020D056E9BEE"),
                },
                MemoryBank = TagMemoryBank.User,
                WordPointer = 0,
                WordCount = 2,
            }, TimeSpan.FromSeconds(5), timeout.Token);

            Assert.True(result.Operation.Success);
            Assert.Equal(new ushort[] { 0, 0 }, result.Operation.ReadData);
            Assert.NotNull(result.Tag.AccessSpecId);

            TagAccessResult write = await reader.WriteTagMemoryAsync(new WriteTagRequest
            {
                Selection = new TagSelection
                {
                    MemoryBank = TagMemoryBank.ElectronicProductCode,
                    BitPointer = 32,
                    BitLength = 96,
                    Mask = Enumerable.Repeat((byte)0xFF, 12).ToArray(),
                    Data = Convert.FromHexString("E28011710000020D056E9BEE"),
                },
                MemoryBank = TagMemoryBank.User,
                WordPointer = 1,
                WriteData = [0xABCD, 0x1234],
            }, TimeSpan.FromSeconds(5), timeout.Token);

            Assert.True(write.Operation.Success);
            Assert.Equal((ushort)2, write.Operation.WordsWritten);

            TagAccessResult readAfterWrite = await reader.ReadTagMemoryAsync(new ReadTagRequest
            {
                Selection = new TagSelection
                {
                    MemoryBank = TagMemoryBank.ElectronicProductCode,
                    BitPointer = 32,
                    BitLength = 96,
                    Mask = Enumerable.Repeat((byte)0xFF, 12).ToArray(),
                    Data = Convert.FromHexString("E28011710000020D056E9BEE"),
                },
                MemoryBank = TagMemoryBank.User,
                WordPointer = 1,
                WordCount = 2,
            }, TimeSpan.FromSeconds(5), timeout.Token);

            Assert.True(readAfterWrite.Operation.Success);
            Assert.Equal(new ushort[] { 0xABCD, 0x1234 }, readAfterWrite.Operation.ReadData);
        }
        finally
        {
            await reader.StopAsync(timeout.Token);
        }
    }

    private sealed class TestDefaultsProvider(string id, int priority, uint intervalMs) : IReaderConfigurationDefaultsProvider
    {
        public string Id { get; } = id;

        public ReaderConfigurationProfile? GetProfile(ReaderConfigurationProfileContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            return new ReaderConfigurationProfile(
                Id + ".profile",
                priority,
                new ReaderConfigurationPatch
                {
                    Keepalive = new KeepaliveConfiguration
                    {
                        TriggerType = LlrpSdk.KeepaliveTriggerType.Periodic,
                        IntervalMs = intervalMs
                    }
                });
        }
    }
}
