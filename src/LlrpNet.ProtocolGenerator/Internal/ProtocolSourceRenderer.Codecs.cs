using System.Globalization;
using LlrpNet.ProtocolGenerator.Generation;
using LlrpNet.ProtocolModel.Definitions;

namespace LlrpNet.ProtocolGenerator.Internal;

internal sealed partial class ProtocolSourceRenderer
{
    public IReadOnlyList<GeneratedSourceFile> RenderCodecs(
        ProtocolDefinition definition,
        byte protocolVersionValue,
        string? registryModuleName)
    {
        var sources = new List<GeneratedSourceFile>
        {
            new(
                $"Codecs/{versionNamespace}/0000.GeneratedCodecRuntime.g.cs",
                GeneratedSourceKind.CodecRuntime,
                ProtocolCodecRuntimeTemplate.Render(rootNamespace, versionNamespace)),
        };

        int index = 1;
        foreach (ParameterDefinition parameter in definition.Parameters)
        {
            sources.Add(RenderParameterCodec(parameter, index, protocolVersionValue));
            index++;
        }

        foreach (CustomParameterDefinition parameter in definition.CustomParameters)
        {
            sources.Add(RenderCustomParameterCodec(parameter, index, protocolVersionValue));
            index++;
        }

        foreach (MessageDefinition message in definition.Messages)
        {
            sources.Add(RenderMessageCodec(message, index, protocolVersionValue));
            index++;
        }

        foreach (CustomMessageDefinition message in definition.CustomMessages)
        {
            sources.Add(RenderCustomMessageCodec(message, index, protocolVersionValue));
            index++;
        }

        sources.Add(RenderRegistryModule(definition, protocolVersionValue, registryModuleName));
        return sources;
    }

    private GeneratedSourceFile RenderMessageCodec(
        MessageDefinition message,
        int index,
        byte protocolVersionValue)
    {
        string modelIdentifier = symbols.GetMessage(message.Name);
        string modelType = Qualify("Messages", modelIdentifier);
        string codecIdentifier = symbols.GetMessageCodec(message.Name);
        var allocator = new CSharpIdentifierAllocator(
            [modelIdentifier, "MessageId", "TypeNumber", "MessageType"]);
        List<GeneratedProperty> properties = CreateProperties(message.Members, allocator);
        var writer = CreateWriter("Codecs");
        WriteCodecClassStart(
            writer,
            codecIdentifier,
            $"global::LlrpNet.Protocol.Codecs.LlrpMessageCodec<{modelType}>");
        WriteStandardMessageDecode(writer, modelType, message.Members, properties, protocolVersionValue);
        WriteLengthMethod(
            writer,
            "GetEncodedPayloadLength",
            modelType,
            "message",
            message.Members,
            properties,
            protocolVersionValue);
        WriteEncodeMethod(
            writer,
            "Encode",
            modelType,
            "message",
            message.Members,
            properties,
            protocolVersionValue);
        writer.WriteLine("}");
        return CreateSource("Codecs", index, codecIdentifier, GeneratedSourceKind.Codec, writer);
    }

    private GeneratedSourceFile RenderCustomMessageCodec(
        CustomMessageDefinition message,
        int index,
        byte protocolVersionValue)
    {
        string modelIdentifier = symbols.GetMessage(message.Name);
        string modelType = Qualify("Messages", modelIdentifier);
        string codecIdentifier = symbols.GetMessageCodec(message.Name);
        var allocator = new CSharpIdentifierAllocator(
            [modelIdentifier, "MessageId", "TypeNumber", "MessageType", "VendorIdentifier", "Subtype"]);
        List<GeneratedProperty> properties = CreateProperties(message.Members, allocator);
        var writer = CreateWriter("Codecs");
        WriteCodecClassStart(
            writer,
            codecIdentifier,
            $"global::LlrpNet.Protocol.Codecs.LlrpCustomMessageCodec<{modelType}>");
        writer.WriteLine($"public override {modelType} Decode(");
        using (writer.Indent())
        {
            writer.WriteLine("global::LlrpNet.Core.Protocol.LlrpProtocolVersion version,");
            writer.WriteLine("uint messageId,");
            writer.WriteLine("global::System.ReadOnlySpan<byte> data)");
        }

        writer.WriteLine("{");
        using (writer.Indent())
        {
            writer.WriteLine($"GeneratedCodecRuntime.ValidateVersion(version, {protocolVersionValue});");
            WriteDecodeBody(writer, modelType, message.Members, properties, "data", "messageId");
        }

        writer.WriteLine("}");
        writer.WriteLine();
        WriteLengthMethod(
            writer,
            "GetEncodedDataLength",
            modelType,
            "message",
            message.Members,
            properties,
            protocolVersionValue);
        WriteEncodeMethod(
            writer,
            "EncodeData",
            modelType,
            "message",
            message.Members,
            properties,
            protocolVersionValue);
        writer.WriteLine("}");
        return CreateSource("Codecs", index, codecIdentifier, GeneratedSourceKind.Codec, writer);
    }

    private GeneratedSourceFile RenderParameterCodec(
        ParameterDefinition parameter,
        int index,
        byte protocolVersionValue)
    {
        string modelIdentifier = symbols.GetParameter(parameter.Name);
        string modelType = Qualify("Parameters", modelIdentifier);
        string codecIdentifier = symbols.GetParameterCodec(parameter.Name);
        var allocator = new CSharpIdentifierAllocator([modelIdentifier, "TypeNumber", "ParameterType"]);
        List<GeneratedProperty> properties = CreateProperties(parameter.Members, allocator);
        var writer = CreateWriter("Codecs");
        WriteCodecClassStart(
            writer,
            codecIdentifier,
            $"global::LlrpNet.Protocol.Codecs.LlrpParameterCodec<{modelType}>");
        WriteParameterDecode(
            writer,
            modelType,
            parameter.Members,
            properties,
            protocolVersionValue,
            isCustom: false);
        WriteLengthMethod(
            writer,
            "GetEncodedPayloadLength",
            modelType,
            "parameter",
            parameter.Members,
            properties,
            protocolVersionValue);
        WriteEncodeMethod(
            writer,
            "Encode",
            modelType,
            "parameter",
            parameter.Members,
            properties,
            protocolVersionValue);
        writer.WriteLine("}");
        return CreateSource("Codecs", index, codecIdentifier, GeneratedSourceKind.Codec, writer);
    }

    private GeneratedSourceFile RenderCustomParameterCodec(
        CustomParameterDefinition parameter,
        int index,
        byte protocolVersionValue)
    {
        string modelIdentifier = symbols.GetParameter(parameter.Name);
        string modelType = Qualify("Parameters", modelIdentifier);
        string codecIdentifier = symbols.GetParameterCodec(parameter.Name);
        var allocator = new CSharpIdentifierAllocator([modelIdentifier, "TypeNumber", "ParameterType", "VendorIdentifier", "Subtype"]);
        List<GeneratedProperty> properties = CreateProperties(parameter.Members, allocator);
        var writer = CreateWriter("Codecs");
        WriteCodecClassStart(
            writer,
            codecIdentifier,
            $"global::LlrpNet.Protocol.Codecs.LlrpCustomParameterCodec<{modelType}>");
        WriteParameterDecode(
            writer,
            modelType,
            parameter.Members,
            properties,
            protocolVersionValue,
            isCustom: true);
        WriteLengthMethod(
            writer,
            "GetEncodedDataLength",
            modelType,
            "parameter",
            parameter.Members,
            properties,
            protocolVersionValue);
        WriteEncodeMethod(
            writer,
            "EncodeData",
            modelType,
            "parameter",
            parameter.Members,
            properties,
            protocolVersionValue);
        writer.WriteLine("}");
        return CreateSource("Codecs", index, codecIdentifier, GeneratedSourceKind.Codec, writer);
    }

    private void WriteCodecClassStart(CodeWriter writer, string codecIdentifier, string baseType)
    {
        writer.WriteLine($"internal sealed class {codecIdentifier} : {baseType}");
        writer.WriteLine("{");
        using (writer.Indent())
        {
            writer.WriteLine("private readonly global::LlrpNet.Protocol.Registry.LlrpCodecRegistry registry;");
            writer.WriteLine();
            writer.WriteLine($"public {codecIdentifier}(global::LlrpNet.Protocol.Registry.LlrpCodecRegistry registry)");
            writer.WriteLine("{");
            using (writer.Indent())
            {
                writer.WriteLine("global::System.ArgumentNullException.ThrowIfNull(registry);");
                writer.WriteLine("this.registry = registry;");
            }

            writer.WriteLine("}");
            writer.WriteLine();
        }
    }

    private void WriteStandardMessageDecode(
        CodeWriter writer,
        string modelType,
        IReadOnlyList<ProtocolMemberDefinition> members,
        IReadOnlyList<GeneratedProperty> properties,
        byte protocolVersionValue)
    {
        using (writer.Indent())
        {
            writer.WriteLine($"public override {modelType} Decode(");
            using (writer.Indent())
            {
                writer.WriteLine("global::LlrpNet.Core.Protocol.LlrpMessageHeader header,");
                writer.WriteLine("global::System.ReadOnlySpan<byte> payload)");
            }

            writer.WriteLine("{");
            using (writer.Indent())
            {
                writer.WriteLine($"GeneratedCodecRuntime.ValidateVersion(header.Version, {protocolVersionValue});");
                writer.WriteLine("var version = header.Version;");
                WriteDecodeBody(writer, modelType, members, properties, "payload", "header.MessageId");
            }

            writer.WriteLine("}");
            writer.WriteLine();
        }
    }

    private void WriteParameterDecode(
        CodeWriter writer,
        string modelType,
        IReadOnlyList<ProtocolMemberDefinition> members,
        IReadOnlyList<GeneratedProperty> properties,
        byte protocolVersionValue,
        bool isCustom)
    {
        string sourceName = isCustom ? "data" : "payload";
        using (writer.Indent())
        {
            writer.WriteLine($"public override {modelType} Decode(");
            using (writer.Indent())
            {
                writer.WriteLine("global::LlrpNet.Core.Protocol.LlrpProtocolVersion version,");
                writer.WriteLine($"global::System.ReadOnlySpan<byte> {sourceName})");
            }

            writer.WriteLine("{");
            using (writer.Indent())
            {
                writer.WriteLine($"GeneratedCodecRuntime.ValidateVersion(version, {protocolVersionValue});");
                WriteDecodeBody(writer, modelType, members, properties, sourceName, messageIdExpression: null);
            }

            writer.WriteLine("}");
            writer.WriteLine();
        }
    }

    private void WriteDecodeBody(
        CodeWriter writer,
        string modelType,
        IReadOnlyList<ProtocolMemberDefinition> members,
        IReadOnlyList<GeneratedProperty> properties,
        string sourceName,
        string? messageIdExpression)
    {
        writer.WriteLine($"var reader = new GeneratedWireReader({sourceName});");
        writer.WriteLine("int offset = 0;");
        int propertyIndex = 0;
        bool hasFieldSegment = false;
        foreach (ProtocolMemberDefinition member in members)
        {
            switch (member)
            {
                case FieldDefinition field:
                    GeneratedProperty fieldProperty = properties[propertyIndex++];
                    writer.WriteLine(
                        $"{fieldProperty.Type} {fieldProperty.Name} = {GetFieldDecodeExpression(field, fieldProperty.Type)};");
                    hasFieldSegment = true;
                    break;

                case ReservedBitsDefinition reserved:
                    writer.WriteLine($"reader.ReadReservedBits({reserved.BitCount.ToString(CultureInfo.InvariantCulture)});");
                    hasFieldSegment = true;
                    break;

                case ParameterReferenceDefinition parameter:
                    FlushDecodeFieldSegment(writer, sourceName, ref hasFieldSegment);
                    GeneratedProperty parameterProperty = properties[propertyIndex++];
                    WriteReferenceDecode(
                        writer,
                        parameter,
                        parameterProperty,
                        sourceName,
                        isChoice: false);
                    writer.WriteLine($"reader = new GeneratedWireReader({sourceName}[offset..]);");
                    break;

                case ChoiceReferenceDefinition choice:
                    FlushDecodeFieldSegment(writer, sourceName, ref hasFieldSegment);
                    GeneratedProperty choiceProperty = properties[propertyIndex++];
                    WriteReferenceDecode(
                        writer,
                        choice,
                        choiceProperty,
                        sourceName,
                        isChoice: true);
                    writer.WriteLine($"reader = new GeneratedWireReader({sourceName}[offset..]);");
                    break;
            }
        }

        FlushDecodeFieldSegment(writer, sourceName, ref hasFieldSegment);
        writer.WriteLine($"GeneratedCodecRuntime.ValidateDecodedEnd(offset, {sourceName}.Length);");
        var arguments = new List<string>();
        if (messageIdExpression is not null)
        {
            arguments.Add(messageIdExpression);
        }

        arguments.AddRange(properties.Select(GetDecodedConstructorArgument));
        WriteConstructorReturn(writer, modelType, arguments);
    }

    private void WriteReferenceDecode(
        CodeWriter writer,
        ProtocolMemberDefinition member,
        GeneratedProperty property,
        string sourceName,
        bool isChoice)
    {
        Cardinality cardinality = member switch
        {
            ParameterReferenceDefinition parameter => parameter.Cardinality,
            ChoiceReferenceDefinition choice => choice.Cardinality,
            _ => throw new InvalidOperationException("A generated reference must be a parameter or choice."),
        };
        string elementType = GetReferenceElementType(member);
        string matchExpression = GetReferenceMatchExpression(member, sourceName);
        bool repeated = cardinality.Maximum is null or > 1;
        if (repeated)
        {
            writer.WriteLine($"var {property.Name} = new global::System.Collections.Generic.List<{elementType}>();");
            string maximumCondition = cardinality.Maximum is int maximum
                ? $" && {property.Name}.Count < {maximum.ToString(CultureInfo.InvariantCulture)}"
                : string.Empty;
            writer.WriteLine(
                $"while (offset < {sourceName}.Length{maximumCondition} && {matchExpression})");
            writer.WriteLine("{");
            using (writer.Indent())
            {
                writer.WriteLine(
                    $"{property.Name}.Add(GeneratedCodecRuntime.DecodeParameter<{elementType}>(registry, version, {sourceName}, ref offset));");
            }

            writer.WriteLine("}");
            writer.WriteLine(
                $"GeneratedCodecRuntime.ValidateRequiredCount({property.Name}.Count, {cardinality.Minimum.ToString(CultureInfo.InvariantCulture)}, \"{EscapeStringLiteral(property.Name)}\");");
            return;
        }

        writer.WriteLine($"{elementType}? {property.Name} = null;");
        writer.WriteLine($"if (offset < {sourceName}.Length && {matchExpression})");
        writer.WriteLine("{");
        using (writer.Indent())
        {
            writer.WriteLine(
                $"{property.Name} = GeneratedCodecRuntime.DecodeParameter<{elementType}>(registry, version, {sourceName}, ref offset);");
        }

        writer.WriteLine("}");
        if (cardinality.Minimum > 0)
        {
            writer.WriteLine($"if ({property.Name} is null)");
            writer.WriteLine("{");
            using (writer.Indent())
            {
                string kind = isChoice ? "choice" : "parameter";
                writer.WriteLine(
                    $"throw GeneratedCodecRuntime.InvalidSequence(\"Required {kind} '{EscapeStringLiteral(property.Name)}' is missing.\");");
            }

            writer.WriteLine("}");
        }
    }

    private static string GetDecodedConstructorArgument(GeneratedProperty property)
    {
        Cardinality? cardinality = property.Definition switch
        {
            ParameterReferenceDefinition parameter => parameter.Cardinality,
            ChoiceReferenceDefinition choice => choice.Cardinality,
            _ => null,
        };
        return cardinality is { Minimum: > 0, Maximum: 1 } ? $"{property.Name}!" : property.Name;
    }

    private void WriteLengthMethod(
        CodeWriter writer,
        string methodName,
        string modelType,
        string valueName,
        IReadOnlyList<ProtocolMemberDefinition> members,
        IReadOnlyList<GeneratedProperty> properties,
        byte protocolVersionValue)
    {
        using (writer.Indent())
        {
            writer.WriteLine($"public override int {methodName}(");
            using (writer.Indent())
            {
                writer.WriteLine("global::LlrpNet.Core.Protocol.LlrpProtocolVersion version,");
                writer.WriteLine($"{modelType} {valueName})");
            }

            writer.WriteLine("{");
            using (writer.Indent())
            {
                writer.WriteLine($"GeneratedCodecRuntime.ValidateVersion(version, {protocolVersionValue});");
                writer.WriteLine($"global::System.ArgumentNullException.ThrowIfNull({valueName});");
                WriteLengthBody(writer, valueName, members, properties);
            }

            writer.WriteLine("}");
            writer.WriteLine();
        }
    }

    private void WriteEncodeMethod(
        CodeWriter writer,
        string methodName,
        string modelType,
        string valueName,
        IReadOnlyList<ProtocolMemberDefinition> members,
        IReadOnlyList<GeneratedProperty> properties,
        byte protocolVersionValue)
    {
        using (writer.Indent())
        {
            writer.WriteLine($"public override int {methodName}(");
            using (writer.Indent())
            {
                writer.WriteLine("global::LlrpNet.Core.Protocol.LlrpProtocolVersion version,");
                writer.WriteLine($"{modelType} {valueName},");
                writer.WriteLine("global::System.Span<byte> destination)");
            }

            writer.WriteLine("{");
            using (writer.Indent())
            {
                writer.WriteLine($"int expectedLength = {GetLengthMethodName(methodName)}(version, {valueName});");
                writer.WriteLine("GeneratedCodecRuntime.ValidateDestination(destination, expectedLength);");
                writer.WriteLine("destination.Clear();");
                writer.WriteLine("var wireWriter = new GeneratedWireWriter(destination);");
                WriteEncodeBody(writer, valueName, members, properties);
            }

            writer.WriteLine("}");
            writer.WriteLine();
        }
    }

    private static string GetLengthMethodName(string encodeMethodName)
    {
        return string.Equals(encodeMethodName, "EncodeData", StringComparison.Ordinal)
            ? "GetEncodedDataLength"
            : "GetEncodedPayloadLength";
    }

    private void WriteLengthBody(
        CodeWriter writer,
        string valueName,
        IReadOnlyList<ProtocolMemberDefinition> members,
        IReadOnlyList<GeneratedProperty> properties)
    {
        writer.WriteLine($"int length = {GetFixedFieldLength(members).ToString(CultureInfo.InvariantCulture)};");
        int propertyIndex = properties.Count > 0 && string.Equals(properties[0].Name, "MessageId", StringComparison.Ordinal) ? 1 : 0;
        foreach (ProtocolMemberDefinition member in members)
        {
            switch (member)
            {
                case FieldDefinition field:
                    GeneratedProperty fieldProperty = properties[propertyIndex++];
                    string? lengthExpression = GetVariableFieldLengthExpression(
                        field,
                        $"{valueName}.{fieldProperty.Name}");
                    if (lengthExpression is not null)
                    {
                        writer.WriteLine($"length = checked(length + {lengthExpression});");
                    }

                    break;

                case ParameterReferenceDefinition parameter:
                    GeneratedProperty parameterProperty = properties[propertyIndex++];
                    WriteReferenceLength(
                        writer,
                        parameter,
                        parameter.Cardinality,
                        parameterProperty,
                        valueName);
                    break;

                case ChoiceReferenceDefinition choice:
                    GeneratedProperty choiceProperty = properties[propertyIndex++];
                    WriteReferenceLength(
                        writer,
                        choice,
                        choice.Cardinality,
                        choiceProperty,
                        valueName);
                    break;
            }
        }

        writer.WriteLine("return length;");
    }

    private void WriteReferenceLength(
        CodeWriter writer,
        ProtocolMemberDefinition member,
        Cardinality cardinality,
        GeneratedProperty property,
        string valueName)
    {
        string access = $"{valueName}.{property.Name}";
        bool repeated = cardinality.Maximum is null or > 1;
        if (repeated)
        {
            WriteRepeatedCardinalityValidation(writer, access, property.Name, cardinality);
            writer.WriteLine($"foreach (global::LlrpNet.Protocol.Parameters.ILlrpParameter nested in {access})");
            writer.WriteLine("{");
            using (writer.Indent())
            {
                WriteReferenceMatchValidation(writer, member, "nested", property.Name);
                writer.WriteLine("length = checked(length + registry.GetEncodedParameterLength(version, nested));");
            }

            writer.WriteLine("}");
            return;
        }

        if (cardinality.Minimum > 0)
        {
            writer.WriteLine($"if ({access} is null)");
            writer.WriteLine("{");
            using (writer.Indent())
            {
                writer.WriteLine(
                    $"throw new global::System.ArgumentNullException(\"{EscapeStringLiteral(property.Name)}\");");
            }

            writer.WriteLine("}");
            WriteReferenceMatchValidation(writer, member, $"{access}!", property.Name);
            writer.WriteLine(
                $"length = checked(length + registry.GetEncodedParameterLength(version, {access}!));");
            return;
        }

        writer.WriteLine($"if ({access} is not null)");
        writer.WriteLine("{");
        using (writer.Indent())
        {
            WriteReferenceMatchValidation(writer, member, access, property.Name);
            writer.WriteLine(
                $"length = checked(length + registry.GetEncodedParameterLength(version, {access}));");
        }

        writer.WriteLine("}");
    }

    private static void FlushDecodeFieldSegment(CodeWriter writer, string sourceName, ref bool hasFieldSegment)
    {
        if (!hasFieldSegment)
        {
            return;
        }

        writer.WriteLine("offset += reader.BytePosition;");
        hasFieldSegment = false;
    }

    private void WriteReferenceMatchValidation(
        CodeWriter writer,
        ProtocolMemberDefinition member,
        string parameterExpression,
        string memberName)
    {
        string matchExpression = GetReferenceParameterMatchExpression(member, parameterExpression);
        writer.WriteLine(
            $"GeneratedCodecRuntime.ValidateParameterMatch({parameterExpression}, \"{EscapeStringLiteral(memberName)}\", {matchExpression});");
    }

    private static void WriteRepeatedCardinalityValidation(
        CodeWriter writer,
        string access,
        string propertyName,
        Cardinality cardinality)
    {
        writer.WriteLine($"if ({access} is null)");
        writer.WriteLine("{");
        using (writer.Indent())
        {
            writer.WriteLine(
                $"throw new global::System.ArgumentNullException(\"{EscapeStringLiteral(propertyName)}\");");
        }

        writer.WriteLine("}");
        string maximumCheck = cardinality.Maximum is int maximum
            ? $" || {access}.Count > {maximum.ToString(CultureInfo.InvariantCulture)}"
            : string.Empty;
        writer.WriteLine(
            $"if ({access}.Count < {cardinality.Minimum.ToString(CultureInfo.InvariantCulture)}{maximumCheck})");
        writer.WriteLine("{");
        using (writer.Indent())
        {
            string maximumText = cardinality.Maximum?.ToString(CultureInfo.InvariantCulture) ?? "N";
            writer.WriteLine(
                $"throw new global::System.ArgumentException(\"Member '{EscapeStringLiteral(propertyName)}' requires cardinality {cardinality.Minimum.ToString(CultureInfo.InvariantCulture)}..{maximumText}.\", \"{EscapeStringLiteral(propertyName)}\");");
        }

        writer.WriteLine("}");
    }

    private void WriteEncodeBody(
        CodeWriter writer,
        string valueName,
        IReadOnlyList<ProtocolMemberDefinition> members,
        IReadOnlyList<GeneratedProperty> properties)
    {
        int propertyIndex = properties.Count > 0 && string.Equals(properties[0].Name, "MessageId", StringComparison.Ordinal) ? 1 : 0;
        bool hasFieldSegment = false;
        writer.WriteLine("int offset = 0;");
        foreach (ProtocolMemberDefinition member in members)
        {
            switch (member)
            {
                case FieldDefinition field:
                    GeneratedProperty fieldProperty = properties[propertyIndex++];
                    writer.WriteLine(GetFieldEncodeStatement(
                        field,
                        $"{valueName}.{fieldProperty.Name}"));
                    hasFieldSegment = true;
                    break;

                case ReservedBitsDefinition reserved:
                    writer.WriteLine(
                        $"wireWriter.WriteReservedBits({reserved.BitCount.ToString(CultureInfo.InvariantCulture)});");
                    hasFieldSegment = true;
                    break;

                case ParameterReferenceDefinition parameter:
                    FlushEncodeFieldSegment(writer, ref hasFieldSegment);
                    GeneratedProperty parameterProperty = properties[propertyIndex++];
                    WriteReferenceEncode(
                        writer,
                        parameter.Cardinality,
                        parameterProperty,
                        valueName);
                    writer.WriteLine("wireWriter = new GeneratedWireWriter(destination[offset..]);");
                    break;

                case ChoiceReferenceDefinition choice:
                    FlushEncodeFieldSegment(writer, ref hasFieldSegment);
                    GeneratedProperty choiceProperty = properties[propertyIndex++];
                    WriteReferenceEncode(
                        writer,
                        choice.Cardinality,
                        choiceProperty,
                        valueName);
                    writer.WriteLine("wireWriter = new GeneratedWireWriter(destination[offset..]);");
                    break;
            }
        }

        FlushEncodeFieldSegment(writer, ref hasFieldSegment);
        writer.WriteLine("if (offset != destination.Length)");
        writer.WriteLine("{");
        using (writer.Indent())
        {
            writer.WriteLine("throw new global::System.InvalidOperationException(\"Generated codec wrote an unexpected payload length.\");");
        }

        writer.WriteLine("}");
        writer.WriteLine("return offset;");
    }

    private static void WriteReferenceEncode(
        CodeWriter writer,
        Cardinality cardinality,
        GeneratedProperty property,
        string valueName)
    {
        string access = $"{valueName}.{property.Name}";
        bool repeated = cardinality.Maximum is null or > 1;
        if (repeated)
        {
            writer.WriteLine($"foreach (global::LlrpNet.Protocol.Parameters.ILlrpParameter nested in {access})");
            writer.WriteLine("{");
            using (writer.Indent())
            {
                writer.WriteLine(
                    "offset += registry.EncodeParameter(version, nested, destination[offset..]);");
            }

            writer.WriteLine("}");
            return;
        }

        if (cardinality.Minimum > 0)
        {
            writer.WriteLine(
                $"offset += registry.EncodeParameter(version, {access}!, destination[offset..]);");
            return;
        }

        writer.WriteLine($"if ({access} is not null)");
        writer.WriteLine("{");
        using (writer.Indent())
        {
            writer.WriteLine(
                $"offset += registry.EncodeParameter(version, {access}, destination[offset..]);");
        }

        writer.WriteLine("}");
    }

    private string GetReferenceElementType(ProtocolMemberDefinition member)
    {
        return member switch
        {
            ParameterReferenceDefinition parameter when symbols.IsEnvelopeParameter(parameter.ParameterType) =>
                "global::LlrpNet.Protocol.Parameters.ILlrpParameter",
            ParameterReferenceDefinition parameter =>
                QualifyParameter(parameter.ParameterType),
            ChoiceReferenceDefinition choice when choicesContainingEnvelope.Contains(choice.ChoiceType) =>
                "global::LlrpNet.Protocol.Parameters.ILlrpParameter",
            ChoiceReferenceDefinition choice => QualifyChoice(choice.ChoiceType),
            _ => throw new InvalidOperationException("A generated reference must be a parameter or choice."),
        };
    }

    private string GetReferenceMatchExpression(
        ProtocolMemberDefinition member,
        string sourceName)
    {
        string[] expressions = GetReferenceWireIdentities(member)
            .Select(identity =>
                $"GeneratedCodecRuntime.IsNextParameter({sourceName}[offset..], " +
                $"{identity.TypeNumber.ToString(CultureInfo.InvariantCulture)}, " +
                $"{identity.MatchCustomMetadata.ToString().ToLowerInvariant()}, " +
                $"{identity.VendorId.ToString(CultureInfo.InvariantCulture)}U, " +
                $"{identity.Subtype.ToString(CultureInfo.InvariantCulture)}U)")
            .ToArray();
        return expressions.Length switch
        {
            0 => "false",
            1 => expressions[0],
            _ => $"({string.Join(" || ", expressions)})",
        };
    }

    private static void FlushEncodeFieldSegment(CodeWriter writer, ref bool hasFieldSegment)
    {
        if (!hasFieldSegment)
        {
            return;
        }

        writer.WriteLine("offset += wireWriter.BytePosition;");
        hasFieldSegment = false;
    }

    private string GetReferenceParameterMatchExpression(
        ProtocolMemberDefinition member,
        string parameterExpression)
    {
        string[] expressions = GetReferenceWireIdentities(member)
            .Select(identity =>
                $"GeneratedCodecRuntime.IsParameterMatch(registry, version, {parameterExpression}, " +
                $"{identity.TypeNumber.ToString(CultureInfo.InvariantCulture)}, " +
                $"{identity.MatchCustomMetadata.ToString().ToLowerInvariant()}, " +
                $"{identity.VendorId.ToString(CultureInfo.InvariantCulture)}U, " +
                $"{identity.Subtype.ToString(CultureInfo.InvariantCulture)}U)")
            .ToArray();
        return expressions.Length switch
        {
            0 => "false",
            1 => expressions[0],
            _ => $"({string.Join(" || ", expressions)})",
        };
    }

    private IEnumerable<ParameterWireIdentity> GetReferenceWireIdentities(ProtocolMemberDefinition member)
    {
        return member switch
        {
            ParameterReferenceDefinition parameter =>
                [symbols.GetParameterWireIdentity(parameter.ParameterType)],
            ChoiceReferenceDefinition choice => choiceDefinitions[choice.ChoiceType]
                .ParameterTypes
                .Where(parameterName =>
                    !canonicalTrailingEnvelopeChoices.Contains(choice.ChoiceType)
                    || !symbols.IsEnvelopeParameter(parameterName))
                .Select(symbols.GetParameterWireIdentity),
            _ => throw new InvalidOperationException("A generated reference must be a parameter or choice."),
        };
    }

    private string GetFieldDecodeExpression(FieldDefinition field, string propertyType)
    {
        if (field.Enumeration is not null)
        {
            return field.FieldType switch
            {
                ProtocolFieldType.U1 => $"GeneratedCodecRuntime.ReadEnum<{propertyType}>(reader.ReadBits(1))",
                ProtocolFieldType.U2 => $"GeneratedCodecRuntime.ReadEnum<{propertyType}>(reader.ReadBits(2))",
                ProtocolFieldType.S8 => $"GeneratedCodecRuntime.ReadEnum<{propertyType}>(reader.ReadSByte())",
                ProtocolFieldType.U8 => $"GeneratedCodecRuntime.ReadEnum<{propertyType}>(reader.ReadByte())",
                ProtocolFieldType.S16 => $"GeneratedCodecRuntime.ReadEnum<{propertyType}>(reader.ReadInt16())",
                ProtocolFieldType.U16 => $"GeneratedCodecRuntime.ReadEnum<{propertyType}>(reader.ReadUInt16())",
                ProtocolFieldType.S32 => $"GeneratedCodecRuntime.ReadEnum<{propertyType}>(reader.ReadInt32())",
                ProtocolFieldType.U32 => $"GeneratedCodecRuntime.ReadEnum<{propertyType}>(reader.ReadUInt32())",
                ProtocolFieldType.U64 => $"GeneratedCodecRuntime.ReadEnum<{propertyType}>(reader.ReadUInt64())",
                ProtocolFieldType.U1Vector =>
                    $"reader.ReadEnumBitVector<{GetEnumerationElementType(field)}>()",
                ProtocolFieldType.U8Vector =>
                    $"reader.ReadEnumByteVector<{GetEnumerationElementType(field)}>()",
                ProtocolFieldType.U16Vector =>
                    $"reader.ReadEnumUInt16Vector<{GetEnumerationElementType(field)}>()",
                ProtocolFieldType.U32Vector =>
                    $"reader.ReadEnumUInt32Vector<{GetEnumerationElementType(field)}>()",
                _ => throw new InvalidOperationException(
                    $"Enumeration field type '{field.FieldType}' is not supported by generated codecs."),
            };
        }

        return field.FieldType switch
        {
            ProtocolFieldType.U1 => "reader.ReadBoolean()",
            ProtocolFieldType.U2 => "(byte)reader.ReadBits(2)",
            ProtocolFieldType.S8 => "reader.ReadSByte()",
            ProtocolFieldType.U8 => "reader.ReadByte()",
            ProtocolFieldType.S16 => "reader.ReadInt16()",
            ProtocolFieldType.U16 => "reader.ReadUInt16()",
            ProtocolFieldType.S32 => "reader.ReadInt32()",
            ProtocolFieldType.U32 => "reader.ReadUInt32()",
            ProtocolFieldType.U64 => "reader.ReadUInt64()",
            ProtocolFieldType.U96 => "reader.ReadU96()",
            ProtocolFieldType.BytesToEnd => "reader.ReadBytesToEnd()",
            ProtocolFieldType.U1Vector => "reader.ReadBitVector()",
            ProtocolFieldType.U8Vector => "reader.ReadByteVector()",
            ProtocolFieldType.U16Vector => "reader.ReadUInt16Vector()",
            ProtocolFieldType.U32Vector => "reader.ReadUInt32Vector()",
            ProtocolFieldType.Utf8Vector => "reader.ReadUtf8()",
            _ => throw new InvalidOperationException($"Field type '{field.FieldType}' is not supported."),
        };
    }

    private string GetFieldEncodeStatement(FieldDefinition field, string access)
    {
        if (field.Enumeration is not null)
        {
            string statement = field.FieldType switch
            {
                ProtocolFieldType.U1 =>
                    $"wireWriter.WriteBits(global::System.Convert.ToUInt64({access}, global::System.Globalization.CultureInfo.InvariantCulture), 1);",
                ProtocolFieldType.U2 =>
                    $"wireWriter.WriteBits(global::System.Convert.ToUInt64({access}, global::System.Globalization.CultureInfo.InvariantCulture), 2);",
                ProtocolFieldType.S8 =>
                    $"wireWriter.WriteSByte(checked((sbyte)global::System.Convert.ToInt64({access}, global::System.Globalization.CultureInfo.InvariantCulture)));",
                ProtocolFieldType.U8 =>
                    $"wireWriter.WriteByte(checked((byte)global::System.Convert.ToUInt64({access}, global::System.Globalization.CultureInfo.InvariantCulture)));",
                ProtocolFieldType.S16 =>
                    $"wireWriter.WriteInt16(checked((short)global::System.Convert.ToInt64({access}, global::System.Globalization.CultureInfo.InvariantCulture)));",
                ProtocolFieldType.U16 =>
                    $"wireWriter.WriteUInt16(checked((ushort)global::System.Convert.ToUInt64({access}, global::System.Globalization.CultureInfo.InvariantCulture)));",
                ProtocolFieldType.S32 =>
                    $"wireWriter.WriteInt32(checked((int)global::System.Convert.ToInt64({access}, global::System.Globalization.CultureInfo.InvariantCulture)));",
                ProtocolFieldType.U32 =>
                    $"wireWriter.WriteUInt32(checked((uint)global::System.Convert.ToUInt64({access}, global::System.Globalization.CultureInfo.InvariantCulture)));",
                ProtocolFieldType.U64 =>
                    $"wireWriter.WriteUInt64(global::System.Convert.ToUInt64({access}, global::System.Globalization.CultureInfo.InvariantCulture));",
                ProtocolFieldType.U1Vector => $"wireWriter.WriteEnumBitVector({access});",
                ProtocolFieldType.U8Vector => $"wireWriter.WriteEnumByteVector({access});",
                ProtocolFieldType.U16Vector => $"wireWriter.WriteEnumUInt16Vector({access});",
                ProtocolFieldType.U32Vector => $"wireWriter.WriteEnumUInt32Vector({access});",
                _ => throw new InvalidOperationException(
                    $"Enumeration field type '{field.FieldType}' is not supported by generated codecs."),
            };
            return field.FieldType is ProtocolFieldType.U1Vector
                or ProtocolFieldType.U8Vector
                or ProtocolFieldType.U16Vector
                or ProtocolFieldType.U32Vector
                ? statement
                : $"GeneratedCodecRuntime.ValidateEnum({access}, \"{EscapeStringLiteral(field.Name)}\"); {statement}";
        }

        return field.FieldType switch
        {
            ProtocolFieldType.U1 => $"wireWriter.WriteBoolean({access});",
            ProtocolFieldType.U2 => $"wireWriter.WriteBits({access}, 2);",
            ProtocolFieldType.S8 => $"wireWriter.WriteSByte({access});",
            ProtocolFieldType.U8 => $"wireWriter.WriteByte({access});",
            ProtocolFieldType.S16 => $"wireWriter.WriteInt16({access});",
            ProtocolFieldType.U16 => $"wireWriter.WriteUInt16({access});",
            ProtocolFieldType.S32 => $"wireWriter.WriteInt32({access});",
            ProtocolFieldType.U32 => $"wireWriter.WriteUInt32({access});",
            ProtocolFieldType.U64 => $"wireWriter.WriteUInt64({access});",
            ProtocolFieldType.U96 => $"wireWriter.WriteU96({access});",
            ProtocolFieldType.BytesToEnd => $"wireWriter.WriteBytesToEnd({access});",
            ProtocolFieldType.U1Vector => $"wireWriter.WriteBitVector({access});",
            ProtocolFieldType.U8Vector => $"wireWriter.WriteByteVector({access});",
            ProtocolFieldType.U16Vector => $"wireWriter.WriteUInt16Vector({access});",
            ProtocolFieldType.U32Vector => $"wireWriter.WriteUInt32Vector({access});",
            ProtocolFieldType.Utf8Vector => $"wireWriter.WriteUtf8({access});",
            _ => throw new InvalidOperationException($"Field type '{field.FieldType}' is not supported."),
        };
    }

    private string? GetVariableFieldLengthExpression(FieldDefinition field, string access)
    {
        return field.FieldType switch
        {
            ProtocolFieldType.BytesToEnd => $"{access}.Length",
            ProtocolFieldType.U1Vector => $"GeneratedCodecRuntime.GetBitVectorLength({access})",
            ProtocolFieldType.U8Vector when field.Enumeration is null =>
                $"GeneratedCodecRuntime.GetByteVectorLength({access})",
            ProtocolFieldType.U8Vector => $"GeneratedCodecRuntime.GetVectorLength({access}, 1)",
            ProtocolFieldType.U16Vector => $"GeneratedCodecRuntime.GetVectorLength({access}, 2)",
            ProtocolFieldType.U32Vector => $"GeneratedCodecRuntime.GetVectorLength({access}, 4)",
            ProtocolFieldType.Utf8Vector => $"GeneratedCodecRuntime.GetUtf8Length({access})",
            _ => null,
        };
    }

    private string GetEnumerationElementType(FieldDefinition field)
    {
        return QualifyEnumeration(field.Enumeration!);
    }

    private static int GetFixedFieldLength(IReadOnlyList<ProtocolMemberDefinition> members)
    {
        long bitLength = 0;
        foreach (ProtocolMemberDefinition member in members)
        {
            switch (member)
            {
                case FieldDefinition field when GetFixedBitWidth(field.FieldType) is int width:
                    bitLength = checked(bitLength + width);
                    break;
                case ReservedBitsDefinition reserved:
                    bitLength = checked(bitLength + reserved.BitCount);
                    break;
            }
        }

        if ((bitLength & 7) != 0)
        {
            throw new InvalidOperationException("Validated generated fixed fields must be octet aligned.");
        }

        return checked((int)(bitLength / 8));
    }

    private static int? GetFixedBitWidth(ProtocolFieldType fieldType)
    {
        return fieldType switch
        {
            ProtocolFieldType.U1 => 1,
            ProtocolFieldType.U2 => 2,
            ProtocolFieldType.S8 or ProtocolFieldType.U8 => 8,
            ProtocolFieldType.S16 or ProtocolFieldType.U16 => 16,
            ProtocolFieldType.S32 or ProtocolFieldType.U32 => 32,
            ProtocolFieldType.U64 => 64,
            ProtocolFieldType.U96 => 96,
            _ => null,
        };
    }

    private static string EscapeStringLiteral(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal);
    }

    private GeneratedSourceFile RenderRegistryModule(
        ProtocolDefinition definition,
        byte protocolVersionValue,
        string? registryModuleName)
    {
        string moduleIdentifier = CSharpIdentifier.Normalize(
            string.IsNullOrWhiteSpace(registryModuleName) ? $"{versionNamespace}ProtocolModule" : registryModuleName,
            "ProtocolModule");
        var writer = CreateWriter("Registry");
        writer.WriteLine("/// <summary>Registers generated protocol codecs in dependency-safe order.</summary>");
        writer.WriteLine($"public static class {moduleIdentifier}");
        writer.WriteLine("{");
        using (writer.Indent())
        {
            writer.WriteLine("public static void Register(global::LlrpNet.Protocol.Registry.LlrpCodecRegistry registry)");
            writer.WriteLine("{");
            using (writer.Indent())
            {
                writer.WriteLine("global::System.ArgumentNullException.ThrowIfNull(registry);");
                writer.WriteLine($"var version = (global::LlrpNet.Core.Protocol.LlrpProtocolVersion){protocolVersionValue};");
                foreach (ParameterDefinition parameter in definition.Parameters.Where(static parameter => parameter.TypeNumber != 1023))
                {
                    string codecType = Qualify("Codecs", symbols.GetParameterCodec(parameter.Name));
                    if (parameter.Encoding == ParameterEncodingKind.Tv)
                    {
                        int encodedLength = checked(1 + GetFixedFieldLength(parameter.Members));
                        writer.WriteLine(
                            $"registry.RegisterTvParameter(version, {parameter.TypeNumber}, {encodedLength}, new {codecType}(registry));");
                    }
                    else
                    {
                        writer.WriteLine(
                            $"registry.RegisterTlvParameter(version, {parameter.TypeNumber}, new {codecType}(registry));");
                    }
                }

                foreach (CustomParameterDefinition parameter in definition.CustomParameters)
                {
                    string codecType = Qualify("Codecs", symbols.GetParameterCodec(parameter.Name));
                    writer.WriteLine(
                        $"registry.RegisterCustomParameter(version, {symbols.GetVendorId(parameter.Vendor)}U, {parameter.Subtype}U, new {codecType}(registry));");
                }

                foreach (MessageDefinition message in definition.Messages.Where(static message => message.TypeNumber != 1023))
                {
                    string codecType = Qualify("Codecs", symbols.GetMessageCodec(message.Name));
                    writer.WriteLine(
                        $"registry.RegisterMessage(version, {message.TypeNumber}, new {codecType}(registry));");
                }

                foreach (CustomMessageDefinition message in definition.CustomMessages)
                {
                    string codecType = Qualify("Codecs", symbols.GetMessageCodec(message.Name));
                    writer.WriteLine(
                        $"registry.RegisterCustomMessage(version, {symbols.GetVendorId(message.Vendor)}U, {message.Subtype}, new {codecType}(registry));");
                }
            }

            writer.WriteLine("}");
        }

        writer.WriteLine("}");
        return new GeneratedSourceFile(
            $"Registry/{versionNamespace}/{CSharpIdentifier.WithoutEscapePrefix(moduleIdentifier)}.g.cs",
            GeneratedSourceKind.RegistryModule,
            writer.ToString());
    }

    private static void WriteConstructorReturn(
        CodeWriter writer,
        string modelType,
        IReadOnlyList<string> arguments)
    {
        if (arguments.Count == 0)
        {
            writer.WriteLine($"return new {modelType}();");
            return;
        }

        writer.WriteLine($"return new {modelType}(");
        using (writer.Indent())
        {
            for (int index = 0; index < arguments.Count; index++)
            {
                string suffix = index == arguments.Count - 1 ? ");" : ",";
                writer.WriteLine($"{arguments[index]}{suffix}");
            }
        }
    }
}
