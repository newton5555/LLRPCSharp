using LlrpNet.Protocol.Enumerations.V1_0_1;
using LlrpNet.Protocol.Parameters;
using LlrpNet.Protocol.Parameters.V1_0_1;

namespace LlrpSdk;

/// <summary>
/// Reverse-compiles the SDK-reserved LLRP 1.0.1 ROSpec (and optional attached-data AccessSpec) back into the
/// version-independent managed inventory intent.
/// </summary>
/// <remarks>This file is version-bound by name and may use bare 1.0.1 protocol references.</remarks>
internal static class Llrp101ManagedRoSpecParser
{
    public static ParsedManagedRoSpec Parse(ROSpec roSpec, IReadOnlyList<ILlrpParameter> accessSpecs)
    {
        ArgumentNullException.ThrowIfNull(roSpec);
        ArgumentNullException.ThrowIfNull(accessSpecs);
        if (roSpec.ROSpecID != LlrpReader.ManagedInventoryRoSpecId)
        {
            throw new InvalidOperationException("The supplied V101Parameters.ROSpec is not the SDK-managed inventory V101Parameters.ROSpec.");
        }
        AISpec aiSpec = roSpec.SpecParameterItems.OfType<AISpec>().SingleOrDefault()
            ?? throw new InvalidOperationException("The reserved SDK V101Parameters.ROSpec must contain exactly one V101Parameters.AISpec.");
        InventoryParameterSpec inventorySpec = aiSpec.InventoryParameterSpecItems.Single();
        C1G2InventoryCommand? command = inventorySpec.AntennaConfigurationItems
            .SelectMany(configuration => configuration.AirProtocolInventoryCommandSettingsItems)
            .OfType<C1G2InventoryCommand>().FirstOrDefault();
        AccessSpec? attachedDataSpec = accessSpecs.OfType<AccessSpec>()
            .SingleOrDefault(spec => spec.AccessSpecID == LlrpReader.ManagedInventoryAttachedDataAccessSpecId);
        if (attachedDataSpec is not null && attachedDataSpec.ROSpecID != LlrpReader.ManagedInventoryRoSpecId)
        {
            throw new InvalidOperationException("The reserved SDK AttachedData V101Parameters.AccessSpec is not associated with the reserved SDK V101Parameters.ROSpec.");
        }
        C1G2Read? read = attachedDataSpec is null
            ? null
            : attachedDataSpec.AccessCommand.AccessCommandOpSpecItems.OfType<C1G2Read>().FirstOrDefault();
        if (attachedDataSpec is not null && read is null)
        {
            throw new InvalidOperationException("The reserved SDK AttachedData V101Parameters.AccessSpec must contain a V101Parameters.C1G2Read operation.");
        }
        InventoryStateAwareSingulation? stateAwareSingulation = ParseStateAwareSingulation(
            command?.C1G2SingulationControl?.C1G2TagInventoryStateAwareSingulationAction);
        var settings = new InventorySettings
        {
            Priority = roSpec.Priority,
            AntennaIds = aiSpec.AntennaIDs,
            InventoryParameterSpecId = inventorySpec.InventoryParameterSpecID,
            ReportEveryNTags = roSpec.ROReportSpec?.N ?? 1,
            Report = ParseReportSettings(roSpec.ROReportSpec),
            Session = command?.C1G2SingulationControl?.Session ?? 0,
            TagPopulationEstimate = command?.C1G2SingulationControl?.TagPopulation ?? 32,
            ModeIndex = command?.C1G2RFControl?.ModeIndex ?? 0,
            Tari = command?.C1G2RFControl?.Tari ?? 0,
            AntennaConfigurations = inventorySpec.AntennaConfigurationItems
                .Where(configuration => configuration.RFReceiver is not null || configuration.RFTransmitter is not null)
                .Select(configuration => new InventoryAntennaConfiguration
                {
                    AntennaId = configuration.AntennaID,
                    ReceiverSensitivityIndex = configuration.RFReceiver?.ReceiverSensitivity,
                    TransmitPowerIndex = configuration.RFTransmitter?.TransmitPower,
                    HopTableId = configuration.RFTransmitter?.HopTableID,
                    ChannelIndex = configuration.RFTransmitter?.ChannelIndex,
                }).ToArray(),
            Filters = command?.C1G2FilterItems.Select(ParseFilter).ToArray() ?? [],
            StartTrigger = ParseStartTrigger(roSpec.ROBoundarySpec.ROSpecStartTrigger),
            StopTrigger = ParseStopTrigger(roSpec.ROBoundarySpec.ROSpecStopTrigger),
            StateAwareSingulation = stateAwareSingulation,
            AttachedData = read is null ? new AttachedDataOptions() : new AttachedDataOptions
            {
                Enabled = true,
                MemoryBank = read.MB,
                WordPointer = read.WordPointer,
                WordCount = read.WordCount,
                AccessPassword = read.AccessPassword.ToString("X8")
            }
        };
        InventoryRuntimeState state = roSpec.CurrentState switch
        {
            ROSpecState.Active => InventoryRuntimeState.Running,
            ROSpecState.Inactive => InventoryRuntimeState.Enabled,
            _ => InventoryRuntimeState.Disabled
        };
        return new ParsedManagedRoSpec(
            settings,
            roSpec.ROReportSpec?.CustomItems ?? [],
            command?.CustomItems ?? [],
            state);
    }

    private static InventorySelectFilter ParseFilter(C1G2Filter filter)
    {
        if (filter.C1G2TagInventoryStateAwareFilterAction is { } stateAware)
        {
            bool[] stateAwareBits = filter.C1G2TagInventoryMask.TagMask.ToArray();
            return new InventorySelectFilter
            {
                MemoryBank = filter.C1G2TagInventoryMask.MB,
                BitPointer = filter.C1G2TagInventoryMask.Pointer,
                Mask = ManagedInventoryStateAssembler.BitsToBytes(stateAwareBits),
                BitLength = checked((ushort)stateAwareBits.Length),
                StateAwareAction = new InventoryStateAwareFilterAction
                {
                    Target = stateAware.Target switch
                    {
                        C1G2StateAwareTarget.SL => InventoryFilterTarget.SelectedFlag,
                        C1G2StateAwareTarget.Inventoried_State_For_Session_S0 => InventoryFilterTarget.Session0,
                        C1G2StateAwareTarget.Inventoried_State_For_Session_S1 => InventoryFilterTarget.Session1,
                        C1G2StateAwareTarget.Inventoried_State_For_Session_S2 => InventoryFilterTarget.Session2,
                        C1G2StateAwareTarget.Inventoried_State_For_Session_S3 => InventoryFilterTarget.Session3,
                        _ => throw new InvalidOperationException("The reserved SDK V101Parameters.ROSpec contains an unsupported state-aware filter target."),
                    },
                    Action = ToFilterAction(stateAware.Action),
                }
            };
        }
        C1G2TagInventoryStateUnawareFilterAction action = filter.C1G2TagInventoryStateUnawareFilterAction
            ?? throw new InvalidOperationException("A C1G2 filter must define exactly one Select action.");
        (InventorySelectAction match, InventorySelectAction nonMatch) = action.Action switch
        {
            C1G2StateUnawareAction.Select_Unselect => (InventorySelectAction.Select, InventorySelectAction.Unselect),
            C1G2StateUnawareAction.Select_DoNothing => (InventorySelectAction.Select, InventorySelectAction.DoNothing),
            C1G2StateUnawareAction.DoNothing_Unselect => (InventorySelectAction.DoNothing, InventorySelectAction.Unselect),
            C1G2StateUnawareAction.Unselect_DoNothing => (InventorySelectAction.Unselect, InventorySelectAction.DoNothing),
            C1G2StateUnawareAction.Unselect_Select => (InventorySelectAction.Unselect, InventorySelectAction.Select),
            _ => (InventorySelectAction.DoNothing, InventorySelectAction.Select)
        };
        bool[] bits = filter.C1G2TagInventoryMask.TagMask.ToArray();
        return new InventorySelectFilter
        {
            MemoryBank = filter.C1G2TagInventoryMask.MB,
            BitPointer = filter.C1G2TagInventoryMask.Pointer,
            Mask = ManagedInventoryStateAssembler.BitsToBytes(bits),
            BitLength = checked((ushort)bits.Length),
            MatchAction = match,
            NonMatchAction = nonMatch
        };
    }

    private static InventoryFilterAction ToFilterAction(C1G2StateAwareAction action) => action switch
    {
        C1G2StateAwareAction.AssertSLOrA_DeassertSLOrB => InventoryFilterAction.AssertSelectedOrStateAAndDeassertSelectedOrStateB,
        C1G2StateAwareAction.AssertSLOrA_Noop => InventoryFilterAction.AssertSelectedOrStateAAndNoOperation,
        C1G2StateAwareAction.Noop_DeassertSLOrB => InventoryFilterAction.NoOperationAndDeassertSelectedOrStateB,
        C1G2StateAwareAction.NegateSLOrABBA_Noop => InventoryFilterAction.NegateSelectedOrStateAndNoOperation,
        C1G2StateAwareAction.DeassertSLOrB_AssertSLOrA => InventoryFilterAction.DeassertSelectedOrStateBAndAssertSelectedOrStateA,
        C1G2StateAwareAction.DeassertSLOrB_Noop => InventoryFilterAction.DeassertSelectedOrStateBAndNoOperation,
        C1G2StateAwareAction.Noop_AssertSLOrA => InventoryFilterAction.NoOperationAndAssertSelectedOrStateA,
        C1G2StateAwareAction.Noop_NegateSLOrABBA => InventoryFilterAction.NoOperationAndNegateSelectedOrState,
        _ => throw new InvalidOperationException("The reserved SDK V101Parameters.ROSpec contains an unsupported state-aware filter action."),
    };

    private static InventoryStartTrigger ParseStartTrigger(ROSpecStartTrigger trigger) => trigger.ROSpecStartTriggerType switch
    {
        ROSpecStartTriggerType.Null => new() { Type = InventoryStartTriggerType.None },
        ROSpecStartTriggerType.Immediate => new() { Type = InventoryStartTriggerType.Immediate },
        ROSpecStartTriggerType.Periodic when trigger.PeriodicTriggerValue is { } periodic => new()
        {
            Type = InventoryStartTriggerType.Periodic,
            OffsetMilliseconds = periodic.Offset,
            PeriodMilliseconds = periodic.Period,
            StartAtUtc = periodic.UTCTimestamp is { } utc ? ManagedInventoryStateAssembler.FromUtcMicroseconds(utc.Microseconds) : null,
        },
        ROSpecStartTriggerType.GPI when trigger.GPITriggerValue is { } gpi => new()
        {
            Type = InventoryStartTriggerType.Gpi,
            GpiPortNumber = gpi.GPIPortNum,
            GpiState = gpi.GPIEvent,
            TimeoutMilliseconds = gpi.Timeout
        },
        _ => throw new InvalidOperationException("The reserved SDK V101Parameters.ROSpec has an unsupported or malformed start trigger."),
    };

    private static InventoryReportSettings ParseReportSettings(ROReportSpec? reportSpec)
    {
        if (reportSpec is null)
        {
            throw new InvalidOperationException("The reserved SDK V101Parameters.ROSpec must contain a V101Parameters.ROReportSpec.");
        }
        TagReportContentSelector selector = reportSpec.TagReportContentSelector;
        C1G2EPCMemorySelector? epc = selector.AirProtocolEPCMemorySelectorItems.OfType<C1G2EPCMemorySelector>().SingleOrDefault();
        if (selector.AirProtocolEPCMemorySelectorItems.Count != 0 && epc is null)
        {
            throw new InvalidOperationException("The reserved SDK V101Parameters.ROSpec has an unsupported EPC report selector.");
        }
        return new InventoryReportSettings
        {
            Trigger = reportSpec.ROReportTrigger switch
            {
                ROReportTriggerType.None => InventoryReportTrigger.None,
                ROReportTriggerType.Upon_N_Tags_Or_End_Of_AISpec => InventoryReportTrigger.UponNTagsOrEndOfAiSpec,
                ROReportTriggerType.Upon_N_Tags_Or_End_Of_ROSpec => InventoryReportTrigger.UponNTagsOrEndOfRoSpec,
                _ => throw new InvalidOperationException("The reserved SDK V101Parameters.ROSpec has an unsupported report trigger."),
            },
            IncludeRoSpecId = selector.EnableROSpecID,
            IncludeSpecIndex = selector.EnableSpecIndex,
            IncludeInventoryParameterSpecId = selector.EnableInventoryParameterSpecID,
            IncludeAntennaId = selector.EnableAntennaID,
            IncludeChannelIndex = selector.EnableChannelIndex,
            IncludePeakRssi = selector.EnablePeakRSSI,
            IncludeFirstSeenTimestamp = selector.EnableFirstSeenTimestamp,
            IncludeLastSeenTimestamp = selector.EnableLastSeenTimestamp,
            IncludeTagSeenCount = selector.EnableTagSeenCount,
            IncludeAccessSpecId = selector.EnableAccessSpecID,
            IncludeCrc = epc?.EnableCRC ?? false,
            IncludePcBits = epc?.EnablePCBits ?? false,
        };
    }

    private static InventoryStopTrigger ParseStopTrigger(ROSpecStopTrigger trigger) => trigger.ROSpecStopTriggerType switch
    {
        ROSpecStopTriggerType.Null => new() { Type = InventoryStopTriggerType.None },
        ROSpecStopTriggerType.Duration => new() { Type = InventoryStopTriggerType.Duration, DurationMilliseconds = trigger.DurationTriggerValue },
        ROSpecStopTriggerType.GPI_With_Timeout when trigger.GPITriggerValue is { } gpi => new()
        {
            Type = InventoryStopTriggerType.GpiWithTimeout,
            GpiPortNumber = gpi.GPIPortNum,
            GpiState = gpi.GPIEvent,
            TimeoutMilliseconds = gpi.Timeout
        },
        _ => throw new InvalidOperationException("The reserved SDK V101Parameters.ROSpec has an unsupported or malformed stop trigger."),
    };

    private static InventoryStateAwareSingulation? ParseStateAwareSingulation(C1G2TagInventoryStateAwareSingulationAction? action) => action is null ? null : new InventoryStateAwareSingulation
    {
        Target = action.I switch
        {
            C1G2TagInventoryStateAwareI.State_A => InventoryTarget.StateA,
            C1G2TagInventoryStateAwareI.State_B => InventoryTarget.StateB,
            _ => throw new InvalidOperationException("The reserved SDK V101Parameters.ROSpec has an unsupported state-aware singulation target."),
        },
        SelectedFlag = action.S switch
        {
            C1G2TagInventoryStateAwareS.SL => InventorySelectedFlag.Set,
            C1G2TagInventoryStateAwareS.Not_SL => InventorySelectedFlag.Clear,
            _ => throw new InvalidOperationException("The reserved SDK V101Parameters.ROSpec has an unsupported state-aware singulation flag."),
        },
    };
}
