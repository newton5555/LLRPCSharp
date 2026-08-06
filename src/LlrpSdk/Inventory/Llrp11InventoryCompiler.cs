using LlrpNet.Protocol.Choices.V1_1;
using TagReportContentSelector = LlrpNet.Protocol.Parameters.V1_0_1.TagReportContentSelector;
using LlrpNet.Protocol.Enumerations.V1_1;
using LlrpNet.Protocol.Parameters;
using LlrpNet.Protocol.Parameters.V1_1;
using V11Choices = LlrpNet.Protocol.Choices.V1_1;
using V11Enumerations = LlrpNet.Protocol.Enumerations.V1_1;
using V11Parameters = LlrpNet.Protocol.Parameters.V1_1;

namespace LlrpSdk;

/// <summary>
/// Compiles the SDK inventory intent into the standard LLRP 1.1 ROSpec graph.
/// </summary>
internal static class Llrp11InventoryCompiler
{
    public static ROSpec Compile(
        InventorySettings settings,
        IReadOnlyList<ILlrpParameter> roReportSpecCustomItems,
        bool supportsStateAwareSingulation = false) =>
        Compile(settings, roReportSpecCustomItems, [], supportsStateAwareSingulation);

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
        bool supportsStateAwareSingulation = false)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(roReportSpecCustomItems);
        ArgumentNullException.ThrowIfNull(c1G2InventoryCommandCustomItems);
        Validate(settings, roSpecId);

        ushort[] antennaIds = settings.AntennaIds.ToArray();
        ROSpecStartTrigger startTrigger = CompileStartTrigger(settings.StartTrigger);
        ROSpecStopTrigger stopTrigger = CompileStopTrigger(settings.StopTrigger);
        var boundary = new ROBoundarySpec(startTrigger, stopTrigger);

        V11Parameters.C1G2TagInventoryStateAwareSingulationAction? stateAwareAction = CompileStateAwareAction(
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
        V11Parameters.C1G2SingulationControl? singulationControl = (settings.AntennaConfigurations.Count != 0 || settings.Session != 0 || settings.TagPopulationEstimate != 32 || stateAwareAction is not null)
            ? new V11Parameters.C1G2SingulationControl(settings.Session, settings.TagPopulationEstimate, 0, stateAwareAction)
            : null;
        V11Parameters.C1G2RFControl? rfControl = (settings.AntennaConfigurations.Count != 0 || settings.ModeIndex != 0 || settings.Tari != 0)
            ? new V11Parameters.C1G2RFControl(settings.ModeIndex, settings.Tari)
            : null;

        bool needsInventoryCommand = settings.AntennaConfigurations.Count != 0 || singulationControl is not null || rfControl is not null || settings.Filters.Count != 0 || c1G2InventoryCommandCustomItems.Count != 0;
        V11Parameters.C1G2InventoryCommand? invCmd = null;
        if (needsInventoryCommand)
        {
            invCmd = new V11Parameters.C1G2InventoryCommand(
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
                configuration.ReceiverSensitivityIndex is ushort sensitivity ? new V11Parameters.RFReceiver(sensitivity) : null,
                configuration.TransmitPowerIndex is ushort transmitPower
                    ? new V11Parameters.RFTransmitter(configuration.HopTableId!.Value, configuration.ChannelIndex!.Value, transmitPower)
                    : null,
                invCmd is null ? [] : [invCmd])).ToArray();

        var aiSpec = new AISpec(
            antennaIds,
            new AISpecStopTrigger(V11Enumerations.AISpecStopTriggerType.Null, 0, null, null),
            [
                new InventoryParameterSpec(
                    settings.InventoryParameterSpecId,
                    V11Enumerations.AirProtocols.EPCGlobalClass1Gen2,
                    antennaConfigs,
                    Array.Empty<ILlrpParameter>()),
            ],
            Array.Empty<ILlrpParameter>());

        ArgumentNullException.ThrowIfNull(settings.Report);
        var reportSelector = new V11Parameters.TagReportContentSelector(
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
                ? [new V11Parameters.C1G2EPCMemorySelector(settings.Report.IncludeCrc, settings.Report.IncludePcBits)]
                : Array.Empty<V11Choices.IAirProtocolEPCMemorySelector>());

        var reportSpec = new ROReportSpec(
            ToReportTrigger(settings.Report.Trigger),
            settings.ReportEveryNTags,
            reportSelector,
            roReportSpecCustomItems);

        return new ROSpec(
            roSpecId,
            settings.Priority,
            V11Enumerations.ROSpecState.Disabled,
            boundary,
            [aiSpec],
            reportSpec);
    }

    private static void Validate(InventorySettings settings, uint roSpecId)
    {
        ArgumentNullException.ThrowIfNull(settings.AntennaIds);
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

    private static IReadOnlyList<V11Parameters.C1G2Filter> CompileFilters(IReadOnlyList<InventorySelectFilter> filters) =>
        filters.Select(filter => new V11Parameters.C1G2Filter(
            V11Enumerations.C1G2TruncateAction.Do_Not_Truncate,
            new V11Parameters.C1G2TagInventoryMask((byte)filter.MemoryBank, filter.BitPointer, ToBits(filter.Mask.Span, filter.BitLength)),
            filter.StateAwareAction is null ? null : new V11Parameters.C1G2TagInventoryStateAwareFilterAction(
                ToStateAwareTarget(filter.StateAwareAction.Target), ToStateAwareAction(filter.StateAwareAction.Action)),
            filter.StateAwareAction is null ? new V11Parameters.C1G2TagInventoryStateUnawareFilterAction(ToAction(filter.MatchAction, filter.NonMatchAction)) : null)).ToArray();

    private static IReadOnlyList<bool> ToBits(ReadOnlySpan<byte> bytes, ushort bitLength)
    {
        int length = bitLength == 0 ? checked(bytes.Length * 8) : bitLength;
        return bytes.ToArray().SelectMany(value => Enumerable.Range(0, 8).Select(bit => (value & (1 << (7 - bit))) != 0)).Take(length).ToArray();
    }

    private static V11Enumerations.C1G2StateAwareTarget ToStateAwareTarget(InventoryFilterTarget target) => target switch
    {
        InventoryFilterTarget.SelectedFlag => V11Enumerations.C1G2StateAwareTarget.SL,
        InventoryFilterTarget.Session0 => V11Enumerations.C1G2StateAwareTarget.Inventoried_State_For_Session_S0,
        InventoryFilterTarget.Session1 => V11Enumerations.C1G2StateAwareTarget.Inventoried_State_For_Session_S1,
        InventoryFilterTarget.Session2 => V11Enumerations.C1G2StateAwareTarget.Inventoried_State_For_Session_S2,
        InventoryFilterTarget.Session3 => V11Enumerations.C1G2StateAwareTarget.Inventoried_State_For_Session_S3,
        _ => throw new ArgumentOutOfRangeException(nameof(target), target, null),
    };

    private static V11Enumerations.C1G2StateAwareAction ToStateAwareAction(InventoryFilterAction action) => action switch
    {
        InventoryFilterAction.AssertSelectedOrStateAAndDeassertSelectedOrStateB => V11Enumerations.C1G2StateAwareAction.AssertSLOrA_DeassertSLOrB,
        InventoryFilterAction.AssertSelectedOrStateAAndNoOperation => V11Enumerations.C1G2StateAwareAction.AssertSLOrA_Noop,
        InventoryFilterAction.NoOperationAndDeassertSelectedOrStateB => V11Enumerations.C1G2StateAwareAction.Noop_DeassertSLOrB,
        InventoryFilterAction.NegateSelectedOrStateAndNoOperation => V11Enumerations.C1G2StateAwareAction.NegateSLOrABBA_Noop,
        InventoryFilterAction.DeassertSelectedOrStateBAndAssertSelectedOrStateA => V11Enumerations.C1G2StateAwareAction.DeassertSLOrB_AssertSLOrA,
        InventoryFilterAction.DeassertSelectedOrStateBAndNoOperation => V11Enumerations.C1G2StateAwareAction.DeassertSLOrB_Noop,
        InventoryFilterAction.NoOperationAndAssertSelectedOrStateA => V11Enumerations.C1G2StateAwareAction.Noop_AssertSLOrA,
        InventoryFilterAction.NoOperationAndNegateSelectedOrState => V11Enumerations.C1G2StateAwareAction.Noop_NegateSLOrABBA,
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
    };

    private static V11Enumerations.C1G2StateUnawareAction ToAction(InventorySelectAction match, InventorySelectAction nonMatch) => (match, nonMatch) switch
    {
        (InventorySelectAction.Select, InventorySelectAction.Unselect) => V11Enumerations.C1G2StateUnawareAction.Select_Unselect,
        (InventorySelectAction.Select, InventorySelectAction.DoNothing) => V11Enumerations.C1G2StateUnawareAction.Select_DoNothing,
        (InventorySelectAction.DoNothing, InventorySelectAction.Unselect) => V11Enumerations.C1G2StateUnawareAction.DoNothing_Unselect,
        (InventorySelectAction.Unselect, InventorySelectAction.DoNothing) => V11Enumerations.C1G2StateUnawareAction.Unselect_DoNothing,
        (InventorySelectAction.Unselect, InventorySelectAction.Select) => V11Enumerations.C1G2StateUnawareAction.Unselect_Select,
        (InventorySelectAction.DoNothing, InventorySelectAction.Select) => V11Enumerations.C1G2StateUnawareAction.DoNothing_Select,
        _ => throw new ArgumentException("The specified inventory Select action pair is not supported by LLRP.")
    };

    private static V11Enumerations.ROReportTriggerType ToReportTrigger(InventoryReportTrigger trigger) => trigger switch
    {
        InventoryReportTrigger.None => V11Enumerations.ROReportTriggerType.None,
        InventoryReportTrigger.UponNTagsOrEndOfAiSpec => V11Enumerations.ROReportTriggerType.Upon_N_Tags_Or_End_Of_AISpec,
        InventoryReportTrigger.UponNTagsOrEndOfRoSpec => V11Enumerations.ROReportTriggerType.Upon_N_Tags_Or_End_Of_ROSpec,
        _ => throw new ArgumentOutOfRangeException(nameof(trigger), trigger, null),
    };

    private static ROSpecStartTrigger CompileStartTrigger(InventoryStartTrigger trigger) => trigger.Type switch
    {
        InventoryStartTriggerType.None => new(V11Enumerations.ROSpecStartTriggerType.Null, null, null),
        InventoryStartTriggerType.Immediate => new(V11Enumerations.ROSpecStartTriggerType.Immediate, null, null),
        InventoryStartTriggerType.Periodic => new(
            V11Enumerations.ROSpecStartTriggerType.Periodic,
            new V11Parameters.PeriodicTriggerValue(
                trigger.OffsetMilliseconds,
                trigger.PeriodMilliseconds,
                trigger.StartAtUtc is { } startAtUtc
                    ? new V11Parameters.UTCTimestamp(ToUtcMicroseconds(startAtUtc))
                    : null),
            null),
        InventoryStartTriggerType.Gpi => new(
            V11Enumerations.ROSpecStartTriggerType.GPI,
            null,
            new V11Parameters.GPITriggerValue(trigger.GpiPortNumber, trigger.GpiState, trigger.TimeoutMilliseconds)),
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
        InventoryStopTriggerType.None => new(V11Enumerations.ROSpecStopTriggerType.Null, 0, null),
        InventoryStopTriggerType.Duration => new(V11Enumerations.ROSpecStopTriggerType.Duration, trigger.DurationMilliseconds, null),
        InventoryStopTriggerType.GpiWithTimeout => new(
            V11Enumerations.ROSpecStopTriggerType.GPI_With_Timeout,
            0,
            new V11Parameters.GPITriggerValue(trigger.GpiPortNumber, trigger.GpiState, trigger.TimeoutMilliseconds)),
        _ => throw new ArgumentOutOfRangeException(nameof(trigger), trigger.Type, null),
    };

    private static V11Parameters.C1G2TagInventoryStateAwareSingulationAction? CompileStateAwareAction(
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
            InventoryTarget.StateA => V11Enumerations.C1G2TagInventoryStateAwareI.State_A,
            InventoryTarget.StateB => V11Enumerations.C1G2TagInventoryStateAwareI.State_B,
            _ => throw new ArgumentOutOfRangeException(nameof(action), action.Target, null),
        };
        var (selectedFlag, sAll) = action.SelectedFlag switch
        {
            InventorySelectedFlag.Set => (V11Enumerations.C1G2TagInventoryStateAwareS.SL, false),
            InventorySelectedFlag.Clear => (V11Enumerations.C1G2TagInventoryStateAwareS.Not_SL, false),
            InventorySelectedFlag.All => (V11Enumerations.C1G2TagInventoryStateAwareS.SL, true),
            _ => throw new ArgumentOutOfRangeException(nameof(action), action.SelectedFlag, null),
        };
        return new V11Parameters.C1G2TagInventoryStateAwareSingulationAction(target, selectedFlag, sAll);
    }
}
