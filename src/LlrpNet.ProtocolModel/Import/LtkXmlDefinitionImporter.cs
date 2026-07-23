using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using LlrpNet.ProtocolModel.Definitions;

namespace LlrpNet.ProtocolModel.Import;

/// <summary>
/// Imports core and vendor-extension declarations from the legacy LLRP Toolkit binary-definition
/// XML format into the normalized model.
/// </summary>
public sealed class LtkXmlDefinitionImporter
{
    private const long MaximumDefinitionCharacters = 64L * 1024 * 1024;

    /// <summary>
    /// Imports a definition from a file without resolving external XML resources.
    /// </summary>
    /// <param name="path">The XML definition path.</param>
    /// <returns>The normalized protocol definition.</returns>
    public ProtocolDefinition Import(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using FileStream stream = File.OpenRead(path);
        return Import(stream, Path.GetFullPath(path));
    }

    /// <summary>
    /// Imports a definition stream without taking ownership of the stream.
    /// </summary>
    /// <param name="stream">A readable XML stream positioned at its definition document.</param>
    /// <param name="sourceName">The logical source name used in diagnostics.</param>
    /// <returns>The normalized protocol definition.</returns>
    public ProtocolDefinition Import(Stream stream, string sourceName)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        if (!stream.CanRead)
        {
            throw new ArgumentException("The definition stream must be readable.", nameof(stream));
        }

        var settings = new XmlReaderSettings
        {
            CloseInput = false,
            DtdProcessing = DtdProcessing.Prohibit,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            MaxCharactersFromEntities = 0,
            MaxCharactersInDocument = MaximumDefinitionCharacters,
            XmlResolver = null,
        };

        XDocument document;
        try
        {
            using XmlReader reader = XmlReader.Create(stream, settings, sourceName);
            document = XDocument.Load(reader, LoadOptions.SetLineInfo);
        }
        catch (XmlException exception)
        {
            throw new DefinitionImportException(
                sourceName,
                exception.LineNumber,
                exception.LinePosition,
                exception.Message,
                exception);
        }

        XElement root = document.Root ?? throw Error(sourceName, null, "The definition document has no root element.");
        if (root.Name.LocalName != "llrpdef")
        {
            throw Error(sourceName, root, $"Expected an llrpdef root element, but found {root.Name}.");
        }

        ValidateRootAttributes(sourceName, root);
        XNamespace definitionNamespace = root.Name.Namespace;
        ValidateTopLevelElements(sourceName, definitionNamespace, root);
        XElement[] namespaceElements = root
            .Elements(definitionNamespace + "namespaceDefinition")
            .ToArray();
        if (namespaceElements.Length > 1)
        {
            throw Error(
                sourceName,
                namespaceElements[1],
                "A definition source cannot declare more than one namespaceDefinition.");
        }

        ProtocolNamespaceDefinition? xmlNamespace = namespaceElements.Length == 0
            ? null
            : ParseNamespace(sourceName, namespaceElements[0]);

        MessageDefinition[] messages = root
            .Elements(definitionNamespace + "messageDefinition")
            .Select(element => ParseMessage(sourceName, definitionNamespace, element))
            .ToArray();
        ParameterDefinition[] parameters = root
            .Elements(definitionNamespace + "parameterDefinition")
            .Select(element => ParseParameter(sourceName, definitionNamespace, element))
            .ToArray();
        EnumerationDefinition[] enumerations = root
            .Elements(definitionNamespace + "enumerationDefinition")
            .Select(element => ParseEnumeration(sourceName, definitionNamespace, element))
            .ToArray();
        ChoiceDefinition[] choices = root
            .Elements(definitionNamespace + "choiceDefinition")
            .Select(element => ParseChoice(sourceName, definitionNamespace, element))
            .ToArray();
        VendorDefinition[] vendors = root
            .Elements(definitionNamespace + "vendorDefinition")
            .Select(element => ParseVendor(sourceName, element))
            .ToArray();
        CustomMessageDefinition[] customMessages = root
            .Elements(definitionNamespace + "customMessageDefinition")
            .Select(element => ParseCustomMessage(sourceName, definitionNamespace, element))
            .ToArray();
        CustomParameterDefinition[] customParameters = root
            .Elements(definitionNamespace + "customParameterDefinition")
            .Select(element => ParseCustomParameter(sourceName, definitionNamespace, element))
            .ToArray();
        CustomEnumerationDefinition[] customEnumerations = root
            .Elements(definitionNamespace + "customEnumerationDefinition")
            .Select(element => ParseCustomEnumeration(sourceName, definitionNamespace, element))
            .ToArray();

        return new ProtocolDefinition(
            sourceName,
            xmlNamespace,
            messages,
            parameters,
            enumerations,
            choices,
            vendors,
            customMessages,
            customParameters,
            customEnumerations);
    }

    private static void ValidateTopLevelElements(
        string sourceName,
        XNamespace definitionNamespace,
        XElement root)
    {
        foreach (XElement element in root.Elements())
        {
            string localName = element.Name.LocalName;
            bool supported = element.Name.Namespace == definitionNamespace &&
                localName is "namespaceDefinition"
                    or "messageDefinition"
                    or "parameterDefinition"
                    or "enumerationDefinition"
                    or "choiceDefinition"
                    or "vendorDefinition"
                    or "customMessageDefinition"
                    or "customParameterDefinition"
                    or "customEnumerationDefinition";
            if (!supported)
            {
                throw Error(sourceName, element, $"Unsupported top-level element {element.Name}.");
            }
        }
    }

    private static ProtocolNamespaceDefinition ParseNamespace(string sourceName, XElement element)
    {
        ValidateAttributes(sourceName, element, "prefix", "URI", "schemaLocation");
        ValidateNoElementChildren(sourceName, element);
        return new ProtocolNamespaceDefinition(
            RequiredAttribute(sourceName, element, "prefix"),
            RequiredAttribute(sourceName, element, "URI"),
            OptionalAttribute(element, "schemaLocation"));
    }

    private static MessageDefinition ParseMessage(
        string sourceName,
        XNamespace definitionNamespace,
        XElement element)
    {
        ValidateAttributes(sourceName, element, "name", "typeNum", "required", "responseType");
        return new MessageDefinition(
            RequiredAttribute(sourceName, element, "name"),
            ParseUShortAttribute(sourceName, element, "typeNum"),
            ParseBooleanAttribute(sourceName, element, "required"),
            OptionalAttribute(element, "responseType"),
            ParseMembers(sourceName, definitionNamespace, element));
    }

    private static ParameterDefinition ParseParameter(
        string sourceName,
        XNamespace definitionNamespace,
        XElement element)
    {
        ValidateAttributes(sourceName, element, "name", "typeNum", "required");
        return new ParameterDefinition(
            RequiredAttribute(sourceName, element, "name"),
            ParseUShortAttribute(sourceName, element, "typeNum"),
            ParseBooleanAttribute(sourceName, element, "required"),
            ParseMembers(sourceName, definitionNamespace, element));
    }

    private static VendorDefinition ParseVendor(string sourceName, XElement element)
    {
        ValidateAttributes(sourceName, element, "name", "vendorID");
        ValidateNoElementChildren(sourceName, element);
        return new VendorDefinition(
            RequiredAttribute(sourceName, element, "name"),
            ParseUInt32Attribute(sourceName, element, "vendorID"));
    }

    private static CustomMessageDefinition ParseCustomMessage(
        string sourceName,
        XNamespace definitionNamespace,
        XElement element)
    {
        ValidateAttributes(sourceName, element, "name", "vendor", "subtype", "namespace", "responseType");
        return new CustomMessageDefinition(
            RequiredAttribute(sourceName, element, "name"),
            RequiredAttribute(sourceName, element, "vendor"),
            ParseByteAttribute(sourceName, element, "subtype"),
            RequiredAttribute(sourceName, element, "namespace"),
            OptionalAttribute(element, "responseType"),
            ParseMembers(sourceName, definitionNamespace, element));
    }

    private static CustomParameterDefinition ParseCustomParameter(
        string sourceName,
        XNamespace definitionNamespace,
        XElement element)
    {
        ValidateAttributes(sourceName, element, "name", "vendor", "subtype", "namespace");
        ProtocolMemberDefinition[] members = ParseMembers(
                sourceName,
                definitionNamespace,
                element,
                allowAllowedIn: true)
            .ToArray();
        AllowedInDefinition[] allowedIn = element
            .Elements(definitionNamespace + "allowedIn")
            .Select(child => ParseAllowedIn(sourceName, child))
            .ToArray();
        return new CustomParameterDefinition(
            RequiredAttribute(sourceName, element, "name"),
            RequiredAttribute(sourceName, element, "vendor"),
            ParseUInt32Attribute(sourceName, element, "subtype"),
            RequiredAttribute(sourceName, element, "namespace"),
            members,
            allowedIn);
    }

    private static AllowedInDefinition ParseAllowedIn(string sourceName, XElement element)
    {
        ValidateAttributes(sourceName, element, "type", "repeat");
        ValidateNoElementChildren(sourceName, element);
        return new AllowedInDefinition(
            RequiredAttribute(sourceName, element, "type"),
            ParseCardinality(sourceName, element));
    }

    private static CustomEnumerationDefinition ParseCustomEnumeration(
        string sourceName,
        XNamespace definitionNamespace,
        XElement element)
    {
        ValidateAttributes(sourceName, element, "name", "namespace");
        return new CustomEnumerationDefinition(
            RequiredAttribute(sourceName, element, "name"),
            RequiredAttribute(sourceName, element, "namespace"),
            ParseEnumerationEntries(sourceName, definitionNamespace, element));
    }

    private static EnumerationDefinition ParseEnumeration(
        string sourceName,
        XNamespace definitionNamespace,
        XElement element)
    {
        ValidateAttributes(sourceName, element, "name");
        return new EnumerationDefinition(
            RequiredAttribute(sourceName, element, "name"),
            ParseEnumerationEntries(sourceName, definitionNamespace, element));
    }

    private static IEnumerable<EnumerationEntryDefinition> ParseEnumerationEntries(
        string sourceName,
        XNamespace definitionNamespace,
        XElement element)
    {
        foreach (XElement child in element.Elements())
        {
            if (child.Name == definitionNamespace + "annotation")
            {
                continue;
            }

            if (child.Name != definitionNamespace + "entry")
            {
                throw Error(sourceName, child, $"Unsupported enumeration child element {child.Name}.");
            }

            ValidateAttributes(sourceName, child, "name", "value");
            ValidateNoElementChildren(sourceName, child);
            yield return new EnumerationEntryDefinition(
                RequiredAttribute(sourceName, child, "name"),
                ParseInt64Attribute(sourceName, child, "value"));
        }
    }

    private static ChoiceDefinition ParseChoice(
        string sourceName,
        XNamespace definitionNamespace,
        XElement element)
    {
        ValidateAttributes(sourceName, element, "name");
        var parameterTypes = new List<string>();
        foreach (XElement child in element.Elements())
        {
            if (child.Name == definitionNamespace + "annotation")
            {
                continue;
            }

            if (child.Name != definitionNamespace + "parameter")
            {
                throw Error(sourceName, child, $"Unsupported choice child element {child.Name}.");
            }

            ValidateAttributes(sourceName, child, "type");
            ValidateNoElementChildren(sourceName, child);
            parameterTypes.Add(RequiredAttribute(sourceName, child, "type"));
        }

        return new ChoiceDefinition(
            RequiredAttribute(sourceName, element, "name"),
            parameterTypes);
    }

    private static IEnumerable<ProtocolMemberDefinition> ParseMembers(
        string sourceName,
        XNamespace definitionNamespace,
        XElement container,
        bool allowAllowedIn = false)
    {
        foreach (XElement element in container.Elements())
        {
            if (element.Name == definitionNamespace + "annotation")
            {
                continue;
            }

            if (element.Name == definitionNamespace + "field")
            {
                ValidateAttributes(sourceName, element, "type", "name", "enumeration", "format");
                ValidateNoElementChildren(sourceName, element);
                yield return new FieldDefinition(
                    RequiredAttribute(sourceName, element, "name"),
                    ParseFieldType(sourceName, element),
                    OptionalAttribute(element, "enumeration"),
                    OptionalAttribute(element, "format"));
                continue;
            }

            if (element.Name == definitionNamespace + "reserved")
            {
                ValidateAttributes(sourceName, element, "bitCount");
                ValidateNoElementChildren(sourceName, element);
                int bitCount = ParseInt32Attribute(sourceName, element, "bitCount");
                if (bitCount <= 0)
                {
                    throw Error(sourceName, element, "reserved bitCount must be positive.");
                }

                yield return new ReservedBitsDefinition(bitCount);
                continue;
            }

            if (element.Name == definitionNamespace + "parameter")
            {
                ValidateAttributes(sourceName, element, "type", "repeat");
                ValidateNoElementChildren(sourceName, element);
                yield return new ParameterReferenceDefinition(
                    RequiredAttribute(sourceName, element, "type"),
                    ParseCardinality(sourceName, element));
                continue;
            }

            if (element.Name == definitionNamespace + "choice")
            {
                ValidateAttributes(sourceName, element, "type", "repeat");
                ValidateNoElementChildren(sourceName, element);
                yield return new ChoiceReferenceDefinition(
                    RequiredAttribute(sourceName, element, "type"),
                    ParseCardinality(sourceName, element));
                continue;
            }

            if (allowAllowedIn && element.Name == definitionNamespace + "allowedIn")
            {
                continue;
            }

            throw Error(
                sourceName,
                element,
                $"Unsupported member element {element.Name.LocalName} in {container.Name.LocalName}.");
        }
    }

    private static Cardinality ParseCardinality(string sourceName, XElement element)
    {
        string repeat = RequiredAttribute(sourceName, element, "repeat");
        return repeat switch
        {
            "1" => Cardinality.Create(1, 1),
            "0-1" => Cardinality.Create(0, 1),
            "1-N" => Cardinality.Create(1, maximum: null),
            "0-N" => Cardinality.Create(0, maximum: null),
            _ => throw Error(
                sourceName,
                element,
                $"Invalid repeat value '{repeat}'; expected 1, 0-1, 1-N, or 0-N."),
        };
    }

    private static void ValidateAttributes(
        string sourceName,
        XElement element,
        params string[] allowedNames)
    {
        var allowed = allowedNames.ToHashSet(StringComparer.Ordinal);
        foreach (XAttribute attribute in element.Attributes())
        {
            if (attribute.IsNamespaceDeclaration)
            {
                continue;
            }

            if (attribute.Name.Namespace != XNamespace.None || !allowed.Contains(attribute.Name.LocalName))
            {
                throw Error(
                    sourceName,
                    attribute,
                    $"Unsupported attribute {attribute.Name} on {element.Name.LocalName}.");
            }
        }
    }

    private static void ValidateRootAttributes(string sourceName, XElement root)
    {
        XName schemaLocation = XNamespace.Get(XmlSchema.InstanceNamespace) + "schemaLocation";
        foreach (XAttribute attribute in root.Attributes())
        {
            if (!attribute.IsNamespaceDeclaration && attribute.Name != schemaLocation)
            {
                throw Error(sourceName, attribute, $"Unsupported attribute {attribute.Name} on llrpdef.");
            }
        }
    }

    private static void ValidateNoElementChildren(string sourceName, XElement element)
    {
        XElement? child = element.Elements().FirstOrDefault();
        if (child is not null)
        {
            throw Error(
                sourceName,
                child,
                $"Element {element.Name.LocalName} cannot contain child element {child.Name}.");
        }

        XText? text = element
            .Nodes()
            .OfType<XText>()
            .FirstOrDefault(static node => !string.IsNullOrWhiteSpace(node.Value));
        if (text is not null)
        {
            throw Error(
                sourceName,
                text,
                $"Element {element.Name.LocalName} cannot contain non-whitespace text.");
        }
    }

    private static string RequiredAttribute(string sourceName, XElement element, string name)
    {
        string? value = OptionalAttribute(element, name);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw Error(sourceName, element, $"Element {element.Name.LocalName} requires attribute {name}.");
        }

        return value;
    }

    private static string? OptionalAttribute(XElement element, string name)
    {
        return (string?)element.Attribute(name);
    }

    private static bool ParseBooleanAttribute(string sourceName, XElement element, string name)
    {
        string value = RequiredAttribute(sourceName, element, name);
        try
        {
            return XmlConvert.ToBoolean(value.Trim());
        }
        catch (FormatException exception)
        {
            throw Error(
                sourceName,
                element,
                $"Attribute {name} value '{value}' is not an XML Boolean.",
                exception);
        }
    }

    private static ProtocolFieldType ParseFieldType(string sourceName, XElement element)
    {
        string value = RequiredAttribute(sourceName, element, "type");
        return value switch
        {
            "u1" => ProtocolFieldType.U1,
            "u2" => ProtocolFieldType.U2,
            "s8" => ProtocolFieldType.S8,
            "u8" => ProtocolFieldType.U8,
            "s16" => ProtocolFieldType.S16,
            "u16" => ProtocolFieldType.U16,
            "u32" => ProtocolFieldType.U32,
            "s32" => ProtocolFieldType.S32,
            "u64" => ProtocolFieldType.U64,
            "u96" => ProtocolFieldType.U96,
            "bytesToEnd" => ProtocolFieldType.BytesToEnd,
            "u1v" => ProtocolFieldType.U1Vector,
            "u8v" => ProtocolFieldType.U8Vector,
            "u16v" => ProtocolFieldType.U16Vector,
            "u32v" => ProtocolFieldType.U32Vector,
            "utf8v" => ProtocolFieldType.Utf8Vector,
            _ => throw Error(sourceName, element, $"Field type '{value}' is not supported."),
        };
    }

    private static ushort ParseUShortAttribute(string sourceName, XElement element, string name)
    {
        string value = RequiredAttribute(sourceName, element, name);
        return ushort.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out ushort result)
            ? result
            : throw Error(sourceName, element, $"Attribute {name} value '{value}' is not an unsigned 16-bit integer.");
    }

    private static byte ParseByteAttribute(string sourceName, XElement element, string name)
    {
        string value = RequiredAttribute(sourceName, element, name);
        return byte.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out byte result)
            ? result
            : throw Error(sourceName, element, $"Attribute {name} value '{value}' is not an unsigned octet.");
    }

    private static uint ParseUInt32Attribute(string sourceName, XElement element, string name)
    {
        string value = RequiredAttribute(sourceName, element, name);
        return uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out uint result)
            ? result
            : throw Error(sourceName, element, $"Attribute {name} value '{value}' is not an unsigned 32-bit integer.");
    }

    private static int ParseInt32Attribute(string sourceName, XElement element, string name)
    {
        string value = RequiredAttribute(sourceName, element, name);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result)
            ? result
            : throw Error(sourceName, element, $"Attribute {name} value '{value}' is not a 32-bit integer.");
    }

    private static long ParseInt64Attribute(string sourceName, XElement element, string name)
    {
        string value = RequiredAttribute(sourceName, element, name);
        return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long result)
            ? result
            : throw Error(sourceName, element, $"Attribute {name} value '{value}' is not a 64-bit integer.");
    }

    private static DefinitionImportException Error(
        string sourceName,
        XObject? node,
        string message,
        Exception? innerException = null)
    {
        var lineInfo = node as IXmlLineInfo;
        int lineNumber = lineInfo?.HasLineInfo() == true ? lineInfo.LineNumber : 0;
        int linePosition = lineInfo?.HasLineInfo() == true ? lineInfo.LinePosition : 0;
        return new DefinitionImportException(
            sourceName,
            lineNumber,
            linePosition,
            message,
            innerException);
    }
}
