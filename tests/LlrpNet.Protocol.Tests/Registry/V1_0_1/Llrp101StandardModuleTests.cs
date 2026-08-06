using LlrpNet.Core.Protocol;
using V101Parameters = LlrpNet.Protocol.Parameters.V1_0_1;
using V101Messages = LlrpNet.Protocol.Messages.V1_0_1;
using LlrpNet.Protocol.Enumerations.V1_0_1;
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

        Assert.IsType<V101Messages.KEEPALIVE>(
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
        Assert.Equal(V101Messages.KEEPALIVE.MessageType, unknown.MessageType);
    }

    [Fact]
    public void Register_ProvidesEveryRoSpecManagementMessageMapping()
    {
        var registry = new LlrpCodecRegistry();
        Llrp101StandardModule.Register(registry);
        (ushort MessageType, Type ExpectedType, byte[] Payload)[] mappings =
        [
            (
                V101Messages.ADD_ROSPEC.MessageType,
                typeof(V101Messages.ADD_ROSPEC),
                registry.EncodeParameter(
                    LlrpProtocolVersion.Version101,
                    CreateMinimalRoSpec())),
            (V101Messages.DELETE_ROSPEC.MessageType, typeof(V101Messages.DELETE_ROSPEC), [0x00, 0x00, 0x00, 0x01]),
            (V101Messages.START_ROSPEC.MessageType, typeof(V101Messages.START_ROSPEC), [0x00, 0x00, 0x00, 0x01]),
            (V101Messages.STOP_ROSPEC.MessageType, typeof(V101Messages.STOP_ROSPEC), [0x00, 0x00, 0x00, 0x01]),
            (V101Messages.ENABLE_ROSPEC.MessageType, typeof(V101Messages.ENABLE_ROSPEC), [0x00, 0x00, 0x00, 0x01]),
            (V101Messages.DISABLE_ROSPEC.MessageType, typeof(V101Messages.DISABLE_ROSPEC), [0x00, 0x00, 0x00, 0x01]),
            (V101Messages.GET_ROSPECS.MessageType, typeof(V101Messages.GET_ROSPECS), []),
            (V101Messages.ADD_ROSPEC_RESPONSE.MessageType, typeof(V101Messages.ADD_ROSPEC_RESPONSE), CreateSuccessStatus()),
            (V101Messages.DELETE_ROSPEC_RESPONSE.MessageType, typeof(V101Messages.DELETE_ROSPEC_RESPONSE), CreateSuccessStatus()),
            (V101Messages.START_ROSPEC_RESPONSE.MessageType, typeof(V101Messages.START_ROSPEC_RESPONSE), CreateSuccessStatus()),
            (V101Messages.STOP_ROSPEC_RESPONSE.MessageType, typeof(V101Messages.STOP_ROSPEC_RESPONSE), CreateSuccessStatus()),
            (V101Messages.ENABLE_ROSPEC_RESPONSE.MessageType, typeof(V101Messages.ENABLE_ROSPEC_RESPONSE), CreateSuccessStatus()),
            (V101Messages.DISABLE_ROSPEC_RESPONSE.MessageType, typeof(V101Messages.DISABLE_ROSPEC_RESPONSE), CreateSuccessStatus()),
            (V101Messages.GET_ROSPECS_RESPONSE.MessageType, typeof(V101Messages.GET_ROSPECS_RESPONSE), CreateSuccessStatus()),
            (V101Messages.ERROR_MESSAGE.MessageType, typeof(V101Messages.ERROR_MESSAGE), CreateSuccessStatus()),
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

    private static V101Parameters.ROSpec CreateMinimalRoSpec()
    {
        return new V101Parameters.ROSpec(
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
