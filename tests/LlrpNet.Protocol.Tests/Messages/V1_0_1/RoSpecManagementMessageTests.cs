using V101Parameters = LlrpNet.Protocol.Parameters.V1_0_1;
using V101Messages = LlrpNet.Protocol.Messages.V1_0_1;
using V101Enumerations = LlrpNet.Protocol.Enumerations.V1_0_1;
using LlrpNet.Protocol.Enumerations.V1_0_1;
using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Messages;
using LlrpNet.Protocol.Messages.V1_0_1;
using LlrpNet.Protocol.Parameters;
using LlrpNet.Protocol.Parameters.V1_0_1;
using LlrpNet.Protocol.Registry;
using LlrpNet.Protocol.Registry.V1_0_1;

namespace LlrpNet.Protocol.Tests.Messages.V1_0_1;

public sealed class RoSpecManagementMessageTests
{
    private const uint MessageId = 0x01020304;
    private const uint RoSpecId = 0xA1B2C3D4;

    [Fact]
    public void AddRoSpec_EncodeAndDecode_MatchesNormativeWireLayout()
    {
        LlrpCodecRegistry registry = CreateRegistry();
        byte[] expected =
        [
            0x04, 0x14,
            0x00, 0x00, 0x00, 0x4B,
            0x01, 0x02, 0x03, 0x04,
            0x00, 0xB1, 0x00, 0x41,
            0x00, 0x00, 0x00, 0x01, 0x00, 0x00,
            0x00, 0xB2, 0x00, 0x12,
            0x00, 0xB3, 0x00, 0x05, 0x00,
            0x00, 0xB6, 0x00, 0x09, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0xB7, 0x00, 0x18,
            0x00, 0x01, 0x00, 0x00,
            0x00, 0xB8, 0x00, 0x09, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0xBA, 0x00, 0x07, 0x00, 0x01, 0x01,
            0x00, 0xED, 0x00, 0x0D, 0x00, 0x00, 0x00,
            0x00, 0xEE, 0x00, 0x06, 0x00, 0x00,
        ];
        var message = new V101Messages.ADD_ROSPEC(MessageId, CreateMinimalRoSpec(1));

        byte[] encoded = registry.EncodeMessage(LlrpProtocolVersion.Version101, message);
        var decoded = Assert.IsType<V101Messages.ADD_ROSPEC>(registry.DecodeMessage(expected));
        byte[] reencoded = registry.EncodeMessage(LlrpProtocolVersion.Version101, decoded);

        Assert.Equal(expected, encoded);
        Assert.Equal(expected, reencoded);
        Assert.Equal(MessageId, decoded.MessageId);
        V101Parameters.ROSpec roSpec= Assert.IsType<V101Parameters.ROSpec>(decoded.ROSpec);
        Assert.Equal(1U, roSpec.ROSpecID);
    }

    [Fact]
    public void AddRoSpec_RejectsMissingWrongDuplicateAndTruncatedParameters()
    {
        LlrpCodecRegistry registry = CreateRegistry();
        byte[] roSpec = registry.EncodeParameter(
            LlrpProtocolVersion.Version101,
            CreateMinimalRoSpec(1));

        LlrpProtocolException missing = Assert.Throws<LlrpProtocolException>(
            () => registry.DecodeMessage(CreateFrame(V101Messages.ADD_ROSPEC.MessageType, MessageId)));
        LlrpProtocolException wrong = Assert.Throws<LlrpProtocolException>(
            () => registry.DecodeMessage(
                CreateFrame(V101Messages.ADD_ROSPEC.MessageType, MessageId, [0x00, 0xB2, 0x00, 0x04])));
        LlrpProtocolException duplicate = Assert.Throws<LlrpProtocolException>(
            () => registry.DecodeMessage(CreateFrame(V101Messages.ADD_ROSPEC.MessageType, MessageId, roSpec, roSpec)));
        LlrpProtocolException truncated = Assert.Throws<LlrpProtocolException>(
            () => registry.DecodeMessage(
                CreateFrame(V101Messages.ADD_ROSPEC.MessageType, MessageId, [0x00, 0xB1, 0x00, 0x08])));

        Assert.Equal(LlrpProtocolErrorCode.InvalidParameterEncoding, missing.ErrorCode);
        Assert.Equal(LlrpProtocolErrorCode.InvalidParameterEncoding, wrong.ErrorCode);
        Assert.Equal(LlrpProtocolErrorCode.InvalidParameterEncoding, duplicate.ErrorCode);
        Assert.Equal(LlrpProtocolErrorCode.TruncatedData, truncated.ErrorCode);
    }

    [Fact]
    public void AddRoSpec_RejectsInvalidReservedBitsAndEncodingType()
    {
        LlrpCodecRegistry registry = CreateRegistry();
        byte[] invalidReservedBits = CreateFrame(
            V101Messages.ADD_ROSPEC.MessageType,
            MessageId,
            [0x04, 0xB1, 0x00, 0x04]);
        var wrongParameter = new UnknownParameter(
            LlrpProtocolVersion.Version101,
            parameterType: 178,
            []);

        LlrpProtocolException reserved = Assert.Throws<LlrpProtocolException>(
            () => registry.DecodeMessage(invalidReservedBits));

        Assert.Equal(LlrpProtocolErrorCode.InvalidParameterEncoding, reserved.ErrorCode);
        // AddRoSpec takes a strongly-typed ROSpec parameter, so wrong-type is a compile-time check.
        // Only null rejection is verified at runtime.
        Assert.Throws<ArgumentNullException>(
            () => registry.EncodeMessage(
                LlrpProtocolVersion.Version101,
                new V101Messages.ADD_ROSPEC(MessageId, null!)));
    }

    [Fact]
    public void RoSpecIdRequests_EncodeAndDecode_UseBigEndianU32()
    {
        LlrpCodecRegistry registry = CreateRegistry();
        (ILlrpMessage Message, ushort MessageType)[] cases =
        [
            (new V101Messages.DELETE_ROSPEC(MessageId, RoSpecId), V101Messages.DELETE_ROSPEC.MessageType),
            (new V101Messages.START_ROSPEC(MessageId, RoSpecId), V101Messages.START_ROSPEC.MessageType),
            (new V101Messages.STOP_ROSPEC(MessageId, RoSpecId), V101Messages.STOP_ROSPEC.MessageType),
            (new V101Messages.ENABLE_ROSPEC(MessageId, RoSpecId), V101Messages.ENABLE_ROSPEC.MessageType),
            (new V101Messages.DISABLE_ROSPEC(MessageId, RoSpecId), V101Messages.DISABLE_ROSPEC.MessageType),
        ];

        foreach ((ILlrpMessage message, ushort messageType) in cases)
        {
            byte[] expected =
            [
                0x04, (byte)messageType,
                0x00, 0x00, 0x00, 0x0E,
                0x01, 0x02, 0x03, 0x04,
                0xA1, 0xB2, 0xC3, 0xD4,
            ];

            byte[] encoded = registry.EncodeMessage(LlrpProtocolVersion.Version101, message);
            ILlrpMessage decoded = registry.DecodeMessage(expected);

            Assert.Equal(expected, encoded);
            Assert.Equal(message.GetType(), decoded.GetType());
            Assert.Equal(MessageId, decoded.MessageId);
            Assert.Equal(RoSpecId, GetRoSpecId(decoded));
        }
    }

    [Fact]
    public void RoSpecIdRequests_PreserveZeroWhereMachineDefinitionAllowsU32()
    {
        LlrpCodecRegistry registry = CreateRegistry();
        var message = new V101Messages.DELETE_ROSPEC(MessageId, 0);

        byte[] encoded = registry.EncodeMessage(LlrpProtocolVersion.Version101, message);
        var decoded = Assert.IsType<V101Messages.DELETE_ROSPEC>(registry.DecodeMessage(encoded));

        Assert.Equal((uint)0, decoded.ROSpecID);
    }

    [Fact]
    public void RoSpecIdRequest_RejectsTruncatedAndTrailingPayload()
    {
        LlrpCodecRegistry registry = CreateRegistry();
        byte[] truncated = CreateFrame(V101Messages.DELETE_ROSPEC.MessageType, MessageId, [0x01, 0x02, 0x03]);
        byte[] trailing = CreateFrame(V101Messages.DELETE_ROSPEC.MessageType, MessageId, [0x01, 0x02, 0x03, 0x04, 0x05]);

        LlrpProtocolException truncatedError = Assert.Throws<LlrpProtocolException>(
            () => registry.DecodeMessage(truncated));
        LlrpProtocolException trailingError = Assert.Throws<LlrpProtocolException>(
            () => registry.DecodeMessage(trailing));

        Assert.Equal(LlrpProtocolErrorCode.TruncatedData, truncatedError.ErrorCode);
        Assert.Equal(LlrpProtocolErrorCode.InvalidParameterEncoding, trailingError.ErrorCode);
    }

    [Fact]
    public void GetRoSpecs_EncodeAndDecode_RequiresEmptyPayload()
    {
        LlrpCodecRegistry registry = CreateRegistry();
        byte[] expected =
        [
            0x04, 0x1A,
            0x00, 0x00, 0x00, 0x0A,
            0x01, 0x02, 0x03, 0x04,
        ];

        byte[] encoded = registry.EncodeMessage(
            LlrpProtocolVersion.Version101,
            new V101Messages.GET_ROSPECS(MessageId));
        var decoded = Assert.IsType<V101Messages.GET_ROSPECS>(registry.DecodeMessage(expected));
        LlrpProtocolException trailing = Assert.Throws<LlrpProtocolException>(
            () => registry.DecodeMessage(
                CreateFrame(V101Messages.GET_ROSPECS.MessageType, MessageId, [0x00])));

        Assert.Equal(expected, encoded);
        Assert.Equal(MessageId, decoded.MessageId);
        Assert.Equal(LlrpProtocolErrorCode.InvalidParameterEncoding, trailing.ErrorCode);
    }

    [Fact]
    public void StatusOnlyResponses_EncodeAndDecode_RequireExactlyOneStatus()
    {
        LlrpCodecRegistry registry = CreateRegistry();
        var status = new V101Parameters.LLRPStatus(V101Enumerations.StatusCode.M_Success, string.Empty, null, null);
        (ILlrpMessage Message, ushort MessageType)[] cases =
        [
            (new V101Messages.ADD_ROSPEC_RESPONSE(MessageId, status), V101Messages.ADD_ROSPEC_RESPONSE.MessageType),
            (new V101Messages.DELETE_ROSPEC_RESPONSE(MessageId, status), V101Messages.DELETE_ROSPEC_RESPONSE.MessageType),
            (new V101Messages.START_ROSPEC_RESPONSE(MessageId, status), V101Messages.START_ROSPEC_RESPONSE.MessageType),
            (new V101Messages.STOP_ROSPEC_RESPONSE(MessageId, status), V101Messages.STOP_ROSPEC_RESPONSE.MessageType),
            (new V101Messages.ENABLE_ROSPEC_RESPONSE(MessageId, status), V101Messages.ENABLE_ROSPEC_RESPONSE.MessageType),
            (new V101Messages.DISABLE_ROSPEC_RESPONSE(MessageId, status), V101Messages.DISABLE_ROSPEC_RESPONSE.MessageType),
        ];

        foreach ((ILlrpMessage message, ushort messageType) in cases)
        {
            byte[] expected =
            [
                0x04, (byte)messageType,
                0x00, 0x00, 0x00, 0x12,
                0x01, 0x02, 0x03, 0x04,
                0x01, 0x1F, 0x00, 0x08,
                0x00, 0x00, 0x00, 0x00,
            ];

            byte[] encoded = registry.EncodeMessage(LlrpProtocolVersion.Version101, message);
            ILlrpMessage decoded = registry.DecodeMessage(expected);
            V101Parameters.LLRPStatus decodedStatus= GetStatus(decoded);

            Assert.Equal(expected, encoded);
            Assert.Equal(message.GetType(), decoded.GetType());
            Assert.Equal(MessageId, decoded.MessageId);
            Assert.Equal(V101Enumerations.StatusCode.M_Success, decodedStatus.StatusCode);
        }
    }

    [Fact]
    public void StatusOnlyResponse_RejectsMissingWrongDuplicateAndTruncatedStatus()
    {
        LlrpCodecRegistry registry = CreateRegistry();
        byte[] status = registry.EncodeParameter(
            LlrpProtocolVersion.Version101,
            new V101Parameters.LLRPStatus(V101Enumerations.StatusCode.M_Success, string.Empty, null, null));

        LlrpProtocolException missing = Assert.Throws<LlrpProtocolException>(
            () => registry.DecodeMessage(CreateFrame(V101Messages.ADD_ROSPEC_RESPONSE.MessageType, MessageId)));
        LlrpProtocolException wrong = Assert.Throws<LlrpProtocolException>(
            () => registry.DecodeMessage(
                CreateFrame(V101Messages.ADD_ROSPEC_RESPONSE.MessageType, MessageId, [0x00, 0xB1, 0x00, 0x04])));
        LlrpProtocolException duplicate = Assert.Throws<LlrpProtocolException>(
            () => registry.DecodeMessage(
                CreateFrame(V101Messages.ADD_ROSPEC_RESPONSE.MessageType, MessageId, status, status)));
        LlrpProtocolException truncated = Assert.Throws<LlrpProtocolException>(
            () => registry.DecodeMessage(
                CreateFrame(V101Messages.ADD_ROSPEC_RESPONSE.MessageType, MessageId, [0x01, 0x1F, 0x00, 0x08])));

        Assert.Equal(LlrpProtocolErrorCode.InvalidParameterEncoding, missing.ErrorCode);
        Assert.Equal(LlrpProtocolErrorCode.InvalidParameterEncoding, wrong.ErrorCode);
        Assert.Equal(LlrpProtocolErrorCode.InvalidParameterEncoding, duplicate.ErrorCode);
        Assert.Equal(LlrpProtocolErrorCode.TruncatedData, truncated.ErrorCode);
    }

    [Fact]
    public void StatusResponseConstructors_RejectNullStatus()
    {
        LlrpCodecRegistry registry = CreateRegistry();
        Assert.Throws<ArgumentNullException>(() => registry.EncodeMessage(
            LlrpProtocolVersion.Version101,
            new V101Messages.ADD_ROSPEC_RESPONSE(MessageId, null!)));
        Assert.Throws<ArgumentNullException>(() => registry.EncodeMessage(
            LlrpProtocolVersion.Version101,
            new V101Messages.DELETE_ROSPEC_RESPONSE(MessageId, null!)));
        Assert.Throws<ArgumentNullException>(() => registry.EncodeMessage(
            LlrpProtocolVersion.Version101,
            new V101Messages.START_ROSPEC_RESPONSE(MessageId, null!)));
        Assert.Throws<ArgumentNullException>(() => registry.EncodeMessage(
            LlrpProtocolVersion.Version101,
            new V101Messages.STOP_ROSPEC_RESPONSE(MessageId, null!)));
        Assert.Throws<ArgumentNullException>(() => registry.EncodeMessage(
            LlrpProtocolVersion.Version101,
            new V101Messages.ENABLE_ROSPEC_RESPONSE(MessageId, null!)));
        Assert.Throws<ArgumentNullException>(() => registry.EncodeMessage(
            LlrpProtocolVersion.Version101,
            new V101Messages.DISABLE_ROSPEC_RESPONSE(MessageId, null!)));
    }

    [Fact]
    public void GetRoSpecsResponse_EncodeAndDecode_PreservesStatusAndRoSpecs()
    {
        LlrpCodecRegistry registry = CreateRegistry();
        var message = new V101Messages.GET_ROSPECS_RESPONSE(
            MessageId,
            new V101Parameters.LLRPStatus(V101Enumerations.StatusCode.M_Success, string.Empty, null, null),
            [
                CreateMinimalRoSpec(1),
                CreateMinimalRoSpec(2),
            ]);

        byte[] encoded = registry.EncodeMessage(LlrpProtocolVersion.Version101, message);
        var decoded = Assert.IsType<V101Messages.GET_ROSPECS_RESPONSE>(registry.DecodeMessage(encoded));
        byte[] reencoded = registry.EncodeMessage(LlrpProtocolVersion.Version101, decoded);

        Assert.Equal(encoded, reencoded);
        Assert.Equal(MessageId, decoded.MessageId);
        Assert.Equal(V101Enumerations.StatusCode.M_Success, decoded.LLRPStatus.StatusCode);
        Assert.Equal(2, decoded.ROSpecItems.Count);
        Assert.Equal(
            [1U, 2U],
            decoded.ROSpecItems.Select(static r => r.ROSpecID));
    }

    [Fact]
    public void GetRoSpecsResponse_StatusOnly_MatchesNormativeWireLayout()
    {
        LlrpCodecRegistry registry = CreateRegistry();
        byte[] expected =
        [
            0x04, 0x24,
            0x00, 0x00, 0x00, 0x12,
            0x01, 0x02, 0x03, 0x04,
            0x01, 0x1F, 0x00, 0x08,
            0x00, 0x00, 0x00, 0x00,
        ];
        var message = new V101Messages.GET_ROSPECS_RESPONSE(
            MessageId,
            new V101Parameters.LLRPStatus(V101Enumerations.StatusCode.M_Success, string.Empty, null, null),
            []);

        byte[] encoded = registry.EncodeMessage(LlrpProtocolVersion.Version101, message);
        var decoded = Assert.IsType<V101Messages.GET_ROSPECS_RESPONSE>(registry.DecodeMessage(expected));

        Assert.Equal(expected, encoded);
        Assert.Empty(decoded.ROSpecItems);
    }

    [Fact]
    public void GetRoSpecsResponse_RejectsUnexpectedParameterOnEncodeAndDecode()
    {
        LlrpCodecRegistry registry = CreateRegistry();
        var status = new V101Parameters.LLRPStatus(V101Enumerations.StatusCode.M_Success, string.Empty, null, null);
        var unexpected = new UnknownParameter(LlrpProtocolVersion.Version101, 178, []);
        byte[] encodedStatus = registry.EncodeParameter(LlrpProtocolVersion.Version101, status);
        byte[] encodedUnexpected = registry.EncodeParameter(LlrpProtocolVersion.Version101, unexpected);

        LlrpProtocolException decoded = Assert.Throws<LlrpProtocolException>(
            () => registry.DecodeMessage(
                CreateFrame(
                    V101Messages.GET_ROSPECS_RESPONSE.MessageType,
                    MessageId,
                    encodedStatus,
                    encodedUnexpected)));

        Assert.Equal(LlrpProtocolErrorCode.InvalidParameterEncoding, decoded.ErrorCode);
    }

    [Fact]
    public void GetRoSpecsResponse_RejectsRepeatedStatusAndTruncatedRoSpec()
    {
        LlrpCodecRegistry registry = CreateRegistry();
        byte[] status = registry.EncodeParameter(
            LlrpProtocolVersion.Version101,
            new V101Parameters.LLRPStatus(V101Enumerations.StatusCode.M_Success, string.Empty, null, null));

        LlrpProtocolException repeatedStatus = Assert.Throws<LlrpProtocolException>(
            () => registry.DecodeMessage(
                CreateFrame(V101Messages.GET_ROSPECS_RESPONSE.MessageType, MessageId, status, status)));
        LlrpProtocolException truncated = Assert.Throws<LlrpProtocolException>(
            () => registry.DecodeMessage(
                CreateFrame(
                    V101Messages.GET_ROSPECS_RESPONSE.MessageType,
                    MessageId,
                    status,
                    [0x00, 0xB1, 0x00, 0x08])));

        Assert.Equal(LlrpProtocolErrorCode.InvalidParameterEncoding, repeatedStatus.ErrorCode);
        Assert.Equal(LlrpProtocolErrorCode.TruncatedData, truncated.ErrorCode);
    }

    [Fact]
    public void GetRoSpecsResponse_ConstructorRejectsInvalidArgumentsAndUsesProvidedCollection()
    {
        var status = new V101Parameters.LLRPStatus(V101Enumerations.StatusCode.M_Success, string.Empty, null, null);
        var mutable = new List<V101Parameters.ROSpec>
        {
            CreateMinimalRoSpec(1),
        };
        var response = new V101Messages.GET_ROSPECS_RESPONSE(MessageId, status, mutable);
        mutable.Clear();

        Assert.Empty(response.ROSpecItems);
        LlrpCodecRegistry registry = CreateRegistry();
        Assert.Throws<ArgumentNullException>(() => registry.EncodeMessage(
            LlrpProtocolVersion.Version101,
            new V101Messages.GET_ROSPECS_RESPONSE(MessageId, null!, [])));
        Assert.Throws<ArgumentNullException>(
            () => registry.EncodeMessage(
                LlrpProtocolVersion.Version101,
                new V101Messages.GET_ROSPECS_RESPONSE(MessageId, status, (IReadOnlyList<V101Parameters.ROSpec>)[null!])));
    }

    [Fact]
    public void RoSpecMessage_RejectsNonzeroReservedMessageHeaderBits()
    {
        LlrpCodecRegistry registry = CreateRegistry();
        byte[] frame =
        [
            0x24, 0x1A,
            0x00, 0x00, 0x00, 0x0A,
            0x01, 0x02, 0x03, 0x04,
        ];

        LlrpProtocolException exception = Assert.Throws<LlrpProtocolException>(
            () => registry.DecodeMessage(frame));

        Assert.Equal(LlrpProtocolErrorCode.InvalidReservedBits, exception.ErrorCode);
    }

    private static LlrpCodecRegistry CreateRegistry()
    {
        var registry = new LlrpCodecRegistry();
        Llrp101StandardModule.Register(registry);
        return registry;
    }

    private static V101Parameters.ROSpec CreateMinimalRoSpec(uint roSpecId)
    {
        return new V101Parameters.ROSpec(
            roSpecId,
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
            new ROReportSpec(
                ROReportTriggerType.None,
                N: 0,
                new V101Parameters.TagReportContentSelector(false, false, false, false, false, false, false, false, false, false, []),
                CustomItems: []));
    }

    private static uint GetRoSpecId(ILlrpMessage message)
    {
        return message switch
        {
            V101Messages.DELETE_ROSPEC value=> value.ROSpecID,
            V101Messages.START_ROSPEC value=> value.ROSpecID,
            V101Messages.STOP_ROSPEC value=> value.ROSpecID,
            V101Messages.ENABLE_ROSPEC value=> value.ROSpecID,
            V101Messages.DISABLE_ROSPEC value=> value.ROSpecID,
            _ => throw new ArgumentException("The supplied message is not a ROSpec-ID request.", nameof(message)),
        };
    }

    private static V101Parameters.LLRPStatus GetStatus(ILlrpMessage message)
    {
        return message switch
        {
            V101Messages.ADD_ROSPEC_RESPONSE value=> value.LLRPStatus,
            V101Messages.DELETE_ROSPEC_RESPONSE value=> value.LLRPStatus,
            V101Messages.START_ROSPEC_RESPONSE value=> value.LLRPStatus,
            V101Messages.STOP_ROSPEC_RESPONSE value=> value.LLRPStatus,
            V101Messages.ENABLE_ROSPEC_RESPONSE value=> value.LLRPStatus,
            V101Messages.DISABLE_ROSPEC_RESPONSE value=> value.LLRPStatus,
            _ => throw new ArgumentException("The supplied message is not a ROSpec status response.", nameof(message)),
        };
    }

    private static byte[] CreateFrame(
        ushort messageType,
        uint messageId,
        params byte[][] payloadParts)
    {
        int payloadLength = payloadParts.Sum(static part => part.Length);
        int frameLength = checked(LlrpMessageHeader.EncodedLength + payloadLength);
        var frame = new byte[frameLength];
        new LlrpMessageHeader(
            LlrpProtocolVersion.Version101,
            messageType,
            (uint)frameLength,
            messageId).Encode(frame);

        int offset = LlrpMessageHeader.EncodedLength;
        foreach (byte[] part in payloadParts)
        {
            part.CopyTo(frame.AsSpan(offset));
            offset += part.Length;
        }

        return frame;
    }
}
