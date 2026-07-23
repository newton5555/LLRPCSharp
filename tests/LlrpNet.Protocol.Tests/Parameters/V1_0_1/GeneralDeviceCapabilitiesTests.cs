using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Parameters;
using LlrpNet.Protocol.Parameters.V1_0_1;
using LlrpNet.Protocol.Registry;
using LlrpNet.Protocol.Registry.V1_0_1;

namespace LlrpNet.Protocol.Tests.Parameters.V1_0_1;

public sealed class GeneralDeviceCapabilitiesTests
{
    [Fact]
    public void EncodeAndDecode_AllFixedFields_MatchesNormativeBytes()
    {
        LlrpCodecRegistry registry = CreateRegistry();
        byte[] expected =
        [
            0x00, 0x89, 0x00, 0x2C,
            0x00, 0x04,
            0xC0, 0x00,
            0x01, 0x02, 0x03, 0x04,
            0xAA, 0xBB, 0xCC, 0xDD,
            0x00, 0x02, 0x76, 0x31,
            0x00, 0x8B, 0x00, 0x08, 0x00, 0x01, 0x00, 0x00,
            0x00, 0x8D, 0x00, 0x08, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x8C, 0x00, 0x08, 0x00, 0x01, 0x01, 0x01,
        ];
        var parameter = new GeneralDeviceCapabilities(
            maxNumberOfAntennaSupported: 4,
            canSetAntennaProperties: true,
            hasUtcClockCapability: true,
            deviceManufacturerName: 0x01020304,
            modelName: 0xAABBCCDD,
            readerFirmwareVersion: "v1",
            CreateRequiredParameters());

        byte[] encoded = registry.EncodeParameter(LlrpProtocolVersion.Version101, parameter);
        LlrpParameterDecodeResult result = registry.DecodeParameter(
            LlrpProtocolVersion.Version101,
            expected);
        var decoded = Assert.IsType<GeneralDeviceCapabilities>(result.Parameter);

        Assert.Equal(expected, encoded);
        Assert.Equal(expected.Length, result.BytesConsumed);
        Assert.Equal((ushort)4, decoded.MaxNumberOfAntennaSupported);
        Assert.True(decoded.CanSetAntennaProperties);
        Assert.True(decoded.HasUtcClockCapability);
        Assert.Equal((uint)0x01020304, decoded.DeviceManufacturerName);
        Assert.Equal((uint)0xAABBCCDD, decoded.ModelName);
        Assert.Equal("v1", decoded.ReaderFirmwareVersion);
        Assert.Equal(new ushort[] { 139, 141, 140 }, GetUnknownParameterTypes(decoded.Parameters));
    }

    [Fact]
    public void RequiredAndOptionalCapabilityParameters_RoundTripWithoutLosingBytes()
    {
        LlrpCodecRegistry registry = CreateRegistry();
        var parameter = new GeneralDeviceCapabilities(
            maxNumberOfAntennaSupported: 1,
            canSetAntennaProperties: false,
            hasUtcClockCapability: false,
            deviceManufacturerName: 2,
            modelName: 3,
            readerFirmwareVersion: "固件",
            [
                new UnknownParameter(LlrpProtocolVersion.Version101, 139, [0x00, 0x01, 0x00, 0x00]),
                new UnknownParameter(LlrpProtocolVersion.Version101, 149, [0x00, 0x01, 0x00, 0x01, 0x00, 0x02]),
                new UnknownParameter(LlrpProtocolVersion.Version101, 141, [0x00, 0x00, 0x00, 0x00]),
                new UnknownParameter(LlrpProtocolVersion.Version101, 140, [0x00, 0x01, 0x01, 0x01]),
            ]);

        byte[] encoded = registry.EncodeParameter(LlrpProtocolVersion.Version101, parameter);
        var decoded = Assert.IsType<GeneralDeviceCapabilities>(registry.DecodeParameter(
            LlrpProtocolVersion.Version101,
            encoded).Parameter);
        byte[] reencoded = registry.EncodeParameter(LlrpProtocolVersion.Version101, decoded);

        Assert.Equal(encoded, reencoded);
        Assert.Equal("固件", decoded.ReaderFirmwareVersion);
        Assert.Equal(new ushort[] { 139, 149, 141, 140 }, GetUnknownParameterTypes(decoded.Parameters));
    }

    [Fact]
    public void Encode_RejectsMissingRequiredCapabilityParameters()
    {
        LlrpCodecRegistry registry = CreateRegistry();
        GeneralDeviceCapabilities parameter = CreateParameter([]);

        Assert.Throws<ArgumentException>(
            () => registry.EncodeParameter(LlrpProtocolVersion.Version101, parameter));
    }

    [Fact]
    public void Encode_RejectsOutOfOrderCapabilityParameters()
    {
        LlrpCodecRegistry registry = CreateRegistry();
        GeneralDeviceCapabilities parameter = CreateParameter(
        [
            new UnknownParameter(LlrpProtocolVersion.Version101, 139, []),
            new UnknownParameter(LlrpProtocolVersion.Version101, 140, []),
            new UnknownParameter(LlrpProtocolVersion.Version101, 141, []),
        ]);

        Assert.Throws<ArgumentException>(
            () => registry.EncodeParameter(LlrpProtocolVersion.Version101, parameter));
    }

    [Fact]
    public void Encode_RejectsDuplicateGpioCapabilities()
    {
        LlrpCodecRegistry registry = CreateRegistry();
        GeneralDeviceCapabilities parameter = CreateParameter(
        [
            new UnknownParameter(LlrpProtocolVersion.Version101, 139, []),
            new UnknownParameter(LlrpProtocolVersion.Version101, 141, []),
            new UnknownParameter(LlrpProtocolVersion.Version101, 141, []),
            new UnknownParameter(LlrpProtocolVersion.Version101, 140, []),
        ]);

        Assert.Throws<ArgumentException>(
            () => registry.EncodeParameter(LlrpProtocolVersion.Version101, parameter));
    }

    [Fact]
    public void Decode_RejectsMissingRequiredCapabilityParameters()
    {
        LlrpCodecRegistry registry = CreateRegistry();
        byte[] encoded =
        [
            0x00, 0x89, 0x00, 0x12,
            0x00, 0x01,
            0x00, 0x00,
            0x00, 0x00, 0x00, 0x02,
            0x00, 0x00, 0x00, 0x03,
            0x00, 0x00,
        ];

        LlrpProtocolException exception = Assert.Throws<LlrpProtocolException>(
            () => registry.DecodeParameter(LlrpProtocolVersion.Version101, encoded));

        Assert.Equal(LlrpProtocolErrorCode.InvalidParameterEncoding, exception.ErrorCode);
    }

    [Fact]
    public void Decode_RejectsOutOfOrderCapabilityParameters()
    {
        LlrpCodecRegistry registry = CreateRegistry();
        byte[] encoded =
        [
            0x00, 0x89, 0x00, 0x2A,
            0x00, 0x01,
            0x00, 0x00,
            0x00, 0x00, 0x00, 0x02,
            0x00, 0x00, 0x00, 0x03,
            0x00, 0x00,
            0x00, 0x8B, 0x00, 0x08, 0x00, 0x01, 0x00, 0x00,
            0x00, 0x8C, 0x00, 0x08, 0x00, 0x01, 0x01, 0x01,
            0x00, 0x8D, 0x00, 0x08, 0x00, 0x00, 0x00, 0x00,
        ];

        LlrpProtocolException exception = Assert.Throws<LlrpProtocolException>(
            () => registry.DecodeParameter(LlrpProtocolVersion.Version101, encoded));

        Assert.Equal(LlrpProtocolErrorCode.InvalidParameterEncoding, exception.ErrorCode);
    }

    [Fact]
    public void Decode_RejectsNonZeroReservedFlagBits()
    {
        LlrpCodecRegistry registry = CreateRegistry();
        byte[] encoded =
        [
            0x00, 0x89, 0x00, 0x12,
            0x00, 0x01,
            0x80, 0x01,
            0x00, 0x00, 0x00, 0x02,
            0x00, 0x00, 0x00, 0x03,
            0x00, 0x00,
        ];

        LlrpProtocolException exception = Assert.Throws<LlrpProtocolException>(
            () => registry.DecodeParameter(LlrpProtocolVersion.Version101, encoded));

        Assert.Equal(LlrpProtocolErrorCode.InvalidReservedBits, exception.ErrorCode);
    }

    [Fact]
    public void Decode_RejectsTruncatedFirmwareValue()
    {
        LlrpCodecRegistry registry = CreateRegistry();
        byte[] encoded =
        [
            0x00, 0x89, 0x00, 0x13,
            0x00, 0x01,
            0x00, 0x00,
            0x00, 0x00, 0x00, 0x02,
            0x00, 0x00, 0x00, 0x03,
            0x00, 0x02, 0x61,
        ];

        LlrpProtocolException exception = Assert.Throws<LlrpProtocolException>(
            () => registry.DecodeParameter(LlrpProtocolVersion.Version101, encoded));

        Assert.Equal(LlrpProtocolErrorCode.TruncatedData, exception.ErrorCode);
    }

    [Fact]
    public void Decode_RejectsMalformedFirmwareUtf8()
    {
        LlrpCodecRegistry registry = CreateRegistry();
        byte[] encoded =
        [
            0x00, 0x89, 0x00, 0x14,
            0x00, 0x01,
            0x00, 0x00,
            0x00, 0x00, 0x00, 0x02,
            0x00, 0x00, 0x00, 0x03,
            0x00, 0x02, 0xC3, 0x28,
        ];

        LlrpProtocolException exception = Assert.Throws<LlrpProtocolException>(
            () => registry.DecodeParameter(LlrpProtocolVersion.Version101, encoded));

        Assert.Equal(LlrpProtocolErrorCode.InvalidParameterEncoding, exception.ErrorCode);
    }

    private static LlrpCodecRegistry CreateRegistry()
    {
        var registry = new LlrpCodecRegistry();
        Llrp101StandardModule.Register(registry);
        return registry;
    }

    private static GeneralDeviceCapabilities CreateParameter(IEnumerable<ILlrpParameter> parameters)
    {
        return new GeneralDeviceCapabilities(
            maxNumberOfAntennaSupported: 1,
            canSetAntennaProperties: false,
            hasUtcClockCapability: false,
            deviceManufacturerName: 2,
            modelName: 3,
            readerFirmwareVersion: string.Empty,
            parameters);
    }

    private static ILlrpParameter[] CreateRequiredParameters()
    {
        return
        [
            new UnknownParameter(LlrpProtocolVersion.Version101, 139, [0x00, 0x01, 0x00, 0x00]),
            new UnknownParameter(LlrpProtocolVersion.Version101, 141, [0x00, 0x00, 0x00, 0x00]),
            new UnknownParameter(LlrpProtocolVersion.Version101, 140, [0x00, 0x01, 0x01, 0x01]),
        ];
    }

    private static ushort[] GetUnknownParameterTypes(IReadOnlyList<ILlrpParameter> parameters)
    {
        return parameters
            .Select(parameter => Assert.IsType<UnknownParameter>(parameter).ParameterType)
            .ToArray();
    }
}
