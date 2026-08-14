using LlrpNet.Protocol.Enumerations.V2_0;
using LlrpNet.Protocol.Messages;
using LlrpNet.Protocol.Messages.V2_0;
using LlrpNet.Protocol.Parameters.V2_0;

namespace LlrpSdk;

/// <summary>
/// Projects LLRP 2.0 READER_EVENT_NOTIFICATION messages into version-independent event projections.
/// </summary>
/// <remarks>This file is version-bound by name and may use bare 2.0 protocol references.</remarks>
internal static class Llrp20EventProjector
{
    public static IReadOnlyList<ReaderEventProjection> Project(ILlrpMessage message)
    {
        if (message is not READER_EVENT_NOTIFICATION notification)
        {
            return [];
        }

        ReaderEventNotificationData data = notification.ReaderEventNotificationData;
        var projections = new List<ReaderEventProjection>(4);
        projections.Add(new ManagedRoSpecEventProjection(
            data.ROSpecEvent?.ROSpecID,
            data.ROSpecEvent?.EventType switch
            {
                ROSpecEventType.Start_Of_ROSpec => InventoryRuntimeState.Running,
                ROSpecEventType.End_Of_ROSpec or ROSpecEventType.Preemption_Of_ROSpec => InventoryRuntimeState.Disabled,
                _ => null,
            }));
        if (data.GPIEvent is { } gpi)
        {
            projections.Add(new GpiChangedEventProjection(gpi.GPIPortNumber, gpi.GPIEvent_2));
        }
        if (data.AntennaEvent is { } antenna)
        {
            projections.Add(new AntennaChangedEventProjection(
                antenna.AntennaID,
                antenna.EventType == AntennaEventType.Antenna_Connected));
        }
        if (data.ReportBufferOverflowErrorEvent is not null)
        {
            projections.Add(new ReportBufferOverflowEventProjection());
        }
        if (data.ReportBufferLevelWarningEvent is { } warning)
        {
            projections.Add(new ReportBufferWarningEventProjection(warning.ReportBufferPercentageFull));
        }
        if (data.ReaderExceptionEvent is { } readerException)
        {
            projections.Add(new ReaderExceptionEventProjection(
                readerException.Message,
                readerException.ROSpecID?.ROSpecID_2,
                readerException.SpecIndex?.SpecIndex_2,
                readerException.InventoryParameterSpecID?.InventoryParameterSpecID_2,
                readerException.AntennaID?.AntennaID_2,
                readerException.AccessSpecID?.AccessSpecID_2,
                readerException.OpSpecID?.OpSpecID_2));
        }

        return projections;
    }
}
