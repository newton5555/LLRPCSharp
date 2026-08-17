using LlrpDevice.Abstractions;
using LlrpDevice.Server;
using LlrpDevice.Virtual;
using LlrpDevice.Virtual.Hosting;
using LlrpNet.Core.Protocol;
using LlrpSdk;

namespace Interop.Tests;

public sealed class LlrpDeviceServerSdkInteropTests
{
    [Fact]
    public async Task Llrp101_end_of_rospec_report_trigger_flushes_the_accumulated_tail()
    {
        await using var host = new VirtualLlrpDeviceHost(
            new VirtualLlrpDeviceHostOptions
            {
                Server = new LlrpDeviceServerOptions
                {
                    Port = 0,
                    ProtocolVersion = LlrpProtocolVersion.Version101,
                    Reports = new LlrpDeviceReportOptions
                    {
                        ReportInterval = TimeSpan.FromMilliseconds(10),
                    },
                },
            });
        await host.StartAsync();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using LlrpReader reader = CreateReader(host.BoundPort);
        await reader.ConnectAsync(timeout.Token);
        await using InventorySession session = await reader.StartInventoryAsync(new InventorySettings
        {
            ReportEveryNTags = 0,
            Report = new InventoryReportSettings
            {
                Trigger = InventoryReportTrigger.UponNTagsOrEndOfRoSpec,
            },
            StopTrigger = new InventoryStopTrigger
            {
                Type = InventoryStopTriggerType.Duration,
                DurationMilliseconds = 120,
            },
        }, timeout.Token);

        await using IAsyncEnumerator<TagReport> reports = session.ReadReportsAsync(timeout.Token)
            .GetAsyncEnumerator(timeout.Token);
        Assert.True(await reports.MoveNextAsync());
        Assert.Equal("E28011710000020D056E9BEE", reports.Current.EpcHex);
        await session.StopAsync(timeout.Token);
    }

    [Fact]
    public async Task Llrp101_state_aware_filter_and_singulation_are_applied_by_the_virtual_device()
    {
        byte[] firstEpc = Convert.FromHexString("E28011710000020D056E9BEE");
        byte[] secondEpc = Convert.FromHexString("300833B2DDD9014000000001");
        await using var host = new VirtualLlrpDeviceHost(
            new VirtualLlrpDeviceHostOptions
            {
                Server = new LlrpDeviceServerOptions
                {
                    Port = 0,
                    ProtocolVersion = LlrpProtocolVersion.Version101,
                    Reports = new LlrpDeviceReportOptions
                    {
                        ReportInterval = TimeSpan.FromMilliseconds(10),
                    },
                },
                Device = new VirtualDeviceOptions
                {
                    Tags =
                    [
                        new VirtualTagDefinition { ElectronicProductCode = firstEpc },
                        new VirtualTagDefinition { ElectronicProductCode = secondEpc },
                    ],
                },
            });
        await host.StartAsync();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using LlrpReader reader = CreateReader(host.BoundPort);
        await reader.ConnectAsync(timeout.Token);
        await using InventorySession session = await reader.StartInventoryAsync(new InventorySettings
        {
            ReportEveryNTags = 0,
            StateAwareSingulation = new InventoryStateAwareSingulation
            {
                Target = InventoryTarget.StateA,
                SelectedFlag = InventorySelectedFlag.Set,
            },
            Filters =
            [
                new InventorySelectFilter
                {
                    MemoryBank = 1,
                    BitPointer = 32,
                    BitLength = 8,
                    Mask = new byte[] { 0xE2 },
                    StateAwareAction = new InventoryStateAwareFilterAction
                    {
                        Target = InventoryFilterTarget.SelectedFlag,
                        Action = InventoryFilterAction.AssertSelectedOrStateAAndDeassertSelectedOrStateB,
                    },
                },
            ],
            Report = new InventoryReportSettings
            {
                Trigger = InventoryReportTrigger.UponNTagsOrEndOfRoSpec,
            },
            StopTrigger = new InventoryStopTrigger
            {
                Type = InventoryStopTriggerType.Duration,
                DurationMilliseconds = 120,
            },
        }, timeout.Token);

        await using IAsyncEnumerator<TagReport> reports = session.ReadReportsAsync(timeout.Token)
            .GetAsyncEnumerator(timeout.Token);
        Assert.True(await reports.MoveNextAsync());
        Assert.Equal(Convert.ToHexString(firstEpc), reports.Current.EpcHex);
        await session.StopAsync(timeout.Token);
    }

    [Fact]
    public async Task Llrp101_get_report_returns_buffered_reports_when_automatic_delivery_is_disabled()
    {
        await using var host = new VirtualLlrpDeviceHost(
            new VirtualLlrpDeviceHostOptions
            {
                Server = new LlrpDeviceServerOptions
                {
                    Port = 0,
                    ProtocolVersion = LlrpProtocolVersion.Version101,
                    Reports = new LlrpDeviceReportOptions
                    {
                        ReportInterval = TimeSpan.FromMilliseconds(10),
                    },
                },
            });
        await host.StartAsync();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using LlrpReader reader = CreateReader(host.BoundPort);
        await reader.ConnectAsync(timeout.Token);
        await using InventorySession session = await reader.StartInventoryAsync(new InventorySettings
        {
            ReportEveryNTags = 1,
            Report = new InventoryReportSettings
            {
                Trigger = InventoryReportTrigger.None,
            },
        }, timeout.Token);

        await Task.Delay(100, timeout.Token);
        IReadOnlyList<TagReport> reports = await reader.GetTagReportsAsync(timeout.Token);

        TagReport report = Assert.Single(reports);
        Assert.Equal("E28011710000020D056E9BEE", report.EpcHex);
        await session.StopAsync(timeout.Token);
    }

    [Fact]
    public async Task Llrp101_attached_data_access_spec_returns_standard_read_results()
    {
        await using var host = new VirtualLlrpDeviceHost(
            new VirtualLlrpDeviceHostOptions
            {
                Server = new LlrpDeviceServerOptions
                {
                    Port = 0,
                    ProtocolVersion = LlrpProtocolVersion.Version101,
                    Reports = new LlrpDeviceReportOptions
                    {
                        ReportInterval = TimeSpan.FromMilliseconds(10),
                    },
                },
            });
        await host.StartAsync();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using LlrpReader reader = CreateReader(host.BoundPort);
        await reader.ConnectAsync(timeout.Token);
        await using InventorySession session = await reader.StartInventoryAsync(new InventorySettings
        {
            AttachedData = new AttachedDataOptions
            {
                Enabled = true,
                MemoryBank = 2,
                WordPointer = 0,
                WordCount = 2,
            },
        }, timeout.Token);

        await using IAsyncEnumerator<TagReport> reports = session.ReadReportsAsync(timeout.Token)
            .GetAsyncEnumerator(timeout.Token);
        Assert.True(await reports.MoveNextAsync());
        TagReport report = reports.Current;
        Assert.Equal(14151U, report.AccessSpecId);
        TagAccessOperationResult operation = Assert.Single(report.AccessOperationResults!);
        Assert.True(operation.Success, operation.Error);
        Assert.Equal(new ushort[] { 0xE200, 0x3412 }, operation.ReadData);
        await session.StopAsync(timeout.Token);
    }

    [Fact]
    public async Task Llrp101_report_selector_and_inventory_filter_are_applied_by_the_device()
    {
        byte[] firstEpc = Convert.FromHexString("E28011710000020D056E9BEE");
        byte[] secondEpc = Convert.FromHexString("300833B2DDD9014000000001");
        await using var host = new VirtualLlrpDeviceHost(
            new VirtualLlrpDeviceHostOptions
            {
                Server = new LlrpDeviceServerOptions
                {
                    Port = 0,
                    ProtocolVersion = LlrpProtocolVersion.Version101,
                    Reports = new LlrpDeviceReportOptions
                    {
                        ReportInterval = TimeSpan.FromMilliseconds(10),
                        ReportCount = 1,
                    },
                },
                Device = new VirtualDeviceOptions
                {
                    Tags =
                    [
                        new VirtualTagDefinition { ElectronicProductCode = firstEpc },
                        new VirtualTagDefinition { ElectronicProductCode = secondEpc },
                    ],
                },
            });
        await host.StartAsync();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using LlrpReader reader = CreateReader(host.BoundPort);
        await reader.ConnectAsync(timeout.Token);
        await using InventorySession session = await reader.StartInventoryAsync(new InventorySettings
        {
            Filters =
            [
                new InventorySelectFilter
                {
                    MemoryBank = 1,
                    BitPointer = 32,
                    BitLength = 8,
                    Mask = new byte[] { 0xE2 },
                    MatchAction = InventorySelectAction.Select,
                    NonMatchAction = InventorySelectAction.Unselect,
                },
            ],
            Report = new InventoryReportSettings
            {
                IncludeRoSpecId = false,
                IncludeSpecIndex = false,
                IncludeInventoryParameterSpecId = false,
                IncludeAntennaId = false,
                IncludeChannelIndex = false,
                IncludePeakRssi = false,
                IncludeFirstSeenTimestamp = false,
                IncludeLastSeenTimestamp = false,
                IncludeTagSeenCount = false,
                IncludeAccessSpecId = false,
                IncludePcBits = true,
            },
        }, timeout.Token);

        await using IAsyncEnumerator<TagReport> reports = session.ReadReportsAsync(timeout.Token)
            .GetAsyncEnumerator(timeout.Token);
        Assert.True(await reports.MoveNextAsync());
        Assert.Equal(Convert.ToHexString(firstEpc), reports.Current.EpcHex);
        Assert.Null(reports.Current.RoSpecId);
        Assert.Null(reports.Current.AntennaId);
        Assert.NotNull(reports.Current.PcBits);
        await session.StopAsync(timeout.Token);
    }

    [Fact]
    public async Task Llrp101_gpi_trigger_and_reader_event_notification_are_executed()
    {
        await using var host = new VirtualLlrpDeviceHost(
            new VirtualLlrpDeviceHostOptions
            {
                Server = new LlrpDeviceServerOptions
                {
                    Port = 0,
                    ProtocolVersion = LlrpProtocolVersion.Version101,
                    Reports = new LlrpDeviceReportOptions
                    {
                        ReportInterval = TimeSpan.FromMilliseconds(10),
                        ReportCount = 1,
                    },
                },
            });
        await host.StartAsync();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using LlrpReader reader = CreateReader(host.BoundPort);
        var gpiChanged = new TaskCompletionSource<GpiChangedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        reader.GpiChanged += (_, args) => gpiChanged.TrySetResult(args);
        await reader.ConnectAsync(timeout.Token);
        await reader.ApplySettingsAsync(new ReaderSettings
        {
            Configuration = new ReaderConfiguration
            {
                Events = new EventNotificationConfiguration { GpiEventEnabled = true },
            },
        }, timeout.Token);

        await using InventorySession session = await reader.StartInventoryAsync(new InventorySettings
        {
            StartTrigger = new InventoryStartTrigger
            {
                Type = InventoryStartTriggerType.Gpi,
                GpiPortNumber = 1,
                GpiState = true,
            },
        }, timeout.Token);
        host.VirtualDevice.SetGpiState(1, true);

        await using IAsyncEnumerator<TagReport> reports = session.ReadReportsAsync(timeout.Token)
            .GetAsyncEnumerator(timeout.Token);
        Assert.True(await reports.MoveNextAsync());
        GpiChangedEventArgs changed = await gpiChanged.Task.WaitAsync(timeout.Token);
        Assert.Equal((ushort)1, changed.PortNumber);
        Assert.True(changed.State);
        await session.StopAsync(timeout.Token);
    }

    [Fact]
    public async Task Virtual_device_can_initiate_standard_close_connection()
    {
        await using var host = new VirtualLlrpDeviceHost(
            new VirtualLlrpDeviceHostOptions
            {
                Server = new LlrpDeviceServerOptions { Port = 0, ProtocolVersion = LlrpProtocolVersion.Version101 },
            });
        await host.StartAsync();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using LlrpReader reader = CreateReader(host.BoundPort);
        var closeTransition = new TaskCompletionSource<ReaderConnectionChangedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        reader.ConnectionChanged += (_, args) =>
        {
            if (args.CurrentState == ReaderConnectionState.Faulted)
            {
                closeTransition.TrySetResult(args);
            }
        };
        await reader.ConnectAsync(timeout.Token);
        host.VirtualDevice.RequestCloseConnection();

        ReaderConnectionChangedEventArgs transition = await closeTransition.Task.WaitAsync(timeout.Token);
        Assert.Equal(ReaderConnectionState.Faulted, transition.CurrentState);
        Assert.True(transition.DeviceInitiatedClose);
    }

    [Fact]
    public async Task Public_single_device_host_facade_supports_sdk_inventory()
    {
        await using var host = new VirtualLlrpDeviceHost(
            new VirtualLlrpDeviceHostOptions
            {
                Server = new LlrpDeviceServerOptions
                {
                    Port = 0,
                    ProtocolVersion = LlrpProtocolVersion.Version101,
                    Reports = new LlrpDeviceReportOptions
                    {
                        ReportInterval = TimeSpan.FromMilliseconds(10),
                        ReportCount = 1,
                    },
                },
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
        await using InventorySession session = await reader.StartInventoryAsync(new InventorySettings(), timeout.Token);
        await using IAsyncEnumerator<TagReport> reports = session.ReadReportsAsync(timeout.Token)
            .GetAsyncEnumerator(timeout.Token);

        Assert.True(await reports.MoveNextAsync());
        Assert.Equal("E28011710000020D056E9BEE", Convert.ToHexString(reports.Current.ElectronicProductCode.Span));
        await session.StopAsync(timeout.Token);
    }

    [Fact]
    public async Task Public_single_device_host_forwards_decoded_message_events()
    {
        await using var host = new VirtualLlrpDeviceHost(
            new VirtualLlrpDeviceHostOptions
            {
                Server = new LlrpDeviceServerOptions
                {
                    Port = 0,
                    ProtocolVersion = LlrpProtocolVersion.Version101,
                },
            });
        var received = new TaskCompletionSource<VirtualLlrpDeviceHostMessageObservedEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var sent = new TaskCompletionSource<VirtualLlrpDeviceHostMessageObservedEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        host.MessageObserved += (_, args) =>
        {
            if (args.Detail == "GET_READER_CAPABILITIES" && args.Incoming)
            {
                received.TrySetResult(args);
            }
            else if (args.Detail == "GET_READER_CAPABILITIES_RESPONSE" && !args.Incoming)
            {
                sent.TrySetResult(args);
            }
        };

        await host.StartAsync();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using LlrpReader reader = LlrpReader.CreateBuilder("127.0.0.1")
            .WithPort(host.BoundPort)
            .WithConnectTimeout(TimeSpan.FromSeconds(2))
            .WithRequestTimeout(TimeSpan.FromSeconds(2))
            .WithProtocolVersionPolicy(LlrpProtocolVersionPolicy.Force101)
            .Build();

        await reader.ConnectAsync(timeout.Token);
        await reader.RefreshCapabilitiesAsync(timeout.Token);

        VirtualLlrpDeviceHostMessageObservedEventArgs incoming =
            await received.Task.WaitAsync(timeout.Token);
        VirtualLlrpDeviceHostMessageObservedEventArgs outgoing =
            await sent.Task.WaitAsync(timeout.Token);

        Assert.Equal(LlrpProtocolVersion.Version101, incoming.Version);
        Assert.Equal(LlrpProtocolVersion.Version101, outgoing.Version);
        Assert.Equal((ushort)LlrpNet.Protocol.Messages.V1_0_1.GET_READER_CAPABILITIES.MessageType, incoming.MessageType);
        Assert.Equal((ushort)LlrpNet.Protocol.Messages.V1_0_1.GET_READER_CAPABILITIES_RESPONSE.MessageType, outgoing.MessageType);
        Assert.Equal(incoming.ConnectionId, outgoing.ConnectionId);
        Assert.True(incoming.Incoming);
        Assert.False(outgoing.Incoming);
    }

    private static LlrpReader CreateReader(int port) => LlrpReader.CreateBuilder("127.0.0.1")
        .WithPort(port)
        .WithConnectTimeout(TimeSpan.FromSeconds(2))
        .WithRequestTimeout(TimeSpan.FromSeconds(2))
        .WithProtocolVersionPolicy(LlrpProtocolVersionPolicy.Force101)
        .Build();

    [Theory]
    [InlineData(LlrpProtocolVersion.Version101, LlrpProtocolVersionPolicy.Force101)]
    [InlineData(LlrpProtocolVersion.Version11, LlrpProtocolVersionPolicy.Force11)]
    [InlineData(LlrpProtocolVersion.Version20, LlrpProtocolVersionPolicy.Force20)]
    public async Task Generic_device_server_supports_capabilities_and_inventory(
        LlrpProtocolVersion version,
        LlrpProtocolVersionPolicy policy)
    {
        await using var device = new VirtualLlrpDevice();
        await using var server = new LlrpDeviceServer(
            new LlrpDeviceServerOptions
            {
                Port = 0,
                ProtocolVersion = version,
                Reports = new LlrpDeviceReportOptions
                {
                    ReportInterval = TimeSpan.FromMilliseconds(10),
                    ReportCount = 2,
                    Repeat = true,
                },
            },
            device);
        await server.StartAsync();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using LlrpReader reader = LlrpReader.CreateBuilder("127.0.0.1")
            .WithPort(server.Port)
            .WithConnectTimeout(TimeSpan.FromSeconds(2))
            .WithRequestTimeout(TimeSpan.FromSeconds(2))
            .WithProtocolVersionPolicy(policy)
            .Build();

        await reader.ConnectAsync(timeout.Token);
        Assert.Equal(version, reader.NegotiatedVersion);
        ReaderCapabilities capabilities = await reader.RefreshCapabilitiesAsync(timeout.Token);
        Assert.Equal((ushort)4, capabilities.MaxNumberOfAntennas);

        await using InventorySession session = await reader.StartInventoryAsync(new InventorySettings(), timeout.Token);
        await using IAsyncEnumerator<TagReport> reports = session.ReadReportsAsync(timeout.Token)
            .GetAsyncEnumerator(timeout.Token);
        Assert.True(await reports.MoveNextAsync());
        Assert.Equal("E28011710000020D056E9BEE", Convert.ToHexString(reports.Current.ElectronicProductCode.Span));
        await session.StopAsync(timeout.Token);
    }

    [Theory]
    [InlineData(LlrpProtocolVersion.Version101, LlrpProtocolVersionPolicy.Force101)]
    [InlineData(LlrpProtocolVersion.Version11, LlrpProtocolVersionPolicy.Force11)]
    public async Task Generic_device_server_round_trips_standard_tag_access(
        LlrpProtocolVersion version,
        LlrpProtocolVersionPolicy policy)
    {
        byte[] epc = Convert.FromHexString("E28011710000020D056E9BEE");
        await using var device = new VirtualLlrpDevice(new VirtualDeviceOptions
        {
            Tags =
            [
                new VirtualTagDefinition
                {
                    ElectronicProductCode = epc,
                    UserMemory = [1, 2, 3, 4],
                    KillPassword = 0x12345678,
                },
            ],
        });
        await using var server = new LlrpDeviceServer(
            new LlrpDeviceServerOptions
            {
                Port = 0,
                ProtocolVersion = version,
                Reports = new LlrpDeviceReportOptions
                {
                    ReportInterval = TimeSpan.FromMilliseconds(10),
                    ReportCount = 1,
                },
            },
            device);
        await server.StartAsync();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using LlrpReader reader = LlrpReader.CreateBuilder("127.0.0.1")
            .WithPort(server.Port)
            .WithConnectTimeout(TimeSpan.FromSeconds(2))
            .WithRequestTimeout(TimeSpan.FromSeconds(2))
            .WithProtocolVersionPolicy(policy)
            .Build();

        await reader.ConnectAsync(timeout.Token);
        TagSelection selection = new()
        {
            MemoryBank = TagMemoryBank.ElectronicProductCode,
            BitPointer = 32,
            BitLength = 96,
            Mask = Enumerable.Repeat((byte)0xFF, 12).ToArray(),
            Data = epc,
        };

        TagAccessSequenceResult result = await reader.ExecuteTagAccessSequenceAsync(
            new TagAccessSequenceRequest
            {
                Operations =
                [
                    new ReadTagRequest
                    {
                        Selection = selection,
                        MemoryBank = TagMemoryBank.User,
                        WordPointer = 0,
                        WordCount = 4,
                    },
                    new WriteTagRequest
                    {
                        Selection = selection,
                        MemoryBank = TagMemoryBank.User,
                        WordPointer = 0,
                        WriteData = [9, 8],
                    },
                    new BlockEraseTagRequest
                    {
                        Selection = selection,
                        MemoryBank = TagMemoryBank.User,
                        WordPointer = 2,
                        WordCount = 1,
                    },
                    new ReadTagRequest
                    {
                        Selection = selection,
                        MemoryBank = TagMemoryBank.User,
                        WordPointer = 0,
                        WordCount = 4,
                    },
                    new LockTagRequest
                    {
                        Selection = selection,
                        UserMemoryLockMode = TagLockMode.AlwaysNotWritable,
                    },
                    new KillTagRequest
                    {
                        Selection = selection,
                        KillPassword = "12345678",
                    },
                ],
            },
            TimeSpan.FromSeconds(5),
            timeout.Token);

        Assert.Equal(6, result.Operations.Count);
        Assert.All(result.Operations, static operation => Assert.True(operation.Success, operation.Error));
        Assert.Equal([1, 2, 3, 4], result.Operations[0].ReadData);
        Assert.Equal((ushort)2, result.Operations[1].WordsWritten);
        Assert.Equal([9, 8, 0, 4], result.Operations[3].ReadData);
        await using IInventoryExecution execution = await device.StartInventoryAsync(
            new LlrpInventoryPlan { RoSpecId = 1 });
        Assert.Empty((await execution.ObserveAsync(new LlrpInventoryRound(1, 0, []))).Tags);
    }
}
