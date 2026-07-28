using LlrpNet.Protocol.Choices.V1_1;
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
    public static ROSpec Compile(ReaderSettings settings, IReadOnlyList<ILlrpParameter> roReportSpecCustomItems)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(roReportSpecCustomItems);
        Validate(settings);

        ushort[] antennaIds = settings.AntennaIds.ToArray();
        var startTrigger = new ROSpecStartTrigger(V11Enumerations.ROSpecStartTriggerType.Immediate, null, null);
        var stopTrigger = new ROSpecStopTrigger(V11Enumerations.ROSpecStopTriggerType.Null, 0, null);
        var boundary = new ROBoundarySpec(startTrigger, stopTrigger);

        V11Parameters.C1G2SingulationControl? singulationControl = (settings.Session != 0 || settings.TagPopulationEstimate != 32)
            ? new V11Parameters.C1G2SingulationControl(settings.Session, settings.TagPopulationEstimate, 0, null)
            : null;
        V11Parameters.C1G2RFControl? rfControl = (settings.ModeIndex != 0 || settings.Tari != 0)
            ? new V11Parameters.C1G2RFControl(settings.ModeIndex, settings.Tari)
            : null;

        AntennaConfiguration[] antennaConfigs = Array.Empty<AntennaConfiguration>();
        if (singulationControl is not null || rfControl is not null)
        {
            var invCmd = new V11Parameters.C1G2InventoryCommand(
                TagInventoryStateAware: false,
                C1G2FilterItems: Array.Empty<V11Parameters.C1G2Filter>(),
                C1G2RFControl: rfControl,
                C1G2SingulationControl: singulationControl,
                CustomItems: Array.Empty<ILlrpParameter>());
            antennaConfigs = [new AntennaConfiguration(0, null, null, [invCmd])];
        }

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

        var reportSelector = new V11Parameters.TagReportContentSelector(
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
            AirProtocolEPCMemorySelectorItems: Array.Empty<V11Choices.IAirProtocolEPCMemorySelector>());

        var reportSpec = new ROReportSpec(
            V11Enumerations.ROReportTriggerType.Upon_N_Tags_Or_End_Of_AISpec,
            settings.ReportEveryNTags,
            reportSelector,
            roReportSpecCustomItems);

        return new ROSpec(
            settings.RoSpecId,
            settings.Priority,
            V11Enumerations.ROSpecState.Disabled,
            boundary,
            [aiSpec],
            reportSpec);
    }

    private static void Validate(ReaderSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings.AntennaIds);
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
    }
}
