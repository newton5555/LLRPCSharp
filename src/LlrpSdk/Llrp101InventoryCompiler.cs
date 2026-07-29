using LlrpNet.Protocol.Enumerations.V1_0_1;
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
        CompileWithDefaults(settings, roReportSpecCustomItems, supportsStateAwareSingulation, compilationDefaults: null);

    public static ROSpec CompileWithDefaults(
        InventorySettings settings,
        IReadOnlyList<ILlrpParameter> roReportSpecCustomItems,
        bool supportsStateAwareSingulation,
        InventoryCompilationDefaults? compilationDefaults)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(roReportSpecCustomItems);
        Validate(settings);

        ushort[] antennaIds = compilationDefaults is not null && settings.AntennaIds.Count == 1 && settings.AntennaIds[0] == 0
            ? compilationDefaults.AntennaIds.ToArray()
            : settings.AntennaIds.ToArray();
        ROSpecStartTrigger startTrigger = CompileStartTrigger(settings.StartTrigger);
        ROSpecStopTrigger stopTrigger = CompileStopTrigger(settings.StopTrigger);
        var boundary = new ROBoundarySpec(startTrigger, stopTrigger);

        C1G2TagInventoryStateAwareSingulationAction? stateAwareAction = CompileStateAwareAction(
            settings.StateAwareSingulation,
            supportsStateAwareSingulation);
        bool useExplicitCompatibilityDefaults = compilationDefaults is not null;
        C1G2SingulationControl? singulationControl = (useExplicitCompatibilityDefaults || settings.Session != 0 || settings.TagPopulationEstimate != 32 || stateAwareAction is not null)
            ? new C1G2SingulationControl(settings.Session, settings.TagPopulationEstimate, 0, stateAwareAction)
            : null;
        C1G2RFControl? rfControl = (useExplicitCompatibilityDefaults || settings.ModeIndex != 0 || settings.Tari != 0)
            ? new C1G2RFControl(settings.ModeIndex, settings.Tari)
            : null;

        AntennaConfiguration[] antennaConfigs = Array.Empty<AntennaConfiguration>();
        if (singulationControl is not null || rfControl is not null)
        {
            var invCmd = new C1G2InventoryCommand(
                TagInventoryStateAware: stateAwareAction is not null,
                C1G2FilterItems: Array.Empty<C1G2Filter>(),
                C1G2RFControl: rfControl,
                C1G2SingulationControl: singulationControl,
                CustomItems: Array.Empty<ILlrpParameter>());
            antennaConfigs = compilationDefaults is null
                ? [new AntennaConfiguration(0, null, null, [invCmd])]
                : antennaIds.Select(antennaId => new AntennaConfiguration(
                    antennaId,
                    new RFReceiver(compilationDefaults.ReceiverSensitivityIndex),
                    new RFTransmitter(
                        compilationDefaults.HopTableId,
                        compilationDefaults.ChannelIndex,
                        compilationDefaults.TransmitPowerIndex),
                    [invCmd])).ToArray();
        }

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

        var reportSelector = new TagReportContentSelector(
            EnableROSpecID: true,
            EnableSpecIndex: true,
            EnableInventoryParameterSpecID: true,
            EnableAntennaID: true,
            EnableChannelIndex: true,
            EnablePeakRSSI: true,
            EnableFirstSeenTimestamp: true,
            EnableLastSeenTimestamp: true,
            EnableTagSeenCount: true,
            EnableAccessSpecID: true,
            AirProtocolEPCMemorySelectorItems: Array.Empty<IAirProtocolEPCMemorySelector>());

        var reportSpec = new ROReportSpec(
            ROReportTriggerType.Upon_N_Tags_Or_End_Of_AISpec,
            settings.ReportEveryNTags,
            reportSelector,
            roReportSpecCustomItems);

        return new ROSpec(
            settings.RoSpecId,
            settings.Priority,
            ROSpecState.Disabled,
            boundary,
            [aiSpec],
            reportSpec);
    }

    private static void Validate(InventorySettings settings)
    {
        if (settings.RoSpecId == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(settings),
                settings.RoSpecId,
                "A managed inventory ROSpec identifier must be non-zero.");
        }

        if (settings.InventoryParameterSpecId == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(settings),
                settings.InventoryParameterSpecId,
                "An inventory parameter specification identifier must be non-zero.");
        }

        if (settings.ReportEveryNTags == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(settings),
                settings.ReportEveryNTags,
                "The report interval must be at least one tag.");
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

        if (settings.Session > 3)
        {
            throw new ArgumentOutOfRangeException(
                nameof(settings),
                settings.Session,
                "C1G2 singulation session must be between 0 and 3.");
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

    private static ROSpecStartTrigger CompileStartTrigger(InventoryStartTrigger trigger) => trigger.Type switch
    {
        InventoryStartTriggerType.None => new(ROSpecStartTriggerType.Null, null, null),
        InventoryStartTriggerType.Immediate => new(ROSpecStartTriggerType.Immediate, null, null),
        InventoryStartTriggerType.Periodic => new(
            ROSpecStartTriggerType.Periodic,
            new PeriodicTriggerValue(trigger.OffsetMilliseconds, trigger.PeriodMilliseconds, null),
            null),
        InventoryStartTriggerType.Gpi => new(
            ROSpecStartTriggerType.GPI,
            null,
            new GPITriggerValue(trigger.GpiPortNumber, trigger.GpiState, trigger.TimeoutMilliseconds)),
        _ => throw new ArgumentOutOfRangeException(nameof(trigger), trigger.Type, null),
    };

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
            _ => throw new ArgumentOutOfRangeException(nameof(action), action.SelectedFlag, null),
        };
        return new C1G2TagInventoryStateAwareSingulationAction(target, selectedFlag);
    }
}
