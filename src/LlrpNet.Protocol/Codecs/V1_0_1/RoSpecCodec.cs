using System.Buffers.Binary;
using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Parameters;
using LlrpNet.Protocol.Parameters.V1_0_1;
using LlrpNet.Protocol.Registry;

namespace LlrpNet.Protocol.Codecs.V1_0_1;

internal sealed class RoSpecCodec(LlrpCodecRegistry registry)
    : LlrpParameterCodec<RoSpec>
{
    private const int FixedFieldLength = sizeof(uint) + sizeof(byte) + sizeof(byte);
    private const ushort RfSurveySpecParameterType = 187;

    private readonly LlrpCodecRegistry _registry = registry ?? throw new ArgumentNullException(nameof(registry));

    public override RoSpec Decode(
        LlrpProtocolVersion version,
        ReadOnlySpan<byte> payload)
    {
        Llrp101CodecValidation.ValidateVersion(version);
        if (payload.Length < FixedFieldLength)
        {
            throw new LlrpProtocolException(
                LlrpProtocolErrorCode.TruncatedData,
                $"ROSpec requires {FixedFieldLength} fixed-field octets before its nested parameters.");
        }

        var currentState = (RoSpecState)payload[5];
        if (!RoSpec.IsDefined(currentState))
        {
            throw new LlrpProtocolException(
                LlrpProtocolErrorCode.InvalidParameterEncoding,
                $"ROSpec state {payload[5]} is not defined by LLRP 1.0.1.");
        }

        List<ILlrpParameter> nested = Llrp101ParameterSequence.Decode(
            _registry,
            version,
            payload[FixedFieldLength..]);
        if (nested.Count < 2)
        {
            ThrowInvalidSequence(forEncoding: false);
        }

        ushort firstType = RoSpecGraphCodecHelpers.GetWireType(_registry, version, nested[0]);
        if (firstType != RoBoundarySpec.ParameterType)
        {
            ThrowInvalidSequence(forEncoding: false);
        }

        RoBoundarySpec boundarySpec = RoSpecGraphCodecHelpers.RequireTyped<RoBoundarySpec>(
            nested[0],
            RoBoundarySpec.ParameterType,
            nameof(RoSpec));
        int specEnd = nested.Count;
        RoReportSpec? reportSpec = null;
        ushort lastType = RoSpecGraphCodecHelpers.GetWireType(_registry, version, nested[^1]);
        if (lastType == RoReportSpec.ParameterType)
        {
            reportSpec = RoSpecGraphCodecHelpers.RequireTyped<RoReportSpec>(
                nested[^1],
                RoReportSpec.ParameterType,
                nameof(RoSpec));
            specEnd--;
        }

        if (specEnd <= 1)
        {
            ThrowInvalidSequence(forEncoding: false);
        }

        var specParameters = new List<ILlrpParameter>(specEnd - 1);
        for (int index = 1; index < specEnd; index++)
        {
            ILlrpParameter parameter = nested[index];
            ValidateSpecParameter(version, parameter, forEncoding: false);
            specParameters.Add(parameter);
        }

        return new RoSpec(
            BinaryPrimitives.ReadUInt32BigEndian(payload),
            payload[sizeof(uint)],
            currentState,
            boundarySpec,
            specParameters,
            reportSpec);
    }

    public override int GetEncodedPayloadLength(
        LlrpProtocolVersion version,
        RoSpec parameter)
    {
        Llrp101CodecValidation.ValidateVersion(version);
        ValidateState(parameter.CurrentState);
        foreach (ILlrpParameter specParameter in parameter.SpecParameters)
        {
            ValidateSpecParameter(version, specParameter, forEncoding: true);
        }

        int length = checked(
            FixedFieldLength
            + _registry.GetEncodedParameterLength(version, parameter.BoundarySpec)
            + Llrp101ParameterSequence.GetEncodedLength(_registry, version, parameter.SpecParameters));
        if (parameter.ReportSpec is not null)
        {
            length = checked(length + _registry.GetEncodedParameterLength(version, parameter.ReportSpec));
        }

        return length;
    }

    public override int Encode(
        LlrpProtocolVersion version,
        RoSpec parameter,
        Span<byte> destination)
    {
        int expectedLength = GetEncodedPayloadLength(version, parameter);
        if (destination.Length != expectedLength)
        {
            throw new ArgumentException(
                $"ROSpec requires an exact {expectedLength}-octet payload destination.",
                nameof(destination));
        }

        BinaryPrimitives.WriteUInt32BigEndian(destination, parameter.RoSpecId);
        destination[sizeof(uint)] = parameter.Priority;
        destination[5] = (byte)parameter.CurrentState;
        int offset = FixedFieldLength;
        offset += _registry.EncodeParameter(version, parameter.BoundarySpec, destination[offset..]);
        offset += Llrp101ParameterSequence.Encode(
            _registry,
            version,
            parameter.SpecParameters,
            destination[offset..]);
        if (parameter.ReportSpec is not null)
        {
            offset += _registry.EncodeParameter(version, parameter.ReportSpec, destination[offset..]);
        }

        return offset;
    }

    private void ValidateSpecParameter(
        LlrpProtocolVersion version,
        ILlrpParameter parameter,
        bool forEncoding)
    {
        ushort parameterType = RoSpecGraphCodecHelpers.GetWireType(_registry, version, parameter);
        bool valid = parameterType switch
        {
            AiSpec.ParameterType => parameter is AiSpec,
            RfSurveySpecParameterType => true,
            RawCustomParameter.CustomParameterType => true,
            _ => false,
        };
        if (!valid)
        {
            ThrowInvalidSequence(forEncoding);
        }
    }

    private static void ValidateState(RoSpecState state)
    {
        if (!RoSpec.IsDefined(state))
        {
            throw new ArgumentOutOfRangeException(
                nameof(state),
                state,
                "The ROSpec state must be defined by LLRP 1.0.1.");
        }
    }

    private static void ThrowInvalidSequence(bool forEncoding)
    {
        const string message =
            "ROSpec requires one ROBoundarySpec (178), followed by one or more SpecParameter choices " +
            "(AISpec 183, RFSurveySpec 187, or Custom 1023), followed by at most one ROReportSpec (237).";
        RoSpecGraphCodecHelpers.ThrowInvalidSequence(message, forEncoding);
    }
}

internal sealed class RoBoundarySpecCodec(LlrpCodecRegistry registry)
    : LlrpParameterCodec<RoBoundarySpec>
{
    private readonly LlrpCodecRegistry _registry = registry ?? throw new ArgumentNullException(nameof(registry));

    public override RoBoundarySpec Decode(
        LlrpProtocolVersion version,
        ReadOnlySpan<byte> payload)
    {
        Llrp101CodecValidation.ValidateVersion(version);
        List<ILlrpParameter> nested = Llrp101ParameterSequence.Decode(_registry, version, payload);
        if (nested.Count != 2
            || RoSpecGraphCodecHelpers.GetWireType(_registry, version, nested[0])
                != RoSpecStartTrigger.ParameterType
            || RoSpecGraphCodecHelpers.GetWireType(_registry, version, nested[1])
                != RoSpecStopTrigger.ParameterType)
        {
            ThrowInvalidSequence(forEncoding: false);
        }

        return new RoBoundarySpec(
            RoSpecGraphCodecHelpers.RequireTyped<RoSpecStartTrigger>(
                nested[0],
                RoSpecStartTrigger.ParameterType,
                nameof(RoBoundarySpec)),
            RoSpecGraphCodecHelpers.RequireTyped<RoSpecStopTrigger>(
                nested[1],
                RoSpecStopTrigger.ParameterType,
                nameof(RoBoundarySpec)));
    }

    public override int GetEncodedPayloadLength(
        LlrpProtocolVersion version,
        RoBoundarySpec parameter)
    {
        Llrp101CodecValidation.ValidateVersion(version);
        return checked(
            _registry.GetEncodedParameterLength(version, parameter.StartTrigger)
            + _registry.GetEncodedParameterLength(version, parameter.StopTrigger));
    }

    public override int Encode(
        LlrpProtocolVersion version,
        RoBoundarySpec parameter,
        Span<byte> destination)
    {
        int expectedLength = GetEncodedPayloadLength(version, parameter);
        if (destination.Length != expectedLength)
        {
            throw new ArgumentException(
                $"ROBoundarySpec requires an exact {expectedLength}-octet payload destination.",
                nameof(destination));
        }

        int offset = _registry.EncodeParameter(version, parameter.StartTrigger, destination);
        offset += _registry.EncodeParameter(version, parameter.StopTrigger, destination[offset..]);
        return offset;
    }

    private static void ThrowInvalidSequence(bool forEncoding)
    {
        RoSpecGraphCodecHelpers.ThrowInvalidSequence(
            "ROBoundarySpec requires exactly one ROSpecStartTrigger (179), followed by exactly one " +
            "ROSpecStopTrigger (182).",
            forEncoding);
    }
}

internal sealed class RoSpecStartTriggerCodec(LlrpCodecRegistry registry)
    : LlrpParameterCodec<RoSpecStartTrigger>
{
    private const ushort PeriodicTriggerValueParameterType = 180;
    private const ushort GpiTriggerValueParameterType = 181;

    private readonly LlrpCodecRegistry _registry = registry ?? throw new ArgumentNullException(nameof(registry));

    public override RoSpecStartTrigger Decode(
        LlrpProtocolVersion version,
        ReadOnlySpan<byte> payload)
    {
        Llrp101CodecValidation.ValidateVersion(version);
        if (payload.IsEmpty)
        {
            throw new LlrpProtocolException(
                LlrpProtocolErrorCode.TruncatedData,
                "ROSpecStartTrigger requires a one-octet trigger type.");
        }

        var triggerType = (RoSpecStartTriggerType)payload[0];
        if (!RoSpecStartTrigger.IsDefined(triggerType))
        {
            throw new LlrpProtocolException(
                LlrpProtocolErrorCode.InvalidParameterEncoding,
                $"ROSpecStartTrigger type {payload[0]} is not defined by LLRP 1.0.1.");
        }

        List<ILlrpParameter> nested = Llrp101ParameterSequence.Decode(_registry, version, payload[1..]);
        ILlrpParameter? periodic = null;
        ILlrpParameter? gpi = null;
        int previousSlot = -1;
        foreach (ILlrpParameter child in nested)
        {
            ushort childType = RoSpecGraphCodecHelpers.GetWireType(_registry, version, child);
            int slot = childType switch
            {
                PeriodicTriggerValueParameterType => 0,
                GpiTriggerValueParameterType => 1,
                _ => -1,
            };
            if (slot <= previousSlot)
            {
                ThrowInvalidSequence(forEncoding: false);
            }

            if (slot == 0)
            {
                periodic = child;
            }
            else if (slot == 1)
            {
                gpi = child;
            }
            else
            {
                ThrowInvalidSequence(forEncoding: false);
            }

            previousSlot = slot;
        }

        return new RoSpecStartTrigger(triggerType, periodic, gpi);
    }

    public override int GetEncodedPayloadLength(
        LlrpProtocolVersion version,
        RoSpecStartTrigger parameter)
    {
        Llrp101CodecValidation.ValidateVersion(version);
        ValidateTriggerType(parameter.TriggerType);
        int length = sizeof(byte);
        if (parameter.PeriodicTriggerValue is not null)
        {
            RoSpecGraphCodecHelpers.ValidateWireType(
                _registry,
                version,
                parameter.PeriodicTriggerValue,
                PeriodicTriggerValueParameterType,
                nameof(RoSpecStartTrigger));
            length = checked(
                length + _registry.GetEncodedParameterLength(version, parameter.PeriodicTriggerValue));
        }

        if (parameter.GpiTriggerValue is not null)
        {
            RoSpecGraphCodecHelpers.ValidateWireType(
                _registry,
                version,
                parameter.GpiTriggerValue,
                GpiTriggerValueParameterType,
                nameof(RoSpecStartTrigger));
            length = checked(
                length + _registry.GetEncodedParameterLength(version, parameter.GpiTriggerValue));
        }

        return length;
    }

    public override int Encode(
        LlrpProtocolVersion version,
        RoSpecStartTrigger parameter,
        Span<byte> destination)
    {
        int expectedLength = GetEncodedPayloadLength(version, parameter);
        if (destination.Length != expectedLength)
        {
            throw new ArgumentException(
                $"ROSpecStartTrigger requires an exact {expectedLength}-octet payload destination.",
                nameof(destination));
        }

        destination[0] = (byte)parameter.TriggerType;
        int offset = 1;
        if (parameter.PeriodicTriggerValue is not null)
        {
            offset += _registry.EncodeParameter(
                version,
                parameter.PeriodicTriggerValue,
                destination[offset..]);
        }

        if (parameter.GpiTriggerValue is not null)
        {
            offset += _registry.EncodeParameter(version, parameter.GpiTriggerValue, destination[offset..]);
        }

        return offset;
    }

    private static void ValidateTriggerType(RoSpecStartTriggerType triggerType)
    {
        if (!RoSpecStartTrigger.IsDefined(triggerType))
        {
            throw new ArgumentOutOfRangeException(
                nameof(triggerType),
                triggerType,
                "The ROSpec start trigger type must be defined by LLRP 1.0.1.");
        }
    }

    private static void ThrowInvalidSequence(bool forEncoding)
    {
        RoSpecGraphCodecHelpers.ThrowInvalidSequence(
            "ROSpecStartTrigger permits at most one PeriodicTriggerValue (180), followed by at most " +
            "one GPITriggerValue (181).",
            forEncoding);
    }
}

internal sealed class RoSpecStopTriggerCodec(LlrpCodecRegistry registry)
    : LlrpParameterCodec<RoSpecStopTrigger>
{
    private const int FixedFieldLength = sizeof(byte) + sizeof(uint);
    private const ushort GpiTriggerValueParameterType = 181;

    private readonly LlrpCodecRegistry _registry = registry ?? throw new ArgumentNullException(nameof(registry));

    public override RoSpecStopTrigger Decode(
        LlrpProtocolVersion version,
        ReadOnlySpan<byte> payload)
    {
        Llrp101CodecValidation.ValidateVersion(version);
        if (payload.Length < FixedFieldLength)
        {
            throw new LlrpProtocolException(
                LlrpProtocolErrorCode.TruncatedData,
                $"ROSpecStopTrigger requires {FixedFieldLength} fixed-field octets.");
        }

        var triggerType = (RoSpecStopTriggerType)payload[0];
        if (!RoSpecStopTrigger.IsDefined(triggerType))
        {
            throw new LlrpProtocolException(
                LlrpProtocolErrorCode.InvalidParameterEncoding,
                $"ROSpecStopTrigger type {payload[0]} is not defined by LLRP 1.0.1.");
        }

        List<ILlrpParameter> nested = Llrp101ParameterSequence.Decode(
            _registry,
            version,
            payload[FixedFieldLength..]);
        if (nested.Count > 1
            || nested.Count == 1
            && RoSpecGraphCodecHelpers.GetWireType(_registry, version, nested[0])
                != GpiTriggerValueParameterType)
        {
            ThrowInvalidSequence(forEncoding: false);
        }

        return new RoSpecStopTrigger(
            triggerType,
            BinaryPrimitives.ReadUInt32BigEndian(payload[1..]),
            nested.Count == 0 ? null : nested[0]);
    }

    public override int GetEncodedPayloadLength(
        LlrpProtocolVersion version,
        RoSpecStopTrigger parameter)
    {
        Llrp101CodecValidation.ValidateVersion(version);
        ValidateTriggerType(parameter.TriggerType);
        if (parameter.GpiTriggerValue is null)
        {
            return FixedFieldLength;
        }

        RoSpecGraphCodecHelpers.ValidateWireType(
            _registry,
            version,
            parameter.GpiTriggerValue,
            GpiTriggerValueParameterType,
            nameof(RoSpecStopTrigger));
        return checked(
            FixedFieldLength + _registry.GetEncodedParameterLength(version, parameter.GpiTriggerValue));
    }

    public override int Encode(
        LlrpProtocolVersion version,
        RoSpecStopTrigger parameter,
        Span<byte> destination)
    {
        int expectedLength = GetEncodedPayloadLength(version, parameter);
        if (destination.Length != expectedLength)
        {
            throw new ArgumentException(
                $"ROSpecStopTrigger requires an exact {expectedLength}-octet payload destination.",
                nameof(destination));
        }

        destination[0] = (byte)parameter.TriggerType;
        BinaryPrimitives.WriteUInt32BigEndian(destination[1..], parameter.DurationTriggerValue);
        int offset = FixedFieldLength;
        if (parameter.GpiTriggerValue is not null)
        {
            offset += _registry.EncodeParameter(version, parameter.GpiTriggerValue, destination[offset..]);
        }

        return offset;
    }

    private static void ValidateTriggerType(RoSpecStopTriggerType triggerType)
    {
        if (!RoSpecStopTrigger.IsDefined(triggerType))
        {
            throw new ArgumentOutOfRangeException(
                nameof(triggerType),
                triggerType,
                "The ROSpec stop trigger type must be defined by LLRP 1.0.1.");
        }
    }

    private static void ThrowInvalidSequence(bool forEncoding)
    {
        RoSpecGraphCodecHelpers.ThrowInvalidSequence(
            "ROSpecStopTrigger permits at most one GPITriggerValue parameter (181).",
            forEncoding);
    }
}
