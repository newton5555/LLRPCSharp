using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Messages.V1_0_1;
using LlrpNet.Protocol.Parameters;
using LlrpNet.Protocol.Parameters.V1_0_1;
using LlrpNet.Protocol.Registry;
using LlrpNet.Protocol.Registry.V1_0_1;

namespace LlrpNet.Protocol.Tests.Messages.V1_0_1;

public sealed class GetReaderCapabilitiesResponseTests
{
    [Fact]
    public void EncodeAndDecode_SuccessStatusOnly_MatchesNormativeBytes()
    {
        LlrpCodecRegistry registry = CreateRegistry();
        byte[] expected =
        [
            0x04, 0x0B,
            0x00, 0x00, 0x00, 0x12,
            0x01, 0x02, 0x03, 0x04,
            0x01, 0x1F, 0x00, 0x08,
            0x00, 0x00, 0x00, 0x00,
        ];
        var message = new GetReaderCapabilitiesResponse(
            0x01020304,
            new LlrpStatus(LlrpStatusCode.MSuccess));

        byte[] encoded = registry.EncodeMessage(LlrpProtocolVersion.Version101, message);
        var decoded = Assert.IsType<GetReaderCapabilitiesResponse>(registry.DecodeMessage(expected));

        Assert.Equal(expected, encoded);
        Assert.Equal((uint)0x01020304, decoded.MessageId);
        Assert.Equal(LlrpStatusCode.MSuccess, decoded.Status.StatusCode);
        Assert.Empty(decoded.Parameters);
    }

    [Fact]
    public void Decode_AcceptsNonSuccessStatus()
    {
        LlrpCodecRegistry registry = CreateRegistry();
        byte[] expected =
        [
            0x04, 0x0B,
            0x00, 0x00, 0x00, 0x15,
            0x0A, 0x0B, 0x0C, 0x0D,
            0x01, 0x1F, 0x00, 0x0B,
            0x00, 0x64, 0x00, 0x03, 0x62, 0x61, 0x64,
        ];

        var decoded = Assert.IsType<GetReaderCapabilitiesResponse>(registry.DecodeMessage(expected));
        byte[] reencoded = registry.EncodeMessage(LlrpProtocolVersion.Version101, decoded);

        Assert.Equal(expected, reencoded);
        Assert.Equal(LlrpStatusCode.MParameterError, decoded.Status.StatusCode);
        Assert.Equal("bad", decoded.Status.ErrorDescription);
    }

    [Fact]
    public void CapabilityAndCustomParameters_RoundTripInSchemaOrder()
    {
        LlrpCodecRegistry registry = CreateRegistry();
        var message = new GetReaderCapabilitiesResponse(
            messageId: 7,
            new LlrpStatus(LlrpStatusCode.MSuccess),
            [
                CreateGeneralDeviceCapabilities(),
                new UnknownParameter(LlrpProtocolVersion.Version101, 142, []),
                new UnknownParameter(LlrpProtocolVersion.Version101, 143, []),
                new UnknownParameter(LlrpProtocolVersion.Version101, 327, []),
                new RawCustomParameter(
                    LlrpProtocolVersion.Version101,
                    vendorId: 0xAABBCCDD,
                    subtype: 0x01020304,
                    [0xFE, 0xED]),
            ]);

        byte[] encoded = registry.EncodeMessage(LlrpProtocolVersion.Version101, message);
        var decoded = Assert.IsType<GetReaderCapabilitiesResponse>(registry.DecodeMessage(encoded));
        byte[] reencoded = registry.EncodeMessage(LlrpProtocolVersion.Version101, decoded);

        Assert.Equal(encoded, reencoded);
        Assert.Equal(5, decoded.Parameters.Count);
        Assert.IsType<GeneralDeviceCapabilities>(decoded.Parameters[0]);
        Assert.Equal((ushort)142, Assert.IsType<UnknownParameter>(decoded.Parameters[1]).ParameterType);
        Assert.Equal((ushort)143, Assert.IsType<UnknownParameter>(decoded.Parameters[2]).ParameterType);
        Assert.Equal((ushort)327, Assert.IsType<UnknownParameter>(decoded.Parameters[3]).ParameterType);
        Assert.IsType<RawCustomParameter>(decoded.Parameters[4]);
    }

    [Fact]
    public void Encode_RejectsDuplicateSingletonCapability()
    {
        LlrpCodecRegistry registry = CreateRegistry();
        var message = new GetReaderCapabilitiesResponse(
            messageId: 1,
            new LlrpStatus(LlrpStatusCode.MSuccess),
            [
                new UnknownParameter(LlrpProtocolVersion.Version101, 142, []),
                new UnknownParameter(LlrpProtocolVersion.Version101, 142, []),
            ]);

        Assert.Throws<ArgumentException>(
            () => registry.EncodeMessage(LlrpProtocolVersion.Version101, message));
    }

    [Fact]
    public void Encode_RejectsCapabilityAfterCustomParameter()
    {
        LlrpCodecRegistry registry = CreateRegistry();
        var message = new GetReaderCapabilitiesResponse(
            messageId: 1,
            new LlrpStatus(LlrpStatusCode.MSuccess),
            [
                new RawCustomParameter(LlrpProtocolVersion.Version101, 1, 2, []),
                new UnknownParameter(LlrpProtocolVersion.Version101, 142, []),
            ]);

        Assert.Throws<ArgumentException>(
            () => registry.EncodeMessage(LlrpProtocolVersion.Version101, message));
    }

    [Fact]
    public void Encode_RejectsUnexpectedCapabilityParameter()
    {
        LlrpCodecRegistry registry = CreateRegistry();
        var message = new GetReaderCapabilitiesResponse(
            messageId: 1,
            new LlrpStatus(LlrpStatusCode.MSuccess),
            [new UnknownParameter(LlrpProtocolVersion.Version101, 138, [])]);

        Assert.Throws<ArgumentException>(
            () => registry.EncodeMessage(LlrpProtocolVersion.Version101, message));
    }

    [Fact]
    public void Decode_RejectsOutOfOrderCapabilities()
    {
        LlrpCodecRegistry registry = CreateRegistry();
        byte[] frame = CreateResponseFrame(
            registry,
            new UnknownParameter(LlrpProtocolVersion.Version101, 142, []),
            CreateGeneralDeviceCapabilities());

        LlrpProtocolException exception = Assert.Throws<LlrpProtocolException>(
            () => registry.DecodeMessage(frame));

        Assert.Equal(LlrpProtocolErrorCode.InvalidParameterEncoding, exception.ErrorCode);
    }

    [Fact]
    public void Decode_RejectsMissingStatus()
    {
        LlrpCodecRegistry registry = CreateRegistry();
        byte[] frame =
        [
            0x04, 0x0B,
            0x00, 0x00, 0x00, 0x0A,
            0x00, 0x00, 0x00, 0x01,
        ];

        LlrpProtocolException exception = Assert.Throws<LlrpProtocolException>(
            () => registry.DecodeMessage(frame));

        Assert.Equal(LlrpProtocolErrorCode.InvalidParameterEncoding, exception.ErrorCode);
    }

    [Fact]
    public void Decode_RejectsWrongFirstParameterType()
    {
        LlrpCodecRegistry registry = CreateRegistry();
        byte[] frame =
        [
            0x04, 0x0B,
            0x00, 0x00, 0x00, 0x0E,
            0x00, 0x00, 0x00, 0x01,
            0x00, 0x8A, 0x00, 0x04,
        ];

        LlrpProtocolException exception = Assert.Throws<LlrpProtocolException>(
            () => registry.DecodeMessage(frame));

        Assert.Equal(LlrpProtocolErrorCode.InvalidParameterType, exception.ErrorCode);
    }

    [Fact]
    public void Decode_RejectsTruncatedStatusParameter()
    {
        LlrpCodecRegistry registry = CreateRegistry();
        byte[] frame =
        [
            0x04, 0x0B,
            0x00, 0x00, 0x00, 0x0E,
            0x00, 0x00, 0x00, 0x01,
            0x01, 0x1F, 0x00, 0x08,
        ];

        LlrpProtocolException exception = Assert.Throws<LlrpProtocolException>(
            () => registry.DecodeMessage(frame));

        Assert.Equal(LlrpProtocolErrorCode.TruncatedData, exception.ErrorCode);
    }

    [Fact]
    public void Constructor_RejectsMissingStatus()
    {
        Assert.Throws<ArgumentNullException>(
            () => new GetReaderCapabilitiesResponse(1, null!));
    }

    private static LlrpCodecRegistry CreateRegistry()
    {
        var registry = new LlrpCodecRegistry();
        Llrp101StandardModule.Register(registry);
        return registry;
    }

    private static GeneralDeviceCapabilities CreateGeneralDeviceCapabilities()
    {
        return new GeneralDeviceCapabilities(
            maxNumberOfAntennaSupported: 2,
            canSetAntennaProperties: true,
            hasUtcClockCapability: false,
            deviceManufacturerName: 0x01020304,
            modelName: 5,
            readerFirmwareVersion: "fw",
            [
                new UnknownParameter(LlrpProtocolVersion.Version101, 139, [0x00, 0x01, 0x00, 0x00]),
                new UnknownParameter(LlrpProtocolVersion.Version101, 141, [0x00, 0x00, 0x00, 0x00]),
                new UnknownParameter(LlrpProtocolVersion.Version101, 140, [0x00, 0x01, 0x01, 0x01]),
            ]);
    }

    private static byte[] CreateResponseFrame(
        LlrpCodecRegistry registry,
        params ILlrpParameter[] parameters)
    {
        byte[] status = registry.EncodeParameter(
            LlrpProtocolVersion.Version101,
            new LlrpStatus(LlrpStatusCode.MSuccess));
        byte[][] trailing = parameters
            .Select(parameter => registry.EncodeParameter(LlrpProtocolVersion.Version101, parameter))
            .ToArray();
        int frameLength = checked(
            LlrpMessageHeader.EncodedLength
            + status.Length
            + trailing.Sum(static parameter => parameter.Length));
        var frame = new byte[frameLength];
        new LlrpMessageHeader(
            LlrpProtocolVersion.Version101,
            GetReaderCapabilitiesResponse.MessageType,
            (uint)frameLength,
            MessageId: 1).Encode(frame);

        int offset = LlrpMessageHeader.EncodedLength;
        status.CopyTo(frame.AsSpan(offset));
        offset += status.Length;
        foreach (byte[] parameter in trailing)
        {
            parameter.CopyTo(frame.AsSpan(offset));
            offset += parameter.Length;
        }

        return frame;
    }
}
