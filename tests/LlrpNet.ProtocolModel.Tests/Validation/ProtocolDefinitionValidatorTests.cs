using LlrpNet.ProtocolModel.Definitions;
using LlrpNet.ProtocolModel.Import;
using LlrpNet.ProtocolModel.Validation;

namespace LlrpNet.ProtocolModel.Tests.Validation;

public sealed class ProtocolDefinitionValidatorTests
{
    [Fact]
    public void Validate_Llrp101Definition_HasNoModelErrors()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "TestData", "llrp-1x0-def.xml");
        ProtocolDefinition definition = new LtkXmlDefinitionImporter().Import(path);

        IReadOnlyList<DefinitionDiagnostic> diagnostics =
            new ProtocolDefinitionValidator().Validate(definition);

        Assert.DoesNotContain(
            diagnostics,
            static diagnostic => diagnostic.Severity == DefinitionDiagnosticSeverity.Error);
    }

    [Fact]
    public void Validate_ReportsDuplicateWireKeysAndDanglingReferences()
    {
        var definition = new ProtocolDefinition(
            "invalid",
            xmlNamespace: null,
            messages:
            [
                new MessageDefinition(
                    "RequestA",
                    1,
                    required: true,
                    responseType: "MissingResponse",
                    [new ParameterReferenceDefinition("MissingParameter", Cardinality.Create(1, 1))]),
                new MessageDefinition("RequestB", 1, required: true, responseType: null, []),
            ],
            parameters: [],
            enumerations: [],
            choices: []);

        IReadOnlyList<DefinitionDiagnostic> diagnostics =
            new ProtocolDefinitionValidator().Validate(definition);

        Assert.Contains(diagnostics, static diagnostic => diagnostic.Code == "LLRPM001");
        Assert.Contains(diagnostics, static diagnostic => diagnostic.Code == "LLRPM016");
        Assert.Contains(diagnostics, static diagnostic => diagnostic.Code == "LLRPM022");
    }

    [Fact]
    public void Validate_RejectsVariableLengthTvParameter()
    {
        var definition = new ProtocolDefinition(
            "invalid-tv",
            xmlNamespace: null,
            messages: [],
            parameters:
            [
                new ParameterDefinition(
                    "VariableTv",
                    1,
                    required: false,
                    [new FieldDefinition("Value", ProtocolFieldType.U8Vector, Enumeration: null, Format: null)]),
            ],
            enumerations: [],
            choices: []);

        IReadOnlyList<DefinitionDiagnostic> diagnostics =
            new ProtocolDefinitionValidator().Validate(definition);

        DefinitionDiagnostic diagnostic = Assert.Single(
            diagnostics,
            static item => item.Code == "LLRPM011");
        Assert.Equal(DefinitionDiagnosticSeverity.Error, diagnostic.Severity);
    }

    [Fact]
    public void Validate_RejectsEnumerationValueOutsideReferencedField()
    {
        var definition = new ProtocolDefinition(
            "invalid-enum",
            xmlNamespace: null,
            messages:
            [
                new MessageDefinition(
                    "Request",
                    1,
                    required: true,
                    responseType: null,
                    [
                        new FieldDefinition(
                            "Mode",
                            ProtocolFieldType.U2,
                            Enumeration: "Modes",
                            Format: null),
                        new ReservedBitsDefinition(6),
                    ]),
            ],
            parameters: [],
            enumerations:
            [
                new EnumerationDefinition(
                    "Modes",
                    [new EnumerationEntryDefinition("TooLarge", 4)]),
            ],
            choices: []);

        IReadOnlyList<DefinitionDiagnostic> diagnostics =
            new ProtocolDefinitionValidator().Validate(definition);

        Assert.Contains(diagnostics, static diagnostic => diagnostic.Code == "LLRPM025");
    }

    [Fact]
    public void Validate_RejectsUnalignedNestedBoundaryAndFieldAfterParameter()
    {
        var definition = new ProtocolDefinition(
            "invalid-layout",
            xmlNamespace: null,
            messages:
            [
                new MessageDefinition(
                    "Request",
                    1,
                    required: true,
                    responseType: null,
                    [
                        new FieldDefinition("Flag", ProtocolFieldType.U1, Enumeration: null, Format: null),
                        new ParameterReferenceDefinition("Child", Cardinality.Create(1, 1)),
                        new ReservedBitsDefinition(7),
                    ]),
            ],
            parameters:
            [
                new ParameterDefinition(
                    "Child",
                    128,
                    required: true,
                    []),
            ],
            enumerations: [],
            choices: []);

        IReadOnlyList<DefinitionDiagnostic> diagnostics =
            new ProtocolDefinitionValidator().Validate(definition);

        Assert.Contains(diagnostics, static diagnostic => diagnostic.Code == "LLRPM023");
        Assert.Contains(diagnostics, static diagnostic => diagnostic.Code == "LLRPM026");
    }

    [Fact]
    public void Validate_CustomDefinitionWithCoreDependency_ResolvesAllExternalReferences()
    {
        ProtocolDefinition core = CreateCoreDependency();
        ProtocolDefinition extension = CreateValidExtension();

        IReadOnlyList<DefinitionDiagnostic> diagnostics = new ProtocolDefinitionValidator().Validate(
            extension,
            new ProtocolDefinitionValidationContext([core]));

        Assert.DoesNotContain(
            diagnostics,
            static diagnostic => diagnostic.Severity == DefinitionDiagnosticSeverity.Error);
    }

    [Fact]
    public void Validate_CustomDefinitions_ReportsWireKeyVendorNamespaceAndNameConflicts()
    {
        ProtocolDefinition core = CreateCoreDependency();
        var extension = new ProtocolDefinition(
            "invalid-extension",
            new ProtocolNamespaceDefinition("v", "urn:vendor", SchemaLocation: null),
            messages: [],
            parameters: [],
            enumerations: [],
            choices: [],
            vendors: [new VendorDefinition("Vendor", 1234)],
            customMessages:
            [
                new CustomMessageDefinition("CoreMessage", "MissingVendor", 1, "missing", null, []),
                new CustomMessageDefinition("OtherMessage", "MissingVendor", 1, "missing", null, []),
            ]);

        IReadOnlyList<DefinitionDiagnostic> diagnostics = new ProtocolDefinitionValidator().Validate(
            extension,
            new ProtocolDefinitionValidationContext([core]));

        Assert.Contains(diagnostics, static diagnostic => diagnostic.Code == "LLRPM028");
        Assert.Contains(diagnostics, static diagnostic => diagnostic.Code == "LLRPM029");
        Assert.Contains(diagnostics, static diagnostic => diagnostic.Code == "LLRPM030");
        Assert.Contains(diagnostics, static diagnostic => diagnostic.Code == "LLRPM032");
    }

    [Fact]
    public void Validate_CustomReferences_ReportsDanglingResponseMemberEnumerationAndAllowedIn()
    {
        var definition = new ProtocolDefinition(
            "dangling-extension",
            new ProtocolNamespaceDefinition("v", "urn:vendor", SchemaLocation: null),
            messages: [],
            parameters: [],
            enumerations: [],
            choices: [],
            vendors: [new VendorDefinition("Vendor", 1234)],
            customMessages:
            [
                new CustomMessageDefinition(
                    "VendorRequest",
                    "Vendor",
                    1,
                    "v",
                    "MissingResponse",
                    [new ParameterReferenceDefinition("MissingParameter", Cardinality.Create(1, 1))]),
            ],
            customParameters:
            [
                new CustomParameterDefinition(
                    "VendorParameter",
                    "Vendor",
                    1,
                    "v",
                    [new FieldDefinition("Mode", ProtocolFieldType.U8, "MissingEnumeration", Format: null)],
                    [new AllowedInDefinition("MissingContainer", Cardinality.Create(0, 1))]),
            ]);

        IReadOnlyList<DefinitionDiagnostic> diagnostics =
            new ProtocolDefinitionValidator().Validate(definition);

        Assert.Contains(diagnostics, static diagnostic => diagnostic.Code == "LLRPM001");
        Assert.Contains(diagnostics, static diagnostic => diagnostic.Code == "LLRPM012");
        Assert.Contains(diagnostics, static diagnostic => diagnostic.Code == "LLRPM016");
        Assert.Contains(diagnostics, static diagnostic => diagnostic.Code == "LLRPM031");
    }

    [Fact]
    public void Validate_CustomClrAndEnumerationEntryNames_MustBeValidIdentifiers()
    {
        var definition = new ProtocolDefinition(
            "invalid-names",
            new ProtocolNamespaceDefinition("v", "urn:vendor", SchemaLocation: null),
            messages: [],
            parameters: [],
            enumerations:
            [
                new EnumerationDefinition(
                    "CoreValues",
                    [new EnumerationEntryDefinition(" ", 0)]),
            ],
            choices: [],
            vendors: [new VendorDefinition("Vendor", 1234)],
            customParameters:
            [
                new CustomParameterDefinition("bad-name", "Vendor", 1, "v", [], []),
            ],
            customEnumerations:
            [
                new CustomEnumerationDefinition(
                    "VendorValues",
                    "v",
                    [new EnumerationEntryDefinition("class", 0)]),
            ]);

        IReadOnlyList<DefinitionDiagnostic> diagnostics =
            new ProtocolDefinitionValidator().Validate(definition);

        Assert.True(diagnostics.Count(static diagnostic => diagnostic.Code == "LLRPM027") >= 3);
    }

    [Fact]
    public void Validate_Signed32Enumeration_UsesSigned32Range()
    {
        var definition = new ProtocolDefinition(
            "s32-range",
            xmlNamespace: null,
            messages:
            [
                new MessageDefinition(
                    "Message",
                    1,
                    required: true,
                    responseType: null,
                    [
                        new FieldDefinition("Value", ProtocolFieldType.S32, "Values", Format: null),
                    ]),
            ],
            parameters: [],
            enumerations:
            [
                new EnumerationDefinition(
                    "Values",
                    [new EnumerationEntryDefinition("TooLarge", (long)int.MaxValue + 1)]),
            ],
            choices: []);

        IReadOnlyList<DefinitionDiagnostic> diagnostics =
            new ProtocolDefinitionValidator().Validate(definition);

        Assert.Contains(diagnostics, static diagnostic => diagnostic.Code == "LLRPM025");
    }

    [Fact]
    public void Validate_MinimumCompleteTlvLength_IncludesHeaderAndVectorPrefix()
    {
        var definition = new ProtocolDefinition(
            "oversize-tlv",
            xmlNamespace: null,
            messages: [],
            parameters:
            [
                new ParameterDefinition(
                    "Huge",
                    128,
                    required: false,
                    [new ReservedBitsDefinition(524288)]),
                new ParameterDefinition(
                    "PrefixOverflow",
                    129,
                    required: false,
                    [
                        new ReservedBitsDefinition(65530 * 8),
                        new FieldDefinition("Values", ProtocolFieldType.U8Vector, Enumeration: null, Format: null),
                    ]),
            ],
            enumerations: [],
            choices: []);

        IReadOnlyList<DefinitionDiagnostic> diagnostics =
            new ProtocolDefinitionValidator().Validate(definition);

        Assert.Equal(2, diagnostics.Count(static diagnostic => diagnostic.Code == "LLRPM033"));
    }

    [Fact]
    public void Validate_RequiredChoiceCycle_IsRejectedButOptionalSelfReferenceIsFinite()
    {
        var definition = new ProtocolDefinition(
            "cycles",
            xmlNamespace: null,
            messages: [],
            parameters:
            [
                new ParameterDefinition(
                    "RequiredCycle",
                    128,
                    required: false,
                    [new ChoiceReferenceDefinition("RecursiveChoice", Cardinality.Create(1, 1))]),
                new ParameterDefinition(
                    "OptionalSelf",
                    129,
                    required: false,
                    [new ParameterReferenceDefinition("OptionalSelf", Cardinality.Create(0, maximum: null))]),
            ],
            enumerations: [],
            choices:
            [
                new ChoiceDefinition("RecursiveChoice", ["RequiredCycle"]),
            ]);

        IReadOnlyList<DefinitionDiagnostic> diagnostics =
            new ProtocolDefinitionValidator().Validate(definition);

        Assert.Contains(
            diagnostics,
            static diagnostic => diagnostic.Code == "LLRPM035" && diagnostic.Location == "parameter:RequiredCycle");
        Assert.DoesNotContain(
            diagnostics,
            static diagnostic => diagnostic.Code == "LLRPM035" && diagnostic.Location == "parameter:OptionalSelf");
    }

    [Fact]
    public void Validate_UnknownDerivedMember_IsReported()
    {
        var definition = new ProtocolDefinition(
            "unknown-member",
            xmlNamespace: null,
            messages:
            [
                new MessageDefinition("Message", 1, required: true, responseType: null, [new UnsupportedMember()]),
            ],
            parameters: [],
            enumerations: [],
            choices: []);

        IReadOnlyList<DefinitionDiagnostic> diagnostics =
            new ProtocolDefinitionValidator().Validate(definition);

        Assert.Contains(diagnostics, static diagnostic => diagnostic.Code == "LLRPM036");
    }

    private static ProtocolDefinition CreateCoreDependency()
    {
        return new ProtocolDefinition(
            "core",
            new ProtocolNamespaceDefinition("core", "urn:core", SchemaLocation: null),
            messages:
            [
                new MessageDefinition("CoreMessage", 1, required: true, responseType: "CoreResponse", []),
                new MessageDefinition("CoreResponse", 2, required: true, responseType: null, []),
            ],
            parameters:
            [
                new ParameterDefinition("CoreParameter", 128, required: true, []),
            ],
            enumerations:
            [
                new EnumerationDefinition("CoreValues", [new EnumerationEntryDefinition("One", 1)]),
            ],
            choices:
            [
                new ChoiceDefinition("CoreChoice", ["CoreParameter"]),
            ]);
    }

    private static ProtocolDefinition CreateValidExtension()
    {
        return new ProtocolDefinition(
            "extension",
            new ProtocolNamespaceDefinition("v", "urn:vendor", SchemaLocation: null),
            messages: [],
            parameters: [],
            enumerations: [],
            choices: [],
            vendors: [new VendorDefinition("Vendor", 1234)],
            customMessages:
            [
                new CustomMessageDefinition(
                    "VendorRequest",
                    "Vendor",
                    1,
                    "v",
                    "VendorResponse",
                    [new ParameterReferenceDefinition("CoreParameter", Cardinality.Create(1, 1))]),
                new CustomMessageDefinition("VendorResponse", "Vendor", 2, "v", null, []),
            ],
            customParameters:
            [
                new CustomParameterDefinition(
                    "VendorParameter",
                    "Vendor",
                    1,
                    "v",
                    [
                        new FieldDefinition("Mode", ProtocolFieldType.S32, "VendorValues", Format: null),
                        new ParameterReferenceDefinition("CoreParameter", Cardinality.Create(0, maximum: null)),
                    ],
                    [
                        new AllowedInDefinition("CoreMessage", Cardinality.Create(0, 1)),
                        new AllowedInDefinition("CoreChoice", Cardinality.Create(0, maximum: null)),
                    ]),
            ],
            customEnumerations:
            [
                new CustomEnumerationDefinition(
                    "VendorValues",
                    "v",
                    [
                        new EnumerationEntryDefinition("Negative", -1),
                        new EnumerationEntryDefinition("Positive", 1),
                    ]),
            ]);
    }

    private sealed record UnsupportedMember : ProtocolMemberDefinition;
}
