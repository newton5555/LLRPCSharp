using LlrpNet.ProtocolModel.Definitions;

namespace LlrpNet.ProtocolGenerator.Internal;

internal sealed class ProtocolSymbolTable
{
    private readonly Dictionary<string, string> messages = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> parameters = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> enumerations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> choices = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> messageCodecs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> parameterCodecs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, uint> vendors = new(StringComparer.Ordinal);
    private readonly HashSet<string> envelopeParameters = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ParameterWireIdentity> parameterWireIdentities = new(StringComparer.Ordinal);

    public ProtocolSymbolTable(IEnumerable<ProtocolDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ProtocolDefinition[] definitionArray = definitions.ToArray();
        var messageNames = new CSharpIdentifierAllocator();
        var parameterNames = new CSharpIdentifierAllocator();
        var enumerationNames = new CSharpIdentifierAllocator();
        var choiceNames = new CSharpIdentifierAllocator();
        var codecNames = new CSharpIdentifierAllocator(
            ["GeneratedCodecRuntime", "GeneratedWireReader", "GeneratedWireWriter"]);

        foreach (VendorDefinition vendor in definitionArray.SelectMany(static definition => definition.Vendors))
        {
            vendors.TryAdd(vendor.Name, vendor.VendorId);
        }

        foreach (ProtocolDefinition definition in definitionArray)
        {
            envelopeParameters.UnionWith(
                definition.Parameters
                    .Where(static parameter => parameter.TypeNumber == 1023)
                    .Select(static parameter => parameter.Name));
            foreach (ParameterDefinition parameter in definition.Parameters)
            {
                parameterWireIdentities.TryAdd(
                    parameter.Name,
                    new ParameterWireIdentity(parameter.TypeNumber, MatchCustomMetadata: false, 0, 0));
            }

            foreach (CustomParameterDefinition parameter in definition.CustomParameters)
            {
                uint vendorId = vendors[parameter.Vendor];
                parameterWireIdentities.TryAdd(
                    parameter.Name,
                    new ParameterWireIdentity(1023, MatchCustomMetadata: true, vendorId, parameter.Subtype));
            }
            AddSymbols(
                definition.Messages.Select(static item => item.Name)
                    .Concat(definition.CustomMessages.Select(static item => item.Name)),
                messages,
                messageNames,
                "Message");
            AddSymbols(
                definition.Parameters.Select(static item => item.Name)
                    .Concat(definition.CustomParameters.Select(static item => item.Name)),
                parameters,
                parameterNames,
                "Parameter");
            AddSymbols(
                definition.Enumerations.Select(static item => item.Name)
                    .Concat(definition.CustomEnumerations.Select(static item => item.Name)),
                enumerations,
                enumerationNames,
                "Enumeration");
            AddSymbols(
                definition.Choices.Select(static item => item.Name),
                choices,
                choiceNames,
                "Choice",
                prefix: "I");
            AddSymbols(
                definition.Parameters.Select(static item => item.Name)
                    .Concat(definition.CustomParameters.Select(static item => item.Name)),
                parameterCodecs,
                codecNames,
                "ParameterCodec",
                suffix: "Codec");
            AddSymbols(
                definition.Messages.Select(static item => item.Name)
                    .Concat(definition.CustomMessages.Select(static item => item.Name)),
                messageCodecs,
                codecNames,
                "MessageCodec",
                suffix: "Codec");
        }
    }

    public string GetMessage(string name) => messages[name];

    public string GetParameter(string name) => parameters[name];

    public string GetEnumeration(string name) => enumerations[name];

    public string GetChoice(string name) => choices[name];

    public string GetMessageCodec(string name) => messageCodecs[name];

    public string GetParameterCodec(string name) => parameterCodecs[name];

    public bool TryGetChoice(string name, out string identifier) => choices.TryGetValue(name, out identifier!);

    public uint GetVendorId(string name) => vendors[name];

    public bool IsEnvelopeParameter(string name) => envelopeParameters.Contains(name);

    public ParameterWireIdentity GetParameterWireIdentity(string name) => parameterWireIdentities[name];

    private static void AddSymbols(
        IEnumerable<string> sourceNames,
        IDictionary<string, string> destination,
        CSharpIdentifierAllocator allocator,
        string fallback,
        string prefix = "",
        string suffix = "")
    {
        foreach (string sourceName in sourceNames)
        {
            if (!destination.ContainsKey(sourceName))
            {
                destination.Add(sourceName, allocator.Allocate($"{prefix}{sourceName}{suffix}", fallback));
            }
        }
    }
}

internal readonly record struct ParameterWireIdentity(
    ushort TypeNumber,
    bool MatchCustomMetadata,
    uint VendorId,
    uint Subtype);
