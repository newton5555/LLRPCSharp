using System.Buffers.Binary;
using LlrpNet.Core.Protocol;

namespace LlrpSdk.Tests.Support;

using LlrpNet.Protocol.Messages;
using LlrpNet.Protocol.Messages.V1_0_1;
using LlrpNet.Protocol.Enumerations.V1_0_1;
using LlrpNet.Protocol.Parameters;
using LlrpNet.Protocol.Parameters.V1_0_1;
using LlrpNet.Protocol.Registry;
using LlrpNet.Protocol.Registry.V1_0_1;

internal static class LlrpTestFrames
{
    public const uint DefaultManufacturerId = 1_000;
    public const uint DefaultModelId = 2_000;
    public const string DefaultFirmwareVersion = "test-fw";

    private static readonly LlrpCodecRegistry Registry = CreateRegistry();

    public static byte[] EmptyMessage(ushort messageType, uint messageId)
    {
        var frame = new byte[LlrpMessageHeader.EncodedLength];
        new LlrpMessageHeader(
            LlrpProtocolVersion.Version101,
            messageType,
            LlrpMessageHeader.EncodedLength,
            messageId)
            .Encode(frame);
        return frame;
    }

    public static GeneralDeviceCapabilities GeneralCapabilities(
        ushort maxNumberOfAntennas = 4,
        bool canSetAntennaProperties = true,
        bool hasUtcClockCapability = true,
        uint manufacturerId = DefaultManufacturerId,
        uint modelId = DefaultModelId,
        string firmwareVersion = DefaultFirmwareVersion,
        IEnumerable<ILlrpParameter>? parameters = null)
    {
        _ = parameters;
        return new GeneralDeviceCapabilities(
            maxNumberOfAntennas,
            canSetAntennaProperties,
            hasUtcClockCapability,
            manufacturerId,
            modelId,
            firmwareVersion,
            [new ReceiveSensitivityTableEntry(1, 0)],
            [],
            new GPIOCapabilities(0, 0),
            [new PerAntennaAirProtocol(1, [AirProtocols.Unspecified])]);
    }

    public static ILlrpParameter[] RequiredGeneralDeviceParameters(
        ReadOnlySpan<byte> receiveSensitivityData = default,
        ReadOnlySpan<byte> gpioData = default,
        ReadOnlySpan<byte> airProtocolData = default)
    {
        return
        [
            new UnknownParameter(
                LlrpProtocolVersion.Version101,
                139,
                receiveSensitivityData),
            new UnknownParameter(
                LlrpProtocolVersion.Version101,
                141,
                gpioData),
            new UnknownParameter(
                LlrpProtocolVersion.Version101,
                140,
                airProtocolData),
        ];
    }

    public static byte[] CapabilitiesResponse(
        uint messageId,
        LLRPStatus? status = null,
        IEnumerable<ILlrpParameter>? parameters = null)
    {
        ILlrpParameter[] items = (parameters ?? [GeneralCapabilities()]).ToArray();
        GeneralDeviceCapabilities? general = items.OfType<GeneralDeviceCapabilities>().SingleOrDefault();
        var response = new GetReaderCapabilitiesResponse(
            messageId,
            status ?? new LLRPStatus(StatusCode.M_Success, string.Empty, null, null),
            general,
            null,
            null,
            null,
            items.Where(parameter => parameter is not GeneralDeviceCapabilities).ToArray());
        return Registry.EncodeMessage(LlrpProtocolVersion.Version101, response);
    }

    public static byte[] CapabilitiesResponseWithDuplicateGeneral(uint messageId)
    {
        byte[] frame = CapabilitiesResponse(messageId);
        int statusOffset = LlrpMessageHeader.EncodedLength;
        int statusLength = BinaryPrimitives.ReadUInt16BigEndian(
            frame.AsSpan(statusOffset + sizeof(ushort), sizeof(ushort)));
        int generalOffset = checked(statusOffset + statusLength);
        int generalLength = BinaryPrimitives.ReadUInt16BigEndian(
            frame.AsSpan(generalOffset + sizeof(ushort), sizeof(ushort)));

        var duplicated = new byte[checked(frame.Length + generalLength)];
        frame.CopyTo(duplicated, 0);
        frame.AsSpan(generalOffset, generalLength).CopyTo(duplicated.AsSpan(frame.Length));
        BinaryPrimitives.WriteUInt32BigEndian(
            duplicated.AsSpan(sizeof(ushort), sizeof(uint)),
            checked((uint)duplicated.Length));
        return duplicated;
    }

    public static byte[] RoSpecStatusResponse(
        ushort responseMessageType,
        uint messageId,
        LLRPStatus? status = null)
    {
        LLRPStatus actualStatus = status ?? new LLRPStatus(StatusCode.M_Success, string.Empty, null, null);
        ILlrpMessage response = responseMessageType switch
        {
            AddRoSpecResponse.MessageType => new AddRoSpecResponse(messageId, actualStatus),
            DeleteRoSpecResponse.MessageType => new DeleteRoSpecResponse(messageId, actualStatus),
            EnableRoSpecResponse.MessageType => new EnableRoSpecResponse(messageId, actualStatus),
            DisableRoSpecResponse.MessageType => new DisableRoSpecResponse(messageId, actualStatus),
            StartRoSpecResponse.MessageType => new StartRoSpecResponse(messageId, actualStatus),
            StopRoSpecResponse.MessageType => new StopRoSpecResponse(messageId, actualStatus),
            _ => throw new ArgumentOutOfRangeException(
                nameof(responseMessageType),
                responseMessageType,
                "The message type is not a ROSpec status-only response."),
        };

        return Registry.EncodeMessage(LlrpProtocolVersion.Version101, response);
    }

    public static byte[] GetRoSpecsResponseFrame(
        uint messageId,
        LLRPStatus? status = null,
        IEnumerable<ROSpec>? roSpecs = null)
    {
        var response = new GetRoSpecsResponse(
            messageId,
            status ?? new LLRPStatus(StatusCode.M_Success, string.Empty, null, null),
            (roSpecs ?? []).ToArray());
        return Registry.EncodeMessage(LlrpProtocolVersion.Version101, response);
    }

    public static byte[] ErrorMessageFrame(uint messageId, LLRPStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        return Registry.EncodeMessage(
            LlrpProtocolVersion.Version101,
            new ErrorMessage(messageId, status));
    }

    private static LlrpCodecRegistry CreateRegistry()
    {
        var registry = new LlrpCodecRegistry();
        Llrp101StandardModule.Register(registry);
        return registry;
    }
}
