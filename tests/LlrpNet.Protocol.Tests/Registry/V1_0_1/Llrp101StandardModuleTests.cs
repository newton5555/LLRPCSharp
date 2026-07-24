using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Messages;
using LlrpNet.Protocol.Messages.V1_0_1;
using LlrpNet.Protocol.Parameters.V1_0_1;
using LlrpNet.Protocol.Registry;
using LlrpNet.Protocol.Registry.V1_0_1;

namespace LlrpNet.Protocol.Tests.Registry.V1_0_1;

public sealed class Llrp101StandardModuleTests
{
    [Fact]
    public void Register_SecondInvocationUsesRegistryConflictFailure()
    {
        var registry = new LlrpCodecRegistry();
        Llrp101StandardModule.Register(registry);

        Assert.Throws<InvalidOperationException>(() => Llrp101StandardModule.Register(registry));

        Assert.IsType<Keepalive>(
            registry.DecodeMessage([0x04, 0x3E, 0x00, 0x00, 0x00, 0x0A, 0x00, 0x00, 0x00, 0x01]));
    }

    [Fact]
    public void Register_IsScopedToVersion101()
    {
        var registry = new LlrpCodecRegistry();
        Llrp101StandardModule.Register(registry);
        byte[] version11Keepalive =
        [
            0x08, 0x3E,
            0x00, 0x00, 0x00, 0x0A,
            0x00, 0x00, 0x00, 0x01,
        ];

        var unknown = Assert.IsType<UnknownMessage>(registry.DecodeMessage(version11Keepalive));

        Assert.Equal(LlrpProtocolVersion.Version11, unknown.Version);
        Assert.Equal(Keepalive.MessageType, unknown.MessageType);
    }

    [Fact]
    public void Register_ProvidesEveryRoSpecManagementMessageMapping()
    {
        var registry = new LlrpCodecRegistry();
        Llrp101StandardModule.Register(registry);
        (ushort MessageType, Type ExpectedType, byte[] Payload)[] mappings =
        [
            (
                AddRoSpec.MessageType,
                typeof(AddRoSpec),
                registry.EncodeParameter(
                    LlrpProtocolVersion.Version101,
                    CreateMinimalRoSpec())),
            (DeleteRoSpec.MessageType, typeof(DeleteRoSpec), [0x00, 0x00, 0x00, 0x01]),
            (StartRoSpec.MessageType, typeof(StartRoSpec), [0x00, 0x00, 0x00, 0x01]),
            (StopRoSpec.MessageType, typeof(StopRoSpec), [0x00, 0x00, 0x00, 0x01]),
            (EnableRoSpec.MessageType, typeof(EnableRoSpec), [0x00, 0x00, 0x00, 0x01]),
            (DisableRoSpec.MessageType, typeof(DisableRoSpec), [0x00, 0x00, 0x00, 0x01]),
            (GetRoSpecs.MessageType, typeof(GetRoSpecs), []),
            (AddRoSpecResponse.MessageType, typeof(AddRoSpecResponse), CreateSuccessStatus()),
            (DeleteRoSpecResponse.MessageType, typeof(DeleteRoSpecResponse), CreateSuccessStatus()),
            (StartRoSpecResponse.MessageType, typeof(StartRoSpecResponse), CreateSuccessStatus()),
            (StopRoSpecResponse.MessageType, typeof(StopRoSpecResponse), CreateSuccessStatus()),
            (EnableRoSpecResponse.MessageType, typeof(EnableRoSpecResponse), CreateSuccessStatus()),
            (DisableRoSpecResponse.MessageType, typeof(DisableRoSpecResponse), CreateSuccessStatus()),
            (GetRoSpecsResponse.MessageType, typeof(GetRoSpecsResponse), CreateSuccessStatus()),
            (ErrorMessage.MessageType, typeof(ErrorMessage), CreateSuccessStatus()),
        ];

        foreach ((ushort messageType, Type expectedType, byte[] payload) in mappings)
        {
            int frameLength = LlrpMessageHeader.EncodedLength + payload.Length;
            var frame = new byte[frameLength];
            new LlrpMessageHeader(
                LlrpProtocolVersion.Version101,
                messageType,
                (uint)frameLength,
                MessageId: 7).Encode(frame);
            payload.CopyTo(frame.AsSpan(LlrpMessageHeader.EncodedLength));

            Assert.Equal(expectedType, registry.DecodeMessage(frame).GetType());
        }
    }

    [Fact]
    public void Register_RejectsNullRegistry()
    {
        Assert.Throws<ArgumentNullException>(() => Llrp101StandardModule.Register(null!));
    }

    private static byte[] CreateSuccessStatus()
    {
        return [0x01, 0x1F, 0x00, 0x08, 0x00, 0x00, 0x00, 0x00];
    }

    private static RoSpec CreateMinimalRoSpec()
    {
        return new RoSpec(
            ROSpecID: 1,
            Priority: 0,
            ROSpecState.Disabled,
            new ROBoundarySpec(
                new ROSpecStartTrigger(ROSpecStartTriggerType.Null, null, null),
                new ROSpecStopTrigger(ROSpecStopTriggerType.Null, 0, null)),
            [
                new AISpec(
                    AntennaIDs: [0],
                    new AISpecStopTrigger(AISpecStopTriggerType.Null, 0, null, null),
                    InventoryParameterSpecItems:
                    [
                        new InventoryParameterSpec(
                            InventoryParameterSpecID: 1,
                            AirProtocols.EPCGlobalClass1Gen2,
                            AntennaConfigurationItems: [],
                            CustomItems: []),
                    ],
                    CustomItems: []),
            ],
            ROReportSpec: null);
    }
}
