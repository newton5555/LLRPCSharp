using LlrpNet.Protocol.Enumerations.V1_0_1;
using TagReportContentSelector = LlrpNet.Protocol.Parameters.V1_0_1.TagReportContentSelector;
using LlrpNet.Protocol.Messages.V1_0_1;
using LlrpNet.Protocol.Choices.V1_0_1;
using LlrpNet.Protocol.Parameters;
using LlrpNet.Protocol.Parameters.V1_0_1;

namespace LlrpSdk;

/// <summary>
/// Compiles the SDK inventory intent into the standard LLRP 1.0.1 ROSpec graph.
/// </summary>
internal static class Llrp101InventoryCompiler
{
    public static ROSpec Compile(
        InventorySettings settings,
        IReadOnlyList<ILlrpParameter> roReportSpecCustomItems,
        bool supportsStateAwareSingulation = false) =>
        Compile(settings, LlrpReader.ManagedInventoryRoSpecId, roReportSpecCustomItems, [], supportsStateAwareSingulation);

    public static ROSpec Compile(
        InventorySettings settings,
        IReadOnlyList<ILlrpParameter> roReportSpecCustomItems,
        IReadOnlyList<ILlrpParameter> c1G2InventoryCommandCustomItems,
        bool supportsStateAwareSingulation = false) =>
        Compile(settings, LlrpReader.ManagedInventoryRoSpecId, roReportSpecCustomItems, c1G2InventoryCommandCustomItems, supportsStateAwareSingulation);

    public static ROSpec Compile(
        InventorySettings settings,
        uint roSpecId,
        IReadOnlyList<ILlrpParameter> roReportSpecCustomItems,
        IReadOnlyList<ILlrpParameter> c1G2InventoryCommandCustomItems,
        bool supportsStateAwareSingulation)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(roReportSpecCustomItems);
        ArgumentNullException.ThrowIfNull(c1G2InventoryCommandCustomItems);
        Validate(settings, roSpecId);

        ushort[] antennaIds = settings.AntennaIds.ToArray();
        ROSpecStartTrigger startTrigger = CompileStartTrigger(settings.StartTrigger);
        ROSpecStopTrigger stopTrigger = CompileStopTrigger(settings.StopTrigger);
        var boundary = new ROBoundarySpec(startTrigger, stopTrigger);

        C1G2TagInventoryStateAwareSingulationAction? stateAwareAction = CompileStateAwareAction(
            settings.StateAwareSingulation,
            supportsStateAwareSingulation);
        bool hasStateAwareFilters = settings.Filters.Any(static filter => filter.StateAwareAction is not null);
        if (hasStateAwareFilters && stateAwareAction is null)
        {
            throw new ArgumentException(
                "State-aware inventory filters require StateAwareSingulation so the reader has an explicit Session, A/B target, and SL selection.",
                nameof(settings));
        }
        if ((hasStateAwareFilters || stateAwareAction is not null) && !supportsStateAwareSingulation)
        {
            throw new NotSupportedException(
                "The connected reader does not advertise C1G2 tag inventory state-aware singulation support.");
        }
        C1G2SingulationControl? singulationControl = (settings.AntennaConfigurations.Count != 0 || settings.Session != 0 || settings.TagPopulationEstimate != 32 || stateAwareAction is not null)
            ? new C1G2SingulationControl(settings.Session, settings.TagPopulationEstimate, 0, stateAwareAction)
            : null;
        C1G2RFControl? rfControl = (settings.AntennaConfigurations.Count != 0 || settings.ModeIndex != 0 || settings.Tari != 0)
            ? new C1G2RFControl(settings.ModeIndex, settings.Tari)
            : null;

        bool needsInventoryCommand = settings.AntennaConfigurations.Count != 0 || singulationControl is not null || rfControl is not null || settings.Filters.Count != 0 || c1G2InventoryCommandCustomItems.Count != 0;
        C1G2InventoryCommand? invCmd = null;
        if (needsInventoryCommand)
        {
            invCmd = new C1G2InventoryCommand(
                TagInventoryStateAware: hasStateAwareFilters || stateAwareAction is not null,
                C1G2FilterItems: CompileFilters(settings.Filters),
                C1G2RFControl: rfControl,
                C1G2SingulationControl: singulationControl,
                CustomItems: c1G2InventoryCommandCustomItems);
        }
        AntennaConfiguration[] antennaConfigs = settings.AntennaConfigurations.Count == 0
            ? invCmd is null ? [] : [new AntennaConfiguration(0, null, null, [invCmd])]
            : settings.AntennaConfigurations.Select(configuration => new AntennaConfiguration(
                configuration.AntennaId,
                configuration.ReceiverSensitivityIndex is ushort sensitivity ? new RFReceiver(sensitivity) : null,
                configuration.TransmitPowerIndex is ushort transmitPower
                    ? new RFTransmitter(configuration.HopTableId!.Value, configuration.ChannelIndex!.Value, transmitPower)
                    : null,
                invCmd is null ? [] : [invCmd])).ToArray();

        var aiSpec = new AISpec(
            antennaIds,
            new AISpecStopTrigger(AISpecStopTriggerType.Null, 0, null, null),
            [
                new InventoryParameterSpec(
                    settings.InventoryParameterSpecId,
                    AirProtocols.EPCGlobalClass1Gen2,
                    antennaConfigs,
                    Array.Empty<ILlrpParameter>()),
            ],
            Array.Empty<ILlrpParameter>());

        ArgumentNullException.ThrowIfNull(settings.Report);
        var reportSelector = new TagReportContentSelector(
            EnableROSpecID: settings.Report.IncludeRoSpecId,
            EnableSpecIndex: settings.Report.IncludeSpecIndex,
            EnableInventoryParameterSpecID: settings.Report.IncludeInventoryParameterSpecId,
            EnableAntennaID: settings.Report.IncludeAntennaId,
            EnableChannelIndex: settings.Report.IncludeChannelIndex,
            EnablePeakRSSI: settings.Report.IncludePeakRssi,
            EnableFirstSeenTimestamp: settings.Report.IncludeFirstSeenTimestamp,
            EnableLastSeenTimestamp: settings.Report.IncludeLastSeenTimestamp,
            EnableTagSeenCount: settings.Report.IncludeTagSeenCount,
            EnableAccessSpecID: settings.Report.IncludeAccessSpecId,
            AirProtocolEPCMemorySelectorItems: settings.Report.IncludeCrc || settings.Report.IncludePcBits
                ? [new C1G2EPCMemorySelector(settings.Report.IncludeCrc, settings.Report.IncludePcBits)]
                : Array.Empty<IAirProtocolEPCMemorySelector>());

        var reportSpec = new ROReportSpec(
            ToReportTrigger(settings.Report.Trigger),
            settings.ReportEveryNTags,
            reportSelector,
            roReportSpecCustomItems);

        return new ROSpec(
            roSpecId,
            settings.Priority,
            ROSpecState.Disabled,
            boundary,
            [aiSpec],
            reportSpec);
    }

    private static void Validate(InventorySettings settings, uint roSpecId)
    {
        if (roSpecId == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(roSpecId),
                roSpecId,
                "A managed inventory ROSpec identifier must be non-zero.");
        }

        if (settings.InventoryParameterSpecId == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(settings),
                settings.InventoryParameterSpecId,
                "An inventory parameter specification identifier must be non-zero.");
        }

        if (settings.ReportEveryNTags == 0 && settings.Report.Trigger != InventoryReportTrigger.UponNTagsOrEndOfRoSpec)
        {
            throw new ArgumentOutOfRangeException(
                nameof(settings),
                settings.ReportEveryNTags,
                "A report interval of zero is valid only with UponNTagsOrEndOfRoSpec, where the reader reports when the ROSpec ends.");
        }

        if (settings.AntennaIds.Count == 0)
        {
            throw new ArgumentException(
                "At least one antenna identifier is required; use antenna identifier 0 to select all antennas.",
                nameof(settings));
        }

        if (settings.AntennaIds.Count > 1 && settings.AntennaIds.Contains((ushort)0))
        {
            throw new ArgumentException(
                "Antenna identifier 0 selects all antennas and cannot be combined with explicit antenna identifiers.",
                nameof(settings));
        }

        ushort[] configuredAntennaIds = settings.AntennaConfigurations.Select(static configuration => configuration.AntennaId).ToArray();
        if (configuredAntennaIds.Distinct().Count() != configuredAntennaIds.Length ||
            (configuredAntennaIds.Contains((ushort)0) && configuredAntennaIds.Length != 1))
        {
            throw new ArgumentException("Inventory antenna configurations must have unique identifiers; antenna 0 cannot be combined with explicit identifiers.", nameof(settings));
        }
        foreach (InventoryAntennaConfiguration configuration in settings.AntennaConfigurations)
        {
            bool hasAnyTransmitterValue = configuration.TransmitPowerIndex.HasValue || configuration.HopTableId.HasValue || configuration.ChannelIndex.HasValue;
            if (hasAnyTransmitterValue && (!configuration.TransmitPowerIndex.HasValue || !configuration.HopTableId.HasValue || !configuration.ChannelIndex.HasValue))
            {
                throw new ArgumentException("An inventory RF transmitter requires transmit power, hop table, and channel index together.", nameof(settings));
            }
            if (configuration.AntennaId != 0 && !settings.AntennaIds.Contains((ushort)0) && !settings.AntennaIds.Contains(configuration.AntennaId))
            {
                throw new ArgumentException("Each inventory antenna configuration must target an antenna selected for inventory.", nameof(settings));
            }
        }

        if (settings.Session > 3)
        {
            throw new ArgumentOutOfRangeException(
                nameof(settings),
                settings.Session,
                "C1G2 singulation session must be between 0 and 3.");
        }
        foreach (InventorySelectFilter filter in settings.Filters)
        {
            int bitLength = filter.BitLength == 0 ? checked(filter.Mask.Length * 8) : filter.BitLength;
            if (filter.MemoryBank > 3 || filter.Mask.IsEmpty || bitLength <= 0 || bitLength > filter.Mask.Length * 8)
            {
                throw new ArgumentException("Inventory filters require a memory bank from 0 to 3 and a non-empty mask.", nameof(settings));
            }
            if (filter.StateAwareAction is null)
            {
                _ = ToAction(filter.MatchAction, filter.NonMatchAction);
            }
        }

        ArgumentNullException.ThrowIfNull(settings.StartTrigger);
        ArgumentNullException.ThrowIfNull(settings.StopTrigger);
        ArgumentNullException.ThrowIfNull(settings.Report);
        if (settings.StartTrigger.Type == InventoryStartTriggerType.Periodic && settings.StartTrigger.PeriodMilliseconds == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(settings), "A periodic inventory start trigger requires a non-zero period.");
        }
        if (settings.StartTrigger.Type == InventoryStartTriggerType.Gpi && settings.StartTrigger.GpiPortNumber == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(settings), "A GPI inventory start trigger requires a non-zero port number.");
        }
        if (settings.StopTrigger.Type == InventoryStopTriggerType.Duration && settings.StopTrigger.DurationMilliseconds == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(settings), "A duration inventory stop trigger requires a non-zero duration.");
        }
        if (settings.StopTrigger.Type == InventoryStopTriggerType.GpiWithTimeout && settings.StopTrigger.GpiPortNumber == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(settings), "A GPI inventory stop trigger requires a non-zero port number.");
        }
    }

    private static IReadOnlyList<C1G2Filter> CompileFilters(IReadOnlyList<InventorySelectFilter> filters) =>
        filters.Select(filter => new C1G2Filter(
            C1G2TruncateAction.Do_Not_Truncate,
            new C1G2TagInventoryMask((byte)filter.MemoryBank, filter.BitPointer, ToBits(filter.Mask.Span, filter.BitLength)),
            filter.StateAwareAction is null ? null : new C1G2TagInventoryStateAwareFilterAction(
                ToStateAwareTarget(filter.StateAwareAction.Target), ToStateAwareAction(filter.StateAwareAction.Action)),
            filter.StateAwareAction is null ? new C1G2TagInventoryStateUnawareFilterAction(ToAction(filter.MatchAction, filter.NonMatchAction)) : null)).ToArray();

    private static IReadOnlyList<bool> ToBits(ReadOnlySpan<byte> bytes, ushort bitLength)
    {
        int length = bitLength == 0 ? checked(bytes.Length * 8) : bitLength;
        return bytes.ToArray().SelectMany(value => Enumerable.Range(0, 8).Select(bit => (value & (1 << (7 - bit))) != 0)).Take(length).ToArray();
    }

    private static C1G2StateAwareTarget ToStateAwareTarget(InventoryFilterTarget target) => target switch
    {
        InventoryFilterTarget.SelectedFlag => C1G2StateAwareTarget.SL,
        InventoryFilterTarget.Session0 => C1G2StateAwareTarget.Inventoried_State_For_Session_S0,
        InventoryFilterTarget.Session1 => C1G2StateAwareTarget.Inventoried_State_For_Session_S1,
        InventoryFilterTarget.Session2 => C1G2StateAwareTarget.Inventoried_State_For_Session_S2,
        InventoryFilterTarget.Session3 => C1G2StateAwareTarget.Inventoried_State_For_Session_S3,
        _ => throw new ArgumentOutOfRangeException(nameof(target), target, null),
    };

    private static C1G2StateAwareAction ToStateAwareAction(InventoryFilterAction action) => action switch
    {
        InventoryFilterAction.AssertSelectedOrStateAAndDeassertSelectedOrStateB => C1G2StateAwareAction.AssertSLOrA_DeassertSLOrB,
        InventoryFilterAction.AssertSelectedOrStateAAndNoOperation => C1G2StateAwareAction.AssertSLOrA_Noop,
        InventoryFilterAction.NoOperationAndDeassertSelectedOrStateB => C1G2StateAwareAction.Noop_DeassertSLOrB,
        InventoryFilterAction.NegateSelectedOrStateAndNoOperation => C1G2StateAwareAction.NegateSLOrABBA_Noop,
        InventoryFilterAction.DeassertSelectedOrStateBAndAssertSelectedOrStateA => C1G2StateAwareAction.DeassertSLOrB_AssertSLOrA,
        InventoryFilterAction.DeassertSelectedOrStateBAndNoOperation => C1G2StateAwareAction.DeassertSLOrB_Noop,
        InventoryFilterAction.NoOperationAndAssertSelectedOrStateA => C1G2StateAwareAction.Noop_AssertSLOrA,
        InventoryFilterAction.NoOperationAndNegateSelectedOrState => C1G2StateAwareAction.Noop_NegateSLOrABBA,
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
    };

    private static ROReportTriggerType ToReportTrigger(InventoryReportTrigger trigger) => trigger switch
    {
        InventoryReportTrigger.None => ROReportTriggerType.None,
        InventoryReportTrigger.UponNTagsOrEndOfAiSpec => ROReportTriggerType.Upon_N_Tags_Or_End_Of_AISpec,
        InventoryReportTrigger.UponNTagsOrEndOfRoSpec => ROReportTriggerType.Upon_N_Tags_Or_End_Of_ROSpec,
        _ => throw new ArgumentOutOfRangeException(nameof(trigger), trigger, null),
    };

    private static C1G2StateUnawareAction ToAction(InventorySelectAction match, InventorySelectAction nonMatch) => (match, nonMatch) switch
    {
        (InventorySelectAction.Select, InventorySelectAction.Unselect) => C1G2StateUnawareAction.Select_Unselect,
        (InventorySelectAction.Select, InventorySelectAction.DoNothing) => C1G2StateUnawareAction.Select_DoNothing,
        (InventorySelectAction.DoNothing, InventorySelectAction.Unselect) => C1G2StateUnawareAction.DoNothing_Unselect,
        (InventorySelectAction.Unselect, InventorySelectAction.DoNothing) => C1G2StateUnawareAction.Unselect_DoNothing,
        (InventorySelectAction.Unselect, InventorySelectAction.Select) => C1G2StateUnawareAction.Unselect_Select,
        (InventorySelectAction.DoNothing, InventorySelectAction.Select) => C1G2StateUnawareAction.DoNothing_Select,
        _ => throw new ArgumentException("The specified inventory Select action pair is not supported by LLRP.")
    };

    private static ROSpecStartTrigger CompileStartTrigger(InventoryStartTrigger trigger) => trigger.Type switch
    {
        InventoryStartTriggerType.None => new(ROSpecStartTriggerType.Null, null, null),
        InventoryStartTriggerType.Immediate => new(ROSpecStartTriggerType.Immediate, null, null),
        InventoryStartTriggerType.Periodic => new(
            ROSpecStartTriggerType.Periodic,
            new PeriodicTriggerValue(
                trigger.OffsetMilliseconds,
                trigger.PeriodMilliseconds,
                trigger.StartAtUtc is { } startAtUtc ? new UTCTimestamp(ToUtcMicroseconds(startAtUtc)) : null),
            null),
        InventoryStartTriggerType.Gpi => new(
            ROSpecStartTriggerType.GPI,
            null,
            new GPITriggerValue(trigger.GpiPortNumber, trigger.GpiState, trigger.TimeoutMilliseconds)),
        _ => throw new ArgumentOutOfRangeException(nameof(trigger), trigger.Type, null),
    };

    private static ulong ToUtcMicroseconds(DateTimeOffset timestamp)
    {
        TimeSpan offset = timestamp.ToUniversalTime() - DateTimeOffset.UnixEpoch;
        if (offset < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timestamp), "A periodic UTC start time must not precede the Unix epoch.");
        }

        return checked((ulong)(offset.Ticks / TimeSpan.TicksPerMicrosecond));
    }

    private static ROSpecStopTrigger CompileStopTrigger(InventoryStopTrigger trigger) => trigger.Type switch
    {
        InventoryStopTriggerType.None => new(ROSpecStopTriggerType.Null, 0, null),
        InventoryStopTriggerType.Duration => new(ROSpecStopTriggerType.Duration, trigger.DurationMilliseconds, null),
        InventoryStopTriggerType.GpiWithTimeout => new(
            ROSpecStopTriggerType.GPI_With_Timeout,
            0,
            new GPITriggerValue(trigger.GpiPortNumber, trigger.GpiState, trigger.TimeoutMilliseconds)),
        _ => throw new ArgumentOutOfRangeException(nameof(trigger), trigger.Type, null),
    };

    private static C1G2TagInventoryStateAwareSingulationAction? CompileStateAwareAction(
        InventoryStateAwareSingulation? action,
        bool supportsStateAwareSingulation)
    {
        if (action is null)
        {
            return null;
        }
        if (!supportsStateAwareSingulation)
        {
            throw new NotSupportedException(
                "The connected reader does not advertise C1G2 tag inventory state-aware singulation support.");
        }

        var target = action.Target switch
        {
            InventoryTarget.StateA => C1G2TagInventoryStateAwareI.State_A,
            InventoryTarget.StateB => C1G2TagInventoryStateAwareI.State_B,
            _ => throw new ArgumentOutOfRangeException(nameof(action), action.Target, null),
        };
        var selectedFlag = action.SelectedFlag switch
        {
            InventorySelectedFlag.Set => C1G2TagInventoryStateAwareS.SL,
            InventorySelectedFlag.Clear => C1G2TagInventoryStateAwareS.Not_SL,
            InventorySelectedFlag.All => throw new NotSupportedException(
                "InventorySelectedFlag.All requires the S_All field introduced by LLRP 1.1 and cannot be represented by LLRP 1.0.1."),
            _ => throw new ArgumentOutOfRangeException(nameof(action), action.SelectedFlag, null),
        };
        return new C1G2TagInventoryStateAwareSingulationAction(target, selectedFlag);
    }
}
