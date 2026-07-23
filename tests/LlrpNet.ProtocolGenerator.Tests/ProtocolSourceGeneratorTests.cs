using LlrpNet.ProtocolGenerator.Generation;
using LlrpNet.ProtocolModel.Definitions;
using LlrpNet.ProtocolModel.Import;
using LlrpNet.ProtocolModel.Validation;

namespace LlrpNet.ProtocolGenerator.Tests;

public sealed class ProtocolSourceGeneratorTests
{
    private static readonly ProtocolGenerationOptions TestOptions = new()
    {
        RootNamespace = "Example.Llrp",
        VersionNamespace = "V9_1",
    };

    [Fact]
    public void Generate_IsDeterministic_AndMapsIdentifiersMembersAndChoices()
    {
        ProtocolDefinition definition = CreateIdentifierDefinition();
        var generator = new ProtocolSourceGenerator();

        ProtocolGenerationResult first = generator.Generate(definition, TestOptions);
        ProtocolGenerationResult second = generator.Generate(definition, TestOptions);

        Assert.True(first.Succeeded);
        Assert.Empty(first.Diagnostics);
        Assert.Equal(first.Sources, second.Sources);
        Assert.Equal(4, first.Sources.Count);
        Assert.All(first.Sources, static source => Assert.DoesNotContain("\r", source.SourceText));

        string message = GetSource(first, GeneratedSourceKind.Message, "class");
        Assert.Contains("public sealed record @class(", message, StringComparison.Ordinal);
        Assert.Contains("uint MessageId", message, StringComparison.Ordinal);
        Assert.Contains("byte MessageId_2", message, StringComparison.Ordinal);
        Assert.Contains("byte @event", message, StringComparison.Ordinal);
        Assert.Contains(
            "global::Example.Llrp.Enumerations.V9_1.Mode Mode",
            message,
            StringComparison.Ordinal);
        Assert.Contains(
            "global::Example.Llrp.Parameters.V9_1.Payload? Payload",
            message,
            StringComparison.Ordinal);
        Assert.Contains(
            "global::System.Collections.Generic.IReadOnlyList<global::Example.Llrp.Choices.V9_1.IPayloadChoice> PayloadChoiceItems",
            message,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Reserved", message, StringComparison.Ordinal);

        string parameter = GetSource(first, GeneratedSourceKind.Parameter, "Payload");
        Assert.Contains("byte A_B", parameter, StringComparison.Ordinal);
        Assert.Contains("byte A_B_2", parameter, StringComparison.Ordinal);
        Assert.Contains(
            "global::Example.Llrp.Choices.V9_1.IPayloadChoice",
            parameter,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_CustomDefinitions_EmitsVendorKeysAndDependencyReferences()
    {
        ProtocolDefinition core = CreateCoreDependency();
        ProtocolDefinition extension = CreateCustomDefinition();
        var context = new ProtocolDefinitionValidationContext([core]);

        ProtocolGenerationResult result = new ProtocolSourceGenerator().Generate(
            extension,
            TestOptions,
            context);

        Assert.True(result.Succeeded);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(3, result.Sources.Count);

        string message = GetSource(result, GeneratedSourceKind.Message, "VendorMessage");
        Assert.Contains("public const ushort TypeNumber = 1023;", message, StringComparison.Ordinal);
        Assert.Contains("public const uint VendorIdentifier = 25882U;", message, StringComparison.Ordinal);
        Assert.Contains("public const byte Subtype = 7;", message, StringComparison.Ordinal);
        Assert.Contains(
            "global::Example.Llrp.Parameters.V9_1.CoreParameter? CoreParameter",
            message,
            StringComparison.Ordinal);

        string parameter = GetSource(result, GeneratedSourceKind.Parameter, "VendorParameter");
        Assert.Contains("int SignedValue", parameter, StringComparison.Ordinal);
        Assert.Contains("public const uint Subtype = 4294967295U;", parameter, StringComparison.Ordinal);

        string enumeration = GetSource(result, GeneratedSourceKind.Enumeration, "VendorMode");
        Assert.Contains("public enum VendorMode : long", enumeration, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_MapsEveryFieldTypeAndDocumentsAllCardinalityForms()
    {
        ProtocolDefinition definition = CreateWireShapeDefinition();

        ProtocolGenerationResult result = new ProtocolSourceGenerator().Generate(definition, TestOptions);

        Assert.True(result.Succeeded);
        Assert.Empty(result.Diagnostics);

        string fields = GetSource(result, GeneratedSourceKind.Message, "FieldTypes");
        Assert.Contains("bool Flag", fields, StringComparison.Ordinal);
        Assert.Contains("byte TwoBits", fields, StringComparison.Ordinal);
        Assert.Contains("sbyte Signed8", fields, StringComparison.Ordinal);
        Assert.Contains("byte Unsigned8", fields, StringComparison.Ordinal);
        Assert.Contains("short Signed16", fields, StringComparison.Ordinal);
        Assert.Contains("ushort Unsigned16", fields, StringComparison.Ordinal);
        Assert.Contains("int Signed32", fields, StringComparison.Ordinal);
        Assert.Contains("uint Unsigned32", fields, StringComparison.Ordinal);
        Assert.Contains("ulong Unsigned64", fields, StringComparison.Ordinal);
        Assert.Contains("global::System.ReadOnlyMemory<byte> Fixed96", fields, StringComparison.Ordinal);
        Assert.Contains(
            "global::System.Collections.Generic.IReadOnlyList<bool> Bits",
            fields,
            StringComparison.Ordinal);
        Assert.Contains("global::System.ReadOnlyMemory<byte> Octets", fields, StringComparison.Ordinal);
        Assert.Contains(
            "global::System.Collections.Generic.IReadOnlyList<ushort> Words",
            fields,
            StringComparison.Ordinal);
        Assert.Contains(
            "global::System.Collections.Generic.IReadOnlyList<uint> DoubleWords",
            fields,
            StringComparison.Ordinal);
        Assert.Contains("string Text", fields, StringComparison.Ordinal);
        Assert.Contains("global::System.ReadOnlyMemory<byte> Tail", fields, StringComparison.Ordinal);

        string cardinalities = GetSource(result, GeneratedSourceKind.Message, "Cardinalities");
        Assert.Contains(
            "global::Example.Llrp.Parameters.V9_1.Child Child",
            cardinalities,
            StringComparison.Ordinal);
        Assert.Contains(
            "global::Example.Llrp.Parameters.V9_1.Child? Child_2",
            cardinalities,
            StringComparison.Ordinal);
        Assert.Contains(
            "global::System.Collections.Generic.IReadOnlyList<global::Example.Llrp.Parameters.V9_1.Child> ChildItems",
            cardinalities,
            StringComparison.Ordinal);
        Assert.Contains("cardinality 1..N", cardinalities, StringComparison.Ordinal);
        Assert.Contains("cardinality 0..N", cardinalities, StringComparison.Ordinal);
        Assert.Contains("LLRP choice 'ChildChoice' with cardinality 0..1", cardinalities, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_Llrp101Definition_EmitsEveryCoreDefinitionWithoutDiagnostics()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "TestData", "llrp-1x0-def.xml");
        ProtocolDefinition definition = new LtkXmlDefinitionImporter().Import(path);

        ProtocolGenerationResult result = new ProtocolSourceGenerator().Generate(
            definition,
            new ProtocolGenerationOptions
            {
                RootNamespace = "Generated.Llrp",
                VersionNamespace = "V1_0_1",
            });

        Assert.True(result.Succeeded);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(209, result.Sources.Count);
        Assert.Equal(42, result.Sources.Count(static source => source.Kind == GeneratedSourceKind.Enumeration));
        Assert.Equal(14, result.Sources.Count(static source => source.Kind == GeneratedSourceKind.Choice));
        Assert.Equal(42, result.Sources.Count(static source => source.Kind == GeneratedSourceKind.Message));
        Assert.Equal(111, result.Sources.Count(static source => source.Kind == GeneratedSourceKind.Parameter));
        Assert.Equal(
            result.Sources.Count,
            result.Sources.Select(static source => source.HintName).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Generate_ModelValidationError_ReturnsNoSourcesAndPreservesDiagnosticCode()
    {
        var definition = new ProtocolDefinition(
            "invalid",
            xmlNamespace: null,
            messages:
            [
                new MessageDefinition(
                    "Request",
                    1,
                    required: true,
                    responseType: null,
                    [new ParameterReferenceDefinition("Missing", Cardinality.Create(1, 1))]),
            ],
            parameters: [],
            enumerations: [],
            choices: []);

        ProtocolGenerationResult result = new ProtocolSourceGenerator().Generate(definition, TestOptions);

        Assert.False(result.Succeeded);
        Assert.Empty(result.Sources);
        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Code == "LLRPM016");
    }

    [Fact]
    public void Generate_InvalidNamespace_ReturnsExplicitOptionDiagnostic()
    {
        var definition = new ProtocolDefinition(
            "empty",
            xmlNamespace: null,
            messages: [],
            parameters: [],
            enumerations: [],
            choices: []);

        ProtocolGenerationResult result = new ProtocolSourceGenerator().Generate(
            definition,
            TestOptions with { RootNamespace = "not-a-namespace" });

        Assert.False(result.Succeeded);
        Assert.Empty(result.Sources);
        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Code == "LLRPG001");
    }

    [Fact]
    public void Generate_ZeroMaximumCardinality_ReturnsUnsupportedLayoutDiagnostic()
    {
        var definition = new ProtocolDefinition(
            "zero-cardinality",
            xmlNamespace: null,
            messages:
            [
                new MessageDefinition(
                    "Request",
                    1,
                    required: true,
                    responseType: null,
                    [new ParameterReferenceDefinition("Child", Cardinality.Create(0, 0))]),
            ],
            parameters:
            [
                new ParameterDefinition("Child", 128, required: true, []),
            ],
            enumerations: [],
            choices: []);

        ProtocolGenerationResult result = new ProtocolSourceGenerator().Generate(definition, TestOptions);

        Assert.False(result.Succeeded);
        Assert.Empty(result.Sources);
        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Code == "LLRPG003");
    }

    private static ProtocolDefinition CreateIdentifierDefinition()
    {
        return new ProtocolDefinition(
            "identifiers",
            xmlNamespace: null,
            messages:
            [
                new MessageDefinition(
                    "class",
                    1,
                    required: true,
                    responseType: null,
                    [
                        new FieldDefinition("MessageId", ProtocolFieldType.U8, Enumeration: null, Format: null),
                        new FieldDefinition("event", ProtocolFieldType.U8, Enumeration: null, Format: null),
                        new FieldDefinition("Mode", ProtocolFieldType.U8, Enumeration: "Mode", Format: null),
                        new ReservedBitsDefinition(8),
                        new ParameterReferenceDefinition("Payload", Cardinality.Create(0, 1)),
                        new ChoiceReferenceDefinition("PayloadChoice", Cardinality.Create(0, maximum: null)),
                    ]),
            ],
            parameters:
            [
                new ParameterDefinition(
                    "Payload",
                    128,
                    required: true,
                    [
                        new FieldDefinition("A-B", ProtocolFieldType.U8, Enumeration: null, Format: null),
                        new FieldDefinition("A_B", ProtocolFieldType.U8, Enumeration: null, Format: null),
                    ]),
            ],
            enumerations:
            [
                new EnumerationDefinition(
                    "Mode",
                    [
                        new EnumerationEntryDefinition("None", 0),
                        new EnumerationEntryDefinition("Active", 1),
                    ]),
            ],
            choices:
            [
                new ChoiceDefinition("PayloadChoice", ["Payload"]),
            ]);
    }

    private static ProtocolDefinition CreateCoreDependency()
    {
        return new ProtocolDefinition(
            "core",
            new ProtocolNamespaceDefinition("c", "urn:core", SchemaLocation: null),
            messages:
            [
                new MessageDefinition("CoreMessage", 1, required: true, responseType: null, []),
            ],
            parameters:
            [
                new ParameterDefinition("CoreParameter", 128, required: true, []),
            ],
            enumerations: [],
            choices: []);
    }

    private static ProtocolDefinition CreateWireShapeDefinition()
    {
        return new ProtocolDefinition(
            "wire-shapes",
            xmlNamespace: null,
            messages:
            [
                new MessageDefinition(
                    "FieldTypes",
                    1,
                    required: true,
                    responseType: null,
                    [
                        new FieldDefinition("Flag", ProtocolFieldType.U1, Enumeration: null, Format: null),
                        new FieldDefinition("TwoBits", ProtocolFieldType.U2, Enumeration: null, Format: null),
                        new ReservedBitsDefinition(5),
                        new FieldDefinition("Signed8", ProtocolFieldType.S8, Enumeration: null, Format: null),
                        new FieldDefinition("Unsigned8", ProtocolFieldType.U8, Enumeration: null, Format: null),
                        new FieldDefinition("Signed16", ProtocolFieldType.S16, Enumeration: null, Format: null),
                        new FieldDefinition("Unsigned16", ProtocolFieldType.U16, Enumeration: null, Format: null),
                        new FieldDefinition("Signed32", ProtocolFieldType.S32, Enumeration: null, Format: null),
                        new FieldDefinition("Unsigned32", ProtocolFieldType.U32, Enumeration: null, Format: null),
                        new FieldDefinition("Unsigned64", ProtocolFieldType.U64, Enumeration: null, Format: null),
                        new FieldDefinition("Fixed96", ProtocolFieldType.U96, Enumeration: null, Format: null),
                        new FieldDefinition("Bits", ProtocolFieldType.U1Vector, Enumeration: null, Format: null),
                        new FieldDefinition("Octets", ProtocolFieldType.U8Vector, Enumeration: null, Format: null),
                        new FieldDefinition("Words", ProtocolFieldType.U16Vector, Enumeration: null, Format: null),
                        new FieldDefinition("DoubleWords", ProtocolFieldType.U32Vector, Enumeration: null, Format: null),
                        new FieldDefinition("Text", ProtocolFieldType.Utf8Vector, Enumeration: null, Format: null),
                        new FieldDefinition("Tail", ProtocolFieldType.BytesToEnd, Enumeration: null, Format: null),
                    ]),
                new MessageDefinition(
                    "Cardinalities",
                    2,
                    required: true,
                    responseType: null,
                    [
                        new ParameterReferenceDefinition("Child", Cardinality.Create(1, 1)),
                        new ParameterReferenceDefinition("Child", Cardinality.Create(0, 1)),
                        new ParameterReferenceDefinition("Child", Cardinality.Create(1, maximum: null)),
                        new ParameterReferenceDefinition("Child", Cardinality.Create(0, maximum: null)),
                        new ChoiceReferenceDefinition("ChildChoice", Cardinality.Create(1, 1)),
                        new ChoiceReferenceDefinition("ChildChoice", Cardinality.Create(0, 1)),
                        new ChoiceReferenceDefinition("ChildChoice", Cardinality.Create(1, maximum: null)),
                        new ChoiceReferenceDefinition("ChildChoice", Cardinality.Create(0, maximum: null)),
                    ]),
            ],
            parameters:
            [
                new ParameterDefinition("Child", 128, required: true, []),
            ],
            enumerations: [],
            choices:
            [
                new ChoiceDefinition("ChildChoice", ["Child"]),
            ]);
    }

    private static ProtocolDefinition CreateCustomDefinition()
    {
        return new ProtocolDefinition(
            "custom",
            new ProtocolNamespaceDefinition("v", "urn:vendor", SchemaLocation: null),
            messages: [],
            parameters: [],
            enumerations: [],
            choices: [],
            vendors:
            [
                new VendorDefinition("Vendor", 25882),
            ],
            customMessages:
            [
                new CustomMessageDefinition(
                    "VendorMessage",
                    "Vendor",
                    subtype: 7,
                    "v",
                    responseType: null,
                    [
                        new FieldDefinition("Mode", ProtocolFieldType.U8, "VendorMode", Format: null),
                        new ParameterReferenceDefinition("CoreParameter", Cardinality.Create(0, 1)),
                    ]),
            ],
            customParameters:
            [
                new CustomParameterDefinition(
                    "VendorParameter",
                    "Vendor",
                    uint.MaxValue,
                    "v",
                    [new FieldDefinition("SignedValue", ProtocolFieldType.S32, Enumeration: null, Format: null)],
                    [new AllowedInDefinition("CoreMessage", Cardinality.Create(0, 1))]),
            ],
            customEnumerations:
            [
                new CustomEnumerationDefinition(
                    "VendorMode",
                    "v",
                    [
                        new EnumerationEntryDefinition("Off", 0),
                        new EnumerationEntryDefinition("On", 1),
                    ]),
            ]);
    }

    private static string GetSource(
        ProtocolGenerationResult result,
        GeneratedSourceKind kind,
        string hintFragment)
    {
        return Assert.Single(
            result.Sources,
            source => source.Kind == kind && source.HintName.Contains(hintFragment, StringComparison.Ordinal))
            .SourceText;
    }
}
