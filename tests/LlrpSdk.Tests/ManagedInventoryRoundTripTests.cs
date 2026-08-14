using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Parameters;
using LlrpNet.Protocol.Registry;
using LlrpSdk.Tests.Support;
using Xunit;
using V101Enumerations = LlrpNet.Protocol.Enumerations.V1_0_1;
using V101Messages = LlrpNet.Protocol.Messages.V1_0_1;
using V101Parameters = LlrpNet.Protocol.Parameters.V1_0_1;
using V11Enumerations = LlrpNet.Protocol.Enumerations.V1_1;
using V11Messages = LlrpNet.Protocol.Messages.V1_1;
using V11Parameters = LlrpNet.Protocol.Parameters.V1_1;

namespace LlrpSdk.Tests;

/// <summary>
/// Pins the compile → parse round-trip contract and the 1.0.1/1.1 equivalence contract that the
/// "adapter is the only version boundary" refactor must preserve byte-for-byte.
/// </summary>
public sealed class ManagedInventoryRoundTripTests
{
    [Fact]
    public async Task RoundTrip_101_CompileThenParse_ReproducesDomainIntent()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var transport = new ScriptedLlrpTransport();
        await using LlrpReader reader = LlrpReaderLifecycleTests.CreateReader(transport);
        await reader.ConnectAsync(timeout.Token);

        var settings = new InventorySettings
        {
            Priority = 7,
            AntennaIds = [1, 2],
            InventoryParameterSpecId = 9,
            AntennaConfigurations =
            [
                new InventoryAntennaConfiguration
                {
                    AntennaId = 1,
                    ReceiverSensitivityIndex = 2,
                    TransmitPowerIndex = 3,
                    HopTableId = 4,
                    ChannelIndex = 5,
                },
                new InventoryAntennaConfiguration
                {
                    AntennaId = 2,
                    ReceiverSensitivityIndex = 6,
                    TransmitPowerIndex = 7,
                    HopTableId = 8,
                    ChannelIndex = 9,
                },
            ],
            Session = 2,
            TagPopulationEstimate = 64,
            ModeIndex = 3,
            Tari = 6250,
            ReportEveryNTags = 6,
            Report = new InventoryReportSettings
            {
                Trigger = InventoryReportTrigger.UponNTagsOrEndOfRoSpec,
                IncludeRoSpecId = true,
                IncludeSpecIndex = true,
                IncludeInventoryParameterSpecId = true,
                IncludeAntennaId = true,
                IncludeChannelIndex = true,
                IncludePeakRssi = true,
                IncludeFirstSeenTimestamp = true,
                IncludeLastSeenTimestamp = true,
                IncludeTagSeenCount = true,
                IncludeAccessSpecId = true,
                IncludeCrc = true,
                IncludePcBits = true,
            },
            Filters =
            [
                new InventorySelectFilter
                {
                    MemoryBank = 1,
                    BitPointer = 32,
                    Mask = new byte[] { 0xE2, 0x80 },
                    BitLength = 9,
                    MatchAction = InventorySelectAction.Select,
                    NonMatchAction = InventorySelectAction.Unselect,
                },
            ],
            StateAwareSingulation = new InventoryStateAwareSingulation
            {
                Target = InventoryTarget.StateA,
                SelectedFlag = InventorySelectedFlag.Set,
            },
            StartTrigger = new InventoryStartTrigger
            {
                Type = InventoryStartTriggerType.Gpi,
                GpiPortNumber = 2,
                GpiState = true,
                TimeoutMilliseconds = 300,
            },
            StopTrigger = new InventoryStopTrigger
            {
                Type = InventoryStopTriggerType.Duration,
                DurationMilliseconds = 400,
            },
        };

        V101Parameters.ROSpec roSpec = Llrp101InventoryCompiler.Compile(
            settings, [], [], supportsStateAwareSingulation: true);

        ManagedRoSpecSnapshot snapshot = ParseForVersion(reader, roSpec, []);

        Assert.Equal(InventoryRuntimeState.Disabled, snapshot.State);
        Assert.Equal((byte)7, snapshot.Inventory.Priority);
        Assert.Equal(new ushort[] { 1, 2 }, snapshot.Inventory.AntennaIds);
        Assert.Equal((ushort)9, snapshot.Inventory.InventoryParameterSpecId);
        Assert.Equal((byte)2, snapshot.Inventory.Session);
        Assert.Equal((ushort)64, snapshot.Inventory.TagPopulationEstimate);
        Assert.Equal((ushort)3, snapshot.Inventory.ModeIndex);
        Assert.Equal((ushort)6250, snapshot.Inventory.Tari);
        Assert.Equal((ushort)6, snapshot.Inventory.ReportEveryNTags);
        Assert.Equal(settings.Report, snapshot.Inventory.Report);
        Assert.Equal(settings.AntennaConfigurations, snapshot.Inventory.AntennaConfigurations);
        Assert.Equal(settings.StartTrigger, snapshot.Inventory.StartTrigger);
        Assert.Equal(settings.StopTrigger, snapshot.Inventory.StopTrigger);
        Assert.Equal(InventoryTarget.StateA, snapshot.Inventory.StateAwareSingulation!.Target);
        Assert.Equal(InventorySelectedFlag.Set, snapshot.Inventory.StateAwareSingulation.SelectedFlag);
        Assert.False(snapshot.Inventory.AttachedData.Enabled);

        InventorySelectFilter filter = Assert.Single(snapshot.Inventory.Filters);
        Assert.Null(filter.StateAwareAction);
        Assert.Equal((ushort)1, filter.MemoryBank);
        Assert.Equal((ushort)32, filter.BitPointer);
        Assert.Equal((ushort)9, filter.BitLength);
        Assert.Equal(new byte[] { 0xE2, 0x80 }, filter.Mask.ToArray());
        Assert.Equal(InventorySelectAction.Select, filter.MatchAction);
        Assert.Equal(InventorySelectAction.Unselect, filter.NonMatchAction);
    }

    [Fact]
    public async Task RoundTrip_101_ParseProjectsAllRuntimeStates()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var transport = new ScriptedLlrpTransport();
        await using LlrpReader reader = LlrpReaderLifecycleTests.CreateReader(transport);
        await reader.ConnectAsync(timeout.Token);

        var settings = new InventorySettings { AntennaIds = [1] };
        V101Parameters.ROSpec roSpec = Llrp101InventoryCompiler.Compile(settings, [], []);

        Assert.Equal(
            InventoryRuntimeState.Disabled,
            ParseForVersion(reader, roSpec, []).State);
        Assert.Equal(
            InventoryRuntimeState.Enabled,
            ParseForVersion(reader, roSpec with { CurrentState = V101Enumerations.ROSpecState.Inactive }, []).State);
        Assert.Equal(
            InventoryRuntimeState.Running,
            ParseForVersion(reader, roSpec with { CurrentState = V101Enumerations.ROSpecState.Active }, []).State);
    }

    [Fact]
    public async Task RoundTrip_11_CompileThenParse_ReproducesDomainIntent()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var transport = new ScriptedLlrpTransport();
        await using LlrpReader reader = LlrpReaderLifecycleTests.CreateReader(transport);
        await reader.ConnectAsync(timeout.Token);

        var settings = new InventorySettings
        {
            Priority = 7,
            AntennaIds = [1, 2],
            InventoryParameterSpecId = 9,
            AntennaConfigurations =
            [
                new InventoryAntennaConfiguration
                {
                    AntennaId = 1,
                    ReceiverSensitivityIndex = 2,
                    TransmitPowerIndex = 3,
                    HopTableId = 4,
                    ChannelIndex = 5,
                },
                new InventoryAntennaConfiguration
                {
                    AntennaId = 2,
                    ReceiverSensitivityIndex = 6,
                    TransmitPowerIndex = 7,
                    HopTableId = 8,
                    ChannelIndex = 9,
                },
            ],
            Session = 2,
            TagPopulationEstimate = 64,
            ModeIndex = 3,
            Tari = 6250,
            ReportEveryNTags = 6,
            Report = new InventoryReportSettings
            {
                Trigger = InventoryReportTrigger.UponNTagsOrEndOfRoSpec,
                IncludeRoSpecId = true,
                IncludeSpecIndex = true,
                IncludeInventoryParameterSpecId = true,
                IncludeAntennaId = true,
                IncludeChannelIndex = true,
                IncludePeakRssi = true,
                IncludeFirstSeenTimestamp = true,
                IncludeLastSeenTimestamp = true,
                IncludeTagSeenCount = true,
                IncludeAccessSpecId = true,
                IncludeCrc = true,
                IncludePcBits = true,
            },
            Filters =
            [
                new InventorySelectFilter
                {
                    MemoryBank = 1,
                    BitPointer = 32,
                    Mask = new byte[] { 0xE2, 0x80 },
                    BitLength = 9,
                    MatchAction = InventorySelectAction.Select,
                    NonMatchAction = InventorySelectAction.Unselect,
                },
                new InventorySelectFilter
                {
                    MemoryBank = 3,
                    BitPointer = 16,
                    Mask = new byte[] { 0x80 },
                    BitLength = 1,
                    StateAwareAction = new InventoryStateAwareFilterAction
                    {
                        Target = InventoryFilterTarget.Session2,
                        Action = InventoryFilterAction.NoOperationAndAssertSelectedOrStateA,
                    },
                },
            ],
            StateAwareSingulation = new InventoryStateAwareSingulation
            {
                Target = InventoryTarget.StateB,
                SelectedFlag = InventorySelectedFlag.All,
            },
            StartTrigger = new InventoryStartTrigger
            {
                Type = InventoryStartTriggerType.Gpi,
                GpiPortNumber = 2,
                GpiState = true,
                TimeoutMilliseconds = 300,
            },
            StopTrigger = new InventoryStopTrigger
            {
                Type = InventoryStopTriggerType.Duration,
                DurationMilliseconds = 400,
            },
        };

        V11Parameters.ROSpec roSpec = Llrp11InventoryCompiler.Compile(
            settings, [], supportsStateAwareSingulation: true);

        ManagedRoSpecSnapshot snapshot = ParseForVersion(reader, roSpec, []);

        Assert.Equal(InventoryRuntimeState.Disabled, snapshot.State);
        Assert.Equal((byte)7, snapshot.Inventory.Priority);
        Assert.Equal(new ushort[] { 1, 2 }, snapshot.Inventory.AntennaIds);
        Assert.Equal((ushort)9, snapshot.Inventory.InventoryParameterSpecId);
        Assert.Equal((byte)2, snapshot.Inventory.Session);
        Assert.Equal((ushort)64, snapshot.Inventory.TagPopulationEstimate);
        Assert.Equal((ushort)3, snapshot.Inventory.ModeIndex);
        Assert.Equal((ushort)6250, snapshot.Inventory.Tari);
        Assert.Equal((ushort)6, snapshot.Inventory.ReportEveryNTags);
        Assert.Equal(settings.Report, snapshot.Inventory.Report);
        Assert.Equal(settings.AntennaConfigurations, snapshot.Inventory.AntennaConfigurations);
        Assert.Equal(settings.StartTrigger, snapshot.Inventory.StartTrigger);
        Assert.Equal(settings.StopTrigger, snapshot.Inventory.StopTrigger);
        Assert.Equal(InventoryTarget.StateB, snapshot.Inventory.StateAwareSingulation!.Target);
        Assert.Equal(InventorySelectedFlag.All, snapshot.Inventory.StateAwareSingulation.SelectedFlag);
        Assert.False(snapshot.Inventory.AttachedData.Enabled);

        Assert.Equal(2, snapshot.Inventory.Filters.Count);
        InventorySelectFilter unaware = snapshot.Inventory.Filters[0];
        Assert.Null(unaware.StateAwareAction);
        Assert.Equal(InventorySelectAction.Select, unaware.MatchAction);
        Assert.Equal(InventorySelectAction.Unselect, unaware.NonMatchAction);

        InventorySelectFilter stateAware = snapshot.Inventory.Filters[1];
        Assert.NotNull(stateAware.StateAwareAction);
        Assert.Equal(InventoryFilterTarget.Session2, stateAware.StateAwareAction!.Target);
        Assert.Equal(InventoryFilterAction.NoOperationAndAssertSelectedOrStateA, stateAware.StateAwareAction.Action);
        Assert.Equal((ushort)3, stateAware.MemoryBank);
        Assert.Equal((ushort)16, stateAware.BitPointer);
        Assert.Equal((ushort)1, stateAware.BitLength);
        // A single set bit round-trips to the most significant bit of the first packed byte.
        Assert.Equal(new byte[] { 0x80 }, stateAware.Mask.ToArray());
    }

    [Fact]
    public async Task RoundTrip_11_ParseProjectsAllRuntimeStates()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var transport = new ScriptedLlrpTransport();
        await using LlrpReader reader = LlrpReaderLifecycleTests.CreateReader(transport);
        await reader.ConnectAsync(timeout.Token);

        var settings = new InventorySettings { AntennaIds = [1] };
        V11Parameters.ROSpec roSpec = Llrp11InventoryCompiler.Compile(settings, []);

        Assert.Equal(
            InventoryRuntimeState.Disabled,
            ParseForVersion(reader, roSpec, []).State);
        Assert.Equal(
            InventoryRuntimeState.Enabled,
            ParseForVersion(reader, roSpec with { CurrentState = V11Enumerations.ROSpecState.Inactive }, []).State);
        Assert.Equal(
            InventoryRuntimeState.Running,
            ParseForVersion(reader, roSpec with { CurrentState = V11Enumerations.ROSpecState.Active }, []).State);
    }

    [Fact]
    public void Translate_101And11_ProduceIdenticalNeutralReports()
    {
        TagReport report101 = Assert.Single(Llrp101TagReportTranslator.Translate(CreateAccessReport101())).Report;
        TagReport report11 = Assert.Single(Llrp11TagReportTranslator.Translate(CreateAccessReport11())).Report;

        // The 1.0.1 and 1.1 wire definitions of the report structures the SDK projects are identical, so the
        // neutral projection must be identical across both translators. Record equality falls back to reference
        // equality for the ReadData lists inside TagAccessOperationResult, so compare the serialized projection.
        string json101 = System.Text.Json.JsonSerializer.Serialize(report101);
        string json11 = System.Text.Json.JsonSerializer.Serialize(report11);
        Assert.Equal(json101, json11);
    }

    [Fact]
    public void Compile_101And11_ProduceIdenticalRospecWireBytes()
    {
        var settings = new InventorySettings
        {
            Priority = 7,
            AntennaIds = [1, 2],
            InventoryParameterSpecId = 9,
            AntennaConfigurations =
            [
                new InventoryAntennaConfiguration
                {
                    AntennaId = 1,
                    ReceiverSensitivityIndex = 2,
                    TransmitPowerIndex = 3,
                    HopTableId = 4,
                    ChannelIndex = 5,
                },
            ],
            Session = 2,
            TagPopulationEstimate = 64,
            ModeIndex = 3,
            Tari = 6250,
            ReportEveryNTags = 6,
            Report = new InventoryReportSettings
            {
                Trigger = InventoryReportTrigger.UponNTagsOrEndOfRoSpec,
                IncludeCrc = true,
                IncludePcBits = true,
            },
            Filters =
            [
                new InventorySelectFilter
                {
                    MemoryBank = 1,
                    BitPointer = 32,
                    Mask = new byte[] { 0xE2, 0x80 },
                    BitLength = 9,
                    MatchAction = InventorySelectAction.Select,
                    NonMatchAction = InventorySelectAction.Unselect,
                },
            ],
            StartTrigger = new InventoryStartTrigger
            {
                Type = InventoryStartTriggerType.Periodic,
                OffsetMilliseconds = 100,
                PeriodMilliseconds = 5000,
            },
            StopTrigger = new InventoryStopTrigger
            {
                Type = InventoryStopTriggerType.Duration,
                DurationMilliseconds = 400,
            },
        };

        ILlrpParameter roSpec101 = Llrp101InventoryCompiler.Compile(settings, [], []);
        ILlrpParameter roSpec11 = Llrp11InventoryCompiler.Compile(settings, []);

        var registry = new LlrpCodecRegistry();
        LlrpNet.Protocol.Registry.V1_0_1.V1_0_1ProtocolModule.Register(registry);
        LlrpNet.Protocol.Registry.V1_1.Llrp11StandardModule.Register(registry);

        byte[] bytes101 = registry.EncodeParameter(LlrpProtocolVersion.Version101, roSpec101);
        byte[] bytes11 = registry.EncodeParameter(LlrpProtocolVersion.Version11, roSpec11);

        Assert.Equal(bytes101, bytes11);
    }

    private static ManagedRoSpecSnapshot ParseForVersion(
        LlrpReader reader,
        ILlrpParameter roSpec,
        IReadOnlyList<ILlrpParameter> accessSpecs) =>
        roSpec is V101Parameters.ROSpec
            ? new Llrp101ProtocolAdapter().ParseManagedRoSpec(reader, roSpec, accessSpecs)
            : new Llrp11ProtocolAdapter().ParseManagedRoSpec(reader, roSpec, accessSpecs);

    private static V101Messages.RO_ACCESS_REPORT CreateAccessReport101()
    {
        var data = new V101Parameters.TagReportData(
            new V101Parameters.EPC_96(new byte[] { 0xE2, 0x80, 0x11, 0x91, 0, 0, 0, 0, 0, 0, 0, 1 }),
            ROSpecID: new V101Parameters.ROSpecID(14150),
            SpecIndex: new V101Parameters.SpecIndex(2),
            InventoryParameterSpecID: new V101Parameters.InventoryParameterSpecID(9),
            AntennaID: new V101Parameters.AntennaID(3),
            PeakRSSI: new V101Parameters.PeakRSSI(-67),
            ChannelIndex: new V101Parameters.ChannelIndex(4),
            FirstSeenTimestampUTC: new V101Parameters.FirstSeenTimestampUTC(1_000_000),
            FirstSeenTimestampUptime: new V101Parameters.FirstSeenTimestampUptime(2_000_000),
            LastSeenTimestampUTC: new V101Parameters.LastSeenTimestampUTC(3_000_000),
            LastSeenTimestampUptime: new V101Parameters.LastSeenTimestampUptime(4_000_000),
            TagSeenCount: new V101Parameters.TagSeenCount(7),
            AirProtocolTagDataItems: [new V101Parameters.C1G2_PC(0x3000)],
            AccessSpecID: new V101Parameters.AccessSpecID(14151),
            AccessCommandOpSpecResultItems:
            [
                new V101Parameters.C1G2ReadOpSpecResult(V101Enumerations.C1G2ReadResultType.Success, 1, [0x1234]),
                new V101Parameters.C1G2WriteOpSpecResult(V101Enumerations.C1G2WriteResultType.Success, 2, 4),
                new V101Parameters.C1G2BlockWriteOpSpecResult(V101Enumerations.C1G2BlockWriteResultType.Success, 3, 2),
                new V101Parameters.C1G2LockOpSpecResult(V101Enumerations.C1G2LockResultType.Success, 4),
                new V101Parameters.C1G2KillOpSpecResult(V101Enumerations.C1G2KillResultType.Success, 5),
                new V101Parameters.C1G2BlockEraseOpSpecResult(V101Enumerations.C1G2BlockEraseResultType.Success, 6),
            ],
            CustomItems: []);

        return new V101Messages.RO_ACCESS_REPORT(
            1,
            TagReportDataItems: [data],
            RFSurveyReportDataItems: [],
            CustomItems: []);
    }

    private static V11Messages.RO_ACCESS_REPORT CreateAccessReport11()
    {
        var data = new V11Parameters.TagReportData(
            new V11Parameters.EPC_96(new byte[] { 0xE2, 0x80, 0x11, 0x91, 0, 0, 0, 0, 0, 0, 0, 1 }),
            ROSpecID: new V11Parameters.ROSpecID(14150),
            SpecIndex: new V11Parameters.SpecIndex(2),
            InventoryParameterSpecID: new V11Parameters.InventoryParameterSpecID(9),
            AntennaID: new V11Parameters.AntennaID(3),
            PeakRSSI: new V11Parameters.PeakRSSI(-67),
            ChannelIndex: new V11Parameters.ChannelIndex(4),
            FirstSeenTimestampUTC: new V11Parameters.FirstSeenTimestampUTC(1_000_000),
            FirstSeenTimestampUptime: new V11Parameters.FirstSeenTimestampUptime(2_000_000),
            LastSeenTimestampUTC: new V11Parameters.LastSeenTimestampUTC(3_000_000),
            LastSeenTimestampUptime: new V11Parameters.LastSeenTimestampUptime(4_000_000),
            TagSeenCount: new V11Parameters.TagSeenCount(7),
            AirProtocolTagDataItems: [new V11Parameters.C1G2_PC(0x3000)],
            AccessSpecID: new V11Parameters.AccessSpecID(14151),
            AccessCommandOpSpecResultItems:
            [
                new V11Parameters.C1G2ReadOpSpecResult(V11Enumerations.C1G2ReadResultType.Success, 1, [0x1234]),
                new V11Parameters.C1G2WriteOpSpecResult(V11Enumerations.C1G2WriteResultType.Success, 2, 4),
                new V11Parameters.C1G2BlockWriteOpSpecResult(V11Enumerations.C1G2BlockWriteResultType.Success, 3, 2),
                new V11Parameters.C1G2LockOpSpecResult(V11Enumerations.C1G2LockResultType.Success, 4),
                new V11Parameters.C1G2KillOpSpecResult(V11Enumerations.C1G2KillResultType.Success, 5),
                new V11Parameters.C1G2BlockEraseOpSpecResult(V11Enumerations.C1G2BlockEraseResultType.Success, 6),
            ],
            CustomItems: []);

        return new V11Messages.RO_ACCESS_REPORT(
            1,
            TagReportDataItems: [data],
            RFSurveyReportDataItems: [],
            CustomItems: []);
    }

    [Fact]
    public void ProjectEvent_101And11_ProduceIdenticalNeutralProjections()
    {
        IReadOnlyList<ReaderEventProjection> full101 = ReaderEventProjector.Project(CreateEventNotification101(includeOverflow: false));
        IReadOnlyList<ReaderEventProjection> full11 = ReaderEventProjector.Project(CreateEventNotification11(includeOverflow: false));
        AssertProjectionEquality(full101, full11);
        Assert.Contains(
            full101,
            static projection => projection is ManagedRoSpecEventProjection
            {
                RoSpecId: 14150,
                State: InventoryRuntimeState.Running,
            });

        IReadOnlyList<ReaderEventProjection> overflow101 = ReaderEventProjector.Project(CreateEventNotification101(includeOverflow: true));
        IReadOnlyList<ReaderEventProjection> overflow11 = ReaderEventProjector.Project(CreateEventNotification11(includeOverflow: true));
        AssertProjectionEquality(overflow101, overflow11);
        Assert.Contains(
            overflow101,
            static projection => projection is ManagedRoSpecEventProjection
            {
                RoSpecId: 14150,
                State: InventoryRuntimeState.Disabled,
            });
    }

    private static void AssertProjectionEquality(
        IReadOnlyList<ReaderEventProjection> expected,
        IReadOnlyList<ReaderEventProjection> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (int index = 0; index < expected.Count; index++)
        {
            Assert.Equal(expected[index].GetType(), actual[index].GetType());
            Assert.Equal(expected[index], actual[index]);
        }
    }

    private static V101Messages.READER_EVENT_NOTIFICATION CreateEventNotification101(bool includeOverflow)
    {
        var data = new V101Parameters.ReaderEventNotificationData(
            new V101Parameters.UTCTimestamp(1_000_000),
            HoppingEvent: null,
            GPIEvent: new V101Parameters.GPIEvent(1, true),
            ROSpecEvent: includeOverflow
                ? new V101Parameters.ROSpecEvent(V101Enumerations.ROSpecEventType.Preemption_Of_ROSpec, 14150, 9)
                : new V101Parameters.ROSpecEvent(V101Enumerations.ROSpecEventType.Start_Of_ROSpec, 14150, 0),
            ReportBufferLevelWarningEvent: includeOverflow ? null : new V101Parameters.ReportBufferLevelWarningEvent(55),
            ReportBufferOverflowErrorEvent: includeOverflow ? new V101Parameters.ReportBufferOverflowErrorEvent() : null,
            ReaderExceptionEvent: includeOverflow ? null : new V101Parameters.ReaderExceptionEvent(
                "boom",
                new V101Parameters.ROSpecID(14150),
                new V101Parameters.SpecIndex(2),
                new V101Parameters.InventoryParameterSpecID(9),
                new V101Parameters.AntennaID(3),
                new V101Parameters.AccessSpecID(14151),
                new V101Parameters.OpSpecID(1),
                []),
            RFSurveyEvent: null,
            AISpecEvent: null,
            AntennaEvent: new V101Parameters.AntennaEvent(V101Enumerations.AntennaEventType.Antenna_Connected, 2),
            ConnectionAttemptEvent: null,
            ConnectionCloseEvent: null,
            CustomItems: []);
        return new V101Messages.READER_EVENT_NOTIFICATION(1, data);
    }

    private static V11Messages.READER_EVENT_NOTIFICATION CreateEventNotification11(bool includeOverflow)
    {
        var data = new V11Parameters.ReaderEventNotificationData(
            new V11Parameters.UTCTimestamp(1_000_000),
            HoppingEvent: null,
            GPIEvent: new V11Parameters.GPIEvent(1, true),
            ROSpecEvent: includeOverflow
                ? new V11Parameters.ROSpecEvent(V11Enumerations.ROSpecEventType.Preemption_Of_ROSpec, 14150, 9)
                : new V11Parameters.ROSpecEvent(V11Enumerations.ROSpecEventType.Start_Of_ROSpec, 14150, 0),
            ReportBufferLevelWarningEvent: includeOverflow ? null : new V11Parameters.ReportBufferLevelWarningEvent(55),
            ReportBufferOverflowErrorEvent: includeOverflow ? new V11Parameters.ReportBufferOverflowErrorEvent() : null,
            ReaderExceptionEvent: includeOverflow ? null : new V11Parameters.ReaderExceptionEvent(
                "boom",
                new V11Parameters.ROSpecID(14150),
                new V11Parameters.SpecIndex(2),
                new V11Parameters.InventoryParameterSpecID(9),
                new V11Parameters.AntennaID(3),
                new V11Parameters.AccessSpecID(14151),
                new V11Parameters.OpSpecID(1),
                []),
            RFSurveyEvent: null,
            AISpecEvent: null,
            AntennaEvent: new V11Parameters.AntennaEvent(V11Enumerations.AntennaEventType.Antenna_Connected, 2),
            ConnectionAttemptEvent: null,
            ConnectionCloseEvent: null,
            SpecLoopEvent: null,
            CustomItems: []);
        return new V11Messages.READER_EVENT_NOTIFICATION(1, data);
    }
}
