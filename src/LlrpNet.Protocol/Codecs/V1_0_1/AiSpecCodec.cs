using System.Buffers.Binary;
using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Parameters;
using LlrpNet.Protocol.Parameters.V1_0_1;
using LlrpNet.Protocol.Registry;

namespace LlrpNet.Protocol.Codecs.V1_0_1;

internal sealed class AiSpecCodec(LlrpCodecRegistry registry)
    : LlrpParameterCodec<AiSpec>
{
    private const int VectorCountLength = sizeof(ushort);

    private readonly LlrpCodecRegistry _registry = registry ?? throw new ArgumentNullException(nameof(registry));

    public override AiSpec Decode(
        LlrpProtocolVersion version,
        ReadOnlySpan<byte> payload)
    {
        Llrp101CodecValidation.ValidateVersion(version);
        if (payload.Length < VectorCountLength)
        {
            throw new LlrpProtocolException(
                LlrpProtocolErrorCode.TruncatedData,
                "AISpec requires a two-octet AntennaIDs vector count.");
        }

        int antennaCount = BinaryPrimitives.ReadUInt16BigEndian(payload);
        int vectorLength = checked(VectorCountLength + antennaCount * sizeof(ushort));
        if (payload.Length < vectorLength)
        {
            throw new LlrpProtocolException(
                LlrpProtocolErrorCode.TruncatedData,
                $"AISpec declares {antennaCount} AntennaIDs requiring {vectorLength} octets, " +
                $"but only {payload.Length} payload octets are available.");
        }

        var antennaIds = new ushort[antennaCount];
        for (int index = 0; index < antennaIds.Length; index++)
        {
            antennaIds[index] = BinaryPrimitives.ReadUInt16BigEndian(
                payload[(VectorCountLength + index * sizeof(ushort))..]);
        }

        List<ILlrpParameter> nested = Llrp101ParameterSequence.Decode(
            _registry,
            version,
            payload[vectorLength..]);
        if (nested.Count < 2
            || RoSpecGraphCodecHelpers.GetWireType(_registry, version, nested[0])
                != AiSpecStopTrigger.ParameterType)
        {
            ThrowInvalidSequence(forEncoding: false);
        }

        AiSpecStopTrigger stopTrigger = RoSpecGraphCodecHelpers.RequireTyped<AiSpecStopTrigger>(
            nested[0],
            AiSpecStopTrigger.ParameterType,
            nameof(AiSpec));
        var inventoryParameters = new List<InventoryParameterSpec>();
        var customParameters = new List<ILlrpParameter>();
        bool reachedCustomSlot = false;
        for (int index = 1; index < nested.Count; index++)
        {
            ILlrpParameter child = nested[index];
            ushort childType = RoSpecGraphCodecHelpers.GetWireType(_registry, version, child);
            if (!reachedCustomSlot && childType == InventoryParameterSpec.ParameterType)
            {
                inventoryParameters.Add(
                    RoSpecGraphCodecHelpers.RequireTyped<InventoryParameterSpec>(
                        child,
                        InventoryParameterSpec.ParameterType,
                        nameof(AiSpec)));
                continue;
            }

            reachedCustomSlot = true;
            if (childType != RawCustomParameter.CustomParameterType)
            {
                ThrowInvalidSequence(forEncoding: false);
            }

            customParameters.Add(child);
        }

        if (inventoryParameters.Count == 0)
        {
            ThrowInvalidSequence(forEncoding: false);
        }

        return new AiSpec(
            antennaIds,
            stopTrigger,
            inventoryParameters,
            customParameters);
    }

    public override int GetEncodedPayloadLength(
        LlrpProtocolVersion version,
        AiSpec parameter)
    {
        Llrp101CodecValidation.ValidateVersion(version);
        if (parameter.AntennaIds.Count > ushort.MaxValue)
        {
            throw new ArgumentException(
                $"AISpec cannot encode more than {ushort.MaxValue} AntennaIDs.",
                nameof(parameter));
        }

        RoSpecGraphCodecHelpers.ValidateCustomParameters(
            _registry,
            version,
            parameter.CustomParameters,
            nameof(AiSpec));
        return checked(
            VectorCountLength
            + parameter.AntennaIds.Count * sizeof(ushort)
            + _registry.GetEncodedParameterLength(version, parameter.StopTrigger)
            + Llrp101ParameterSequence.GetEncodedLength(
                _registry,
                version,
                parameter.InventoryParameterSpecs)
            + Llrp101ParameterSequence.GetEncodedLength(
                _registry,
                version,
                parameter.CustomParameters));
    }

    public override int Encode(
        LlrpProtocolVersion version,
        AiSpec parameter,
        Span<byte> destination)
    {
        int expectedLength = GetEncodedPayloadLength(version, parameter);
        if (destination.Length != expectedLength)
        {
            throw new ArgumentException(
                $"AISpec requires an exact {expectedLength}-octet payload destination.",
                nameof(destination));
        }

        BinaryPrimitives.WriteUInt16BigEndian(destination, (ushort)parameter.AntennaIds.Count);
        int offset = VectorCountLength;
        foreach (ushort antennaId in parameter.AntennaIds)
        {
            BinaryPrimitives.WriteUInt16BigEndian(destination[offset..], antennaId);
            offset += sizeof(ushort);
        }

        offset += _registry.EncodeParameter(version, parameter.StopTrigger, destination[offset..]);
        offset += Llrp101ParameterSequence.Encode(
            _registry,
            version,
            parameter.InventoryParameterSpecs,
            destination[offset..]);
        offset += Llrp101ParameterSequence.Encode(
            _registry,
            version,
            parameter.CustomParameters,
            destination[offset..]);
        return offset;
    }

    private static void ThrowInvalidSequence(bool forEncoding)
    {
        RoSpecGraphCodecHelpers.ThrowInvalidSequence(
            "AISpec requires one AISpecStopTrigger (184), followed by one or more " +
            "InventoryParameterSpec parameters (186), followed only by Custom parameters (1023).",
            forEncoding);
    }
}

internal sealed class AiSpecStopTriggerCodec(LlrpCodecRegistry registry)
    : LlrpParameterCodec<AiSpecStopTrigger>
{
    private const int FixedFieldLength = sizeof(byte) + sizeof(uint);
    private const ushort GpiTriggerValueParameterType = 181;
    private const ushort TagObservationTriggerParameterType = 185;

    private readonly LlrpCodecRegistry _registry = registry ?? throw new ArgumentNullException(nameof(registry));

    public override AiSpecStopTrigger Decode(
        LlrpProtocolVersion version,
        ReadOnlySpan<byte> payload)
    {
        Llrp101CodecValidation.ValidateVersion(version);
        if (payload.Length < FixedFieldLength)
        {
            throw new LlrpProtocolException(
                LlrpProtocolErrorCode.TruncatedData,
                $"AISpecStopTrigger requires {FixedFieldLength} fixed-field octets.");
        }

        var triggerType = (AiSpecStopTriggerType)payload[0];
        if (!AiSpecStopTrigger.IsDefined(triggerType))
        {
            throw new LlrpProtocolException(
                LlrpProtocolErrorCode.InvalidParameterEncoding,
                $"AISpecStopTrigger type {payload[0]} is not defined by LLRP 1.0.1.");
        }

        List<ILlrpParameter> nested = Llrp101ParameterSequence.Decode(
            _registry,
            version,
            payload[FixedFieldLength..]);
        ILlrpParameter? gpi = null;
        ILlrpParameter? observation = null;
        int previousSlot = -1;
        foreach (ILlrpParameter child in nested)
        {
            ushort childType = RoSpecGraphCodecHelpers.GetWireType(_registry, version, child);
            int slot = childType switch
            {
                GpiTriggerValueParameterType => 0,
                TagObservationTriggerParameterType => 1,
                _ => -1,
            };
            if (slot <= previousSlot)
            {
                ThrowInvalidSequence(forEncoding: false);
            }

            if (slot == 0)
            {
                gpi = child;
            }
            else if (slot == 1)
            {
                observation = child;
            }
            else
            {
                ThrowInvalidSequence(forEncoding: false);
            }

            previousSlot = slot;
        }

        return new AiSpecStopTrigger(
            triggerType,
            BinaryPrimitives.ReadUInt32BigEndian(payload[1..]),
            gpi,
            observation);
    }

    public override int GetEncodedPayloadLength(
        LlrpProtocolVersion version,
        AiSpecStopTrigger parameter)
    {
        Llrp101CodecValidation.ValidateVersion(version);
        ValidateTriggerType(parameter.TriggerType);
        int length = FixedFieldLength;
        if (parameter.GpiTriggerValue is not null)
        {
            RoSpecGraphCodecHelpers.ValidateWireType(
                _registry,
                version,
                parameter.GpiTriggerValue,
                GpiTriggerValueParameterType,
                nameof(AiSpecStopTrigger));
            length = checked(
                length + _registry.GetEncodedParameterLength(version, parameter.GpiTriggerValue));
        }

        if (parameter.TagObservationTrigger is not null)
        {
            RoSpecGraphCodecHelpers.ValidateWireType(
                _registry,
                version,
                parameter.TagObservationTrigger,
                TagObservationTriggerParameterType,
                nameof(AiSpecStopTrigger));
            length = checked(
                length + _registry.GetEncodedParameterLength(version, parameter.TagObservationTrigger));
        }

        return length;
    }

    public override int Encode(
        LlrpProtocolVersion version,
        AiSpecStopTrigger parameter,
        Span<byte> destination)
    {
        int expectedLength = GetEncodedPayloadLength(version, parameter);
        if (destination.Length != expectedLength)
        {
            throw new ArgumentException(
                $"AISpecStopTrigger requires an exact {expectedLength}-octet payload destination.",
                nameof(destination));
        }

        destination[0] = (byte)parameter.TriggerType;
        BinaryPrimitives.WriteUInt32BigEndian(destination[1..], parameter.DurationTrigger);
        int offset = FixedFieldLength;
        if (parameter.GpiTriggerValue is not null)
        {
            offset += _registry.EncodeParameter(version, parameter.GpiTriggerValue, destination[offset..]);
        }

        if (parameter.TagObservationTrigger is not null)
        {
            offset += _registry.EncodeParameter(
                version,
                parameter.TagObservationTrigger,
                destination[offset..]);
        }

        return offset;
    }

    private static void ValidateTriggerType(AiSpecStopTriggerType triggerType)
    {
        if (!AiSpecStopTrigger.IsDefined(triggerType))
        {
            throw new ArgumentOutOfRangeException(
                nameof(triggerType),
                triggerType,
                "The AISpec stop trigger type must be defined by LLRP 1.0.1.");
        }
    }

    private static void ThrowInvalidSequence(bool forEncoding)
    {
        RoSpecGraphCodecHelpers.ThrowInvalidSequence(
            "AISpecStopTrigger permits at most one GPITriggerValue (181), followed by at most " +
            "one TagObservationTrigger (185).",
            forEncoding);
    }
}

internal sealed class InventoryParameterSpecCodec(LlrpCodecRegistry registry)
    : LlrpParameterCodec<InventoryParameterSpec>
{
    private const int FixedFieldLength = sizeof(ushort) + sizeof(byte);
    private const ushort AntennaConfigurationParameterType = 222;

    private readonly LlrpCodecRegistry _registry = registry ?? throw new ArgumentNullException(nameof(registry));

    public override InventoryParameterSpec Decode(
        LlrpProtocolVersion version,
        ReadOnlySpan<byte> payload)
    {
        Llrp101CodecValidation.ValidateVersion(version);
        if (payload.Length < FixedFieldLength)
        {
            throw new LlrpProtocolException(
                LlrpProtocolErrorCode.TruncatedData,
                $"InventoryParameterSpec requires {FixedFieldLength} fixed-field octets.");
        }

        var protocolId = (AirProtocolId)payload[sizeof(ushort)];
        if (!InventoryParameterSpec.IsDefined(protocolId))
        {
            throw new LlrpProtocolException(
                LlrpProtocolErrorCode.InvalidParameterEncoding,
                $"InventoryParameterSpec ProtocolID {payload[sizeof(ushort)]} is not defined by LLRP 1.0.1.");
        }

        List<ILlrpParameter> nested = Llrp101ParameterSequence.Decode(
            _registry,
            version,
            payload[FixedFieldLength..]);
        var antennaConfigurations = new List<ILlrpParameter>();
        var customParameters = new List<ILlrpParameter>();
        bool reachedCustomSlot = false;
        foreach (ILlrpParameter child in nested)
        {
            ushort childType = RoSpecGraphCodecHelpers.GetWireType(_registry, version, child);
            if (!reachedCustomSlot && childType == AntennaConfigurationParameterType)
            {
                antennaConfigurations.Add(child);
                continue;
            }

            reachedCustomSlot = true;
            if (childType != RawCustomParameter.CustomParameterType)
            {
                ThrowInvalidSequence(forEncoding: false);
            }

            customParameters.Add(child);
        }

        return new InventoryParameterSpec(
            BinaryPrimitives.ReadUInt16BigEndian(payload),
            protocolId,
            antennaConfigurations,
            customParameters);
    }

    public override int GetEncodedPayloadLength(
        LlrpProtocolVersion version,
        InventoryParameterSpec parameter)
    {
        Llrp101CodecValidation.ValidateVersion(version);
        ValidateProtocolId(parameter.ProtocolId);
        foreach (ILlrpParameter antennaConfiguration in parameter.AntennaConfigurations)
        {
            RoSpecGraphCodecHelpers.ValidateWireType(
                _registry,
                version,
                antennaConfiguration,
                AntennaConfigurationParameterType,
                nameof(InventoryParameterSpec));
        }

        RoSpecGraphCodecHelpers.ValidateCustomParameters(
            _registry,
            version,
            parameter.CustomParameters,
            nameof(InventoryParameterSpec));
        return checked(
            FixedFieldLength
            + Llrp101ParameterSequence.GetEncodedLength(
                _registry,
                version,
                parameter.AntennaConfigurations)
            + Llrp101ParameterSequence.GetEncodedLength(
                _registry,
                version,
                parameter.CustomParameters));
    }

    public override int Encode(
        LlrpProtocolVersion version,
        InventoryParameterSpec parameter,
        Span<byte> destination)
    {
        int expectedLength = GetEncodedPayloadLength(version, parameter);
        if (destination.Length != expectedLength)
        {
            throw new ArgumentException(
                $"InventoryParameterSpec requires an exact {expectedLength}-octet payload destination.",
                nameof(destination));
        }

        BinaryPrimitives.WriteUInt16BigEndian(destination, parameter.InventoryParameterSpecId);
        destination[sizeof(ushort)] = (byte)parameter.ProtocolId;
        int offset = FixedFieldLength;
        offset += Llrp101ParameterSequence.Encode(
            _registry,
            version,
            parameter.AntennaConfigurations,
            destination[offset..]);
        offset += Llrp101ParameterSequence.Encode(
            _registry,
            version,
            parameter.CustomParameters,
            destination[offset..]);
        return offset;
    }

    private static void ValidateProtocolId(AirProtocolId protocolId)
    {
        if (!InventoryParameterSpec.IsDefined(protocolId))
        {
            throw new ArgumentOutOfRangeException(
                nameof(protocolId),
                protocolId,
                "The air protocol identifier must be defined by LLRP 1.0.1.");
        }
    }

    private static void ThrowInvalidSequence(bool forEncoding)
    {
        RoSpecGraphCodecHelpers.ThrowInvalidSequence(
            "InventoryParameterSpec permits zero or more AntennaConfiguration parameters (222), " +
            "followed only by Custom parameters (1023).",
            forEncoding);
    }
}
