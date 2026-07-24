using System.Globalization;
using LlrpNet.ProtocolModel.Definitions;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace LlrpNet.ProtocolModel.Import;

/// <summary>
/// Imports the native, human-maintained YAML protocol definition format.
/// XML and YAML sources intentionally normalize to the same <see cref="ProtocolDefinition"/>.
/// </summary>
public sealed class YamlProtocolDefinitionImporter
{
    private static readonly HashSet<string> RootKeys =
    ["namespace", "messages", "parameters", "enumerations", "choices", "vendors", "customMessages", "customParameters", "customEnumerations"];

    /// <summary>Imports a definition from a file.</summary>
    public ProtocolDefinition Import(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using FileStream stream = File.OpenRead(path);
        return Import(stream, Path.GetFullPath(path));
    }

    /// <summary>Imports a definition stream without taking ownership of it.</summary>
    public ProtocolDefinition Import(Stream stream, string sourceName)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        if (!stream.CanRead)
        {
            throw new ArgumentException("The definition stream must be readable.", nameof(stream));
        }

        var yaml = new YamlStream();
        try
        {
            using var reader = new StreamReader(stream, leaveOpen: true);
            yaml.Load(reader);
        }
        catch (YamlException exception)
        {
            throw Error(sourceName, exception.Start, exception.Message, exception);
        }

        if (yaml.Documents.Count != 1)
        {
            throw Error(sourceName, default, "A YAML definition must contain exactly one document.");
        }

        YamlMappingNode root = Map(sourceName, yaml.Documents[0].RootNode, "definition root");
        Keys(sourceName, root, RootKeys, "definition root");
        ProtocolNamespaceDefinition? xmlNamespace = Has(root, "namespace", out YamlNode ns) ? Namespace(sourceName, ns) : null;
        return new ProtocolDefinition(
            sourceName, xmlNamespace,
            Sequence(sourceName, root, "messages").Select(x => Message(sourceName, x)),
            Sequence(sourceName, root, "parameters").Select(x => Parameter(sourceName, x)),
            Sequence(sourceName, root, "enumerations").Select(x => Enumeration(sourceName, x)),
            Sequence(sourceName, root, "choices").Select(x => Choice(sourceName, x)),
            Sequence(sourceName, root, "vendors").Select(x => Vendor(sourceName, x)),
            Sequence(sourceName, root, "customMessages").Select(x => CustomMessage(sourceName, x)),
            Sequence(sourceName, root, "customParameters").Select(x => CustomParameter(sourceName, x)),
            Sequence(sourceName, root, "customEnumerations").Select(x => CustomEnumeration(sourceName, x)));
    }

    private static ProtocolNamespaceDefinition Namespace(string source, YamlNode node)
    {
        YamlMappingNode map = Map(source, node, "namespace");
        Keys(source, map, ["prefix", "uri", "schemaLocation"], "namespace");
        return new ProtocolNamespaceDefinition(Required(source, map, "prefix"), Required(source, map, "uri"), Optional(source, map, "schemaLocation"));
    }

    private static MessageDefinition Message(string source, YamlNode node)
    {
        YamlMappingNode map = Map(source, node, "message");
        Keys(source, map, ["name", "typeNumber", "required", "responseType", "members"], "message");
        return new MessageDefinition(Required(source, map, "name"), UShort(source, map, "typeNumber"), Boolean(source, map, "required"), Optional(source, map, "responseType"), Members(source, Sequence(source, map, "members")));
    }

    private static ParameterDefinition Parameter(string source, YamlNode node)
    {
        YamlMappingNode map = Map(source, node, "parameter");
        Keys(source, map, ["name", "typeNumber", "required", "members"], "parameter");
        return new ParameterDefinition(Required(source, map, "name"), UShort(source, map, "typeNumber"), Boolean(source, map, "required"), Members(source, Sequence(source, map, "members")));
    }

    private static EnumerationDefinition Enumeration(string source, YamlNode node)
    {
        YamlMappingNode map = Map(source, node, "enumeration");
        Keys(source, map, ["name", "entries"], "enumeration");
        return new EnumerationDefinition(Required(source, map, "name"), Entries(source, Sequence(source, map, "entries")));
    }

    private static ChoiceDefinition Choice(string source, YamlNode node)
    {
        YamlMappingNode map = Map(source, node, "choice");
        Keys(source, map, ["name", "parameterTypes"], "choice");
        return new ChoiceDefinition(Required(source, map, "name"), Sequence(source, map, "parameterTypes").Select(x => Scalar(source, x, "choice parameter type")));
    }

    private static VendorDefinition Vendor(string source, YamlNode node)
    {
        YamlMappingNode map = Map(source, node, "vendor");
        Keys(source, map, ["name", "vendorId"], "vendor");
        return new VendorDefinition(Required(source, map, "name"), UInt(source, map, "vendorId"));
    }

    private static CustomMessageDefinition CustomMessage(string source, YamlNode node)
    {
        YamlMappingNode map = Map(source, node, "custom message");
        Keys(source, map, ["name", "vendor", "subtype", "namespace", "responseType", "members"], "custom message");
        return new CustomMessageDefinition(Required(source, map, "name"), Required(source, map, "vendor"), Byte(source, map, "subtype"), Required(source, map, "namespace"), Optional(source, map, "responseType"), Members(source, Sequence(source, map, "members")));
    }

    private static CustomParameterDefinition CustomParameter(string source, YamlNode node)
    {
        YamlMappingNode map = Map(source, node, "custom parameter");
        Keys(source, map, ["name", "vendor", "subtype", "namespace", "members", "allowedIn"], "custom parameter");
        return new CustomParameterDefinition(Required(source, map, "name"), Required(source, map, "vendor"), UInt(source, map, "subtype"), Required(source, map, "namespace"), Members(source, Sequence(source, map, "members")), Sequence(source, map, "allowedIn").Select(x => AllowedIn(source, x)));
    }

    private static CustomEnumerationDefinition CustomEnumeration(string source, YamlNode node)
    {
        YamlMappingNode map = Map(source, node, "custom enumeration");
        Keys(source, map, ["name", "namespace", "entries"], "custom enumeration");
        return new CustomEnumerationDefinition(Required(source, map, "name"), Required(source, map, "namespace"), Entries(source, Sequence(source, map, "entries")));
    }

    private static IEnumerable<EnumerationEntryDefinition> Entries(string source, IEnumerable<YamlNode> nodes)
    {
        foreach (YamlNode node in nodes)
        {
            YamlMappingNode map = Map(source, node, "enumeration entry");
            Keys(source, map, ["name", "value"], "enumeration entry");
            yield return new EnumerationEntryDefinition(Required(source, map, "name"), Long(source, map, "value"));
        }
    }

    private static AllowedInDefinition AllowedIn(string source, YamlNode node)
    {
        YamlMappingNode map = Map(source, node, "allowedIn");
        Keys(source, map, ["type", "cardinality"], "allowedIn");
        return new AllowedInDefinition(Required(source, map, "type"), ReadCardinality(source, map));
    }

    private static IEnumerable<ProtocolMemberDefinition> Members(string source, IEnumerable<YamlNode> nodes)
    {
        foreach (YamlNode node in nodes)
        {
            YamlMappingNode wrapper = Map(source, node, "member");
            if (wrapper.Children.Count != 1)
            {
                throw Error(source, wrapper.Start, "Each member must contain exactly one of field, reserved, parameter, or choice.");
            }

            KeyValuePair<YamlNode, YamlNode> pair = wrapper.Children.Single();
            string kind = Scalar(source, pair.Key, "member kind");
            yield return kind switch
            {
                "field" => Field(source, pair.Value),
                "reserved" => Reserved(source, pair.Value),
                "parameter" => ParameterReference(source, pair.Value),
                "choice" => ChoiceReference(source, pair.Value),
                _ => throw Error(source, pair.Key.Start, $"Unsupported member kind '{kind}'."),
            };
        }
    }

    private static FieldDefinition Field(string source, YamlNode node)
    {
        YamlMappingNode map = Map(source, node, "field");
        Keys(source, map, ["name", "type", "enumeration", "format"], "field");
        string fieldType = Required(source, map, "type");
        return new FieldDefinition(Required(source, map, "name"), FieldType(source, map.Start, fieldType), Optional(source, map, "enumeration"), Optional(source, map, "format"));
    }

    private static ProtocolFieldType FieldType(string source, Mark mark, string value) => value switch
    {
        "u1" => ProtocolFieldType.U1, "u2" => ProtocolFieldType.U2, "s8" => ProtocolFieldType.S8, "u8" => ProtocolFieldType.U8,
        "s16" => ProtocolFieldType.S16, "u16" => ProtocolFieldType.U16, "u32" => ProtocolFieldType.U32, "s32" => ProtocolFieldType.S32,
        "u64" => ProtocolFieldType.U64, "u96" => ProtocolFieldType.U96, "bytesToEnd" => ProtocolFieldType.BytesToEnd,
        "u1v" => ProtocolFieldType.U1Vector, "u8v" => ProtocolFieldType.U8Vector, "u16v" => ProtocolFieldType.U16Vector,
        "u32v" => ProtocolFieldType.U32Vector, "utf8v" => ProtocolFieldType.Utf8Vector,
        _ => throw Error(source, mark, $"Field type '{value}' is not supported."),
    };

    private static ReservedBitsDefinition Reserved(string source, YamlNode node)
    {
        string value = Scalar(source, node, "reserved bit count");
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int bitCount) || bitCount <= 0)
        {
            throw Error(source, node.Start, "reserved must be a positive 32-bit integer.");
        }

        return new ReservedBitsDefinition(bitCount);
    }

    private static ParameterReferenceDefinition ParameterReference(string source, YamlNode node)
    {
        YamlMappingNode map = Map(source, node, "parameter reference");
        Keys(source, map, ["type", "cardinality"], "parameter reference");
        return new ParameterReferenceDefinition(Required(source, map, "type"), ReadCardinality(source, map));
    }

    private static ChoiceReferenceDefinition ChoiceReference(string source, YamlNode node)
    {
        YamlMappingNode map = Map(source, node, "choice reference");
        Keys(source, map, ["type", "cardinality"], "choice reference");
        return new ChoiceReferenceDefinition(Required(source, map, "type"), ReadCardinality(source, map));
    }

    private static Cardinality ReadCardinality(string source, YamlMappingNode map)
    {
        string value = Required(source, map, "cardinality");
        return value switch
        {
            "1" => Cardinality.Create(1, 1), "0-1" => Cardinality.Create(0, 1),
            "1-N" => Cardinality.Create(1, null), "0-N" => Cardinality.Create(0, null),
            _ => throw Error(source, map.Start, $"Invalid cardinality '{value}'; expected 1, 0-1, 1-N, or 0-N."),
        };
    }

    private static IEnumerable<YamlNode> Sequence(string source, YamlMappingNode map, string key)
    {
        if (!Has(map, key, out YamlNode node))
        {
            return [];
        }

        return node as YamlSequenceNode is { } sequence
            ? sequence.Children
            : throw Error(source, node.Start, $"{key} must be a sequence.");
    }

    private static bool Has(YamlMappingNode map, string key, out YamlNode value)
    {
        foreach ((YamlNode candidate, YamlNode candidateValue) in map.Children)
        {
            if (candidate is YamlScalarNode { Value: not null } scalar && scalar.Value == key)
            {
                value = candidateValue;
                return true;
            }
        }

        value = null!;
        return false;
    }

    private static void Keys(string source, YamlMappingNode map, IEnumerable<string> allowed, string description)
    {
        var supported = allowed.ToHashSet(StringComparer.Ordinal);
        foreach (YamlNode key in map.Children.Keys)
        {
            string value = Scalar(source, key, $"{description} key");
            if (!supported.Contains(value))
            {
                throw Error(source, key.Start, $"Unsupported {description} key '{value}'.");
            }
        }
    }

    private static string Required(string source, YamlMappingNode map, string key)
    {
        if (!Has(map, key, out YamlNode node))
        {
            throw Error(source, map.Start, $"{key} is required.");
        }

        string value = Scalar(source, node, key);
        return string.IsNullOrWhiteSpace(value) ? throw Error(source, node.Start, $"{key} cannot be empty.") : value;
    }

    private static string? Optional(string source, YamlMappingNode map, string key) => Has(map, key, out YamlNode node) ? Scalar(source, node, key) : null;

    private static YamlMappingNode Map(string source, YamlNode node, string description) => node as YamlMappingNode ?? throw Error(source, node.Start, $"{description} must be a mapping.");

    private static string Scalar(string source, YamlNode node, string description) => node is YamlScalarNode { Value: not null } scalar ? scalar.Value : throw Error(source, node.Start, $"{description} must be a scalar value.");

    private static bool Boolean(string source, YamlMappingNode map, string key) => bool.TryParse(Required(source, map, key), out bool value) ? value : throw Error(source, map.Start, $"{key} must be a Boolean.");

    private static ushort UShort(string source, YamlMappingNode map, string key) => ushort.TryParse(Required(source, map, key), NumberStyles.None, CultureInfo.InvariantCulture, out ushort value) ? value : throw Error(source, map.Start, $"{key} must be an unsigned 16-bit integer.");

    private static byte Byte(string source, YamlMappingNode map, string key) => byte.TryParse(Required(source, map, key), NumberStyles.None, CultureInfo.InvariantCulture, out byte value) ? value : throw Error(source, map.Start, $"{key} must be an unsigned octet.");

    private static uint UInt(string source, YamlMappingNode map, string key) => uint.TryParse(Required(source, map, key), NumberStyles.None, CultureInfo.InvariantCulture, out uint value) ? value : throw Error(source, map.Start, $"{key} must be an unsigned 32-bit integer.");

    private static long Long(string source, YamlMappingNode map, string key) => long.TryParse(Required(source, map, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out long value) ? value : throw Error(source, map.Start, $"{key} must be a 64-bit integer.");

    private static DefinitionImportException Error(string source, Mark mark, string message, Exception? inner = null) => new(source, checked((int)(mark.Line + 1)), checked((int)(mark.Column + 1)), message, inner);
}
