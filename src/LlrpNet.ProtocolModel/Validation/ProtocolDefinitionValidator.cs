using System.Collections.ObjectModel;
using System.Globalization;
using System.Numerics;
using LlrpNet.ProtocolModel.Definitions;

namespace LlrpNet.ProtocolModel.Validation;

/// <summary>
/// Validates normalized definitions before they are consumed by a code generator.
/// </summary>
public sealed class ProtocolDefinitionValidator
{
    private const int LlrpMessageHeaderLength = 10;
    private const int TlvParameterHeaderLength = 4;
    private const int TvParameterHeaderLength = 1;
    private const int CustomMessageDiscriminatorLength = 5;
    private const int CustomParameterDiscriminatorLength = 8;
    private const int VectorLengthPrefixLength = 2;

    private static readonly IReadOnlyDictionary<ProtocolFieldType, int?> FieldBitWidths =
        new Dictionary<ProtocolFieldType, int?>
        {
            [ProtocolFieldType.U1] = 1,
            [ProtocolFieldType.U2] = 2,
            [ProtocolFieldType.S8] = 8,
            [ProtocolFieldType.U8] = 8,
            [ProtocolFieldType.S16] = 16,
            [ProtocolFieldType.U16] = 16,
            [ProtocolFieldType.U32] = 32,
            [ProtocolFieldType.U64] = 64,
            [ProtocolFieldType.U96] = 96,
            [ProtocolFieldType.BytesToEnd] = null,
            [ProtocolFieldType.U1Vector] = null,
            [ProtocolFieldType.U8Vector] = null,
            [ProtocolFieldType.U16Vector] = null,
            [ProtocolFieldType.U32Vector] = null,
            [ProtocolFieldType.Utf8Vector] = null,
            [ProtocolFieldType.S32] = 32,
        };

    private static readonly IReadOnlySet<string> CSharpKeywords = new HashSet<string>(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
        "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else",
        "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for", "foreach",
        "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock", "long",
        "namespace", "new", "null", "object", "operator", "out", "override", "params", "private",
        "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof",
        "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true", "try", "typeof",
        "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile", "while",
    };

    /// <summary>
    /// Validates a complete normalized definition without external dependencies.
    /// </summary>
    public IReadOnlyList<DefinitionDiagnostic> Validate(ProtocolDefinition definition)
    {
        return Validate(definition, new ProtocolDefinitionValidationContext([]));
    }

    /// <summary>
    /// Validates a normalized definition while resolving references against imported dependencies.
    /// </summary>
    public IReadOnlyList<DefinitionDiagnostic> Validate(
        ProtocolDefinition definition,
        ProtocolDefinitionValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(context);
        var diagnostics = new List<DefinitionDiagnostic>();
        ProtocolDefinition[] visibleDefinitions = [definition, .. context.Dependencies];

        ValidateUniqueNames(definition.Messages, static item => item.Name, "message", diagnostics);
        ValidateUniqueNames(definition.Parameters, static item => item.Name, "parameter", diagnostics);
        ValidateUniqueNames(definition.Enumerations, static item => item.Name, "enumeration", diagnostics);
        ValidateUniqueNames(definition.Choices, static item => item.Name, "choice", diagnostics);
        ValidateUniqueNames(definition.Vendors, static item => item.Name, "vendor", diagnostics);
        ValidateUniqueNames(definition.CustomMessages, static item => item.Name, "custom message", diagnostics);
        ValidateUniqueNames(definition.CustomParameters, static item => item.Name, "custom parameter", diagnostics);
        ValidateUniqueNames(
            definition.CustomEnumerations,
            static item => item.Name,
            "custom enumeration",
            diagnostics);
        ValidateWireTypes(
            definition.Messages,
            static item => item.TypeNumber,
            static item => item.Name,
            maximum: 1023,
            "message",
            diagnostics);
        ValidateWireTypes(
            definition.Parameters,
            static item => item.TypeNumber,
            static item => item.Name,
            maximum: 1023,
            "parameter",
            diagnostics);

        ValidateVendorDefinitions(definition.Vendors, diagnostics);
        ValidateCustomWireKeys(definition.CustomMessages, diagnostics);
        ValidateCustomWireKeys(definition.CustomParameters, diagnostics);
        ValidateCustomClrNames(definition, visibleDefinitions, diagnostics);

        HashSet<string> messageNames = visibleDefinitions
            .SelectMany(static item => item.Messages.Select(static message => message.Name)
                .Concat(item.CustomMessages.Select(static message => message.Name)))
            .ToHashSet(StringComparer.Ordinal);
        HashSet<string> parameterNames = visibleDefinitions
            .SelectMany(static item => item.Parameters.Select(static parameter => parameter.Name)
                .Concat(item.CustomParameters.Select(static parameter => parameter.Name)))
            .ToHashSet(StringComparer.Ordinal);
        IReadOnlyDictionary<string, EnumerationDescriptor> enumerations = visibleDefinitions
            .SelectMany(static item => item.Enumerations.Select(
                    static enumeration => new EnumerationDescriptor(enumeration.Name, enumeration.Entries))
                .Concat(item.CustomEnumerations.Select(
                    static enumeration => new EnumerationDescriptor(enumeration.Name, enumeration.Entries))))
            .GroupBy(static enumeration => enumeration.Name, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.Ordinal);
        IReadOnlyDictionary<string, ChoiceDefinition> choices = visibleDefinitions
            .SelectMany(static item => item.Choices)
            .GroupBy(static choice => choice.Name, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.Ordinal);
        HashSet<string> choiceNames = choices.Keys.ToHashSet(StringComparer.Ordinal);
        HashSet<string> vendorNames = visibleDefinitions
            .SelectMany(static item => item.Vendors)
            .Select(static vendor => vendor.Name)
            .ToHashSet(StringComparer.Ordinal);
        HashSet<string> namespacePrefixes = visibleDefinitions
            .Select(static item => item.XmlNamespace?.Prefix)
            .Where(static prefix => prefix is not null)
            .Select(static prefix => prefix!)
            .ToHashSet(StringComparer.Ordinal);
        HashSet<string> containerNames = messageNames
            .Concat(parameterNames)
            .Concat(choiceNames)
            .ToHashSet(StringComparer.Ordinal);

        foreach (MessageDefinition message in definition.Messages)
        {
            string location = $"message:{message.Name}";
            ValidateResponseType(message.ResponseType, location, messageNames, diagnostics);
            ValidateMembers(
                message.Members,
                location,
                parameterNames,
                enumerations,
                choiceNames,
                requireFixedLength: false,
                diagnostics);
        }

        foreach (ParameterDefinition parameter in definition.Parameters)
        {
            ValidateMembers(
                parameter.Members,
                $"parameter:{parameter.Name}",
                parameterNames,
                enumerations,
                choiceNames,
                requireFixedLength: parameter.Encoding == ParameterEncodingKind.Tv,
                diagnostics);
        }

        foreach (CustomMessageDefinition message in definition.CustomMessages)
        {
            string location = $"custom-message:{message.Name}";
            ValidateVendorAndNamespace(
                message.Vendor,
                message.Namespace,
                location,
                vendorNames,
                namespacePrefixes,
                diagnostics);
            ValidateResponseType(message.ResponseType, location, messageNames, diagnostics);
            ValidateMembers(
                message.Members,
                location,
                parameterNames,
                enumerations,
                choiceNames,
                requireFixedLength: false,
                diagnostics);
        }

        foreach (CustomParameterDefinition parameter in definition.CustomParameters)
        {
            string location = $"custom-parameter:{parameter.Name}";
            ValidateVendorAndNamespace(
                parameter.Vendor,
                parameter.Namespace,
                location,
                vendorNames,
                namespacePrefixes,
                diagnostics);
            ValidateMembers(
                parameter.Members,
                location,
                parameterNames,
                enumerations,
                choiceNames,
                requireFixedLength: false,
                diagnostics);
            ValidateAllowedIn(parameter, location, containerNames, diagnostics);
        }

        foreach (EnumerationDefinition enumeration in definition.Enumerations)
        {
            ValidateEnumeration(
                new EnumerationDescriptor(enumeration.Name, enumeration.Entries),
                $"enumeration:{enumeration.Name}",
                diagnostics);
        }

        foreach (CustomEnumerationDefinition enumeration in definition.CustomEnumerations)
        {
            string location = $"custom-enumeration:{enumeration.Name}";
            if (!namespacePrefixes.Contains(enumeration.Namespace))
            {
                AddError(
                    diagnostics,
                    "LLRPM029",
                    location,
                    $"XML namespace prefix '{enumeration.Namespace}' is not defined.");
            }

            ValidateEnumeration(
                new EnumerationDescriptor(enumeration.Name, enumeration.Entries),
                location,
                diagnostics);
        }

        foreach (ChoiceDefinition choice in definition.Choices)
        {
            string location = $"choice:{choice.Name}";
            if (choice.ParameterTypes.Count == 0)
            {
                AddError(diagnostics, "LLRPM005", location, "A choice must contain at least one parameter type.");
            }

            foreach (string parameterType in choice.ParameterTypes)
            {
                ValidateReferenceIdentifier(parameterType, location, diagnostics);
                if (!parameterNames.Contains(parameterType))
                {
                    AddError(
                        diagnostics,
                        "LLRPM006",
                        location,
                        $"Parameter type '{parameterType}' is not defined.");
                }
            }

            foreach (IGrouping<string, string> duplicate in choice.ParameterTypes
                         .GroupBy(static type => type, StringComparer.Ordinal)
                         .Where(static group => group.Count() > 1))
            {
                AddError(
                    diagnostics,
                    "LLRPM007",
                    location,
                    $"Parameter type '{duplicate.Key}' occurs more than once in the choice.");
            }
        }

        ValidateMinimumEncodedLengths(definition, visibleDefinitions, choices, diagnostics);
        return new ReadOnlyCollection<DefinitionDiagnostic>(diagnostics);
    }

    private static void ValidateVendorDefinitions(
        IReadOnlyList<VendorDefinition> vendors,
        ICollection<DefinitionDiagnostic> diagnostics)
    {
        foreach (VendorDefinition vendor in vendors)
        {
            string location = $"vendor:{vendor.Name}";
            if (string.IsNullOrWhiteSpace(vendor.Name))
            {
                AddError(diagnostics, "LLRPM027", location, "A vendor name cannot be empty.");
            }

            if (vendor.VendorId == 0)
            {
                AddError(diagnostics, "LLRPM037", location, "A vendor identifier must be nonzero.");
            }
        }

        foreach (IGrouping<uint, VendorDefinition> duplicate in vendors
                     .GroupBy(static vendor => vendor.VendorId)
                     .Where(static group => group.Count() > 1))
        {
            AddError(
                diagnostics,
                "LLRPM038",
                "vendor",
                $"Vendor identifier {duplicate.Key} is used by: " +
                $"{string.Join(", ", duplicate.Select(static vendor => vendor.Name))}.");
        }
    }

    private static void ValidateCustomWireKeys(
        IReadOnlyList<CustomMessageDefinition> messages,
        ICollection<DefinitionDiagnostic> diagnostics)
    {
        foreach (IGrouping<(string Vendor, byte Subtype), CustomMessageDefinition> duplicate in messages
                     .GroupBy(static item => (item.Vendor, item.Subtype))
                     .Where(static group => group.Count() > 1))
        {
            AddError(
                diagnostics,
                "LLRPM030",
                "custom-message",
                $"Vendor '{duplicate.Key.Vendor}' custom-message subtype {duplicate.Key.Subtype} is used by: " +
                $"{string.Join(", ", duplicate.Select(static item => item.Name))}.");
        }
    }

    private static void ValidateCustomWireKeys(
        IReadOnlyList<CustomParameterDefinition> parameters,
        ICollection<DefinitionDiagnostic> diagnostics)
    {
        foreach (IGrouping<(string Vendor, uint Subtype), CustomParameterDefinition> duplicate in parameters
                     .GroupBy(static item => (item.Vendor, item.Subtype))
                     .Where(static group => group.Count() > 1))
        {
            AddError(
                diagnostics,
                "LLRPM030",
                "custom-parameter",
                $"Vendor '{duplicate.Key.Vendor}' custom-parameter subtype {duplicate.Key.Subtype} is used by: " +
                $"{string.Join(", ", duplicate.Select(static item => item.Name))}.");
        }
    }

    private static void ValidateCustomClrNames(
        ProtocolDefinition definition,
        IReadOnlyList<ProtocolDefinition> visibleDefinitions,
        ICollection<DefinitionDiagnostic> diagnostics)
    {
        HashSet<string> occupiedNames = visibleDefinitions
            .SelectMany(static item => item.Messages.Select(static value => value.Name)
                .Concat(item.Parameters.Select(static value => value.Name))
                .Concat(item.Enumerations.Select(static value => value.Name))
                .Concat(item.Choices.Select(static value => value.Name)))
            .ToHashSet(StringComparer.Ordinal);
        foreach (ProtocolDefinition dependency in visibleDefinitions.Where(item => !ReferenceEquals(item, definition)))
        {
            occupiedNames.UnionWith(dependency.CustomMessages.Select(static value => value.Name));
            occupiedNames.UnionWith(dependency.CustomParameters.Select(static value => value.Name));
            occupiedNames.UnionWith(dependency.CustomEnumerations.Select(static value => value.Name));
        }

        var customNames = new List<(string Name, string Location)>();
        customNames.AddRange(definition.CustomMessages.Select(
            static item => (item.Name, $"custom-message:{item.Name}")));
        customNames.AddRange(definition.CustomParameters.Select(
            static item => (item.Name, $"custom-parameter:{item.Name}")));
        customNames.AddRange(definition.CustomEnumerations.Select(
            static item => (item.Name, $"custom-enumeration:{item.Name}")));

        foreach ((string name, string location) in customNames)
        {
            ValidateClrIdentifier(name, location, "Custom definition name", diagnostics);
            if (!occupiedNames.Add(name))
            {
                AddError(
                    diagnostics,
                    "LLRPM032",
                    location,
                    $"Generated CLR type name '{name}' collides with another visible definition.");
            }
        }
    }

    private static void ValidateVendorAndNamespace(
        string vendor,
        string xmlNamespace,
        string location,
        IReadOnlySet<string> vendorNames,
        IReadOnlySet<string> namespacePrefixes,
        ICollection<DefinitionDiagnostic> diagnostics)
    {
        if (!vendorNames.Contains(vendor))
        {
            AddError(diagnostics, "LLRPM028", location, $"Vendor '{vendor}' is not defined.");
        }

        if (!namespacePrefixes.Contains(xmlNamespace))
        {
            AddError(
                diagnostics,
                "LLRPM029",
                location,
                $"XML namespace prefix '{xmlNamespace}' is not defined.");
        }
    }

    private static void ValidateResponseType(
        string? responseType,
        string location,
        IReadOnlySet<string> messageNames,
        ICollection<DefinitionDiagnostic> diagnostics)
    {
        if (responseType is null)
        {
            return;
        }

        ValidateReferenceIdentifier(responseType, location, diagnostics);
        if (!messageNames.Contains(responseType))
        {
            AddError(diagnostics, "LLRPM001", location, $"Response type '{responseType}' is not defined.");
        }
    }

    private static void ValidateAllowedIn(
        CustomParameterDefinition parameter,
        string location,
        IReadOnlySet<string> containerNames,
        ICollection<DefinitionDiagnostic> diagnostics)
    {
        foreach (AllowedInDefinition allowed in parameter.AllowedIn)
        {
            ValidateReferenceIdentifier(allowed.Type, location, diagnostics);
            if (!containerNames.Contains(allowed.Type))
            {
                AddError(
                    diagnostics,
                    "LLRPM031",
                    location,
                    $"Allowed-in container '{allowed.Type}' is not defined as a message, parameter, or choice.");
            }

            ValidateCardinality(allowed.Cardinality, location, diagnostics);
        }

        foreach (IGrouping<string, AllowedInDefinition> duplicate in parameter.AllowedIn
                     .GroupBy(static item => item.Type, StringComparer.Ordinal)
                     .Where(static group => group.Count() > 1))
        {
            AddError(
                diagnostics,
                "LLRPM039",
                location,
                $"Allowed-in container '{duplicate.Key}' occurs more than once.");
        }
    }

    private static void ValidateEnumeration(
        EnumerationDescriptor enumeration,
        string location,
        ICollection<DefinitionDiagnostic> diagnostics)
    {
        if (enumeration.Entries.Count == 0)
        {
            AddError(diagnostics, "LLRPM003", location, "An enumeration must contain at least one entry.");
        }

        ValidateUniqueNames(enumeration.Entries, static entry => entry.Name, "enumeration entry", diagnostics, location);
        foreach (EnumerationEntryDefinition entry in enumeration.Entries)
        {
            ValidateClrIdentifier(entry.Name, location, "Enumeration entry name", diagnostics);
        }

        foreach (IGrouping<long, EnumerationEntryDefinition> duplicate in enumeration.Entries
                     .GroupBy(static entry => entry.Value)
                     .Where(static group => group.Count() > 1))
        {
            AddError(
                diagnostics,
                "LLRPM004",
                location,
                $"Enumeration value {duplicate.Key} is used by: " +
                $"{string.Join(", ", duplicate.Select(static entry => entry.Name))}.");
        }
    }

    private static void ValidateMembers(
        IReadOnlyList<ProtocolMemberDefinition> members,
        string ownerLocation,
        IReadOnlySet<string> parameterNames,
        IReadOnlyDictionary<string, EnumerationDescriptor> enumerations,
        IReadOnlySet<string> choiceNames,
        bool requireFixedLength,
        ICollection<DefinitionDiagnostic> diagnostics)
    {
        var fieldNames = new HashSet<string>(StringComparer.Ordinal);
        long fixedBitOffset = 0;
        bool sawNestedMember = false;
        for (int index = 0; index < members.Count; index++)
        {
            ProtocolMemberDefinition member = members[index];
            string location = $"{ownerLocation}/member[{index}]";
            switch (member)
            {
                case FieldDefinition field:
                    if (sawNestedMember)
                    {
                        AddError(
                            diagnostics,
                            "LLRPM023",
                            location,
                            "Fields and reserved bits must precede parameter and choice members.");
                    }

                    if (string.IsNullOrWhiteSpace(field.Name))
                    {
                        AddError(diagnostics, "LLRPM008", location, "A field name cannot be empty.");
                    }
                    else if (!fieldNames.Add(field.Name))
                    {
                        AddError(
                            diagnostics,
                            "LLRPM009",
                            location,
                            $"Field name '{field.Name}' occurs more than once in the container.");
                    }

                    if (!FieldBitWidths.TryGetValue(field.FieldType, out int? bitWidth))
                    {
                        AddError(
                            diagnostics,
                            "LLRPM010",
                            location,
                            $"Field type value '{field.FieldType}' is not supported.");
                    }
                    else if (requireFixedLength && bitWidth is null)
                    {
                        AddError(
                            diagnostics,
                            "LLRPM011",
                            location,
                            $"TV parameters cannot contain variable-length field type '{field.FieldType}'.");
                    }

                    if (bitWidth is int fixedWidth)
                    {
                        fixedBitOffset = checked(fixedBitOffset + fixedWidth);
                    }
                    else
                    {
                        ValidateOctetBoundary(fixedBitOffset, location, diagnostics);
                        fixedBitOffset = 0;
                    }

                    if (field.Enumeration is not null)
                    {
                        ValidateReferenceIdentifier(field.Enumeration, location, diagnostics);
                        if (!enumerations.TryGetValue(field.Enumeration, out EnumerationDescriptor? enumeration))
                        {
                            AddError(
                                diagnostics,
                                "LLRPM012",
                                location,
                                $"Enumeration '{field.Enumeration}' is not defined.");
                        }
                        else
                        {
                            ValidateEnumerationRange(field, enumeration, location, diagnostics);
                        }
                    }

                    if (field.FieldType == ProtocolFieldType.BytesToEnd && index != members.Count - 1)
                    {
                        AddError(
                            diagnostics,
                            "LLRPM013",
                            location,
                            "A bytesToEnd field must be the final member of its container.");
                    }

                    break;

                case ReservedBitsDefinition reserved:
                    if (sawNestedMember)
                    {
                        AddError(
                            diagnostics,
                            "LLRPM023",
                            location,
                            "Fields and reserved bits must precede parameter and choice members.");
                    }

                    if (reserved.BitCount <= 0)
                    {
                        AddError(diagnostics, "LLRPM014", location, "Reserved bit count must be positive.");
                    }

                    fixedBitOffset = checked(fixedBitOffset + reserved.BitCount);
                    break;

                case ParameterReferenceDefinition parameter:
                    ValidateOctetBoundary(fixedBitOffset, location, diagnostics);
                    fixedBitOffset = 0;
                    sawNestedMember = true;
                    if (requireFixedLength)
                    {
                        AddError(diagnostics, "LLRPM015", location, "TV parameters cannot contain nested parameters.");
                    }

                    ValidateReferenceIdentifier(parameter.ParameterType, location, diagnostics);
                    if (!parameterNames.Contains(parameter.ParameterType))
                    {
                        AddError(
                            diagnostics,
                            "LLRPM016",
                            location,
                            $"Parameter type '{parameter.ParameterType}' is not defined.");
                    }

                    ValidateCardinality(parameter.Cardinality, location, diagnostics);
                    break;

                case ChoiceReferenceDefinition choice:
                    ValidateOctetBoundary(fixedBitOffset, location, diagnostics);
                    fixedBitOffset = 0;
                    sawNestedMember = true;
                    if (requireFixedLength)
                    {
                        AddError(diagnostics, "LLRPM017", location, "TV parameters cannot contain a choice.");
                    }

                    ValidateReferenceIdentifier(choice.ChoiceType, location, diagnostics);
                    if (!choiceNames.Contains(choice.ChoiceType))
                    {
                        AddError(
                            diagnostics,
                            "LLRPM018",
                            location,
                            $"Choice type '{choice.ChoiceType}' is not defined.");
                    }

                    ValidateCardinality(choice.Cardinality, location, diagnostics);
                    break;

                default:
                    AddError(
                        diagnostics,
                        "LLRPM036",
                        location,
                        $"Member type '{member.GetType().FullName}' is not supported.");
                    break;
            }
        }

        ValidateOctetBoundary(fixedBitOffset, ownerLocation, diagnostics);
    }

    private static void ValidateEnumerationRange(
        FieldDefinition field,
        EnumerationDescriptor enumeration,
        string location,
        ICollection<DefinitionDiagnostic> diagnostics)
    {
        (long Minimum, long Maximum)? range = field.FieldType switch
        {
            ProtocolFieldType.U1 or ProtocolFieldType.U1Vector => (0, 1),
            ProtocolFieldType.U2 => (0, 3),
            ProtocolFieldType.S8 => (sbyte.MinValue, sbyte.MaxValue),
            ProtocolFieldType.U8 or ProtocolFieldType.U8Vector => (0, byte.MaxValue),
            ProtocolFieldType.S16 => (short.MinValue, short.MaxValue),
            ProtocolFieldType.U16 or ProtocolFieldType.U16Vector => (0, ushort.MaxValue),
            ProtocolFieldType.S32 => (int.MinValue, int.MaxValue),
            ProtocolFieldType.U32 or ProtocolFieldType.U32Vector => (0, uint.MaxValue),
            ProtocolFieldType.U64 or ProtocolFieldType.U96 => (0, long.MaxValue),
            _ => null,
        };
        if (range is null)
        {
            AddError(
                diagnostics,
                "LLRPM024",
                location,
                $"Field type '{field.FieldType}' cannot reference an enumeration.");
            return;
        }

        EnumerationEntryDefinition[] outsideRange = enumeration.Entries
            .Where(entry => entry.Value < range.Value.Minimum || entry.Value > range.Value.Maximum)
            .ToArray();
        if (outsideRange.Length != 0)
        {
            AddError(
                diagnostics,
                "LLRPM025",
                location,
                $"Enumeration '{enumeration.Name}' contains value(s) outside the {field.FieldType} range: " +
                string.Join(", ", outsideRange.Select(static entry => $"{entry.Name}={entry.Value}")) + ".");
        }
    }

    private static void ValidateMinimumEncodedLengths(
        ProtocolDefinition definition,
        IReadOnlyList<ProtocolDefinition> visibleDefinitions,
        IReadOnlyDictionary<string, ChoiceDefinition> choices,
        ICollection<DefinitionDiagnostic> diagnostics)
    {
        ParameterDescriptor[] visibleParameters = visibleDefinitions
            .SelectMany(static item => item.Parameters.Select(
                    static parameter => new ParameterDescriptor(
                        parameter.Name,
                        parameter.Members,
                        parameter.Encoding == ParameterEncodingKind.Tlv
                            ? TlvParameterHeaderLength
                            : TvParameterHeaderLength))
                .Concat(item.CustomParameters.Select(
                    static parameter => new ParameterDescriptor(
                        parameter.Name,
                        parameter.Members,
                        TlvParameterHeaderLength + CustomParameterDiscriminatorLength))))
            .ToArray();
        IReadOnlyDictionary<string, ParameterDescriptor> parameters = visibleParameters
            .GroupBy(static parameter => parameter.Name, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.Ordinal);
        var analyzer = new MinimumEncodedLengthAnalyzer(parameters, choices);
        var cycleLocations = new HashSet<string>(StringComparer.Ordinal);

        foreach (ParameterDefinition parameter in definition.Parameters)
        {
            string location = $"parameter:{parameter.Name}";
            MinimumLengthResult result = analyzer.GetParameterCompleteLength(parameter.Name);
            ValidateRequiredCycle(result, location, cycleLocations, diagnostics);
            if (parameter.Encoding == ParameterEncodingKind.Tlv &&
                result.Kind != MinimumLengthKind.Cycle &&
                result.Minimum > ushort.MaxValue)
            {
                AddError(
                    diagnostics,
                    "LLRPM033",
                    location,
                    $"Minimum complete TLV length {result.Minimum} exceeds the 16-bit length field maximum 65535.");
            }
        }

        foreach (CustomParameterDefinition parameter in definition.CustomParameters)
        {
            string location = $"custom-parameter:{parameter.Name}";
            MinimumLengthResult result = analyzer.GetParameterCompleteLength(parameter.Name);
            ValidateRequiredCycle(result, location, cycleLocations, diagnostics);
            if (result.Kind != MinimumLengthKind.Cycle && result.Minimum > ushort.MaxValue)
            {
                AddError(
                    diagnostics,
                    "LLRPM033",
                    location,
                    $"Minimum complete TLV length {result.Minimum} exceeds the 16-bit length field maximum 65535.");
            }
        }

        foreach (MessageDefinition message in definition.Messages)
        {
            ValidateMessageMinimumLength(
                message.Name,
                message.Members,
                LlrpMessageHeaderLength,
                analyzer,
                cycleLocations,
                diagnostics,
                isCustom: false);
        }

        foreach (CustomMessageDefinition message in definition.CustomMessages)
        {
            ValidateMessageMinimumLength(
                message.Name,
                message.Members,
                LlrpMessageHeaderLength + CustomMessageDiscriminatorLength,
                analyzer,
                cycleLocations,
                diagnostics,
                isCustom: true);
        }
    }

    private static void ValidateMessageMinimumLength(
        string name,
        IReadOnlyList<ProtocolMemberDefinition> members,
        int prefixLength,
        MinimumEncodedLengthAnalyzer analyzer,
        ISet<string> cycleLocations,
        ICollection<DefinitionDiagnostic> diagnostics,
        bool isCustom)
    {
        string location = isCustom ? $"custom-message:{name}" : $"message:{name}";
        MinimumLengthResult payload = analyzer.GetMembersMinimumLength(members);
        ValidateRequiredCycle(payload, location, cycleLocations, diagnostics);
        if (payload.Kind == MinimumLengthKind.Cycle)
        {
            return;
        }

        BigInteger completeLength = prefixLength + payload.Minimum;
        if (completeLength > uint.MaxValue)
        {
            AddError(
                diagnostics,
                "LLRPM034",
                location,
                $"Minimum complete message length {completeLength} exceeds the 32-bit length field maximum {uint.MaxValue}.");
        }
    }

    private static void ValidateRequiredCycle(
        MinimumLengthResult result,
        string location,
        ISet<string> cycleLocations,
        ICollection<DefinitionDiagnostic> diagnostics)
    {
        if (result.Kind == MinimumLengthKind.Cycle && cycleLocations.Add(location))
        {
            AddError(
                diagnostics,
                "LLRPM035",
                location,
                "Required parameter/choice references form a cycle with no finite base case.");
        }
    }

    private static void ValidateOctetBoundary(
        long fixedBitOffset,
        string location,
        ICollection<DefinitionDiagnostic> diagnostics)
    {
        if (fixedBitOffset % 8 != 0)
        {
            AddError(
                diagnostics,
                "LLRPM026",
                location,
                $"The fixed field segment ends at bit offset {fixedBitOffset % 8}; wire boundaries must be octet aligned.");
        }
    }

    private static void ValidateCardinality(
        Cardinality cardinality,
        string location,
        ICollection<DefinitionDiagnostic> diagnostics)
    {
        if (cardinality.Minimum < 0 || cardinality.Maximum < cardinality.Minimum)
        {
            AddError(
                diagnostics,
                "LLRPM019",
                location,
                $"Cardinality {cardinality.Minimum}..{cardinality.Maximum?.ToString(CultureInfo.InvariantCulture) ?? "N"} is invalid.");
        }
    }

    private static void ValidateReferenceIdentifier(
        string name,
        string location,
        ICollection<DefinitionDiagnostic> diagnostics)
    {
        ValidateClrIdentifier(name, location, "Referenced type name", diagnostics);
    }

    private static void ValidateClrIdentifier(
        string name,
        string location,
        string kind,
        ICollection<DefinitionDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(name) || !IsCSharpIdentifier(name))
        {
            AddError(
                diagnostics,
                "LLRPM027",
                location,
                $"{kind} '{name}' is not a valid unescaped C# identifier.");
        }
    }

    private static bool IsCSharpIdentifier(string value)
    {
        if (value.Length == 0 || CSharpKeywords.Contains(value) || !IsIdentifierStart(value[0]))
        {
            return false;
        }

        return value.Skip(1).All(IsIdentifierPart);
    }

    private static bool IsIdentifierStart(char value)
    {
        UnicodeCategory category = char.GetUnicodeCategory(value);
        return value == '_' || category is UnicodeCategory.UppercaseLetter
            or UnicodeCategory.LowercaseLetter
            or UnicodeCategory.TitlecaseLetter
            or UnicodeCategory.ModifierLetter
            or UnicodeCategory.OtherLetter
            or UnicodeCategory.LetterNumber;
    }

    private static bool IsIdentifierPart(char value)
    {
        UnicodeCategory category = char.GetUnicodeCategory(value);
        return IsIdentifierStart(value) || category is UnicodeCategory.DecimalDigitNumber
            or UnicodeCategory.ConnectorPunctuation
            or UnicodeCategory.NonSpacingMark
            or UnicodeCategory.SpacingCombiningMark
            or UnicodeCategory.Format;
    }

    private static void ValidateUniqueNames<T>(
        IEnumerable<T> values,
        Func<T, string> nameSelector,
        string kind,
        ICollection<DefinitionDiagnostic> diagnostics,
        string? parentLocation = null)
    {
        foreach (IGrouping<string, T> duplicate in values
                     .GroupBy(nameSelector, StringComparer.Ordinal)
                     .Where(static group => group.Count() > 1))
        {
            AddError(
                diagnostics,
                "LLRPM020",
                parentLocation ?? kind,
                $"Duplicate {kind} name '{duplicate.Key}'.");
        }
    }

    private static void ValidateWireTypes<T>(
        IEnumerable<T> values,
        Func<T, ushort> typeSelector,
        Func<T, string> nameSelector,
        ushort maximum,
        string kind,
        ICollection<DefinitionDiagnostic> diagnostics)
    {
        foreach (T value in values)
        {
            ushort type = typeSelector(value);
            if (type == 0 || type > maximum)
            {
                AddError(
                    diagnostics,
                    "LLRPM021",
                    $"{kind}:{nameSelector(value)}",
                    $"Wire type {type} is outside 1..{maximum}.");
            }
        }

        foreach (IGrouping<ushort, T> duplicate in values
                     .GroupBy(typeSelector)
                     .Where(static group => group.Count() > 1))
        {
            AddError(
                diagnostics,
                "LLRPM022",
                kind,
                $"Wire type {duplicate.Key} is used by: {string.Join(", ", duplicate.Select(nameSelector))}.");
        }
    }

    private static void AddError(
        ICollection<DefinitionDiagnostic> diagnostics,
        string code,
        string location,
        string message)
    {
        diagnostics.Add(new DefinitionDiagnostic(
            code,
            DefinitionDiagnosticSeverity.Error,
            location,
            message));
    }

    private sealed record EnumerationDescriptor(
        string Name,
        IReadOnlyList<EnumerationEntryDefinition> Entries);

    private sealed record ParameterDescriptor(
        string Name,
        IReadOnlyList<ProtocolMemberDefinition> Members,
        int PrefixLength);

    private enum MinimumLengthKind
    {
        Finite,
        Unknown,
        Cycle,
    }

    private readonly record struct MinimumLengthResult(MinimumLengthKind Kind, BigInteger Minimum)
    {
        public static MinimumLengthResult Finite(BigInteger minimum) =>
            new(MinimumLengthKind.Finite, minimum);

        public static MinimumLengthResult Unknown(BigInteger minimum) =>
            new(MinimumLengthKind.Unknown, minimum);

        public static MinimumLengthResult Cycle => new(MinimumLengthKind.Cycle, BigInteger.Zero);
    }

    private sealed class MinimumEncodedLengthAnalyzer
    {
        private readonly IReadOnlyDictionary<string, ParameterDescriptor> parameters;
        private readonly IReadOnlyDictionary<string, ChoiceDefinition> choices;
        private readonly Dictionary<string, MinimumLengthResult> finiteParameterLengths = new(StringComparer.Ordinal);

        public MinimumEncodedLengthAnalyzer(
            IReadOnlyDictionary<string, ParameterDescriptor> parameters,
            IReadOnlyDictionary<string, ChoiceDefinition> choices)
        {
            this.parameters = parameters;
            this.choices = choices;
        }

        public MinimumLengthResult GetParameterCompleteLength(string name)
        {
            return GetParameterCompleteLength(name, new HashSet<string>(StringComparer.Ordinal));
        }

        public MinimumLengthResult GetMembersMinimumLength(IReadOnlyList<ProtocolMemberDefinition> members)
        {
            return GetMembersMinimumLength(members, new HashSet<string>(StringComparer.Ordinal));
        }

        private MinimumLengthResult GetParameterCompleteLength(string name, ISet<string> visiting)
        {
            if (finiteParameterLengths.TryGetValue(name, out MinimumLengthResult cached))
            {
                return cached;
            }

            if (!parameters.TryGetValue(name, out ParameterDescriptor? parameter))
            {
                return MinimumLengthResult.Unknown(BigInteger.Zero);
            }

            if (!visiting.Add(name))
            {
                return MinimumLengthResult.Cycle;
            }

            MinimumLengthResult payload = GetMembersMinimumLength(parameter.Members, visiting);
            visiting.Remove(name);
            if (payload.Kind == MinimumLengthKind.Cycle)
            {
                return payload;
            }

            MinimumLengthResult result = payload.Kind == MinimumLengthKind.Finite
                ? MinimumLengthResult.Finite(parameter.PrefixLength + payload.Minimum)
                : MinimumLengthResult.Unknown(parameter.PrefixLength + payload.Minimum);
            if (result.Kind == MinimumLengthKind.Finite)
            {
                finiteParameterLengths[name] = result;
            }

            return result;
        }

        private MinimumLengthResult GetMembersMinimumLength(
            IReadOnlyList<ProtocolMemberDefinition> members,
            ISet<string> visiting)
        {
            BigInteger total = BigInteger.Zero;
            BigInteger fixedBits = BigInteger.Zero;
            bool unknown = false;
            foreach (ProtocolMemberDefinition member in members)
            {
                switch (member)
                {
                    case FieldDefinition field:
                        if (FieldBitWidths.TryGetValue(field.FieldType, out int? width) && width is int fixedWidth)
                        {
                            fixedBits += fixedWidth;
                        }
                        else
                        {
                            total += BitsToOctets(fixedBits);
                            fixedBits = BigInteger.Zero;
                            if (field.FieldType is ProtocolFieldType.U1Vector
                                or ProtocolFieldType.U8Vector
                                or ProtocolFieldType.U16Vector
                                or ProtocolFieldType.U32Vector
                                or ProtocolFieldType.Utf8Vector)
                            {
                                total += VectorLengthPrefixLength;
                            }
                            else if (field.FieldType != ProtocolFieldType.BytesToEnd)
                            {
                                unknown = true;
                            }
                        }

                        break;

                    case ReservedBitsDefinition reserved:
                        if (reserved.BitCount > 0)
                        {
                            fixedBits += reserved.BitCount;
                        }

                        break;

                    case ParameterReferenceDefinition parameter:
                        total += BitsToOctets(fixedBits);
                        fixedBits = BigInteger.Zero;
                        MinimumLengthResult parameterResult = GetRepeatedParameterLength(
                            parameter.ParameterType,
                            parameter.Cardinality.Minimum,
                            visiting);
                        if (parameterResult.Kind == MinimumLengthKind.Cycle)
                        {
                            return MinimumLengthResult.Cycle;
                        }

                        total += parameterResult.Minimum;
                        unknown |= parameterResult.Kind == MinimumLengthKind.Unknown;
                        break;

                    case ChoiceReferenceDefinition choice:
                        total += BitsToOctets(fixedBits);
                        fixedBits = BigInteger.Zero;
                        MinimumLengthResult choiceResult = GetRepeatedChoiceLength(
                            choice.ChoiceType,
                            choice.Cardinality.Minimum,
                            visiting);
                        if (choiceResult.Kind == MinimumLengthKind.Cycle)
                        {
                            return MinimumLengthResult.Cycle;
                        }

                        total += choiceResult.Minimum;
                        unknown |= choiceResult.Kind == MinimumLengthKind.Unknown;
                        break;

                    default:
                        unknown = true;
                        break;
                }
            }

            total += BitsToOctets(fixedBits);
            return unknown ? MinimumLengthResult.Unknown(total) : MinimumLengthResult.Finite(total);
        }

        private MinimumLengthResult GetRepeatedParameterLength(
            string parameterType,
            int minimum,
            ISet<string> visiting)
        {
            if (minimum <= 0)
            {
                return MinimumLengthResult.Finite(BigInteger.Zero);
            }

            MinimumLengthResult result = GetParameterCompleteLength(parameterType, visiting);
            return result.Kind switch
            {
                MinimumLengthKind.Finite => MinimumLengthResult.Finite(result.Minimum * minimum),
                MinimumLengthKind.Unknown => MinimumLengthResult.Unknown(result.Minimum * minimum),
                _ => MinimumLengthResult.Cycle,
            };
        }

        private MinimumLengthResult GetRepeatedChoiceLength(
            string choiceType,
            int minimum,
            ISet<string> visiting)
        {
            if (minimum <= 0)
            {
                return MinimumLengthResult.Finite(BigInteger.Zero);
            }

            if (!choices.TryGetValue(choiceType, out ChoiceDefinition? choice) || choice.ParameterTypes.Count == 0)
            {
                return MinimumLengthResult.Unknown(BigInteger.Zero);
            }

            MinimumLengthResult[] alternatives = choice.ParameterTypes
                .Select(parameterType => GetParameterCompleteLength(parameterType, visiting))
                .ToArray();
            MinimumLengthResult[] nonCycles = alternatives
                .Where(static result => result.Kind != MinimumLengthKind.Cycle)
                .ToArray();
            if (nonCycles.Length == 0)
            {
                return MinimumLengthResult.Cycle;
            }

            BigInteger minimumAlternative = nonCycles.Min(static result => result.Minimum);
            bool exact = nonCycles
                .Where(result => result.Minimum == minimumAlternative)
                .Any(static result => result.Kind == MinimumLengthKind.Finite);
            return exact
                ? MinimumLengthResult.Finite(minimumAlternative * minimum)
                : MinimumLengthResult.Unknown(minimumAlternative * minimum);
        }

        private static BigInteger BitsToOctets(BigInteger bitCount)
        {
            return (bitCount + 7) / 8;
        }
    }
}
