using LlrpSdk;
using LlrpVirtualReader;
using LlrpNet.Protocol.Messages.V1_0_1;
using LlrpNet.Protocol.Enumerations.V1_0_1;

namespace Interop.Tests;

public sealed class VirtualReaderSdkInteropTests
{
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
            () => reader.StartAsync(new InventorySettings(), timeout.Token));
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
            () => reader.StartAsync(new InventorySettings(), timeout.Token));

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
        Assert.Null(reader.CurrentInventorySettings);
    }

    [Fact]
    public async Task TruncatedResponse_FaultsTheReceiveLoop()
    {
        await using var host = new VirtualReaderHost(options: new VirtualReaderOptions
        {
            TruncateResponseForMessageTypes = new HashSet<ushort> { GET_ROSPECS.MessageType }
        });
        host.Start();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using LlrpReader reader = LlrpReader.CreateBuilder("127.0.0.1")
            .WithPort(host.Port).WithConnectTimeout(TimeSpan.FromSeconds(2))
            .WithRequestTimeout(TimeSpan.FromSeconds(2))
            .WithProtocolVersionPolicy(LlrpProtocolVersionPolicy.Force101).Build();
        await reader.ConnectAsync(timeout.Token);

        await Assert.ThrowsAnyAsync<Exception>(() => reader.RoSpecs.GetAllAsync(timeout.Token));
        await Task.Delay(50, timeout.Token);
        Assert.Equal(ReaderConnectionState.Faulted, reader.ConnectionState);
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
        ReaderSettings initialSettings = (await reader.QuerySettingsAsync(timeout.Token)).Settings;
        ReaderConfiguration initial = initialSettings.Configuration;
        Assert.Equal(4, initial.Antennas.Count);
        Assert.Equal(LlrpSdk.KeepaliveTriggerType.None, initial.Keepalive.TriggerType);
        Assert.False(Assert.Single(initial.Gpos).GpoData);

        await reader.ApplySettingsAsync(new ReaderSettings
        {
            Configuration = initial with
            {
                Keepalive = new KeepaliveConfiguration
                {
                    TriggerType = LlrpSdk.KeepaliveTriggerType.Periodic,
                    IntervalMs = 1500
                },
                Gpos = [new GpoConfiguration { GpoPortNumber = 1, GpoData = true }]
            }
        }, timeout.Token);

        ReaderConfiguration updated = (await reader.QuerySettingsAsync(timeout.Token)).Settings.Configuration;
        Assert.Equal(LlrpSdk.KeepaliveTriggerType.Periodic, updated.Keepalive.TriggerType);
        Assert.Equal(1500U, updated.Keepalive.IntervalMs);
        Assert.True(Assert.Single(updated.Gpos).GpoData);
    }

    [Fact]
    public async Task ApplySettings_WithInventory_ContinuesAfterManagedConfigurationWrite()
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
        ReaderConfiguration configuration = (await reader.QuerySettingsAsync(timeout.Token)).Settings.Configuration;

        await reader.ApplySettingsAsync(new ReaderSettings
        {
            Configuration = configuration,
            Inventory = new InventorySettings { AntennaIds = [1] }
        }, timeout.Token);

        Assert.True(reader.IsManagedStateSynchronized);
        Assert.Equal(ReaderResourceMode.HighLevelConfigured, reader.ResourceMode);
        ReaderSettingsSnapshot deployed = await reader.QuerySettingsAsync(timeout.Token);
        Assert.Equal(InventoryRuntimeState.Disabled, deployed.Inventory!.State);
        await reader.StartAsync(timeout.Token);
        Assert.Equal(ReaderResourceMode.HighLevelRunning, reader.ResourceMode);
        await reader.StopAsync(timeout.Token);
        await reader.ClearManagedSettingsAsync(timeout.Token);
    }

    [Fact]
    public async Task QuerySettings_RehydratesManaged101InventoryFiltersAttachedDataAndState()
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

        var inventory = new InventorySettings
        {
            Session = 2,
            TagPopulationEstimate = 64,
            Filters =
            [
                new InventorySelectFilter
                {
                    MemoryBank = 1,
                    BitPointer = 32,
                    Mask = new byte[] { 0b_1010_0000 },
                    BitLength = 4,
                    MatchAction = InventorySelectAction.Select,
                    NonMatchAction = InventorySelectAction.DoNothing,
                }
            ],
            AttachedData = new AttachedDataOptions
            {
                Enabled = true,
                MemoryBank = 2,
                WordPointer = 3,
                WordCount = 2,
                AccessPassword = "00000000",
            },
        };

        await reader.ConnectAsync(timeout.Token);
        await reader.StartAsync(inventory, timeout.Token);
        try
        {
            ReaderSettingsSnapshot snapshot = await reader.QuerySettingsAsync(timeout.Token);
            InventorySnapshot managed = Assert.IsType<InventorySnapshot>(snapshot.Inventory);

            Assert.Equal(InventoryRuntimeState.Running, managed.State);
            Assert.Equal((byte)2, managed.Settings.Session);
            Assert.Equal((ushort)64, managed.Settings.TagPopulationEstimate);
            InventorySelectFilter filter = Assert.Single(managed.Settings.Filters);
            Assert.Equal((ushort)4, filter.BitLength);
            Assert.Equal(new byte[] { 0b_1010_0000 }, filter.Mask);
            Assert.True(managed.Settings.AttachedData.Enabled);
            Assert.Equal((ushort)3, managed.Settings.AttachedData.WordPointer);
            Assert.Equal((ushort)2, managed.Settings.AttachedData.WordCount);
        }
        finally
        {
            await reader.StopAsync(timeout.Token);
        }
    }

    [Fact]
    public async Task ManualMode_HighLevelTakeoverAndRawSynchronizationFollowResourceContract()
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
        Assert.Equal(ReaderResourceMode.Idle, reader.ResourceMode);

        await reader.EnterManualResourceModeAsync(timeout.Token);
        Assert.Equal(ReaderResourceMode.ManualResources, reader.ResourceMode);
        await reader.RoSpecs.AddDefaultAsync(600, new InventorySettings(), timeout.Token);

        await reader.StartAsync(new InventorySettings(), timeout.Token);
        try
        {
            Assert.Equal(ReaderResourceMode.HighLevelRunning, reader.ResourceMode);
            IReadOnlyList<LlrpNet.Protocol.Parameters.ILlrpParameter> resources = await reader.RoSpecs.GetAllAsync(timeout.Token);
            Assert.Single(resources);
            Assert.Equal(14150U, Assert.IsType<LlrpNet.Protocol.Parameters.V1_0_1.ROSpec>(resources[0]).ROSpecID);
            await Assert.ThrowsAsync<InvalidOperationException>(() => reader.EnterManualResourceModeAsync(timeout.Token));
        }
        finally
        {
            await reader.StopAsync(timeout.Token);
        }

        Assert.Equal(ReaderResourceMode.HighLevelConfigured, reader.ResourceMode);
        Assert.NotNull(reader.CurrentInventorySettings);
        ReaderSettingsSnapshot stopped = await reader.QuerySettingsAsync(timeout.Token);
        Assert.NotNull(stopped.Settings.Inventory);
        Assert.Equal(InventoryRuntimeState.Disabled, stopped.Inventory!.State);
        await reader.StartAsync(timeout.Token);
        Assert.Equal(ReaderResourceMode.HighLevelRunning, reader.ResourceMode);
        await reader.StopAsync(timeout.Token);
        _ = await reader.Protocol.TransactAsync<GET_ROSPECS_RESPONSE>(
            new GET_ROSPECS(reader.Protocol.NextMessageId()), cancellationToken: timeout.Token);
        Assert.Equal(ReaderResourceMode.StateUnknown, reader.ResourceMode);
        await Assert.ThrowsAsync<InvalidOperationException>(() => reader.StartAsync(new InventorySettings(), timeout.Token));

        await reader.SynchronizeStateAsync(timeout.Token);
        Assert.Equal(ReaderResourceMode.HighLevelConfigured, reader.ResourceMode);
        await reader.ClearManagedSettingsAsync(timeout.Token);
        Assert.Equal(ReaderResourceMode.Idle, reader.ResourceMode);
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
        await reader.StartAsync(new InventorySettings(), timeout.Token);
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
        await reader.StartAsync(new InventorySettings(), timeout.Token);
        try
        {
            await using IAsyncEnumerator<TagReport> reports = reader.ReadTagReportsAsync(timeout.Token)
                .GetAsyncEnumerator(timeout.Token);
            Assert.True(await reports.MoveNextAsync());
            TagReport inventoryReport = reports.Current;
            Assert.Equal("E28011710000020D056E9BEE", Convert.ToHexString(inventoryReport.ElectronicProductCode.Span));
            Assert.Equal(14150U, inventoryReport.RoSpecId);

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

}
