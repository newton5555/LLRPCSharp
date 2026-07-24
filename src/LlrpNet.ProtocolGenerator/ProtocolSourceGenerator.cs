using LlrpNet.ProtocolGenerator.Generation;
using LlrpNet.ProtocolGenerator.Internal;
using LlrpNet.ProtocolModel.Definitions;
using LlrpNet.ProtocolModel.Validation;

namespace LlrpNet.ProtocolGenerator;

/// <summary>
/// Generates deterministic, strongly typed C# protocol model sources from a normalized definition.
/// </summary>
public sealed class ProtocolSourceGenerator
{
    private readonly ProtocolDefinitionValidator validator;

    /// <summary>
    /// Initializes a generator with the default protocol definition validator.
    /// </summary>
    public ProtocolSourceGenerator()
        : this(new ProtocolDefinitionValidator())
    {
    }

    /// <summary>
    /// Initializes a generator with an explicit protocol definition validator.
    /// </summary>
    public ProtocolSourceGenerator(ProtocolDefinitionValidator validator)
    {
        ArgumentNullException.ThrowIfNull(validator);
        this.validator = validator;
    }

    /// <summary>
    /// Validates and generates one self-contained protocol definition.
    /// </summary>
    public ProtocolGenerationResult Generate(
        ProtocolDefinition definition,
        ProtocolGenerationOptions options)
    {
        return Generate(definition, options, new ProtocolDefinitionValidationContext([]));
    }

    /// <summary>
    /// Validates and generates one protocol definition whose references may resolve against dependencies.
    /// </summary>
    public ProtocolGenerationResult Generate(
        ProtocolDefinition definition,
        ProtocolGenerationOptions options,
        ProtocolDefinitionValidationContext validationContext)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(validationContext);

        var diagnostics = validator
            .Validate(definition, validationContext)
            .Select(MapDiagnostic)
            .ToList();
        if (HasErrors(diagnostics))
        {
            return new ProtocolGenerationResult([], diagnostics);
        }

        if (!CSharpIdentifier.TryEscapeQualifiedName(options.RootNamespace, out string rootNamespace))
        {
            diagnostics.Add(new ProtocolGenerationDiagnostic(
                "LLRPG001",
                ProtocolGenerationDiagnosticSeverity.Error,
                "options:RootNamespace",
                $"'{options.RootNamespace}' is not a valid C# namespace."));
        }

        string dependencyRootNamespaceInput = options.DependencyRootNamespace ?? options.RootNamespace;
        if (!CSharpIdentifier.TryEscapeQualifiedName(dependencyRootNamespaceInput, out string dependencyRootNamespace))
        {
            diagnostics.Add(new ProtocolGenerationDiagnostic(
                "LLRPG001",
                ProtocolGenerationDiagnosticSeverity.Error,
                "options:DependencyRootNamespace",
                $"'{dependencyRootNamespaceInput}' is not a valid C# dependency namespace."));
        }

        if (!CSharpIdentifier.TryEscapeQualifiedName(options.VersionNamespace, out string versionNamespace))
        {
            diagnostics.Add(new ProtocolGenerationDiagnostic(
                "LLRPG001",
                ProtocolGenerationDiagnosticSeverity.Error,
                "options:VersionNamespace",
                $"'{options.VersionNamespace}' is not a valid C# namespace suffix."));
        }

        if (options.GenerateCodecs && options.ProtocolVersionValue is < 1 or > 3)
        {
            diagnostics.Add(new ProtocolGenerationDiagnostic(
                "LLRPG004",
                ProtocolGenerationDiagnosticSeverity.Error,
                "options:ProtocolVersionValue",
                $"Protocol version value {options.ProtocolVersionValue} is outside the supported range 1..3."));
        }

        ProtocolDefinition[] visibleDefinitions = [.. validationContext.Dependencies, definition];
        ValidateVisibleSymbolAmbiguity(visibleDefinitions, diagnostics);
        ValidateSupportedCardinalities(definition, diagnostics);
        if (options.GenerateCodecs)
        {
            AddCanonicalWireAmbiguityDiagnostics(definition, visibleDefinitions, diagnostics);
        }

        if (HasErrors(diagnostics))
        {
            return new ProtocolGenerationResult([], diagnostics);
        }

        var symbols = new ProtocolSymbolTable(
            definition,
            validationContext.Dependencies,
            rootNamespace,
            dependencyRootNamespace);
        var renderer = new ProtocolSourceRenderer(
            rootNamespace,
            versionNamespace,
            symbols,
            visibleDefinitions);
        IEnumerable<GeneratedSourceFile> renderedSources = renderer.Render(definition);
        if (options.GenerateCodecs)
        {
            renderedSources = renderedSources.Concat(
                renderer.RenderCodecs(definition, options.ProtocolVersionValue, options.RegistryModuleName));
        }

        IReadOnlyList<GeneratedSourceFile> sources = renderedSources
            .OrderBy(static source => source.HintName, StringComparer.Ordinal)
            .ToArray();
        return new ProtocolGenerationResult(sources, diagnostics);
    }

    private static ProtocolGenerationDiagnostic MapDiagnostic(DefinitionDiagnostic diagnostic)
    {
        return new ProtocolGenerationDiagnostic(
            diagnostic.Code,
            diagnostic.Severity == DefinitionDiagnosticSeverity.Error
                ? ProtocolGenerationDiagnosticSeverity.Error
                : ProtocolGenerationDiagnosticSeverity.Warning,
            diagnostic.Location,
            diagnostic.Message);
    }

    private static bool HasErrors(IEnumerable<ProtocolGenerationDiagnostic> diagnostics)
    {
        return diagnostics.Any(
            static diagnostic => diagnostic.Severity == ProtocolGenerationDiagnosticSeverity.Error);
    }

    private static void ValidateVisibleSymbolAmbiguity(
        IReadOnlyList<ProtocolDefinition> definitions,
        ICollection<ProtocolGenerationDiagnostic> diagnostics)
    {
        ValidateNameAmbiguity(
            definitions.SelectMany(static item => item.Messages.Select(static value => value.Name)
                .Concat(item.CustomMessages.Select(static value => value.Name))),
            "message",
            diagnostics);
        ValidateNameAmbiguity(
            definitions.SelectMany(static item => item.Parameters.Select(static value => value.Name)
                .Concat(item.CustomParameters.Select(static value => value.Name))),
            "parameter",
            diagnostics);
        ValidateNameAmbiguity(
            definitions.SelectMany(static item => item.Enumerations.Select(static value => value.Name)
                .Concat(item.CustomEnumerations.Select(static value => value.Name))),
            "enumeration",
            diagnostics);
        ValidateNameAmbiguity(
            definitions.SelectMany(static item => item.Choices.Select(static value => value.Name)),
            "choice",
            diagnostics);

        foreach (IGrouping<string, VendorDefinition> duplicate in definitions
                     .SelectMany(static item => item.Vendors)
                     .GroupBy(static vendor => vendor.Name, StringComparer.Ordinal)
                     .Where(static group => group.Select(static vendor => vendor.VendorId).Distinct().Count() > 1))
        {
            diagnostics.Add(new ProtocolGenerationDiagnostic(
                "LLRPG002",
                ProtocolGenerationDiagnosticSeverity.Error,
                $"vendor:{duplicate.Key}",
                $"Visible definitions assign more than one vendor identifier to '{duplicate.Key}'."));
        }
    }

    private static void ValidateNameAmbiguity(
        IEnumerable<string> names,
        string kind,
        ICollection<ProtocolGenerationDiagnostic> diagnostics)
    {
        foreach (IGrouping<string, string> duplicate in names
                     .GroupBy(static name => name, StringComparer.Ordinal)
                     .Where(static group => group.Count() > 1))
        {
            diagnostics.Add(new ProtocolGenerationDiagnostic(
                "LLRPG002",
                ProtocolGenerationDiagnosticSeverity.Error,
                $"{kind}:{duplicate.Key}",
                $"Visible definitions contain more than one {kind} named '{duplicate.Key}'."));
        }
    }

    private static void ValidateSupportedCardinalities(
        ProtocolDefinition definition,
        ICollection<ProtocolGenerationDiagnostic> diagnostics)
    {
        foreach ((string Location, IReadOnlyList<ProtocolMemberDefinition> Members) owner in
                 EnumerateMemberOwners(definition))
        {
            for (int index = 0; index < owner.Members.Count; index++)
            {
                Cardinality? cardinality = owner.Members[index] switch
                {
                    ParameterReferenceDefinition parameter => parameter.Cardinality,
                    ChoiceReferenceDefinition choice => choice.Cardinality,
                    _ => null,
                };
                if (cardinality?.Maximum == 0)
                {
                    diagnostics.Add(new ProtocolGenerationDiagnostic(
                        "LLRPG003",
                        ProtocolGenerationDiagnosticSeverity.Error,
                        $"{owner.Location}/member[{index}]",
                        "A reference with cardinality 0..0 has no generated model representation."));
                }
            }
        }
    }

    private static IEnumerable<(string Location, IReadOnlyList<ProtocolMemberDefinition> Members)>
        EnumerateMemberOwners(ProtocolDefinition definition)
    {
        foreach (MessageDefinition message in definition.Messages)
        {
            yield return ($"message:{message.Name}", message.Members);
        }

        foreach (ParameterDefinition parameter in definition.Parameters)
        {
            yield return ($"parameter:{parameter.Name}", parameter.Members);
        }

        foreach (CustomMessageDefinition message in definition.CustomMessages)
        {
            yield return ($"custom-message:{message.Name}", message.Members);
        }

        foreach (CustomParameterDefinition parameter in definition.CustomParameters)
        {
            yield return ($"custom-parameter:{parameter.Name}", parameter.Members);
        }
    }

    private static void AddCanonicalWireAmbiguityDiagnostics(
        ProtocolDefinition definition,
        IReadOnlyList<ProtocolDefinition> visibleDefinitions,
        ICollection<ProtocolGenerationDiagnostic> diagnostics)
    {
        HashSet<string> envelopeParameters = visibleDefinitions
            .SelectMany(static item => item.Parameters)
            .Where(static parameter => parameter.TypeNumber == 1023)
            .Select(static parameter => parameter.Name)
            .ToHashSet(StringComparer.Ordinal);
        IReadOnlyDictionary<string, ChoiceDefinition> choices = visibleDefinitions
            .SelectMany(static item => item.Choices)
            .GroupBy(static choice => choice.Name, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.Ordinal);

        foreach ((string Location, IReadOnlyList<ProtocolMemberDefinition> Members) owner in
                 EnumerateMemberOwners(definition))
        {
            for (int index = 0; index < owner.Members.Count - 1; index++)
            {
                if (owner.Members[index] is not ChoiceReferenceDefinition choice
                    || owner.Members[index + 1] is not ParameterReferenceDefinition parameter
                    || !envelopeParameters.Contains(parameter.ParameterType)
                    || !choices[choice.ChoiceType].ParameterTypes.Contains(
                        parameter.ParameterType,
                        StringComparer.Ordinal))
                {
                    continue;
                }

                diagnostics.Add(new ProtocolGenerationDiagnostic(
                    "LLRPG005",
                    ProtocolGenerationDiagnosticSeverity.Warning,
                    $"{owner.Location}/member[{index + 1}]",
                    "Adjacent choice and Custom slots both accept TLV type 1023; the generated decoder " +
                    "uses the canonical assignment to the trailing Custom slot."));
            }
        }
    }
}
