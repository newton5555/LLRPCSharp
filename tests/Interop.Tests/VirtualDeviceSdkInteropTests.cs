using LlrpDevice.Server;
using LlrpDevice.Virtual;
using LlrpDevice.Virtual.Hosting;
using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Enumerations.V1_0_1;
using LlrpNet.Protocol.Messages.V1_0_1;
using LlrpNet.Protocol.Parameters.V1_0_1;
using LlrpNet.Protocol.Messages;
using LlrpSdk;
using V101Messages = LlrpNet.Protocol.Messages.V1_0_1;

namespace Interop.Tests;

public sealed class VirtualDeviceSdkInteropTests
{
    private static VirtualLlrpDeviceHost CreateHost(
        LlrpDeviceServerOptions? serverOptions = null,
        VirtualDeviceOptions? deviceOptions = null) =>
        new(new VirtualLlrpDeviceHostOptions
        {
            Server = serverOptions ?? new LlrpDeviceServerOptions { Port = 0 },
            Device = deviceOptions ?? new VirtualDeviceOptions(),
        });

    private static LlrpReader CreateReader(int port) => LlrpReader.CreateBuilder("127.0.0.1")
        .WithPort(port)
        .WithConnectTimeout(TimeSpan.FromSeconds(2))
        .WithRequestTimeout(TimeSpan.FromSeconds(2))
        .WithProtocolVersionPolicy(LlrpProtocolVersionPolicy.Force101)
        .Build();

    [Fact]
    public async Task Unknown_message_receives_correlated_unsupported_message_error()
    {
        await using var host = CreateHost();
        await host.StartAsync();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using LlrpReader reader = LlrpReader.CreateBuilder("127.0.0.1")
            .WithPort(host.BoundPort)
            .WithConnectTimeout(TimeSpan.FromSeconds(2))
            .WithRequestTimeout(TimeSpan.FromSeconds(2))
            .WithProtocolVersionPolicy(LlrpProtocolVersionPolicy.Force101)
            .Build();

        await reader.ConnectAsync(timeout.Token);
        uint messageId = reader.Protocol.NextMessageId();
        var request = new UnknownMessage(
            LlrpProtocolVersion.Version101,
            messageType: 999,
            messageId,
            payload: []);
        ReadOnlyMemory<byte> responseFrame = await reader.Protocol.TransactRawAsync(
            reader.Registry.EncodeMessage(LlrpProtocolVersion.Version101, request),
            (header, _) => header.MessageId == messageId && header.MessageType == ERROR_MESSAGE.MessageType,
            cancellationToken: timeout.Token);

        V101Messages.ERROR_MESSAGE response = Assert.IsType<V101Messages.ERROR_MESSAGE>(
            reader.Registry.DecodeMessage(responseFrame.Span));
        Assert.Equal(messageId, response.MessageId);
        Assert.Equal(StatusCode.M_UnsupportedMessage, response.LLRPStatus.StatusCode);
    }

    [Fact]
    public async Task DisableActiveRoSpec_is_strict_by_default_and_can_be_relaxed_for_compatibility()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await using (var strictHost = CreateHost())
        {
            await strictHost.StartAsync(timeout.Token);
            await using var reader = CreateReader(strictHost.BoundPort);
            await reader.ConnectAsync(timeout.Token);
            _ = await reader.StartInventoryAsync(new InventorySettings(), timeout.Token);

            V101Messages.DISABLE_ROSPEC_RESPONSE strictResponse = await reader.Protocol.TransactAsync<V101Messages.DISABLE_ROSPEC_RESPONSE>(
                new V101Messages.DISABLE_ROSPEC(reader.Protocol.NextMessageId(), 14150),
                cancellationToken: timeout.Token);
            Assert.Equal(StatusCode.M_ParameterError, strictResponse.LLRPStatus.StatusCode);
            Assert.Contains("active", strictResponse.LLRPStatus.ErrorDescription, StringComparison.OrdinalIgnoreCase);
        }

        await using (var compatibilityHost = CreateHost(
            new LlrpDeviceServerOptions
            {
                Port = 0,
                AllowImplicitStopOnDisable = true,
            }))
        {
            await compatibilityHost.StartAsync(timeout.Token);
            await using var reader = CreateReader(compatibilityHost.BoundPort);
            await reader.ConnectAsync(timeout.Token);
            _ = await reader.StartInventoryAsync(new InventorySettings(), timeout.Token);

            _ = await reader.Protocol.TransactAsync<V101Messages.DISABLE_ROSPEC_RESPONSE>(
                new V101Messages.DISABLE_ROSPEC(reader.Protocol.NextMessageId(), 14150),
                cancellationToken: timeout.Token);
            V101Messages.GET_ROSPECS_RESPONSE response = await reader.Protocol.TransactAsync<V101Messages.GET_ROSPECS_RESPONSE>(
                new V101Messages.GET_ROSPECS(reader.Protocol.NextMessageId()),
                cancellationToken: timeout.Token);

            Assert.Equal(ROSpecState.Disabled, Assert.Single(response.ROSpecItems).CurrentState);
        }
    }

    [Fact]
    public async Task Llrp11Negotiation_and_inventory_workflow_complete_over_message_level_host()
    {
        await using var host = CreateHost(
            new LlrpDeviceServerOptions
            {
                Port = 0,
                ProtocolVersion = LlrpProtocolVersion.Version11,
                Reports = new LlrpDeviceReportOptions
                {
                    ReportInterval = TimeSpan.FromMilliseconds(20),
                    ReportCount = 2,
                    Repeat = true,
                },
            });
        await host.StartAsync();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using LlrpReader reader = LlrpReader.CreateBuilder("127.0.0.1")
            .WithPort(host.BoundPort)
            .WithConnectTimeout(TimeSpan.FromSeconds(2))
            .WithRequestTimeout(TimeSpan.FromSeconds(2))
            .WithProtocolVersionPolicy(LlrpProtocolVersionPolicy.Force11)
            .Build();

        await reader.ConnectAsync(timeout.Token);
        Assert.Equal(LlrpNet.Core.Protocol.LlrpProtocolVersion.Version11, reader.NegotiatedVersion);
        ReaderCapabilities capabilities = await reader.RefreshCapabilitiesAsync(timeout.Token);
        Assert.Equal((ushort)4, capabilities.MaxNumberOfAntennas);

        await using InventorySession session = await reader.StartInventoryAsync(new InventorySettings(), timeout.Token);
        await using IAsyncEnumerator<TagReport> reports = session.ReadReportsAsync(timeout.Token)
            .GetAsyncEnumerator(timeout.Token);
        Assert.True(await reports.MoveNextAsync());
        Assert.Equal("E28011710000020D056E9BEE", Convert.ToHexString(reports.Current.ElectronicProductCode.Span));
        await session.StopAsync(timeout.Token);
    }

    [Fact]
    public async Task InventorySessionAndEventObserver_AreMutuallyExclusive()
    {
        await using var host = CreateHost();
        await host.StartAsync();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using LlrpReader reader = LlrpReader.CreateBuilder("127.0.0.1")
            .WithPort(host.BoundPort)
            .WithConnectTimeout(TimeSpan.FromSeconds(2))
            .WithRequestTimeout(TimeSpan.FromSeconds(2))
            .WithProtocolVersionPolicy(LlrpProtocolVersionPolicy.Force101)
            .Build();

        await reader.ConnectAsync(timeout.Token);
        await using InventorySession session = await reader.StartInventoryAsync(new InventorySettings(), timeout.Token);
        await using IAsyncEnumerator<TagReport> sessionReports = session.ReadReportsAsync(timeout.Token)
            .GetAsyncEnumerator(timeout.Token);
        EventHandler<TagReportEventArgs> handler = (_, _) => { };
        Assert.Throws<InvalidOperationException>(() => reader.TagsReported += handler);
        await session.StopAsync(timeout.Token);
    }

    [Fact]
    public async Task EventObserverSelectedBeforeInventory_RejectsSessionReader()
    {
        await using var host = CreateHost();
        await host.StartAsync();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using LlrpReader reader = LlrpReader.CreateBuilder("127.0.0.1")
            .WithPort(host.BoundPort)
            .WithConnectTimeout(TimeSpan.FromSeconds(2))
            .WithRequestTimeout(TimeSpan.FromSeconds(2))
            .WithProtocolVersionPolicy(LlrpProtocolVersionPolicy.Force101)
            .Build();

        EventHandler<TagReportEventArgs> handler = (_, _) => { };
        reader.TagsReported += handler;
        try
        {
            await reader.ConnectAsync(timeout.Token);
            await using InventorySession session = await reader.StartInventoryAsync(new InventorySettings(), timeout.Token);
            Assert.Throws<InvalidOperationException>(() => session.ReadReportsAsync(timeout.Token));
            Assert.Throws<InvalidOperationException>(() => reader.ReadTagReportsAsync(timeout.Token));
            await session.StopAsync(timeout.Token);
        }
        finally
        {
            reader.TagsReported -= handler;
        }
    }

    [Fact]
    public async Task ReaderAsyncObserverSelected_RejectsSessionReader()
    {
        await using var host = CreateHost();
        await host.StartAsync();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using LlrpReader reader = LlrpReader.CreateBuilder("127.0.0.1")
            .WithPort(host.BoundPort)
            .WithConnectTimeout(TimeSpan.FromSeconds(2))
            .WithRequestTimeout(TimeSpan.FromSeconds(2))
            .WithProtocolVersionPolicy(LlrpProtocolVersionPolicy.Force101)
            .Build();

        await reader.ConnectAsync(timeout.Token);
        await using InventorySession session = await reader.StartInventoryAsync(new InventorySettings(), timeout.Token);
        await using IAsyncEnumerator<TagReport> reports = reader.ReadTagReportsAsync(timeout.Token)
            .GetAsyncEnumerator(timeout.Token);
        Assert.Throws<InvalidOperationException>(() => session.ReadReportsAsync(timeout.Token));
        Assert.Throws<InvalidOperationException>(() => reader.TagsReported += (_, _) => { });
        await session.StopAsync(timeout.Token);
    }

    [Fact]
    public async Task DroppedAddRoSpecResponse_ProducesSdkRequestTimeout()
    {
        await using var host = CreateHost(
            new LlrpDeviceServerOptions
            {
                Port = 0,
                DropResponseForMessageTypes = new HashSet<ushort> { ADD_ROSPEC.MessageType }
            });
        await host.StartAsync();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using LlrpReader reader = LlrpReader.CreateBuilder("127.0.0.1")
            .WithPort(host.BoundPort)
            .WithConnectTimeout(TimeSpan.FromSeconds(2))
            .WithRequestTimeout(TimeSpan.FromMilliseconds(100))
            .WithProtocolVersionPolicy(LlrpProtocolVersionPolicy.Force101)
            .Build();

        await reader.ConnectAsync(timeout.Token);
        await Assert.ThrowsAsync<TimeoutException>(
            () => reader.StartInventoryAsync(new InventorySettings(), timeout.Token));
    }

    [Fact]
    public async Task InjectedAddRoSpecError_ProducesSdkOperationException()
    {
        await using var host = CreateHost(
            new LlrpDeviceServerOptions
            {
                Port = 0,
                ErrorResponseForMessageTypes = new Dictionary<ushort, LlrpDeviceServerErrorResponse>
                {
                    [ADD_ROSPEC.MessageType] = new((ushort)StatusCode.M_ParameterError, "Injected ADD_ROSPEC failure.")
                }
            });
        await host.StartAsync();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using LlrpReader reader = LlrpReader.CreateBuilder("127.0.0.1")
            .WithPort(host.BoundPort)
            .WithConnectTimeout(TimeSpan.FromSeconds(2))
            .WithRequestTimeout(TimeSpan.FromSeconds(2))
            .WithProtocolVersionPolicy(LlrpProtocolVersionPolicy.Force101)
            .Build();

        await reader.ConnectAsync(timeout.Token);
        LlrpReaderOperationException exception = await Assert.ThrowsAsync<LlrpReaderOperationException>(
            () => reader.StartInventoryAsync(new InventorySettings(), timeout.Token));

        Assert.Equal((ushort)StatusCode.M_ParameterError, exception.StatusCode);
        Assert.Equal("Injected ADD_ROSPEC failure.", exception.ErrorDescription);
    }

    [Fact]
    public async Task ConnectionClose_TriggersAutomaticReconnectWithoutRestoringInventory()
    {
        await using var host = CreateHost(
            new LlrpDeviceServerOptions
            {
                Port = 0,
                CloseConnectionAfterRequestMessageTypes = new HashSet<ushort> { GET_READER_CONFIG.MessageType }
            });
        await host.StartAsync();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using LlrpReader reader = LlrpReader.CreateBuilder("127.0.0.1")
            .WithPort(host.BoundPort)
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
        await Assert.ThrowsAnyAsync<Exception>(() => reader.Protocol.TransactAsync<GET_READER_CONFIG_RESPONSE>(
            new GET_READER_CONFIG(
                reader.Protocol.NextMessageId(),
                AntennaID: 0,
                RequestedData: GetReaderConfigRequestedData.All,
                GPIPortNum: 0,
                GPOPortNum: 0,
                CustomItems: []),
            cancellationToken: timeout.Token));
        await reconnected.Task.WaitAsync(timeout.Token);

        Assert.Equal(ReaderConnectionState.Ready, reader.ConnectionState);
        Assert.Null(reader.CurrentInventorySettings);
    }

    [Fact]
    public async Task TruncatedResponse_FaultsTheReceiveLoop()
    {
        await using var host = CreateHost(
            new LlrpDeviceServerOptions
            {
                Port = 0,
                TruncateResponseForMessageTypes = new HashSet<ushort> { GET_ROSPECS.MessageType }
            });
        await host.StartAsync();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using LlrpReader reader = LlrpReader.CreateBuilder("127.0.0.1")
            .WithPort(host.BoundPort).WithConnectTimeout(TimeSpan.FromSeconds(2))
            .WithRequestTimeout(TimeSpan.FromSeconds(2))
            .WithProtocolVersionPolicy(LlrpProtocolVersionPolicy.Force101).Build();
        await reader.ConnectAsync(timeout.Token);

        await Assert.ThrowsAnyAsync<Exception>(() => reader.RoSpecs.GetAllAsync(timeout.Token));
        await Task.Delay(50, timeout.Token);
        Assert.Equal(ReaderConnectionState.Faulted, reader.ConnectionState);
    }

    [Fact]
    public async Task QueryAndApplySettings_RoundTripAgainstVirtualDevice()
    {
        await using var host = CreateHost();
        await host.StartAsync();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using LlrpReader reader = LlrpReader.CreateBuilder("127.0.0.1")
            .WithPort(host.BoundPort)
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
        await using var host = CreateHost();
        await host.StartAsync();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using LlrpReader reader = LlrpReader.CreateBuilder("127.0.0.1")
            .WithPort(host.BoundPort)
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
        Assert.Equal(InventoryRuntimeState.Disabled, deployed.ManagedRoSpec!.State);
        await reader.StartInventoryAsync(timeout.Token);
        Assert.Equal(ReaderResourceMode.HighLevelRunning, reader.ResourceMode);
        await reader.StopAsync(timeout.Token);
        await reader.ClearManagedSettingsAsync(timeout.Token);
    }

    [Fact]
    public async Task CliDefaultAndExpandedPlatformInventory_BothStartAndReportAgainstStrictReader()
    {
        InventoryRunEvidence cliDefault = await StartAndReceiveInventoryReportAsync(new InventorySettings());
        InventoryRunEvidence expandedPlatform = await StartAndReceiveInventoryReportAsync(
            new InventorySettings
            {
                AntennaIds = [1, 2, 3, 4],
                ModeIndex = 20,
                Tari = 12_500,
                AntennaConfigurations =
                [
                    CreateAntennaConfiguration(1),
                    CreateAntennaConfiguration(2),
                    CreateAntennaConfiguration(3),
                    CreateAntennaConfiguration(4),
                ],
            });

        Assert.Equal(cliDefault.Epc, expandedPlatform.Epc);
        Assert.Equal(new ushort[] { 1, 2, 3, 4 }, cliDefault.AntennaIds);
        Assert.Empty(cliDefault.AntennaConfigurationIds);
        Assert.Equal(new ushort[] { 1, 2, 3, 4 }, expandedPlatform.AntennaIds);
        Assert.Equal(new ushort[] { 1, 2, 3, 4 }, expandedPlatform.AntennaConfigurationIds);
        Assert.Equal((ushort)20, expandedPlatform.ModeIndex);
        Assert.Equal((ushort)12_500, expandedPlatform.Tari);
    }

    [Fact]
    public async Task QuerySettings_RehydratesManaged101InventoryFiltersAttachedDataAndState()
    {
        await using var host = CreateHost();
        await host.StartAsync();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using LlrpReader reader = LlrpReader.CreateBuilder("127.0.0.1")
            .WithPort(host.BoundPort)
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
        await reader.StartInventoryAsync(inventory, timeout.Token);
        try
        {
            ReaderSettingsSnapshot snapshot = await reader.QuerySettingsAsync(timeout.Token);
            ManagedRoSpecSnapshot managed = Assert.IsType<ManagedRoSpecSnapshot>(snapshot.ManagedRoSpec);

            Assert.Equal(InventoryRuntimeState.Running, managed.State);
            Assert.Equal((byte)2, managed.Inventory.Session);
            Assert.Equal((ushort)64, managed.Inventory.TagPopulationEstimate);
            InventorySelectFilter filter = Assert.Single(managed.Inventory.Filters);
            Assert.Equal((ushort)4, filter.BitLength);
            Assert.Equal(new byte[] { 0b_1010_0000 }, filter.Mask);
            Assert.True(managed.Inventory.AttachedData.Enabled);
            Assert.Equal((ushort)3, managed.Inventory.AttachedData.WordPointer);
            Assert.Equal((ushort)2, managed.Inventory.AttachedData.WordCount);
        }
        finally
        {
            await reader.StopAsync(timeout.Token);
        }
    }

    [Fact]
    public async Task ManualMode_HighLevelTakeoverAndRawSynchronizationFollowResourceContract()
    {
        await using var host = CreateHost();
        await host.StartAsync();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using LlrpReader reader = LlrpReader.CreateBuilder("127.0.0.1")
            .WithPort(host.BoundPort)
            .WithConnectTimeout(TimeSpan.FromSeconds(2))
            .WithRequestTimeout(TimeSpan.FromSeconds(2))
            .WithProtocolVersionPolicy(LlrpProtocolVersionPolicy.Force101)
            .Build();

        await reader.ConnectAsync(timeout.Token);
        Assert.Equal(ReaderResourceMode.Idle, reader.ResourceMode);

        await reader.EnterManualResourceModeAsync(timeout.Token);
        Assert.Equal(ReaderResourceMode.ManualResources, reader.ResourceMode);
        await reader.RoSpecs.AddDefaultAsync(600, new InventorySettings(), timeout.Token);

        await reader.StartInventoryAsync(new InventorySettings(), timeout.Token);
        try
        {
            Assert.Equal(ReaderResourceMode.HighLevelRunning, reader.ResourceMode);
            IReadOnlyList<LlrpNet.Protocol.Parameters.ILlrpParameter> resources = await reader.RoSpecs.GetAllAsync(timeout.Token);
            Assert.Equal(2, resources.Count);
            Assert.Contains(resources, static resource =>
                Assert.IsType<LlrpNet.Protocol.Parameters.V1_0_1.ROSpec>(resource).ROSpecID == 600U);
            Assert.Contains(resources, static resource =>
                Assert.IsType<LlrpNet.Protocol.Parameters.V1_0_1.ROSpec>(resource).ROSpecID == 14150U);
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
        Assert.Equal(InventoryRuntimeState.Disabled, stopped.ManagedRoSpec!.State);
        await reader.StartInventoryAsync(timeout.Token);
        Assert.Equal(ReaderResourceMode.HighLevelRunning, reader.ResourceMode);
        await reader.StopAsync(timeout.Token);
        _ = await reader.Protocol.TransactAsync<GET_ROSPECS_RESPONSE>(
            new GET_ROSPECS(reader.Protocol.NextMessageId()), cancellationToken: timeout.Token);
        Assert.Equal(ReaderResourceMode.HighLevelConfigured, reader.ResourceMode);
        Assert.True(reader.IsManagedStateSynchronized);
        await using InventorySession recovered = await reader.StartInventoryAsync(new InventorySettings(), timeout.Token);
        Assert.Equal(ReaderResourceMode.HighLevelRunning, reader.ResourceMode);
        Assert.Equal(ReaderOperationState.Inventorying, reader.OperationState);
        await recovered.StopAsync(timeout.Token);

        await reader.SynchronizeStateAsync(timeout.Token);
        Assert.Equal(ReaderResourceMode.HighLevelConfigured, reader.ResourceMode);
        await reader.ClearManagedSettingsAsync(timeout.Token);
        Assert.Equal(ReaderResourceMode.Idle, reader.ResourceMode);
    }

    [Fact]
    public async Task ExpertRoSpec_StartExistingSessionReceivesReportsAndStopPreservesResource()
    {
        await using var host = CreateHost();
        await host.StartAsync();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using LlrpReader reader = CreateReader(host.BoundPort);
        await reader.ConnectAsync(timeout.Token);

        await reader.EnterManualResourceModeAsync(timeout.Token);
        await reader.RoSpecs.AddDefaultAsync(600, new InventorySettings(), timeout.Token);
        await reader.ExitManualResourceModeAsync(timeout.Token);

        var afterExit = await reader.RoSpecs.GetAllAsync(timeout.Token);
        Assert.Contains(afterExit, static resource =>
            Assert.IsType<ROSpec>(resource).ROSpecID == 600U);

        await using InventorySession session = await reader.StartExistingRoSpecAsync(600, timeout.Token);
        await using IAsyncEnumerator<TagReport> reports = session.ReadReportsAsync(timeout.Token)
            .GetAsyncEnumerator(timeout.Token);
        Assert.True(await reports.MoveNextAsync());
        Assert.Equal(600U, reports.Current.RoSpecId);

        await session.StopAsync(timeout.Token);
        Assert.Equal(InventoryRuntimeState.Disabled, session.State);
        var afterStop = await reader.RoSpecs.GetAllAsync(timeout.Token);
        Assert.Contains(afterStop, static resource =>
            Assert.IsType<ROSpec>(resource).ROSpecID == 600U);
    }

    [Fact]
    public async Task ManagedTakeover_PreserveForeignAndReplaceAllHaveExplicitResourceScopes()
    {
        await using var host = CreateHost();
        await host.StartAsync();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using LlrpReader reader = CreateReader(host.BoundPort);
        await reader.ConnectAsync(timeout.Token);

        await reader.EnterManualResourceModeAsync(timeout.Token);
        await reader.RoSpecs.AddDefaultAsync(600, new InventorySettings(), timeout.Token);
        await reader.ExitManualResourceModeAsync(timeout.Token);

        await using InventorySession preserved = await reader.StartInventoryAsync(
            new InventorySettings(),
            ResourceTakeoverPolicy.PreserveForeign,
            timeout.Token);
        await preserved.StopAsync(timeout.Token);

        var preservedResources = await reader.RoSpecs.GetAllAsync(timeout.Token);
        Assert.Contains(preservedResources, static resource =>
            Assert.IsType<ROSpec>(resource).ROSpecID == 600U);
        Assert.Contains(preservedResources, static resource =>
            Assert.IsType<ROSpec>(resource).ROSpecID == 14150U);

        await using InventorySession replaced = await reader.StartInventoryAsync(
            new InventorySettings(),
            ResourceTakeoverPolicy.ReplaceAll,
            timeout.Token);
        var replacedResources = await reader.RoSpecs.GetAllAsync(timeout.Token);
        Assert.DoesNotContain(replacedResources, static resource =>
            Assert.IsType<ROSpec>(resource).ROSpecID == 600U);
        Assert.Contains(replacedResources, static resource =>
            Assert.IsType<ROSpec>(resource).ROSpecID == 14150U);
        await replaced.StopAsync(timeout.Token);
    }

    private static async Task<InventoryRunEvidence> StartAndReceiveInventoryReportAsync(
        InventorySettings settings)
    {
        await using var host = CreateHost(
            new LlrpDeviceServerOptions
            {
                Port = 0,
                UseStrictStandardInventoryProfile = true,
            });
        await host.StartAsync();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using LlrpReader reader = LlrpReader.CreateBuilder("127.0.0.1")
            .WithPort(host.BoundPort)
            .WithConnectTimeout(TimeSpan.FromSeconds(2))
            .WithRequestTimeout(TimeSpan.FromSeconds(2))
            .WithProtocolVersionPolicy(LlrpProtocolVersionPolicy.Force101)
            .Build();

        await reader.ConnectAsync(timeout.Token);
        await using InventorySession session = await reader.StartInventoryAsync(settings, timeout.Token);
        await using IAsyncEnumerator<TagReport> reports = session.ReadReportsAsync(timeout.Token)
            .GetAsyncEnumerator(timeout.Token);
        Assert.True(await reports.MoveNextAsync());

        TagReport report = reports.Current;
        ReaderSettingsSnapshot snapshot = await reader.QuerySettingsAsync(timeout.Token);
        ManagedRoSpecSnapshot managed = Assert.IsType<ManagedRoSpecSnapshot>(snapshot.ManagedRoSpec);
        Assert.Equal(InventoryRuntimeState.Running, managed.State);
        Assert.DoesNotContain((ushort)0, managed.Inventory.AntennaIds);
        Assert.DoesNotContain(managed.Inventory.AntennaConfigurations, static antenna => antenna.AntennaId == 0);

        var evidence = new InventoryRunEvidence(
            Convert.ToHexString(report.ElectronicProductCode.Span),
            managed.Inventory.AntennaIds.ToArray(),
            managed.Inventory.AntennaConfigurations.Select(static antenna => antenna.AntennaId).ToArray(),
            managed.Inventory.ModeIndex,
            managed.Inventory.Tari);
        await session.StopAsync(timeout.Token);
        return evidence;
    }

    private static InventoryAntennaConfiguration CreateAntennaConfiguration(ushort antennaId) => new()
    {
        AntennaId = antennaId,
        ReceiverSensitivityIndex = 1,
        TransmitPowerIndex = 20,
        HopTableId = 1,
        ChannelIndex = 1,
    };

    private sealed record InventoryRunEvidence(
        string Epc,
        IReadOnlyList<ushort> AntennaIds,
        IReadOnlyList<ushort> AntennaConfigurationIds,
        ushort ModeIndex,
        ushort Tari);

    [Fact]
    public async Task RawDeleteOfActiveManagedResource_EndsSessionAndSettingsEntryPointTakesOver()
    {
        await using var host = CreateHost();
        await host.StartAsync();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using LlrpReader reader = LlrpReader.CreateBuilder("127.0.0.1")
            .WithPort(host.BoundPort)
            .WithConnectTimeout(TimeSpan.FromSeconds(2))
            .WithRequestTimeout(TimeSpan.FromSeconds(2))
            .WithProtocolVersionPolicy(LlrpProtocolVersionPolicy.Force101)
            .Build();

        await reader.ConnectAsync(timeout.Token);
        var desired = new InventorySettings { Session = 1, TagPopulationEstimate = 32 };
        await using InventorySession previous = await reader.StartInventoryAsync(desired, timeout.Token);

        _ = await reader.Protocol.TransactAsync<DELETE_ROSPEC_RESPONSE>(
            new DELETE_ROSPEC(reader.Protocol.NextMessageId(), 14150),
            cancellationToken: timeout.Token);

        Assert.Equal(InventoryRuntimeState.Disabled, previous.State);
        Assert.Equal(ReaderResourceMode.StateUnknown, reader.ResourceMode);
        Assert.Same(desired, reader.DesiredSettings!.Inventory);

        await using InventorySession recovered = await reader.StartInventoryAsync(timeout.Token);
        Assert.Equal(ReaderResourceMode.HighLevelRunning, reader.ResourceMode);
        Assert.Same(desired, recovered.Settings);
        await recovered.StopAsync(timeout.Token);
    }

    [Fact]
    public async Task QuerySettings_DoesNotRequireStateSynchronizationBeforeManagedInventory()
    {
        await using var host = CreateHost();
        await host.StartAsync();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using LlrpReader reader = LlrpReader.CreateBuilder("127.0.0.1")
            .WithPort(host.BoundPort)
            .WithConnectTimeout(TimeSpan.FromSeconds(2))
            .WithRequestTimeout(TimeSpan.FromSeconds(2))
            .WithProtocolVersionPolicy(LlrpProtocolVersionPolicy.Force101)
            .Build();

        await reader.ConnectAsync(timeout.Token);
        _ = await reader.QuerySettingsAsync(timeout.Token);
        await reader.StartInventoryAsync(new InventorySettings(), timeout.Token);
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
    public async Task ManagedInventoryAndReadAccess_CompleteAgainstVirtualDevice()
    {
        await using var host = CreateHost();
        await host.StartAsync();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using LlrpReader reader = LlrpReader.CreateBuilder("127.0.0.1")
            .WithPort(host.BoundPort)
            .WithConnectTimeout(TimeSpan.FromSeconds(5))
            .WithRequestTimeout(TimeSpan.FromSeconds(5))
            .WithProtocolVersionPolicy(LlrpProtocolVersionPolicy.Force101)
            .Build();

        await reader.ConnectAsync(timeout.Token);
        await reader.StartInventoryAsync(new InventorySettings(), timeout.Token);
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
