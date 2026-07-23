using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Codecs.V1_0_1;
using LlrpNet.Protocol.Messages.V1_0_1;
using LlrpNet.Protocol.Parameters.V1_0_1;

namespace LlrpNet.Protocol.Registry.V1_0_1;

/// <summary>
/// Registers the currently implemented standard LLRP 1.0.1 parameter and message codecs.
/// </summary>
public static class Llrp101StandardModule
{
    /// <summary>
    /// Registers the implemented capability, status, ROSpec-management, GET_READER_CAPABILITIES,
    /// and keepalive mappings.
    /// </summary>
    /// <param name="registry">The mutable codec registry to populate.</param>
    /// <remarks>
    /// This method is intentionally not idempotent. Existing wire or CLR mappings are rejected by
    /// <see cref="LlrpCodecRegistry"/> without replacement.
    /// </remarks>
    public static void Register(LlrpCodecRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        registry.RegisterTlvParameter(
            LlrpProtocolVersion.Version101,
            LlrpStatus.ParameterType,
            new LlrpStatusCodec(registry));
        registry.RegisterTlvParameter(
            LlrpProtocolVersion.Version101,
            GeneralDeviceCapabilities.ParameterType,
            new GeneralDeviceCapabilitiesCodec(registry));
        registry.RegisterTlvParameter(
            LlrpProtocolVersion.Version101,
            TagReportContentSelector.ParameterType,
            new TagReportContentSelectorCodec(registry));
        registry.RegisterTlvParameter(
            LlrpProtocolVersion.Version101,
            RoReportSpec.ParameterType,
            new RoReportSpecCodec(registry));
        registry.RegisterTlvParameter(
            LlrpProtocolVersion.Version101,
            InventoryParameterSpec.ParameterType,
            new InventoryParameterSpecCodec(registry));
        registry.RegisterTlvParameter(
            LlrpProtocolVersion.Version101,
            AiSpecStopTrigger.ParameterType,
            new AiSpecStopTriggerCodec(registry));
        registry.RegisterTlvParameter(
            LlrpProtocolVersion.Version101,
            AiSpec.ParameterType,
            new AiSpecCodec(registry));
        registry.RegisterTlvParameter(
            LlrpProtocolVersion.Version101,
            RoSpecStartTrigger.ParameterType,
            new RoSpecStartTriggerCodec(registry));
        registry.RegisterTlvParameter(
            LlrpProtocolVersion.Version101,
            RoSpecStopTrigger.ParameterType,
            new RoSpecStopTriggerCodec(registry));
        registry.RegisterTlvParameter(
            LlrpProtocolVersion.Version101,
            RoBoundarySpec.ParameterType,
            new RoBoundarySpecCodec(registry));
        registry.RegisterTlvParameter(
            LlrpProtocolVersion.Version101,
            RoSpec.ParameterType,
            new RoSpecCodec(registry));

        registry.RegisterMessage(
            LlrpProtocolVersion.Version101,
            GetReaderCapabilities.MessageType,
            new GetReaderCapabilitiesCodec(registry));
        registry.RegisterMessage(
            LlrpProtocolVersion.Version101,
            GetReaderCapabilitiesResponse.MessageType,
            new GetReaderCapabilitiesResponseCodec(registry));
        registry.RegisterMessage(
            LlrpProtocolVersion.Version101,
            AddRoSpec.MessageType,
            new AddRoSpecCodec(registry));
        registry.RegisterMessage(
            LlrpProtocolVersion.Version101,
            DeleteRoSpec.MessageType,
            new DeleteRoSpecCodec());
        registry.RegisterMessage(
            LlrpProtocolVersion.Version101,
            StartRoSpec.MessageType,
            new StartRoSpecCodec());
        registry.RegisterMessage(
            LlrpProtocolVersion.Version101,
            StopRoSpec.MessageType,
            new StopRoSpecCodec());
        registry.RegisterMessage(
            LlrpProtocolVersion.Version101,
            EnableRoSpec.MessageType,
            new EnableRoSpecCodec());
        registry.RegisterMessage(
            LlrpProtocolVersion.Version101,
            DisableRoSpec.MessageType,
            new DisableRoSpecCodec());
        registry.RegisterMessage(
            LlrpProtocolVersion.Version101,
            GetRoSpecs.MessageType,
            new GetRoSpecsCodec());
        registry.RegisterMessage(
            LlrpProtocolVersion.Version101,
            AddRoSpecResponse.MessageType,
            new AddRoSpecResponseCodec(registry));
        registry.RegisterMessage(
            LlrpProtocolVersion.Version101,
            DeleteRoSpecResponse.MessageType,
            new DeleteRoSpecResponseCodec(registry));
        registry.RegisterMessage(
            LlrpProtocolVersion.Version101,
            StartRoSpecResponse.MessageType,
            new StartRoSpecResponseCodec(registry));
        registry.RegisterMessage(
            LlrpProtocolVersion.Version101,
            StopRoSpecResponse.MessageType,
            new StopRoSpecResponseCodec(registry));
        registry.RegisterMessage(
            LlrpProtocolVersion.Version101,
            EnableRoSpecResponse.MessageType,
            new EnableRoSpecResponseCodec(registry));
        registry.RegisterMessage(
            LlrpProtocolVersion.Version101,
            DisableRoSpecResponse.MessageType,
            new DisableRoSpecResponseCodec(registry));
        registry.RegisterMessage(
            LlrpProtocolVersion.Version101,
            GetRoSpecsResponse.MessageType,
            new GetRoSpecsResponseCodec(registry));
        registry.RegisterMessage(
            LlrpProtocolVersion.Version101,
            Keepalive.MessageType,
            new KeepaliveCodec());
        registry.RegisterMessage(
            LlrpProtocolVersion.Version101,
            KeepaliveAck.MessageType,
            new KeepaliveAckCodec());
        registry.RegisterMessage(
            LlrpProtocolVersion.Version101,
            ErrorMessage.MessageType,
            new ErrorMessageCodec(registry));
    }
}
