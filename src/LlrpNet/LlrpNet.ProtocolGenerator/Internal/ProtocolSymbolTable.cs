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
    private readonly Dictionary<string, string> messageRootNamespaces = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> parameterRootNamespaces = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> enumerationRootNamespaces = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> choiceRootNamespaces = new(StringComparer.Ordinal);
    private readonly Dictionary<string, uint> vendors = new(StringComparer.Ordinal);
    private readonly HashSet<string> envelopeParameters = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ParameterWireIdentity> parameterWireIdentities = new(StringComparer.Ordinal);

    public ProtocolSymbolTable(
        ProtocolDefinition definition,
        IEnumerable<ProtocolDefinition> dependencies,
        string rootNamespace,
        string dependencyRootNamespace)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(dependencies);
        ArgumentNullException.ThrowIfNull(rootNamespace);
        ArgumentNullException.ThrowIfNull(dependencyRootNamespace);
        ProtocolDefinition[] dependencyArray = dependencies.ToArray();
        ProtocolDefinition[] definitionArray = [definition, .. dependencyArray];
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

        foreach ((ProtocolDefinition currentDefinition, string currentRootNamespace) in
                 new[] { (definition, rootNamespace) }
                     .Concat(dependencyArray.Select(item => (item, dependencyRootNamespace))))
        {
            envelopeParameters.UnionWith(
                currentDefinition.Parameters
                    .Where(static parameter => parameter.TypeNumber == 1023)
                    .Select(static parameter => parameter.Name));
            foreach (ParameterDefinition parameter in currentDefinition.Parameters)
            {
                parameterWireIdentities.TryAdd(
                    parameter.Name,
                    new ParameterWireIdentity(parameter.TypeNumber, MatchCustomMetadata: false, 0, 0));
            }

            foreach (CustomParameterDefinition parameter in currentDefinition.CustomParameters)
            {
                uint vendorId = vendors[parameter.Vendor];
                parameterWireIdentities.TryAdd(
                    parameter.Name,
                    new ParameterWireIdentity(1023, MatchCustomMetadata: true, vendorId, parameter.Subtype));
            }
            AddSymbols(
                currentDefinition.Messages.Select(static item => item.Name)
                    .Concat(currentDefinition.CustomMessages.Select(static item => item.Name)),
                messages,
                messageRootNamespaces,
                currentRootNamespace,
                messageNames,
                "Message");
            AddSymbols(
                currentDefinition.Parameters.Select(static item => item.Name)
                    .Concat(currentDefinition.CustomParameters.Select(static item => item.Name)),
                parameters,
                parameterRootNamespaces,
                currentRootNamespace,
                parameterNames,
                "Parameter");
            AddSymbols(
                currentDefinition.Enumerations.Select(static item => item.Name)
                    .Concat(currentDefinition.CustomEnumerations.Select(static item => item.Name)),
                enumerations,
                enumerationRootNamespaces,
                currentRootNamespace,
                enumerationNames,
                "Enumeration");
            AddSymbols(
                currentDefinition.Choices.Select(static item => item.Name),
                choices,
                choiceRootNamespaces,
                currentRootNamespace,
                choiceNames,
                "Choice",
                prefix: "I");
            AddSymbols(
                currentDefinition.Parameters.Select(static item => item.Name)
                    .Concat(currentDefinition.CustomParameters.Select(static item => item.Name)),
                parameterCodecs,
                null,
                currentRootNamespace,
                codecNames,
                "ParameterCodec",
                suffix: "Codec");
            AddSymbols(
                currentDefinition.Messages.Select(static item => item.Name)
                    .Concat(currentDefinition.CustomMessages.Select(static item => item.Name)),
                messageCodecs,
                null,
                currentRootNamespace,
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

    public string GetMessageRootNamespace(string name) => messageRootNamespaces[name];

    public string GetParameterRootNamespace(string name) => parameterRootNamespaces[name];

    public string GetEnumerationRootNamespace(string name) => enumerationRootNamespaces[name];

    public string GetChoiceRootNamespace(string name) => choiceRootNamespaces[name];

    public bool TryGetChoice(string name, out string identifier) => choices.TryGetValue(name, out identifier!);

    public uint GetVendorId(string name) => vendors[name];

    public bool IsEnvelopeParameter(string name) => envelopeParameters.Contains(name);

    public ParameterWireIdentity GetParameterWireIdentity(string name) => parameterWireIdentities[name];

    private static void AddSymbols(
        IEnumerable<string> sourceNames,
        IDictionary<string, string> destination,
        IDictionary<string, string>? rootNamespaces,
        string rootNamespace,
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
                rootNamespaces?.Add(sourceName, rootNamespace);
            }
        }
    }
}

internal readonly record struct ParameterWireIdentity(
    ushort TypeNumber,
    bool MatchCustomMetadata,
    uint VendorId,
    uint Subtype);
