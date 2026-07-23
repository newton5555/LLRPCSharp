using System.Buffers.Binary;
using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Parameters;
using LlrpNet.Protocol.Parameters.V1_0_1;
using LlrpNet.Protocol.Registry;

namespace LlrpNet.Protocol.Codecs.V1_0_1;

internal sealed class RoReportSpecCodec(LlrpCodecRegistry registry)
    : LlrpParameterCodec<RoReportSpec>
{
    private const int FixedFieldLength = sizeof(byte) + sizeof(ushort);

    private readonly LlrpCodecRegistry _registry = registry ?? throw new ArgumentNullException(nameof(registry));

    public override RoReportSpec Decode(
        LlrpProtocolVersion version,
        ReadOnlySpan<byte> payload)
    {
        Llrp101CodecValidation.ValidateVersion(version);
        if (payload.Length < FixedFieldLength)
        {
            throw new LlrpProtocolException(
                LlrpProtocolErrorCode.TruncatedData,
                $"ROReportSpec requires {FixedFieldLength} fixed-field octets.");
        }

        var triggerType = (RoReportTriggerType)payload[0];
        if (!RoReportSpec.IsDefined(triggerType))
        {
            throw new LlrpProtocolException(
                LlrpProtocolErrorCode.InvalidParameterEncoding,
                $"ROReportSpec trigger {payload[0]} is not defined by LLRP 1.0.1.");
        }

        List<ILlrpParameter> nested = Llrp101ParameterSequence.Decode(
            _registry,
            version,
            payload[FixedFieldLength..]);
        if (nested.Count == 0
            || RoSpecGraphCodecHelpers.GetWireType(_registry, version, nested[0])
                != TagReportContentSelector.ParameterType)
        {
            ThrowInvalidSequence(forEncoding: false);
        }

        TagReportContentSelector contentSelector =
            RoSpecGraphCodecHelpers.RequireTyped<TagReportContentSelector>(
                nested[0],
                TagReportContentSelector.ParameterType,
                nameof(RoReportSpec));
        var customParameters = new List<ILlrpParameter>(Math.Max(0, nested.Count - 1));
        for (int index = 1; index < nested.Count; index++)
        {
            ILlrpParameter child = nested[index];
            if (RoSpecGraphCodecHelpers.GetWireType(_registry, version, child)
                != RawCustomParameter.CustomParameterType)
            {
                ThrowInvalidSequence(forEncoding: false);
            }

            customParameters.Add(child);
        }

        return new RoReportSpec(
            triggerType,
            BinaryPrimitives.ReadUInt16BigEndian(payload[1..]),
            contentSelector,
            customParameters);
    }

    public override int GetEncodedPayloadLength(
        LlrpProtocolVersion version,
        RoReportSpec parameter)
    {
        Llrp101CodecValidation.ValidateVersion(version);
        ValidateTriggerType(parameter.TriggerType);
        RoSpecGraphCodecHelpers.ValidateCustomParameters(
            _registry,
            version,
            parameter.CustomParameters,
            nameof(RoReportSpec));
        return checked(
            FixedFieldLength
            + _registry.GetEncodedParameterLength(version, parameter.ContentSelector)
            + Llrp101ParameterSequence.GetEncodedLength(
                _registry,
                version,
                parameter.CustomParameters));
    }

    public override int Encode(
        LlrpProtocolVersion version,
        RoReportSpec parameter,
        Span<byte> destination)
    {
        int expectedLength = GetEncodedPayloadLength(version, parameter);
        if (destination.Length != expectedLength)
        {
            throw new ArgumentException(
                $"ROReportSpec requires an exact {expectedLength}-octet payload destination.",
                nameof(destination));
        }

        destination[0] = (byte)parameter.TriggerType;
        BinaryPrimitives.WriteUInt16BigEndian(destination[1..], parameter.N);
        int offset = FixedFieldLength;
        offset += _registry.EncodeParameter(version, parameter.ContentSelector, destination[offset..]);
        offset += Llrp101ParameterSequence.Encode(
            _registry,
            version,
            parameter.CustomParameters,
            destination[offset..]);
        return offset;
    }

    private static void ValidateTriggerType(RoReportTriggerType triggerType)
    {
        if (!RoReportSpec.IsDefined(triggerType))
        {
            throw new ArgumentOutOfRangeException(
                nameof(triggerType),
                triggerType,
                "The RO report trigger must be defined by LLRP 1.0.1.");
        }
    }

    private static void ThrowInvalidSequence(bool forEncoding)
    {
        RoSpecGraphCodecHelpers.ThrowInvalidSequence(
            "ROReportSpec requires exactly one TagReportContentSelector (238), followed only by " +
            "Custom parameters (1023).",
            forEncoding);
    }
}

internal sealed class TagReportContentSelectorCodec(LlrpCodecRegistry registry)
    : LlrpParameterCodec<TagReportContentSelector>
{
    private const int FixedFieldLength = sizeof(ushort);
    private const ushort C1G2EpcMemorySelectorParameterType = 348;
    private const ushort EnableRoSpecIdMask = 0x8000;
    private const ushort EnableSpecIndexMask = 0x4000;
    private const ushort EnableInventoryParameterSpecIdMask = 0x2000;
    private const ushort EnableAntennaIdMask = 0x1000;
    private const ushort EnableChannelIndexMask = 0x0800;
    private const ushort EnablePeakRssiMask = 0x0400;
    private const ushort EnableFirstSeenTimestampMask = 0x0200;
    private const ushort EnableLastSeenTimestampMask = 0x0100;
    private const ushort EnableTagSeenCountMask = 0x0080;
    private const ushort EnableAccessSpecIdMask = 0x0040;
    private const ushort ReservedBitsMask = 0x003F;

    private readonly LlrpCodecRegistry _registry = registry ?? throw new ArgumentNullException(nameof(registry));

    public override TagReportContentSelector Decode(
        LlrpProtocolVersion version,
        ReadOnlySpan<byte> payload)
    {
        Llrp101CodecValidation.ValidateVersion(version);
        if (payload.Length < FixedFieldLength)
        {
            throw new LlrpProtocolException(
                LlrpProtocolErrorCode.TruncatedData,
                "TagReportContentSelector requires two flag octets.");
        }

        ushort flags = BinaryPrimitives.ReadUInt16BigEndian(payload);
        if ((flags & ReservedBitsMask) != 0)
        {
            throw new LlrpProtocolException(
                LlrpProtocolErrorCode.InvalidReservedBits,
                "The six reserved TagReportContentSelector bits must be zero.");
        }

        List<ILlrpParameter> selectors = Llrp101ParameterSequence.Decode(
            _registry,
            version,
            payload[FixedFieldLength..]);
        foreach (ILlrpParameter selector in selectors)
        {
            if (RoSpecGraphCodecHelpers.GetWireType(_registry, version, selector)
                != C1G2EpcMemorySelectorParameterType)
            {
                ThrowInvalidSequence(forEncoding: false);
            }
        }

        return new TagReportContentSelector(
            enableRoSpecId: (flags & EnableRoSpecIdMask) != 0,
            enableSpecIndex: (flags & EnableSpecIndexMask) != 0,
            enableInventoryParameterSpecId: (flags & EnableInventoryParameterSpecIdMask) != 0,
            enableAntennaId: (flags & EnableAntennaIdMask) != 0,
            enableChannelIndex: (flags & EnableChannelIndexMask) != 0,
            enablePeakRssi: (flags & EnablePeakRssiMask) != 0,
            enableFirstSeenTimestamp: (flags & EnableFirstSeenTimestampMask) != 0,
            enableLastSeenTimestamp: (flags & EnableLastSeenTimestampMask) != 0,
            enableTagSeenCount: (flags & EnableTagSeenCountMask) != 0,
            enableAccessSpecId: (flags & EnableAccessSpecIdMask) != 0,
            selectors);
    }

    public override int GetEncodedPayloadLength(
        LlrpProtocolVersion version,
        TagReportContentSelector parameter)
    {
        Llrp101CodecValidation.ValidateVersion(version);
        foreach (ILlrpParameter selector in parameter.AirProtocolEpcMemorySelectors)
        {
            RoSpecGraphCodecHelpers.ValidateWireType(
                _registry,
                version,
                selector,
                C1G2EpcMemorySelectorParameterType,
                nameof(TagReportContentSelector));
        }

        return checked(
            FixedFieldLength
            + Llrp101ParameterSequence.GetEncodedLength(
                _registry,
                version,
                parameter.AirProtocolEpcMemorySelectors));
    }

    public override int Encode(
        LlrpProtocolVersion version,
        TagReportContentSelector parameter,
        Span<byte> destination)
    {
        int expectedLength = GetEncodedPayloadLength(version, parameter);
        if (destination.Length != expectedLength)
        {
            throw new ArgumentException(
                $"TagReportContentSelector requires an exact {expectedLength}-octet payload destination.",
                nameof(destination));
        }

        ushort flags = 0;
        flags |= parameter.EnableRoSpecId ? EnableRoSpecIdMask : (ushort)0;
        flags |= parameter.EnableSpecIndex ? EnableSpecIndexMask : (ushort)0;
        flags |= parameter.EnableInventoryParameterSpecId
            ? EnableInventoryParameterSpecIdMask
            : (ushort)0;
        flags |= parameter.EnableAntennaId ? EnableAntennaIdMask : (ushort)0;
        flags |= parameter.EnableChannelIndex ? EnableChannelIndexMask : (ushort)0;
        flags |= parameter.EnablePeakRssi ? EnablePeakRssiMask : (ushort)0;
        flags |= parameter.EnableFirstSeenTimestamp ? EnableFirstSeenTimestampMask : (ushort)0;
        flags |= parameter.EnableLastSeenTimestamp ? EnableLastSeenTimestampMask : (ushort)0;
        flags |= parameter.EnableTagSeenCount ? EnableTagSeenCountMask : (ushort)0;
        flags |= parameter.EnableAccessSpecId ? EnableAccessSpecIdMask : (ushort)0;
        BinaryPrimitives.WriteUInt16BigEndian(destination, flags);
        int offset = FixedFieldLength;
        offset += Llrp101ParameterSequence.Encode(
            _registry,
            version,
            parameter.AirProtocolEpcMemorySelectors,
            destination[offset..]);
        return offset;
    }

    private static void ThrowInvalidSequence(bool forEncoding)
    {
        RoSpecGraphCodecHelpers.ThrowInvalidSequence(
            "TagReportContentSelector permits only AirProtocolEPCMemorySelector choices " +
            "(C1G2EPCMemorySelector type 348).",
            forEncoding);
    }
}
