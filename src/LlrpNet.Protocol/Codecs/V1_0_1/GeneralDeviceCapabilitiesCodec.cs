using System.Buffers.Binary;
using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Parameters;
using LlrpNet.Protocol.Parameters.V1_0_1;
using LlrpNet.Protocol.Registry;

namespace LlrpNet.Protocol.Codecs.V1_0_1;

internal sealed class GeneralDeviceCapabilitiesCodec(LlrpCodecRegistry registry)
    : LlrpParameterCodec<GeneralDeviceCapabilities>
{
    private const int FixedFieldLength = 12;
    private const ushort CanSetAntennaPropertiesMask = 0x8000;
    private const ushort HasUtcClockCapabilityMask = 0x4000;
    private const ushort ReservedBitsMask = 0x3FFF;

    private readonly LlrpCodecRegistry _registry = registry ?? throw new ArgumentNullException(nameof(registry));

    public override GeneralDeviceCapabilities Decode(
        LlrpProtocolVersion version,
        ReadOnlySpan<byte> payload)
    {
        Llrp101CodecValidation.ValidateVersion(version);
        if (payload.Length < FixedFieldLength)
        {
            throw new LlrpProtocolException(
                LlrpProtocolErrorCode.TruncatedData,
                $"GeneralDeviceCapabilities requires at least {FixedFieldLength} fixed-field octets before its utf8v value.");
        }

        ushort flags = BinaryPrimitives.ReadUInt16BigEndian(payload[sizeof(ushort)..]);
        if ((flags & ReservedBitsMask) != 0)
        {
            throw new LlrpProtocolException(
                LlrpProtocolErrorCode.InvalidReservedBits,
                "The fourteen reserved GeneralDeviceCapabilities flag bits must be zero.");
        }

        string firmwareVersion = Llrp101Utf8.Decode(
            payload[FixedFieldLength..],
            nameof(GeneralDeviceCapabilities.ReaderFirmwareVersion),
            out int firmwareLength);
        int parameterOffset = FixedFieldLength + firmwareLength;
        List<ILlrpParameter> parameters = Llrp101ParameterSequence.Decode(
            _registry,
            version,
            payload[parameterOffset..]);
        ValidateCapabilityParameterSequence(version, parameters, forEncoding: false);

        return new GeneralDeviceCapabilities(
            BinaryPrimitives.ReadUInt16BigEndian(payload),
            (flags & CanSetAntennaPropertiesMask) != 0,
            (flags & HasUtcClockCapabilityMask) != 0,
            BinaryPrimitives.ReadUInt32BigEndian(payload[4..]),
            BinaryPrimitives.ReadUInt32BigEndian(payload[8..]),
            firmwareVersion,
            parameters);
    }

    public override int GetEncodedPayloadLength(
        LlrpProtocolVersion version,
        GeneralDeviceCapabilities parameter)
    {
        Llrp101CodecValidation.ValidateVersion(version);
        ValidateCapabilityParameterSequence(version, parameter.Parameters, forEncoding: true);
        return checked(
            FixedFieldLength
            + Llrp101Utf8.GetEncodedLength(
                parameter.ReaderFirmwareVersion,
                nameof(GeneralDeviceCapabilities.ReaderFirmwareVersion))
            + Llrp101ParameterSequence.GetEncodedLength(
                _registry,
                version,
                parameter.Parameters));
    }

    public override int Encode(
        LlrpProtocolVersion version,
        GeneralDeviceCapabilities parameter,
        Span<byte> destination)
    {
        int expectedLength = GetEncodedPayloadLength(version, parameter);
        if (destination.Length != expectedLength)
        {
            throw new ArgumentException(
                $"GeneralDeviceCapabilities requires an exact {expectedLength}-octet payload destination.",
                nameof(destination));
        }

        BinaryPrimitives.WriteUInt16BigEndian(destination, parameter.MaxNumberOfAntennaSupported);
        ushort flags = 0;
        if (parameter.CanSetAntennaProperties)
        {
            flags |= CanSetAntennaPropertiesMask;
        }

        if (parameter.HasUtcClockCapability)
        {
            flags |= HasUtcClockCapabilityMask;
        }

        BinaryPrimitives.WriteUInt16BigEndian(destination[2..], flags);
        BinaryPrimitives.WriteUInt32BigEndian(destination[4..], parameter.DeviceManufacturerName);
        BinaryPrimitives.WriteUInt32BigEndian(destination[8..], parameter.ModelName);

        int offset = FixedFieldLength;
        offset += Llrp101Utf8.Encode(
            parameter.ReaderFirmwareVersion,
            destination[offset..],
            nameof(GeneralDeviceCapabilities.ReaderFirmwareVersion));
        offset += Llrp101ParameterSequence.Encode(
            _registry,
            version,
            parameter.Parameters,
            destination[offset..]);
        return offset;
    }

    private void ValidateCapabilityParameterSequence(
        LlrpProtocolVersion version,
        IReadOnlyList<ILlrpParameter> parameters,
        bool forEncoding)
    {
        const ushort receiveSensitivityTableEntryParameterType = 139;
        const ushort perAntennaReceiveSensitivityRangeParameterType = 149;
        const ushort gpioCapabilitiesParameterType = 141;
        const ushort perAntennaAirProtocolParameterType = 140;

        int receiveSensitivityCount = 0;
        int gpioCapabilitiesCount = 0;
        int perAntennaAirProtocolCount = 0;
        int previousSlot = 0;

        foreach (ILlrpParameter nestedParameter in parameters)
        {
            ushort parameterType = _registry.GetParameterWireType(version, nestedParameter);
            int slot = parameterType switch
            {
                receiveSensitivityTableEntryParameterType => 0,
                perAntennaReceiveSensitivityRangeParameterType => 1,
                gpioCapabilitiesParameterType => 2,
                perAntennaAirProtocolParameterType => 3,
                _ => -1,
            };

            if (slot < previousSlot || slot < 0)
            {
                ThrowInvalidCapabilitySequence(forEncoding);
            }

            switch (slot)
            {
                case 0:
                    receiveSensitivityCount++;
                    break;
                case 2:
                    gpioCapabilitiesCount++;
                    break;
                case 3:
                    perAntennaAirProtocolCount++;
                    break;
            }

            previousSlot = slot;
        }

        if (receiveSensitivityCount < 1
            || gpioCapabilitiesCount != 1
            || perAntennaAirProtocolCount < 1)
        {
            ThrowInvalidCapabilitySequence(forEncoding);
        }
    }

    private static void ThrowInvalidCapabilitySequence(bool forEncoding)
    {
        const string message =
            "GeneralDeviceCapabilities requires one or more ReceiveSensitivityTableEntry parameters (139), " +
            "followed by zero or more PerAntennaReceiveSensitivityRange parameters (149), exactly one " +
            "GPIOCapabilities parameter (141), and one or more PerAntennaAirProtocol parameters (140).";
        if (forEncoding)
        {
            throw new ArgumentException(message, "parameters");
        }

        throw new LlrpProtocolException(
            LlrpProtocolErrorCode.InvalidParameterEncoding,
            message);
    }
}
