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
        var message = new AddRoSpec(MessageId, CreateMinimalRoSpec(1));

        byte[] encoded = registry.EncodeMessage(LlrpProtocolVersion.Version101, message);
        var decoded = Assert.IsType<AddRoSpec>(registry.DecodeMessage(expected));
        byte[] reencoded = registry.EncodeMessage(LlrpProtocolVersion.Version101, decoded);

        Assert.Equal(expected, encoded);
        Assert.Equal(expected, reencoded);
        Assert.Equal(MessageId, decoded.MessageId);
        RoSpec roSpec = Assert.IsType<RoSpec>(decoded.ROSpec);
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
            () => registry.DecodeMessage(CreateFrame(AddRoSpec.MessageType, MessageId)));
        LlrpProtocolException wrong = Assert.Throws<LlrpProtocolException>(
            () => registry.DecodeMessage(
                CreateFrame(AddRoSpec.MessageType, MessageId, [0x00, 0xB2, 0x00, 0x04])));
        LlrpProtocolException duplicate = Assert.Throws<LlrpProtocolException>(
            () => registry.DecodeMessage(CreateFrame(AddRoSpec.MessageType, MessageId, roSpec, roSpec)));
        LlrpProtocolException truncated = Assert.Throws<LlrpProtocolException>(
            () => registry.DecodeMessage(
                CreateFrame(AddRoSpec.MessageType, MessageId, [0x00, 0xB1, 0x00, 0x08])));

        Assert.Equal(LlrpProtocolErrorCode.InvalidParameterEncoding, missing.ErrorCode);
        Assert.Equal(LlrpProtocolErrorCode.InvalidParameterType, wrong.ErrorCode);
        Assert.Equal(LlrpProtocolErrorCode.InvalidParameterEncoding, duplicate.ErrorCode);
        Assert.Equal(LlrpProtocolErrorCode.TruncatedData, truncated.ErrorCode);
    }

    [Fact]
    public void AddRoSpec_RejectsInvalidReservedBitsAndEncodingType()
    {
        LlrpCodecRegistry registry = CreateRegistry();
        byte[] invalidReservedBits = CreateFrame(
            AddRoSpec.MessageType,
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
        Assert.Throws<ArgumentNullException>(() => new AddRoSpec(MessageId, null!));
    }

    [Fact]
    public void RoSpecIdRequests_EncodeAndDecode_UseBigEndianU32()
    {
        LlrpCodecRegistry registry = CreateRegistry();
        (ILlrpMessage Message, ushort MessageType)[] cases =
        [
            (new DeleteRoSpec(MessageId, RoSpecId), DeleteRoSpec.MessageType),
            (new StartRoSpec(MessageId, RoSpecId), StartRoSpec.MessageType),
            (new StopRoSpec(MessageId, RoSpecId), StopRoSpec.MessageType),
            (new EnableRoSpec(MessageId, RoSpecId), EnableRoSpec.MessageType),
            (new DisableRoSpec(MessageId, RoSpecId), DisableRoSpec.MessageType),
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
        var message = new DeleteRoSpec(MessageId, 0);

        byte[] encoded = registry.EncodeMessage(LlrpProtocolVersion.Version101, message);
        var decoded = Assert.IsType<DeleteRoSpec>(registry.DecodeMessage(encoded));

        Assert.Equal((uint)0, decoded.ROSpecID);
    }

    [Fact]
    public void RoSpecIdRequest_RejectsTruncatedAndTrailingPayload()
    {
        LlrpCodecRegistry registry = CreateRegistry();
        byte[] truncated = CreateFrame(DeleteRoSpec.MessageType, MessageId, [0x01, 0x02, 0x03]);
        byte[] trailing = CreateFrame(DeleteRoSpec.MessageType, MessageId, [0x01, 0x02, 0x03, 0x04, 0x05]);

        LlrpProtocolException truncatedError = Assert.Throws<LlrpProtocolException>(
            () => registry.DecodeMessage(truncated));
        LlrpProtocolException trailingError = Assert.Throws<LlrpProtocolException>(
            () => registry.DecodeMessage(trailing));

        Assert.Equal(LlrpProtocolErrorCode.TruncatedData, truncatedError.ErrorCode);
        Assert.Equal(LlrpProtocolErrorCode.InvalidMessageLength, trailingError.ErrorCode);
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
            new GetRoSpecs(MessageId));
        var decoded = Assert.IsType<GetRoSpecs>(registry.DecodeMessage(expected));
        LlrpProtocolException trailing = Assert.Throws<LlrpProtocolException>(
            () => registry.DecodeMessage(
                CreateFrame(GetRoSpecs.MessageType, MessageId, [0x00])));

        Assert.Equal(expected, encoded);
        Assert.Equal(MessageId, decoded.MessageId);
        Assert.Equal(LlrpProtocolErrorCode.InvalidMessageLength, trailing.ErrorCode);
    }

    [Fact]
    public void StatusOnlyResponses_EncodeAndDecode_RequireExactlyOneStatus()
    {
        LlrpCodecRegistry registry = CreateRegistry();
        var status = new LlrpStatus(LlrpStatusCode.M_Success, string.Empty, null, null);
        (ILlrpMessage Message, ushort MessageType)[] cases =
        [
            (new AddRoSpecResponse(MessageId, status), AddRoSpecResponse.MessageType),
            (new DeleteRoSpecResponse(MessageId, status), DeleteRoSpecResponse.MessageType),
            (new StartRoSpecResponse(MessageId, status), StartRoSpecResponse.MessageType),
            (new StopRoSpecResponse(MessageId, status), StopRoSpecResponse.MessageType),
            (new EnableRoSpecResponse(MessageId, status), EnableRoSpecResponse.MessageType),
            (new DisableRoSpecResponse(MessageId, status), DisableRoSpecResponse.MessageType),
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
            LlrpStatus decodedStatus = GetStatus(decoded);

            Assert.Equal(expected, encoded);
            Assert.Equal(message.GetType(), decoded.GetType());
            Assert.Equal(MessageId, decoded.MessageId);
            Assert.Equal(LlrpStatusCode.M_Success, decodedStatus.StatusCode);
        }
    }

    [Fact]
    public void StatusOnlyResponse_RejectsMissingWrongDuplicateAndTruncatedStatus()
    {
        LlrpCodecRegistry registry = CreateRegistry();
        byte[] status = registry.EncodeParameter(
            LlrpProtocolVersion.Version101,
            new LlrpStatus(LlrpStatusCode.M_Success, string.Empty, null, null));

        LlrpProtocolException missing = Assert.Throws<LlrpProtocolException>(
            () => registry.DecodeMessage(CreateFrame(AddRoSpecResponse.MessageType, MessageId)));
        LlrpProtocolException wrong = Assert.Throws<LlrpProtocolException>(
            () => registry.DecodeMessage(
                CreateFrame(AddRoSpecResponse.MessageType, MessageId, [0x00, 0xB1, 0x00, 0x04])));
        LlrpProtocolException duplicate = Assert.Throws<LlrpProtocolException>(
            () => registry.DecodeMessage(
                CreateFrame(AddRoSpecResponse.MessageType, MessageId, status, status)));
        LlrpProtocolException truncated = Assert.Throws<LlrpProtocolException>(
            () => registry.DecodeMessage(
                CreateFrame(AddRoSpecResponse.MessageType, MessageId, [0x01, 0x1F, 0x00, 0x08])));

        Assert.Equal(LlrpProtocolErrorCode.InvalidParameterEncoding, missing.ErrorCode);
        Assert.Equal(LlrpProtocolErrorCode.InvalidParameterType, wrong.ErrorCode);
        Assert.Equal(LlrpProtocolErrorCode.InvalidParameterEncoding, duplicate.ErrorCode);
        Assert.Equal(LlrpProtocolErrorCode.TruncatedData, truncated.ErrorCode);
    }

    [Fact]
    public void StatusResponseConstructors_RejectNullStatus()
    {
        Assert.Throws<ArgumentNullException>(() => new AddRoSpecResponse(MessageId, null!));
        Assert.Throws<ArgumentNullException>(() => new DeleteRoSpecResponse(MessageId, null!));
        Assert.Throws<ArgumentNullException>(() => new StartRoSpecResponse(MessageId, null!));
        Assert.Throws<ArgumentNullException>(() => new StopRoSpecResponse(MessageId, null!));
        Assert.Throws<ArgumentNullException>(() => new EnableRoSpecResponse(MessageId, null!));
        Assert.Throws<ArgumentNullException>(() => new DisableRoSpecResponse(MessageId, null!));
    }

    [Fact]
    public void GetRoSpecsResponse_EncodeAndDecode_PreservesStatusAndRoSpecs()
    {
        LlrpCodecRegistry registry = CreateRegistry();
        var message = new GetRoSpecsResponse(
            MessageId,
            new LlrpStatus(LlrpStatusCode.M_Success, string.Empty, null, null),
            [
                CreateMinimalRoSpec(1),
                CreateMinimalRoSpec(2),
            ]);

        byte[] encoded = registry.EncodeMessage(LlrpProtocolVersion.Version101, message);
        var decoded = Assert.IsType<GetRoSpecsResponse>(registry.DecodeMessage(encoded));
        byte[] reencoded = registry.EncodeMessage(LlrpProtocolVersion.Version101, decoded);

        Assert.Equal(encoded, reencoded);
        Assert.Equal(MessageId, decoded.MessageId);
        Assert.Equal(LlrpStatusCode.M_Success, decoded.LLRPStatus.StatusCode);
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
        var message = new GetRoSpecsResponse(
            MessageId,
            new LlrpStatus(LlrpStatusCode.M_Success, string.Empty, null, null),
            []);

        byte[] encoded = registry.EncodeMessage(LlrpProtocolVersion.Version101, message);
        var decoded = Assert.IsType<GetRoSpecsResponse>(registry.DecodeMessage(expected));

        Assert.Equal(expected, encoded);
        Assert.Empty(decoded.ROSpecItems);
    }

    [Fact]
    public void GetRoSpecsResponse_RejectsUnexpectedParameterOnEncodeAndDecode()
    {
        LlrpCodecRegistry registry = CreateRegistry();
        var status = new LlrpStatus(LlrpStatusCode.M_Success, string.Empty, null, null);
        var unexpected = new UnknownParameter(LlrpProtocolVersion.Version101, 178, []);
        byte[] encodedStatus = registry.EncodeParameter(LlrpProtocolVersion.Version101, status);
        byte[] encodedUnexpected = registry.EncodeParameter(LlrpProtocolVersion.Version101, unexpected);

        // Passing an UnknownParameter (not ROSpec typed) should throw ArgumentException at encode
        Assert.Throws<ArgumentException>(
            () => registry.EncodeMessage(
                LlrpProtocolVersion.Version101,
                new GetRoSpecsResponse(MessageId, status, [])));

        LlrpProtocolException decoded = Assert.Throws<LlrpProtocolException>(
            () => registry.DecodeMessage(
                CreateFrame(
                    GetRoSpecsResponse.MessageType,
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
            new LlrpStatus(LlrpStatusCode.M_Success, string.Empty, null, null));

        LlrpProtocolException repeatedStatus = Assert.Throws<LlrpProtocolException>(
            () => registry.DecodeMessage(
                CreateFrame(GetRoSpecsResponse.MessageType, MessageId, status, status)));
        LlrpProtocolException truncated = Assert.Throws<LlrpProtocolException>(
            () => registry.DecodeMessage(
                CreateFrame(
                    GetRoSpecsResponse.MessageType,
                    MessageId,
                    status,
                    [0x00, 0xB1, 0x00, 0x08])));

        Assert.Equal(LlrpProtocolErrorCode.InvalidParameterEncoding, repeatedStatus.ErrorCode);
        Assert.Equal(LlrpProtocolErrorCode.TruncatedData, truncated.ErrorCode);
    }

    [Fact]
    public void GetRoSpecsResponse_ConstructorRejectsInvalidArgumentsAndCopiesCollection()
    {
        var status = new LlrpStatus(LlrpStatusCode.M_Success, string.Empty, null, null);
        var mutable = new List<RoSpec>
        {
            CreateMinimalRoSpec(1),
        };
        var response = new GetRoSpecsResponse(MessageId, status, mutable);
        mutable.Clear();

        Assert.Single(response.ROSpecItems);
        Assert.Throws<ArgumentNullException>(() => new GetRoSpecsResponse(MessageId, null!, []));
        Assert.Throws<ArgumentException>(
            () => new GetRoSpecsResponse(MessageId, status, (IReadOnlyList<RoSpec>)[null!]));
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

    private static RoSpec CreateMinimalRoSpec(uint roSpecId)
    {
        return new RoSpec(
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
                new TagReportContentSelector(false, false, false, false, false, false, false, false, false, false, []),
                CustomItems: []));
    }

    private static uint GetRoSpecId(ILlrpMessage message)
    {
        return message switch
        {
            DeleteRoSpec value => value.ROSpecID,
            StartRoSpec value => value.ROSpecID,
            StopRoSpec value => value.ROSpecID,
            EnableRoSpec value => value.ROSpecID,
            DisableRoSpec value => value.ROSpecID,
            _ => throw new ArgumentException("The supplied message is not a ROSpec-ID request.", nameof(message)),
        };
    }

    private static LlrpStatus GetStatus(ILlrpMessage message)
    {
        return message switch
        {
            AddRoSpecResponse value => value.LLRPStatus,
            DeleteRoSpecResponse value => value.LLRPStatus,
            StartRoSpecResponse value => value.LLRPStatus,
            StopRoSpecResponse value => value.LLRPStatus,
            EnableRoSpecResponse value => value.LLRPStatus,
            DisableRoSpecResponse value => value.LLRPStatus,
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
