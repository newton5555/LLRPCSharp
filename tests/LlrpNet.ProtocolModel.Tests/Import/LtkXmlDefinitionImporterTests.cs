using System.Text;
using LlrpNet.ProtocolModel.Definitions;
using LlrpNet.ProtocolModel.Import;

namespace LlrpNet.ProtocolModel.Tests.Import;

public sealed class LtkXmlDefinitionImporterTests
{
    [Fact]
    public void Import_Llrp101Definition_PreservesTypesMembersAndCardinality()
    {
        ProtocolDefinition definition = ImportLlrp101();

        Assert.Equal(42, definition.Messages.Count);
        Assert.Equal(111, definition.Parameters.Count);
        Assert.Equal(42, definition.Enumerations.Count);
        Assert.Equal(14, definition.Choices.Count);
        Assert.Equal("llrp", definition.XmlNamespace?.Prefix);
        Assert.Equal(
            "http://www.llrp.org/ltk/schema/core/encoding/xml/1.0",
            definition.XmlNamespace?.Uri);

        MessageDefinition request = Assert.Single(
            definition.Messages,
            static message => message.Name == "GET_READER_CAPABILITIES");
        Assert.Equal((ushort)1, request.TypeNumber);
        Assert.True(request.Required);
        Assert.Equal("GET_READER_CAPABILITIES_RESPONSE", request.ResponseType);
        FieldDefinition requestedData = Assert.IsType<FieldDefinition>(request.Members[0]);
        Assert.Equal("RequestedData", requestedData.Name);
        Assert.Equal(ProtocolFieldType.U8, requestedData.FieldType);
        Assert.Equal("GetReaderCapabilitiesRequestedData", requestedData.Enumeration);
        var custom = Assert.IsType<ParameterReferenceDefinition>(request.Members[1]);
        Assert.Equal("Custom", custom.ParameterType);
        Assert.Equal(Cardinality.Create(0, maximum: null), custom.Cardinality);
    }

    [Fact]
    public void Import_DerivesTvAndTlvEncodingFromWireTypeRanges()
    {
        ProtocolDefinition definition = ImportLlrp101();

        ParameterDefinition epc96 = Assert.Single(
            definition.Parameters,
            static parameter => parameter.Name == "EPC_96");
        ParameterDefinition generalCapabilities = Assert.Single(
            definition.Parameters,
            static parameter => parameter.Name == "GeneralDeviceCapabilities");

        Assert.Equal((ushort)13, epc96.TypeNumber);
        Assert.Equal(ParameterEncodingKind.Tv, epc96.Encoding);
        Assert.Equal((ushort)137, generalCapabilities.TypeNumber);
        Assert.Equal(ParameterEncodingKind.Tlv, generalCapabilities.Encoding);
    }

    [Fact]
    public void Import_Stream_LeavesCallerStreamOpen()
    {
        const string xml = """
            <llrpdef xmlns="urn:test">
              <namespaceDefinition prefix="t" URI="urn:test:xml" />
            </llrpdef>
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        var importer = new LtkXmlDefinitionImporter();

        ProtocolDefinition definition = importer.Import(stream, "memory.xml");

        Assert.True(stream.CanRead);
        Assert.Equal("memory.xml", definition.SourceName);
        Assert.Equal("t", definition.XmlNamespace?.Prefix);
    }

    [Fact]
    public void Import_InvalidRepeat_ReportsSourceAndLine()
    {
        const string xml = """
            <llrpdef xmlns="urn:test">
              <messageDefinition name="REQUEST" typeNum="1" required="true">
                <parameter type="SomeParameter" repeat="many" />
              </messageDefinition>
            </llrpdef>
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        var importer = new LtkXmlDefinitionImporter();

        DefinitionImportException exception = Assert.Throws<DefinitionImportException>(
            () => importer.Import(stream, "invalid.xml"));

        Assert.Equal("invalid.xml", exception.SourceName);
        Assert.Equal(3, exception.LineNumber);
        Assert.Contains("repeat", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Import_Dtd_IsRejectedWithoutResolvingExternalResources()
    {
        const string xml = """
            <!DOCTYPE llrpdef [<!ENTITY injected SYSTEM "file:///not-allowed">]>
            <llrpdef xmlns="urn:test">&injected;</llrpdef>
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        var importer = new LtkXmlDefinitionImporter();

        DefinitionImportException exception = Assert.Throws<DefinitionImportException>(
            () => importer.Import(stream, "dtd.xml"));

        Assert.Contains("DTD", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Import_CustomDefinitions_PreservesVendorKeysNamespaceMembersAndAllowedIn()
    {
        const string xml = """
            <llrpdef xmlns="urn:test">
              <vendorDefinition name="Vendor" vendorID="1234" />
              <namespaceDefinition prefix="v" URI="urn:vendor:xml" />
              <customMessageDefinition name="VendorRequest" vendor="Vendor" subtype="1"
                                       namespace="v" responseType="VendorResponse">
                <field type="s32" name="SignedValue" enumeration="VendorValues" />
                <parameter type="CoreParameter" repeat="0-N" />
              </customMessageDefinition>
              <customMessageDefinition name="VendorResponse" vendor="Vendor" subtype="2" namespace="v" />
              <customParameterDefinition name="VendorParameter" vendor="Vendor" subtype="4294967295" namespace="v">
                <field type="u16v" name="Values" />
                <allowedIn type="CoreMessage" repeat="0-1" />
              </customParameterDefinition>
              <customEnumerationDefinition name="VendorValues" namespace="v">
                <entry name="Negative" value="-1" />
                <entry name="Positive" value="1" />
              </customEnumerationDefinition>
            </llrpdef>
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        var importer = new LtkXmlDefinitionImporter();

        ProtocolDefinition definition = importer.Import(stream, "custom.xml");

        VendorDefinition vendor = Assert.Single(definition.Vendors);
        Assert.Equal("Vendor", vendor.Name);
        Assert.Equal(1234U, vendor.VendorId);
        CustomMessageDefinition request = Assert.Single(
            definition.CustomMessages,
            static item => item.Name == "VendorRequest");
        Assert.Equal(byte.MinValue + 1, request.Subtype);
        Assert.Equal("v", request.Namespace);
        Assert.Equal("VendorResponse", request.ResponseType);
        FieldDefinition field = Assert.IsType<FieldDefinition>(request.Members[0]);
        Assert.Equal(ProtocolFieldType.S32, field.FieldType);
        CustomParameterDefinition parameter = Assert.Single(definition.CustomParameters);
        Assert.Equal(uint.MaxValue, parameter.Subtype);
        AllowedInDefinition allowed = Assert.Single(parameter.AllowedIn);
        Assert.Equal("CoreMessage", allowed.Type);
        Assert.Equal(Cardinality.Create(0, 1), allowed.Cardinality);
        CustomEnumerationDefinition enumeration = Assert.Single(definition.CustomEnumerations);
        Assert.Equal([-1L, 1L], enumeration.Entries.Select(static entry => entry.Value));
    }

    [Fact]
    public void Import_CustomSubtypeOutsideDeclaredWidth_IsRejected()
    {
        const string xml = """
            <llrpdef xmlns="urn:test">
              <customMessageDefinition name="VendorMessage" vendor="Vendor" subtype="256" namespace="v" />
            </llrpdef>
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));

        DefinitionImportException exception = Assert.Throws<DefinitionImportException>(
            () => new LtkXmlDefinitionImporter().Import(stream, "subtype.xml"));

        Assert.Contains("unsigned octet", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Import_XmlBooleanLexicalZeroAndOne_AreAccepted()
    {
        const string xml = """
            <llrpdef xmlns="urn:test">
              <messageDefinition name="FalseMessage" typeNum="1" required="0" />
              <messageDefinition name="TrueMessage" typeNum="2" required="1" />
            </llrpdef>
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));

        ProtocolDefinition definition = new LtkXmlDefinitionImporter().Import(stream, "boolean.xml");

        Assert.False(definition.Messages[0].Required);
        Assert.True(definition.Messages[1].Required);
    }

    [Fact]
    public void Import_TruncatedCustomDefinition_ReportsSourceAndLine()
    {
        const string xml = """
            <llrpdef xmlns="urn:test">
              <customParameterDefinition name="CutOff" vendor="Vendor" subtype="1" namespace="v">
                <field type="u8" name="Value" />
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));

        DefinitionImportException exception = Assert.Throws<DefinitionImportException>(
            () => new LtkXmlDefinitionImporter().Import(stream, "truncated.xml"));

        Assert.Equal("truncated.xml", exception.SourceName);
        Assert.True(exception.LineNumber > 0);
    }

    [Theory]
    [InlineData("<customMessageDefinition name=\"M\" vendor=\"V\" subtype=\"1\" namespace=\"v\" typo=\"x\" />")]
    [InlineData("<customParameterDefinition name=\"P\" vendor=\"V\" subtype=\"1\" namespace=\"v\"><unexpected /></customParameterDefinition>")]
    [InlineData("<customEnumerationDefinition name=\"E\" namespace=\"v\"><entry name=\"A\" value=\"1\" typo=\"x\" /></customEnumerationDefinition>")]
    [InlineData("<vendorDefinition name=\"V\" vendorID=\"1\">text</vendorDefinition>")]
    public void Import_UnknownCustomAttributeChildOrLeafText_IsRejected(string definitionElement)
    {
        string xml = $"<llrpdef xmlns=\"urn:test\">{definitionElement}</llrpdef>";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));

        Assert.Throws<DefinitionImportException>(
            () => new LtkXmlDefinitionImporter().Import(stream, "strict-custom.xml"));
    }

    [Theory]
    [InlineData("2")]
    [InlineData("0")]
    [InlineData("2-7")]
    [InlineData("many")]
    public void Import_RepeatOutsideLtkCardinalities_IsRejected(string repeat)
    {
        string xml = $$"""
            <llrpdef xmlns="urn:test">
              <messageDefinition name="REQUEST" typeNum="1" required="true">
                <parameter type="SomeParameter" repeat="{{repeat}}" />
              </messageDefinition>
            </llrpdef>
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));

        DefinitionImportException exception = Assert.Throws<DefinitionImportException>(
            () => new LtkXmlDefinitionImporter().Import(stream, "repeat.xml"));

        Assert.Contains("expected 1, 0-1, 1-N, or 0-N", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Import_UnknownNestedElementOrAttribute_IsRejected()
    {
        const string unknownChild = """
            <llrpdef xmlns="urn:test">
              <enumerationDefinition name="Values">
                <unexpected />
              </enumerationDefinition>
            </llrpdef>
            """;
        const string unknownAttribute = """
            <llrpdef xmlns="urn:test">
              <parameterDefinition name="Value" typeNum="128" required="true" typo="ignored" />
            </llrpdef>
            """;

        using var childStream = new MemoryStream(Encoding.UTF8.GetBytes(unknownChild));
        using var attributeStream = new MemoryStream(Encoding.UTF8.GetBytes(unknownAttribute));
        var importer = new LtkXmlDefinitionImporter();

        Assert.Throws<DefinitionImportException>(
            () => importer.Import(childStream, "child.xml"));
        Assert.Throws<DefinitionImportException>(
            () => importer.Import(attributeStream, "attribute.xml"));
    }

    [Fact]
    public void Import_UnknownRootAttribute_IsRejected()
    {
        const string xml = "<llrpdef xmlns=\"urn:test\" typo=\"x\" />";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));

        Assert.Throws<DefinitionImportException>(
            () => new LtkXmlDefinitionImporter().Import(stream, "root-attribute.xml"));
    }

    private static ProtocolDefinition ImportLlrp101()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "TestData", "llrp-1x0-def.xml");
        return new LtkXmlDefinitionImporter().Import(path);
    }
}
