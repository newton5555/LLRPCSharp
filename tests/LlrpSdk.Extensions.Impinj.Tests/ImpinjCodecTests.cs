using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Parameters.V1_0_1;
using LlrpNet.Protocol.Registry;
using LlrpSdk.Extensions.Impinj.Enumerations.V1_0_1;
using LlrpSdk.Extensions.Impinj.Messages.V1_0_1;
using LlrpSdk.Extensions.Impinj.Parameters.V1_0_1;

namespace LlrpSdk.Extensions.Impinj.Tests;

public sealed class ImpinjCodecTests
{
    private readonly LlrpCodecRegistry _registry;

    public ImpinjCodecTests()
    {
        _registry = new LlrpCodecRegistry();
        LlrpNet.Protocol.Registry.V1_0_1.Llrp101StandardModule.Register(_registry);
        ImpinjProtocolModule.Instance.Register(_registry);
    }

    [Fact]
    public void ImpinjCustomMessages_HaveCorrectMessageTypeVendorAndSubtype()
    {
        Assert.Equal(1023, IMPINJ_ENABLE_EXTENSIONS.MessageType);
        Assert.Equal(25882U, IMPINJ_ENABLE_EXTENSIONS.VendorIdentifier);
        Assert.Equal((byte)21, IMPINJ_ENABLE_EXTENSIONS.Subtype);

        Assert.Equal(1023, IMPINJ_ENABLE_EXTENSIONS_RESPONSE.MessageType);
        Assert.Equal(25882U, IMPINJ_ENABLE_EXTENSIONS_RESPONSE.VendorIdentifier);
        Assert.Equal((byte)22, IMPINJ_ENABLE_EXTENSIONS_RESPONSE.Subtype);

        Assert.Equal(1023, IMPINJ_SAVE_SETTINGS.MessageType);
        Assert.Equal(25882U, IMPINJ_SAVE_SETTINGS.VendorIdentifier);
        Assert.Equal((byte)23, IMPINJ_SAVE_SETTINGS.Subtype);

        Assert.Equal(1023, IMPINJ_SAVE_SETTINGS_RESPONSE.MessageType);
        Assert.Equal(25882U, IMPINJ_SAVE_SETTINGS_RESPONSE.VendorIdentifier);
        Assert.Equal((byte)24, IMPINJ_SAVE_SETTINGS_RESPONSE.Subtype);
    }

    [Fact]
    public void ImpinjEnableExtensions_EncodeAndDecode_RoundTripsCorrectly()
    {
        var original = new IMPINJ_ENABLE_EXTENSIONS(
            MessageId: 12345,
            CustomItems: Array.Empty<LlrpNet.Protocol.Parameters.ILlrpParameter>());

        byte[] encoded = _registry.EncodeMessage(LlrpProtocolVersion.Version101, original);
        Assert.NotNull(encoded);
        Assert.True(encoded.Length >= 10);

        var decoded = _registry.DecodeMessage(encoded);
        var message = Assert.IsType<IMPINJ_ENABLE_EXTENSIONS>(decoded);

        Assert.Equal(12345U, message.MessageId);
        Assert.Empty(message.CustomItems);
    }

    [Fact]
    public void ImpinjEnableExtensionsResponse_EncodeAndDecode_RoundTripsCorrectly()
    {
        var status = new LLRPStatus(
            StatusCode: StatusCode.M_Success,
            ErrorDescription: "OK",
            FieldError: null,
            ParameterError: null);

        var original = new IMPINJ_ENABLE_EXTENSIONS_RESPONSE(
            MessageId: 12345,
            LLRPStatus: status,
            CustomItems: Array.Empty<LlrpNet.Protocol.Parameters.ILlrpParameter>());

        byte[] encoded = _registry.EncodeMessage(LlrpProtocolVersion.Version101, original);
        Assert.NotNull(encoded);

        var decoded = _registry.DecodeMessage(encoded);
        var message = Assert.IsType<IMPINJ_ENABLE_EXTENSIONS_RESPONSE>(decoded);

        Assert.Equal(12345U, message.MessageId);
        Assert.Equal(StatusCode.M_Success, message.LLRPStatus.StatusCode);
    }

    [Fact]
    public void ImpinjSaveSettings_EncodeAndDecode_RoundTripsCorrectly()
    {
        var original = new IMPINJ_SAVE_SETTINGS(
            MessageId: 99,
            SaveConfiguration: true,
            CustomItems: Array.Empty<LlrpNet.Protocol.Parameters.ILlrpParameter>());

        byte[] encoded = _registry.EncodeMessage(LlrpProtocolVersion.Version101, original);
        Assert.NotNull(encoded);

        var decoded = _registry.DecodeMessage(encoded);
        var message = Assert.IsType<IMPINJ_SAVE_SETTINGS>(decoded);

        Assert.Equal(99U, message.MessageId);
        Assert.True(message.SaveConfiguration);
    }

    [Fact]
    public void ImpinjGen2XInventoryConfig_EncodeAndDecode_RoundTripsCorrectly()
    {
        var config = new ImpinjGen2XInventoryConfig(
            CR: ImpinjGen2XCR.ID16,
            ID: ImpinjGen2XID.Part,
            Protection: ImpinjGen2XProtection.CRC5,
            CustomItems: Array.Empty<LlrpNet.Protocol.Parameters.ILlrpParameter>());

        byte[] encoded = _registry.EncodeParameter(LlrpProtocolVersion.Version101, config);
        Assert.NotNull(encoded);
        Assert.True(encoded.Length > 0);

        var decodeResult = _registry.DecodeParameter(LlrpProtocolVersion.Version101, encoded);
        var parameter = Assert.IsType<ImpinjGen2XInventoryConfig>(decodeResult.Parameter);

        Assert.Equal(ImpinjGen2XCR.ID16, parameter.CR);
        Assert.Equal(ImpinjGen2XID.Part, parameter.ID);
        Assert.Equal(ImpinjGen2XProtection.CRC5, parameter.Protection);
    }
}
