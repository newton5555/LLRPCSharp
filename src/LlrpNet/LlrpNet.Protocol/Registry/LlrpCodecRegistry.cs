using System.Buffers.Binary;
using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Codecs;
using LlrpNet.Protocol.Messages;
using LlrpNet.Protocol.Parameters;

namespace LlrpNet.Protocol.Registry;

/// <summary>
/// Maps versioned standard and vendor-specific LLRP wire identities to codecs, and exact CLR types to encoders.
/// </summary>
/// <remarks>
/// Registration updates both indexes atomically. A duplicate wire key or CLR key is rejected and never
/// replaces an existing registration. CUSTOM_MESSAGE and custom-parameter metadata are owned by this
/// registry and cannot be claimed through the ordinary standard-type registration APIs.
/// </remarks>
public sealed class LlrpCodecRegistry
{
    private readonly Dictionary<MessageWireKey, MessageRegistration> _messageDecoders = [];
    private readonly Dictionary<ClrKey, MessageRegistration> _messageEncoders = [];
    private readonly Dictionary<CustomMessageWireKey, CustomMessageRegistration> _customMessageDecoders = [];
    private readonly Dictionary<ClrKey, CustomMessageRegistration> _customMessageEncoders = [];
    private readonly Dictionary<ParameterWireKey, ParameterRegistration> _parameterDecoders = [];
    private readonly Dictionary<ClrKey, ParameterRegistration> _parameterEncoders = [];
    private readonly Dictionary<CustomParameterWireKey, CustomParameterRegistration> _customParameterDecoders = [];
    private readonly Dictionary<ClrKey, CustomParameterRegistration> _customParameterEncoders = [];
    private readonly object _sync = new();

    /// <summary>
    /// Registers a codec for a message wire type and exact CLR type under one protocol version.
    /// </summary>
    /// <param name="version">The protocol version for which the mapping is valid.</param>
    /// <param name="messageType">The ten-bit message wire type.</param>
    /// <param name="codec">The codec to register.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="messageType"/> is the reserved CUSTOM_MESSAGE type 1023.
    /// </exception>
    /// <exception cref="InvalidOperationException">Either the wire key or CLR encoding key is already registered.</exception>
    public void RegisterMessage(
        LlrpProtocolVersion version,
        ushort messageType,
        ILlrpMessageCodec codec)
    {
        LlrpCodecValidation.ValidateVersion(version, nameof(version));
        LlrpCodecValidation.ValidateMessageType(messageType, nameof(messageType));

        if (messageType == RawCustomMessage.CustomMessageType)
        {
            throw new ArgumentException(
                $"Message type {RawCustomMessage.CustomMessageType} is reserved for CUSTOM_MESSAGE; " +
                $"use {nameof(RegisterCustomMessage)} instead.",
                nameof(messageType));
        }

        ArgumentNullException.ThrowIfNull(codec);
        ValidateCodecValueType(codec.ValueType, typeof(ILlrpMessage), nameof(codec));

        if (codec.ValueType == typeof(UnknownMessage) || codec.ValueType == typeof(RawCustomMessage))
        {
            throw new ArgumentException(
                $"{codec.ValueType.Name} is reserved for registry fallback and cannot be registered.",
                nameof(codec));
        }

        var registration = new MessageRegistration(version, messageType, codec);
        var wireKey = new MessageWireKey(version, messageType);
        var clrKey = new ClrKey(version, codec.ValueType);

        lock (_sync)
        {
            if (_messageDecoders.ContainsKey(wireKey))
            {
                throw new InvalidOperationException(
                    $"A message codec is already registered for version {version}, wire type {messageType}.");
            }

            if (_messageEncoders.ContainsKey(clrKey) || _customMessageEncoders.ContainsKey(clrKey))
            {
                throw new InvalidOperationException(
                    $"A message encoder is already registered for version {version}, CLR type {codec.ValueType.FullName}.");
            }

            _messageDecoders.Add(wireKey, registration);
            _messageEncoders.Add(clrKey, registration);
        }
    }

    /// <summary>
    /// Registers a typed CUSTOM_MESSAGE codec under a version, vendor identifier, and message subtype.
    /// </summary>
    /// <param name="version">The protocol version for which the mapping is valid.</param>
    /// <param name="vendorId">The IANA Private Enterprise Number carried on the wire.</param>
    /// <param name="messageSubtype">The vendor-defined one-octet message subtype.</param>
    /// <param name="codec">A codec that processes only the vendor-defined Data bytes.</param>
    /// <exception cref="InvalidOperationException">
    /// Either the custom wire key or the versioned exact-CLR encoding key is already registered.
    /// </exception>
    public void RegisterCustomMessage(
        LlrpProtocolVersion version,
        uint vendorId,
        byte messageSubtype,
        ILlrpCustomMessageCodec codec)
    {
        LlrpCodecValidation.ValidateVersion(version, nameof(version));
        ArgumentNullException.ThrowIfNull(codec);
        ValidateCodecValueType(codec.ValueType, typeof(ILlrpMessage), nameof(codec));

        if (codec.ValueType == typeof(UnknownMessage) || codec.ValueType == typeof(RawCustomMessage))
        {
            throw new ArgumentException(
                $"{codec.ValueType.Name} is reserved for registry fallback and cannot be registered.",
                nameof(codec));
        }

        var registration = new CustomMessageRegistration(
            version,
            vendorId,
            messageSubtype,
            codec);
        var wireKey = new CustomMessageWireKey(version, vendorId, messageSubtype);
        var clrKey = new ClrKey(version, codec.ValueType);

        lock (_sync)
        {
            if (_customMessageDecoders.ContainsKey(wireKey))
            {
                throw new InvalidOperationException(
                    $"A custom message codec is already registered for version {version}, " +
                    $"vendor {vendorId}, subtype {messageSubtype}.");
            }

            if (_messageEncoders.ContainsKey(clrKey) || _customMessageEncoders.ContainsKey(clrKey))
            {
                throw new InvalidOperationException(
                    $"A message encoder is already registered for version {version}, CLR type {codec.ValueType.FullName}.");
            }

            _customMessageDecoders.Add(wireKey, registration);
            _customMessageEncoders.Add(clrKey, registration);
        }
    }

    /// <summary>
    /// Registers a codec for a fixed-length TV parameter.
    /// </summary>
    /// <param name="version">The protocol version for which the mapping is valid.</param>
    /// <param name="parameterType">The seven-bit TV wire type.</param>
    /// <param name="encodedLength">The complete fixed wire length, including the one-octet TV header.</param>
    /// <param name="codec">The codec to register.</param>
    /// <exception cref="InvalidOperationException">Either the wire key or CLR encoding key is already registered.</exception>
    public void RegisterTvParameter(
        LlrpProtocolVersion version,
        byte parameterType,
        int encodedLength,
        ILlrpParameterCodec codec)
    {
        LlrpCodecValidation.ValidateTvParameterType(parameterType, nameof(parameterType));
        if (encodedLength < LlrpTvParameterHeader.EncodedLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(encodedLength),
                encodedLength,
                $"A TV parameter must include its {LlrpTvParameterHeader.EncodedLength}-octet header.");
        }

        RegisterParameter(
            version,
            parameterType,
            LlrpParameterEncoding.Tv,
            encodedLength,
            codec);
    }

    /// <summary>
    /// Registers a codec for a length-delimited TLV parameter.
    /// </summary>
    /// <param name="version">The protocol version for which the mapping is valid.</param>
    /// <param name="parameterType">The ten-bit TLV wire type.</param>
    /// <param name="codec">The codec to register.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="parameterType"/> is the reserved custom-parameter type 1023.
    /// </exception>
    /// <exception cref="InvalidOperationException">Either the wire key or CLR encoding key is already registered.</exception>
    public void RegisterTlvParameter(
        LlrpProtocolVersion version,
        ushort parameterType,
        ILlrpParameterCodec codec)
    {
        LlrpCodecValidation.ValidateTlvParameterType(parameterType, nameof(parameterType));

        if (parameterType == RawCustomParameter.CustomParameterType)
        {
            throw new ArgumentException(
                $"Parameter type {RawCustomParameter.CustomParameterType} is reserved for custom parameters; " +
                $"use {nameof(RegisterCustomParameter)} instead.",
                nameof(parameterType));
        }

        RegisterParameter(version, parameterType, LlrpParameterEncoding.Tlv, fixedEncodedLength: null, codec);
    }

    /// <summary>
    /// Registers a typed custom-parameter codec under a version, vendor identifier, and parameter subtype.
    /// </summary>
    /// <param name="version">The protocol version for which the mapping is valid.</param>
    /// <param name="vendorId">The IANA Private Enterprise Number carried on the wire.</param>
    /// <param name="parameterSubtype">The vendor-defined 32-bit parameter subtype.</param>
    /// <param name="codec">A codec that processes only the vendor-defined Data bytes.</param>
    /// <exception cref="InvalidOperationException">
    /// Either the custom wire key or the versioned exact-CLR encoding key is already registered.
    /// </exception>
    public void RegisterCustomParameter(
        LlrpProtocolVersion version,
        uint vendorId,
        uint parameterSubtype,
        ILlrpCustomParameterCodec codec)
    {
        LlrpCodecValidation.ValidateVersion(version, nameof(version));
        ArgumentNullException.ThrowIfNull(codec);
        ValidateCodecValueType(codec.ValueType, typeof(ILlrpParameter), nameof(codec));

        if (codec.ValueType == typeof(UnknownParameter) || codec.ValueType == typeof(RawCustomParameter))
        {
            throw new ArgumentException(
                $"{codec.ValueType.Name} is reserved for registry fallback and cannot be registered.",
                nameof(codec));
        }

        var registration = new CustomParameterRegistration(
            version,
            vendorId,
            parameterSubtype,
            codec);
        var wireKey = new CustomParameterWireKey(version, vendorId, parameterSubtype);
        var clrKey = new ClrKey(version, codec.ValueType);

        lock (_sync)
        {
            if (_customParameterDecoders.ContainsKey(wireKey))
            {
                throw new InvalidOperationException(
                    $"A custom parameter codec is already registered for version {version}, " +
                    $"vendor {vendorId}, subtype {parameterSubtype}.");
            }

            if (_parameterEncoders.ContainsKey(clrKey) || _customParameterEncoders.ContainsKey(clrKey))
            {
                throw new InvalidOperationException(
                    $"A parameter encoder is already registered for version {version}, CLR type {codec.ValueType.FullName}.");
            }

            _customParameterDecoders.Add(wireKey, registration);
            _customParameterEncoders.Add(clrKey, registration);
        }
    }

    /// <summary>
    /// Decodes one exact complete LLRP message frame.
    /// </summary>
    /// <param name="frame">The common header and complete payload with no trailing frame.</param>
    /// <returns>
    /// A registered strongly typed message, <see cref="UnknownMessage"/>, or <see cref="RawCustomMessage"/>.
    /// </returns>
    public ILlrpMessage DecodeMessage(ReadOnlySpan<byte> frame)
    {
        LlrpMessageHeader header = LlrpMessageHeader.Decode(frame);
        return DecodeMessage(header, frame[LlrpMessageHeader.EncodedLength..]);
    }

    /// <summary>
    /// Decodes an exact payload using its already parsed common header.
    /// </summary>
    /// <param name="header">The validated common LLRP message header.</param>
    /// <param name="payload">The complete payload with no trailing frame.</param>
    /// <returns>
    /// A registered strongly typed message, <see cref="UnknownMessage"/>, or <see cref="RawCustomMessage"/>.
    /// </returns>
    public ILlrpMessage DecodeMessage(LlrpMessageHeader header, ReadOnlySpan<byte> payload)
    {
        ValidateMessageHeaderAndPayload(header, payload.Length);

        if (header.MessageType == RawCustomMessage.CustomMessageType)
        {
            return DecodeCustomMessage(header, payload);
        }

        MessageRegistration? registration = FindMessageDecoder(header.Version, header.MessageType);
        if (registration is null)
        {
            return new UnknownMessage(header.Version, header.MessageType, header.MessageId, payload);
        }

        ILlrpMessage message = registration.Codec.Decode(header, payload);
        ValidateDecodedValue(registration.Codec.ValueType, message, "message");
        ValidateDecodedMessageId(registration.Codec, header.MessageId, message.MessageId);
        return message;
    }

    /// <summary>
    /// Calculates the complete wire length of a message using its exact CLR-type registration.
    /// </summary>
    /// <param name="version">The target protocol version.</param>
    /// <param name="message">The message to measure.</param>
    /// <returns>The common header and payload length.</returns>
    public int GetEncodedMessageLength(LlrpProtocolVersion version, ILlrpMessage message)
    {
        MessageEncoding encoding = ResolveMessageEncoding(version, message);
        return GetCompleteMessageLength(encoding.PayloadLength);
    }

    /// <summary>
    /// Encodes a complete LLRP message into a caller-provided destination.
    /// </summary>
    /// <param name="version">The target protocol version.</param>
    /// <param name="message">The message to encode.</param>
    /// <param name="destination">A destination large enough for the complete message.</param>
    /// <returns>The number of wire octets written.</returns>
    public int EncodeMessage(
        LlrpProtocolVersion version,
        ILlrpMessage message,
        Span<byte> destination)
    {
        MessageEncoding encoding = ResolveMessageEncoding(version, message);
        int completeLength = GetCompleteMessageLength(encoding.PayloadLength);
        EnsureDestination(destination, completeLength);

        var header = new LlrpMessageHeader(
            version,
            encoding.MessageType,
            (uint)completeLength,
            message.MessageId);
        header.Encode(destination);

        Span<byte> payloadDestination = destination.Slice(
            LlrpMessageHeader.EncodedLength,
            encoding.PayloadLength);

        if (encoding.RawPayload is ReadOnlyMemory<byte> rawPayload)
        {
            rawPayload.Span.CopyTo(payloadDestination);
        }
        else if (encoding.RawCustom is RawCustomMessage rawCustom)
        {
            WriteCustomMessageMetadata(
                payloadDestination,
                rawCustom.VendorId,
                rawCustom.MessageSubtype);
            rawCustom.Data.Span.CopyTo(payloadDestination[RawCustomMessage.MetadataLength..]);
        }
        else if (encoding.CustomRegistration is CustomMessageRegistration customRegistration)
        {
            WriteCustomMessageMetadata(
                payloadDestination,
                customRegistration.VendorId,
                customRegistration.MessageSubtype);
            Span<byte> dataDestination = payloadDestination[RawCustomMessage.MetadataLength..];
            int written = customRegistration.Codec.EncodeData(version, message, dataDestination);
            ValidateCodecWriteLength(customRegistration.Codec, dataDestination.Length, written);
        }
        else
        {
            int written = encoding.Codec!.Encode(version, message, payloadDestination);
            ValidateCodecWriteLength(encoding.Codec, encoding.PayloadLength, written);
        }

        return completeLength;
    }

    /// <summary>
    /// Allocates and encodes one complete LLRP message.
    /// </summary>
    /// <param name="version">The target protocol version.</param>
    /// <param name="message">The message to encode.</param>
    /// <returns>The complete wire message.</returns>
    public byte[] EncodeMessage(LlrpProtocolVersion version, ILlrpMessage message)
    {
        int length = GetEncodedMessageLength(version, message);
        var destination = new byte[length];
        EncodeMessage(version, message, destination);
        return destination;
    }

    /// <summary>
    /// Decodes one parameter from the beginning of a buffer.
    /// </summary>
    /// <param name="version">The active protocol version.</param>
    /// <param name="source">A buffer beginning with a TV or TLV parameter.</param>
    /// <returns>The decoded parameter and its exact consumed length.</returns>
    /// <exception cref="UnknownTvParameterException">
    /// The TV type is not registered, so its boundary cannot be determined.
    /// </exception>
    public LlrpParameterDecodeResult DecodeParameter(
        LlrpProtocolVersion version,
        ReadOnlySpan<byte> source)
    {
        LlrpCodecValidation.ValidateVersion(version, nameof(version));
        if (source.IsEmpty)
        {
            throw new LlrpProtocolException(
                LlrpProtocolErrorCode.TruncatedData,
                "An LLRP parameter requires at least one header octet.");
        }

        return (source[0] & 0x80) != 0
            ? DecodeTvParameter(version, source)
            : DecodeTlvParameter(version, source);
    }

    /// <summary>
    /// Calculates the complete wire length of a parameter using its exact CLR-type registration.
    /// </summary>
    /// <param name="version">The target protocol version.</param>
    /// <param name="parameter">The parameter to measure.</param>
    /// <returns>The complete TV or TLV wire length.</returns>
    public int GetEncodedParameterLength(
        LlrpProtocolVersion version,
        ILlrpParameter parameter)
    {
        ParameterEncoding encoding = ResolveParameterEncoding(version, parameter);
        return encoding.CompleteLength;
    }

    /// <summary>
    /// Resolves the complete wire identity that would be used to encode a parameter.
    /// </summary>
    /// <param name="version">The target protocol version.</param>
    /// <param name="parameter">The parameter to inspect.</param>
    /// <returns>The parameter type, header encoding, and custom metadata when applicable.</returns>
    public LlrpParameterWireIdentity GetParameterWireIdentity(
        LlrpProtocolVersion version,
        ILlrpParameter parameter)
    {
        LlrpCodecValidation.ValidateVersion(version, nameof(version));
        ArgumentNullException.ThrowIfNull(parameter);

        if (parameter is UnknownParameter unknown)
        {
            EnsureMatchingVersion(version, unknown.Version, nameof(parameter));
            return new LlrpParameterWireIdentity(
                unknown.ParameterType,
                LlrpParameterEncoding.Tlv,
                VendorId: null,
                ParameterSubtype: null);
        }

        if (parameter is RawCustomParameter rawCustom)
        {
            EnsureMatchingVersion(version, rawCustom.Version, nameof(parameter));
            return new LlrpParameterWireIdentity(
                RawCustomParameter.CustomParameterType,
                LlrpParameterEncoding.Tlv,
                rawCustom.VendorId,
                rawCustom.Subtype);
        }

        ParameterRegistration? registration = FindParameterEncoder(version, parameter.GetType());
        if (registration is not null)
        {
            return new LlrpParameterWireIdentity(
                registration.ParameterType,
                registration.Encoding,
                VendorId: null,
                ParameterSubtype: null);
        }

        CustomParameterRegistration? customRegistration = FindCustomParameterEncoder(
            version,
            parameter.GetType());
        if (customRegistration is not null)
        {
            return new LlrpParameterWireIdentity(
                RawCustomParameter.CustomParameterType,
                LlrpParameterEncoding.Tlv,
                customRegistration.VendorId,
                customRegistration.ParameterSubtype);
        }

        throw new NotSupportedException(
            $"No parameter encoder is registered for version {version}, CLR type {parameter.GetType().FullName}.");
    }

    internal ushort GetParameterWireType(
        LlrpProtocolVersion version,
        ILlrpParameter parameter) => GetParameterWireIdentity(version, parameter).ParameterType;

    /// <summary>
    /// Encodes a complete LLRP TV or TLV parameter into a caller-provided destination.
    /// </summary>
    /// <param name="version">The target protocol version.</param>
    /// <param name="parameter">The parameter to encode.</param>
    /// <param name="destination">A destination large enough for the complete parameter.</param>
    /// <returns>The number of wire octets written.</returns>
    public int EncodeParameter(
        LlrpProtocolVersion version,
        ILlrpParameter parameter,
        Span<byte> destination)
    {
        ParameterEncoding encoding = ResolveParameterEncoding(version, parameter);
        EnsureDestination(destination, encoding.CompleteLength);

        int headerLength;
        if (encoding.Encoding == LlrpParameterEncoding.Tv)
        {
            new LlrpTvParameterHeader((byte)encoding.ParameterType).Encode(destination);
            headerLength = LlrpTvParameterHeader.EncodedLength;
        }
        else
        {
            new LlrpTlvParameterHeader(encoding.ParameterType, (ushort)encoding.CompleteLength)
                .Encode(destination);
            headerLength = LlrpTlvParameterHeader.EncodedLength;
        }

        Span<byte> payloadDestination = destination.Slice(headerLength, encoding.PayloadLength);
        if (encoding.RawPayload is ReadOnlyMemory<byte> rawPayload)
        {
            rawPayload.Span.CopyTo(payloadDestination);
        }
        else if (encoding.RawCustom is RawCustomParameter rawCustom)
        {
            BinaryPrimitives.WriteUInt32BigEndian(payloadDestination, rawCustom.VendorId);
            BinaryPrimitives.WriteUInt32BigEndian(payloadDestination[sizeof(uint)..], rawCustom.Subtype);
            rawCustom.Data.Span.CopyTo(payloadDestination[RawCustomParameter.MetadataLength..]);
        }
        else if (encoding.CustomRegistration is CustomParameterRegistration customRegistration)
        {
            BinaryPrimitives.WriteUInt32BigEndian(payloadDestination, customRegistration.VendorId);
            BinaryPrimitives.WriteUInt32BigEndian(
                payloadDestination[sizeof(uint)..],
                customRegistration.ParameterSubtype);
            Span<byte> dataDestination = payloadDestination[RawCustomParameter.MetadataLength..];
            int written = customRegistration.Codec.EncodeData(version, parameter, dataDestination);
            ValidateCodecWriteLength(customRegistration.Codec, dataDestination.Length, written);
        }
        else
        {
            int written = encoding.Codec!.Encode(version, parameter, payloadDestination);
            ValidateCodecWriteLength(encoding.Codec, encoding.PayloadLength, written);
        }

        return encoding.CompleteLength;
    }

    /// <summary>
    /// Allocates and encodes one complete LLRP TV or TLV parameter.
    /// </summary>
    /// <param name="version">The target protocol version.</param>
    /// <param name="parameter">The parameter to encode.</param>
    /// <returns>The complete wire parameter.</returns>
    public byte[] EncodeParameter(LlrpProtocolVersion version, ILlrpParameter parameter)
    {
        int length = GetEncodedParameterLength(version, parameter);
        var destination = new byte[length];
        EncodeParameter(version, parameter, destination);
        return destination;
    }

    private static void ValidateCodecValueType(Type valueType, Type markerType, string parameterName)
    {
        if (valueType is null)
        {
            throw new ArgumentException("A codec must expose a non-null CLR value type.", parameterName);
        }

        if (!markerType.IsAssignableFrom(valueType))
        {
            throw new ArgumentException(
                $"Codec value type {valueType.FullName} does not implement {markerType.FullName}.",
                parameterName);
        }

        if (valueType.IsInterface || valueType.IsAbstract || valueType.ContainsGenericParameters)
        {
            throw new ArgumentException(
                $"Codec value type {valueType.FullName} must be a closed, concrete CLR type.",
                parameterName);
        }
    }

    private static void ValidateDecodedValue(Type expectedType, object value, string kind)
    {
        if (value is null || value.GetType() != expectedType)
        {
            throw new InvalidOperationException(
                $"The registered {kind} codec for {expectedType.FullName} returned " +
                $"{value?.GetType().FullName ?? "null"}.");
        }
    }

    private static void ValidateDecodedMessageId(object codec, uint expected, uint actual)
    {
        if (actual != expected)
        {
            throw new InvalidOperationException(
                $"Codec {codec.GetType().FullName} returned MessageId {actual}; " +
                $"expected wire MessageId {expected}.");
        }
    }

    private static void ValidateMessageHeaderAndPayload(LlrpMessageHeader header, int payloadLength)
    {
        LlrpCodecValidation.ValidateVersion(header.Version, nameof(header));
        LlrpCodecValidation.ValidateMessageType(header.MessageType, nameof(header));
        if (header.MessageLength < LlrpMessageHeader.EncodedLength)
        {
            throw new LlrpProtocolException(
                LlrpProtocolErrorCode.InvalidMessageLength,
                $"The encoded message length {header.MessageLength} is smaller than the common header.");
        }

        uint encodedPayloadLength = header.MessageLength - LlrpMessageHeader.EncodedLength;
        if ((ulong)payloadLength < encodedPayloadLength)
        {
            throw new LlrpProtocolException(
                LlrpProtocolErrorCode.TruncatedData,
                $"The message header declares {encodedPayloadLength} payload octets, but only {payloadLength} are available.");
        }

        if ((ulong)payloadLength > encodedPayloadLength)
        {
            throw new LlrpProtocolException(
                LlrpProtocolErrorCode.InvalidMessageLength,
                $"The message header declares {encodedPayloadLength} payload octets, but {payloadLength} were supplied.");
        }
    }

    private static int GetCompleteMessageLength(int payloadLength)
    {
        if (payloadLength < 0)
        {
            throw new InvalidOperationException($"A message codec reported a negative payload length of {payloadLength}.");
        }

        long completeLength = (long)LlrpMessageHeader.EncodedLength + payloadLength;
        if (completeLength > int.MaxValue)
        {
            throw new InvalidOperationException("The encoded message is too large for a contiguous CLR buffer.");
        }

        return (int)completeLength;
    }

    private static void EnsureDestination(Span<byte> destination, int requiredLength)
    {
        if (destination.Length < requiredLength)
        {
            throw new ArgumentException(
                $"The destination requires {requiredLength} octets, but only {destination.Length} are available.",
                nameof(destination));
        }
    }

    private static void ValidateCodecWriteLength(object codec, int expected, int actual)
    {
        if (actual != expected)
        {
            throw new InvalidOperationException(
                $"Codec {codec.GetType().FullName} reported {actual} written octets; exactly {expected} were required.");
        }
    }

    private void RegisterParameter(
        LlrpProtocolVersion version,
        ushort parameterType,
        LlrpParameterEncoding encoding,
        int? fixedEncodedLength,
        ILlrpParameterCodec codec)
    {
        LlrpCodecValidation.ValidateVersion(version, nameof(version));
        ArgumentNullException.ThrowIfNull(codec);
        ValidateCodecValueType(codec.ValueType, typeof(ILlrpParameter), nameof(codec));

        if (encoding == LlrpParameterEncoding.Tlv && parameterType == RawCustomParameter.CustomParameterType)
        {
            throw new ArgumentException(
                $"Parameter type {RawCustomParameter.CustomParameterType} must be registered with " +
                $"{nameof(RegisterCustomParameter)}.",
                nameof(parameterType));
        }

        if (codec.ValueType == typeof(UnknownParameter) || codec.ValueType == typeof(RawCustomParameter))
        {
            throw new ArgumentException(
                $"{codec.ValueType.Name} is reserved for registry fallback and cannot be registered.",
                nameof(codec));
        }

        var registration = new ParameterRegistration(
            version,
            parameterType,
            encoding,
            fixedEncodedLength,
            codec);
        var wireKey = new ParameterWireKey(version, encoding, parameterType);
        var clrKey = new ClrKey(version, codec.ValueType);

        lock (_sync)
        {
            if (_parameterDecoders.ContainsKey(wireKey))
            {
                throw new InvalidOperationException(
                    $"A parameter codec is already registered for version {version}, {encoding} wire type {parameterType}.");
            }

            if (_parameterEncoders.ContainsKey(clrKey) || _customParameterEncoders.ContainsKey(clrKey))
            {
                throw new InvalidOperationException(
                    $"A parameter encoder is already registered for version {version}, CLR type {codec.ValueType.FullName}.");
            }

            _parameterDecoders.Add(wireKey, registration);
            _parameterEncoders.Add(clrKey, registration);
        }
    }

    private ILlrpMessage DecodeCustomMessage(
        LlrpMessageHeader header,
        ReadOnlySpan<byte> payload)
    {
        if (payload.Length < RawCustomMessage.MetadataLength)
        {
            throw new LlrpProtocolException(
                LlrpProtocolErrorCode.InvalidMessageLength,
                $"CUSTOM_MESSAGE requires at least {RawCustomMessage.MetadataLength} payload octets, " +
                $"but only {payload.Length} were supplied.");
        }

        uint vendorId = BinaryPrimitives.ReadUInt32BigEndian(payload);
        byte messageSubtype = payload[sizeof(uint)];
        ReadOnlySpan<byte> data = payload[RawCustomMessage.MetadataLength..];
        CustomMessageRegistration? registration = FindCustomMessageDecoder(
            header.Version,
            vendorId,
            messageSubtype);
        if (registration is null)
        {
            return new RawCustomMessage(
                header.Version,
                header.MessageId,
                vendorId,
                messageSubtype,
                data);
        }

        ILlrpMessage message = registration.Codec.Decode(header.Version, header.MessageId, data);
        ValidateDecodedValue(registration.Codec.ValueType, message, "custom message");
        ValidateDecodedMessageId(registration.Codec, header.MessageId, message.MessageId);
        return message;
    }

    private static void WriteCustomMessageMetadata(
        Span<byte> destination,
        uint vendorId,
        byte messageSubtype)
    {
        BinaryPrimitives.WriteUInt32BigEndian(destination, vendorId);
        destination[sizeof(uint)] = messageSubtype;
    }

    private LlrpParameterDecodeResult DecodeTvParameter(
        LlrpProtocolVersion version,
        ReadOnlySpan<byte> source)
    {
        LlrpTvParameterHeader header = LlrpTvParameterHeader.Decode(source);
        ParameterRegistration? registration = FindParameterDecoder(
            version,
            LlrpParameterEncoding.Tv,
            header.ParameterType);
        if (registration is null)
        {
            throw new UnknownTvParameterException(version, header.ParameterType);
        }

        int encodedLength = registration.FixedEncodedLength!.Value;
        EnsureAvailableParameterBytes(source, encodedLength, LlrpParameterEncoding.Tv, header.ParameterType);
        ReadOnlySpan<byte> payload = source.Slice(
            LlrpTvParameterHeader.EncodedLength,
            encodedLength - LlrpTvParameterHeader.EncodedLength);
        ILlrpParameter parameter = registration.Codec.Decode(version, payload);
        ValidateDecodedValue(registration.Codec.ValueType, parameter, "parameter");
        return new LlrpParameterDecodeResult(parameter, encodedLength);
    }

    private LlrpParameterDecodeResult DecodeTlvParameter(
        LlrpProtocolVersion version,
        ReadOnlySpan<byte> source)
    {
        LlrpTlvParameterHeader header = LlrpTlvParameterHeader.Decode(source);
        EnsureAvailableParameterBytes(
            source,
            header.ParameterLength,
            LlrpParameterEncoding.Tlv,
            header.ParameterType);

        ReadOnlySpan<byte> payload = source.Slice(
            LlrpTlvParameterHeader.EncodedLength,
            header.ParameterLength - LlrpTlvParameterHeader.EncodedLength);
        if (header.ParameterType == RawCustomParameter.CustomParameterType)
        {
            if (payload.Length < RawCustomParameter.MetadataLength)
            {
                throw new LlrpProtocolException(
                    LlrpProtocolErrorCode.InvalidParameterLength,
                    $"A custom parameter requires at least {RawCustomParameter.MetadataLength} value octets, " +
                    $"but its TLV length provides only {payload.Length}.");
            }

            uint vendorId = BinaryPrimitives.ReadUInt32BigEndian(payload);
            uint subtype = BinaryPrimitives.ReadUInt32BigEndian(payload[sizeof(uint)..]);
            ReadOnlySpan<byte> data = payload[RawCustomParameter.MetadataLength..];
            CustomParameterRegistration? customRegistration = FindCustomParameterDecoder(
                version,
                vendorId,
                subtype);
            if (customRegistration is not null)
            {
                ILlrpParameter parameter = customRegistration.Codec.Decode(version, data);
                ValidateDecodedValue(customRegistration.Codec.ValueType, parameter, "custom parameter");
                return new LlrpParameterDecodeResult(parameter, header.ParameterLength);
            }

            var rawCustom = new RawCustomParameter(
                version,
                vendorId,
                subtype,
                data);
            return new LlrpParameterDecodeResult(rawCustom, header.ParameterLength);
        }

        ParameterRegistration? registration = FindParameterDecoder(
            version,
            LlrpParameterEncoding.Tlv,
            header.ParameterType);
        if (registration is not null)
        {
            ILlrpParameter parameter = registration.Codec.Decode(version, payload);
            ValidateDecodedValue(registration.Codec.ValueType, parameter, "parameter");
            return new LlrpParameterDecodeResult(parameter, header.ParameterLength);
        }

        var unknown = new UnknownParameter(version, header.ParameterType, payload);
        return new LlrpParameterDecodeResult(unknown, header.ParameterLength);
    }

    private static void EnsureAvailableParameterBytes(
        ReadOnlySpan<byte> source,
        int encodedLength,
        LlrpParameterEncoding encoding,
        ushort parameterType)
    {
        if (source.Length < encodedLength)
        {
            throw new LlrpProtocolException(
                LlrpProtocolErrorCode.TruncatedData,
                $"The {encoding} parameter type {parameterType} requires {encodedLength} octets, " +
                $"but only {source.Length} are available.");
        }
    }

    private MessageEncoding ResolveMessageEncoding(
        LlrpProtocolVersion version,
        ILlrpMessage message)
    {
        LlrpCodecValidation.ValidateVersion(version, nameof(version));
        ArgumentNullException.ThrowIfNull(message);

        if (message is RawCustomMessage rawCustom)
        {
            EnsureMatchingVersion(version, rawCustom.Version, nameof(message));
            int rawCustomPayloadLength = checked(RawCustomMessage.MetadataLength + rawCustom.Data.Length);
            return new MessageEncoding(
                RawCustomMessage.CustomMessageType,
                rawCustomPayloadLength,
                Codec: null,
                RawPayload: null,
                RawCustom: rawCustom,
                CustomRegistration: null);
        }

        if (message is UnknownMessage unknown)
        {
            EnsureMatchingVersion(version, unknown.Version, nameof(message));
            return new MessageEncoding(
                unknown.MessageType,
                unknown.Payload.Length,
                Codec: null,
                RawPayload: unknown.Payload,
                RawCustom: null,
                CustomRegistration: null);
        }

        MessageRegistration? registration = FindMessageEncoder(version, message.GetType());
        if (registration is not null)
        {
            int payloadLength = registration.Codec.GetEncodedPayloadLength(version, message);
            if (payloadLength < 0)
            {
                throw new InvalidOperationException(
                    $"Codec {registration.Codec.GetType().FullName} reported a negative payload length of {payloadLength}.");
            }

            return new MessageEncoding(
                registration.MessageType,
                payloadLength,
                registration.Codec,
                RawPayload: null,
                RawCustom: null,
                CustomRegistration: null);
        }

        CustomMessageRegistration? customRegistration = FindCustomMessageEncoder(
            version,
            message.GetType());
        if (customRegistration is null)
        {
            throw new NotSupportedException(
                $"No message encoder is registered for version {version}, CLR type {message.GetType().FullName}.");
        }

        int dataLength = customRegistration.Codec.GetEncodedDataLength(version, message);
        if (dataLength < 0)
        {
            throw new InvalidOperationException(
                $"Custom codec {customRegistration.Codec.GetType().FullName} reported a negative Data length of {dataLength}.");
        }

        int customPayloadLength = checked(RawCustomMessage.MetadataLength + dataLength);
        return new MessageEncoding(
            RawCustomMessage.CustomMessageType,
            customPayloadLength,
            Codec: null,
            RawPayload: null,
            RawCustom: null,
            CustomRegistration: customRegistration);
    }

    private ParameterEncoding ResolveParameterEncoding(
        LlrpProtocolVersion version,
        ILlrpParameter parameter)
    {
        LlrpCodecValidation.ValidateVersion(version, nameof(version));
        ArgumentNullException.ThrowIfNull(parameter);

        if (parameter is UnknownParameter unknown)
        {
            EnsureMatchingVersion(version, unknown.Version, nameof(parameter));
            int rawCompleteLength = checked(LlrpTlvParameterHeader.EncodedLength + unknown.Data.Length);
            return new ParameterEncoding(
                unknown.ParameterType,
                LlrpParameterEncoding.Tlv,
                unknown.Data.Length,
                rawCompleteLength,
                Codec: null,
                RawPayload: unknown.Data,
                RawCustom: null,
                CustomRegistration: null);
        }

        if (parameter is RawCustomParameter rawCustom)
        {
            EnsureMatchingVersion(version, rawCustom.Version, nameof(parameter));
            int rawCustomPayloadLength = checked(RawCustomParameter.MetadataLength + rawCustom.Data.Length);
            int rawCustomCompleteLength = checked(LlrpTlvParameterHeader.EncodedLength + rawCustomPayloadLength);
            return new ParameterEncoding(
                RawCustomParameter.CustomParameterType,
                LlrpParameterEncoding.Tlv,
                rawCustomPayloadLength,
                rawCustomCompleteLength,
                Codec: null,
                RawPayload: null,
                RawCustom: rawCustom,
                CustomRegistration: null);
        }

        ParameterRegistration? registration = FindParameterEncoder(version, parameter.GetType());
        if (registration is not null)
        {
            int payloadLength = registration.Codec.GetEncodedPayloadLength(version, parameter);
            if (payloadLength < 0)
            {
                throw new InvalidOperationException(
                    $"Codec {registration.Codec.GetType().FullName} reported a negative payload length of {payloadLength}.");
            }

            int completeLength;
            if (registration.Encoding == LlrpParameterEncoding.Tv)
            {
                completeLength = registration.FixedEncodedLength!.Value;
                int expectedPayloadLength = completeLength - LlrpTvParameterHeader.EncodedLength;
                if (payloadLength != expectedPayloadLength)
                {
                    throw new InvalidOperationException(
                        $"TV codec {registration.Codec.GetType().FullName} reported {payloadLength} payload octets; " +
                        $"its registered fixed length requires exactly {expectedPayloadLength}.");
                }
            }
            else
            {
                LlrpCodecValidation.ValidateTlvPayloadLength(payloadLength, nameof(parameter));
                completeLength = LlrpTlvParameterHeader.EncodedLength + payloadLength;
            }

            return new ParameterEncoding(
                registration.ParameterType,
                registration.Encoding,
                payloadLength,
                completeLength,
                registration.Codec,
                RawPayload: null,
                RawCustom: null,
                CustomRegistration: null);
        }

        CustomParameterRegistration? customRegistration = FindCustomParameterEncoder(
            version,
            parameter.GetType());
        if (customRegistration is null)
        {
            throw new NotSupportedException(
                $"No parameter encoder is registered for version {version}, CLR type {parameter.GetType().FullName}.");
        }

        int dataLength = customRegistration.Codec.GetEncodedDataLength(version, parameter);
        if (dataLength < 0)
        {
            throw new InvalidOperationException(
                $"Custom codec {customRegistration.Codec.GetType().FullName} reported a negative Data length of {dataLength}.");
        }

        int customPayloadLength = checked(RawCustomParameter.MetadataLength + dataLength);
        LlrpCodecValidation.ValidateTlvPayloadLength(customPayloadLength, nameof(parameter));
        int customCompleteLength = LlrpTlvParameterHeader.EncodedLength + customPayloadLength;
        return new ParameterEncoding(
            RawCustomParameter.CustomParameterType,
            LlrpParameterEncoding.Tlv,
            customPayloadLength,
            customCompleteLength,
            Codec: null,
            RawPayload: null,
            RawCustom: null,
            CustomRegistration: customRegistration);
    }

    private static void EnsureMatchingVersion(
        LlrpProtocolVersion requested,
        LlrpProtocolVersion preserved,
        string parameterName)
    {
        if (requested != preserved)
        {
            throw new ArgumentException(
                $"Raw wire data preserved for version {preserved} cannot be encoded as version {requested}.",
                parameterName);
        }
    }

    private MessageRegistration? FindMessageDecoder(LlrpProtocolVersion version, ushort messageType)
    {
        lock (_sync)
        {
            _messageDecoders.TryGetValue(new MessageWireKey(version, messageType), out MessageRegistration? result);
            return result;
        }
    }

    private MessageRegistration? FindMessageEncoder(LlrpProtocolVersion version, Type valueType)
    {
        lock (_sync)
        {
            _messageEncoders.TryGetValue(new ClrKey(version, valueType), out MessageRegistration? result);
            return result;
        }
    }

    private CustomMessageRegistration? FindCustomMessageDecoder(
        LlrpProtocolVersion version,
        uint vendorId,
        byte messageSubtype)
    {
        lock (_sync)
        {
            _customMessageDecoders.TryGetValue(
                new CustomMessageWireKey(version, vendorId, messageSubtype),
                out CustomMessageRegistration? result);
            return result;
        }
    }

    private CustomMessageRegistration? FindCustomMessageEncoder(
        LlrpProtocolVersion version,
        Type valueType)
    {
        lock (_sync)
        {
            _customMessageEncoders.TryGetValue(
                new ClrKey(version, valueType),
                out CustomMessageRegistration? result);
            return result;
        }
    }

    private ParameterRegistration? FindParameterDecoder(
        LlrpProtocolVersion version,
        LlrpParameterEncoding encoding,
        ushort parameterType)
    {
        lock (_sync)
        {
            _parameterDecoders.TryGetValue(
                new ParameterWireKey(version, encoding, parameterType),
                out ParameterRegistration? result);
            return result;
        }
    }

    private ParameterRegistration? FindParameterEncoder(LlrpProtocolVersion version, Type valueType)
    {
        lock (_sync)
        {
            _parameterEncoders.TryGetValue(new ClrKey(version, valueType), out ParameterRegistration? result);
            return result;
        }
    }

    private CustomParameterRegistration? FindCustomParameterDecoder(
        LlrpProtocolVersion version,
        uint vendorId,
        uint parameterSubtype)
    {
        lock (_sync)
        {
            _customParameterDecoders.TryGetValue(
                new CustomParameterWireKey(version, vendorId, parameterSubtype),
                out CustomParameterRegistration? result);
            return result;
        }
    }

    private CustomParameterRegistration? FindCustomParameterEncoder(
        LlrpProtocolVersion version,
        Type valueType)
    {
        lock (_sync)
        {
            _customParameterEncoders.TryGetValue(
                new ClrKey(version, valueType),
                out CustomParameterRegistration? result);
            return result;
        }
    }

    private readonly record struct MessageWireKey(
        LlrpProtocolVersion Version,
        ushort MessageType);

    private readonly record struct CustomMessageWireKey(
        LlrpProtocolVersion Version,
        uint VendorId,
        byte MessageSubtype);

    private readonly record struct ParameterWireKey(
        LlrpProtocolVersion Version,
        LlrpParameterEncoding Encoding,
        ushort ParameterType);

    private readonly record struct CustomParameterWireKey(
        LlrpProtocolVersion Version,
        uint VendorId,
        uint ParameterSubtype);

    private readonly record struct ClrKey(
        LlrpProtocolVersion Version,
        Type ValueType);

    private sealed record MessageRegistration(
        LlrpProtocolVersion Version,
        ushort MessageType,
        ILlrpMessageCodec Codec);

    private sealed record CustomMessageRegistration(
        LlrpProtocolVersion Version,
        uint VendorId,
        byte MessageSubtype,
        ILlrpCustomMessageCodec Codec);

    private sealed record ParameterRegistration(
        LlrpProtocolVersion Version,
        ushort ParameterType,
        LlrpParameterEncoding Encoding,
        int? FixedEncodedLength,
        ILlrpParameterCodec Codec);

    private sealed record CustomParameterRegistration(
        LlrpProtocolVersion Version,
        uint VendorId,
        uint ParameterSubtype,
        ILlrpCustomParameterCodec Codec);

    private readonly record struct MessageEncoding(
        ushort MessageType,
        int PayloadLength,
        ILlrpMessageCodec? Codec,
        ReadOnlyMemory<byte>? RawPayload,
        RawCustomMessage? RawCustom,
        CustomMessageRegistration? CustomRegistration);

    private readonly record struct ParameterEncoding(
        ushort ParameterType,
        LlrpParameterEncoding Encoding,
        int PayloadLength,
        int CompleteLength,
        ILlrpParameterCodec? Codec,
        ReadOnlyMemory<byte>? RawPayload,
        RawCustomParameter? RawCustom,
        CustomParameterRegistration? CustomRegistration);
}
