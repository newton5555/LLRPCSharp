using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Enumerations.V1_0_1;
using LlrpNet.Protocol.Messages;
using LlrpNet.Protocol.Messages.V1_0_1;
using LlrpNet.Protocol.Parameters;
using LlrpNet.Protocol.Parameters.V1_0_1;
using LlrpNet.Protocol.Registry;
using LlrpNet.Protocol.Registry.V1_0_1;

namespace LlrpSdk;

/// <summary>LLRP 1.0.1 implementation of the SDK protocol-adapter boundary.</summary>
internal sealed class Llrp101ProtocolAdapter : ILlrpProtocolAdapter
{
    public LlrpProtocolVersion Version => LlrpProtocolVersion.Version101;

    public void RegisterStandardCodecs(LlrpCodecRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        Llrp101StandardModule.Register(registry);
    }

    public ILlrpParameter CompileInventory(ReaderSettings settings) => Llrp101InventoryCompiler.Compile(settings);

    public IReadOnlyList<TagReport> TranslateTagReports(ILlrpMessage message) =>
        message is RO_ACCESS_REPORT report ? Llrp101TagReportTranslator.Translate(report) : [];

    public async Task AddRoSpecAsync(LlrpReader reader, uint messageId, ILlrpParameter roSpec, CancellationToken cancellationToken)
    {
        ADD_ROSPEC_RESPONSE response = await reader.TransactAsync<ADD_ROSPEC_RESPONSE>(
            new ADD_ROSPEC(messageId, RequireRoSpec(roSpec)), timeout: null, cancellationToken).ConfigureAwait(false);
        EnsureSuccess("ADD_ROSPEC", response.LLRPStatus);
    }

    public async Task DeleteRoSpecAsync(LlrpReader reader, uint messageId, uint roSpecId, CancellationToken cancellationToken)
    {
        DELETE_ROSPEC_RESPONSE response = await reader.TransactAsync<DELETE_ROSPEC_RESPONSE>(
            new DELETE_ROSPEC(messageId, roSpecId), timeout: null, cancellationToken).ConfigureAwait(false);
        EnsureSuccess("DELETE_ROSPEC", response.LLRPStatus);
    }

    public async Task EnableRoSpecAsync(LlrpReader reader, uint messageId, uint roSpecId, CancellationToken cancellationToken)
    {
        ENABLE_ROSPEC_RESPONSE response = await reader.TransactAsync<ENABLE_ROSPEC_RESPONSE>(
            new ENABLE_ROSPEC(messageId, roSpecId), timeout: null, cancellationToken).ConfigureAwait(false);
        EnsureSuccess("ENABLE_ROSPEC", response.LLRPStatus);
    }

    public async Task DisableRoSpecAsync(LlrpReader reader, uint messageId, uint roSpecId, CancellationToken cancellationToken)
    {
        DISABLE_ROSPEC_RESPONSE response = await reader.TransactAsync<DISABLE_ROSPEC_RESPONSE>(
            new DISABLE_ROSPEC(messageId, roSpecId), timeout: null, cancellationToken).ConfigureAwait(false);
        EnsureSuccess("DISABLE_ROSPEC", response.LLRPStatus);
    }

    public async Task StartRoSpecAsync(LlrpReader reader, uint messageId, uint roSpecId, CancellationToken cancellationToken)
    {
        START_ROSPEC_RESPONSE response = await reader.TransactAsync<START_ROSPEC_RESPONSE>(
            new START_ROSPEC(messageId, roSpecId), timeout: null, cancellationToken).ConfigureAwait(false);
        EnsureSuccess("START_ROSPEC", response.LLRPStatus);
    }

    public async Task StopRoSpecAsync(LlrpReader reader, uint messageId, uint roSpecId, CancellationToken cancellationToken)
    {
        STOP_ROSPEC_RESPONSE response = await reader.TransactAsync<STOP_ROSPEC_RESPONSE>(
            new STOP_ROSPEC(messageId, roSpecId), timeout: null, cancellationToken).ConfigureAwait(false);
        EnsureSuccess("STOP_ROSPEC", response.LLRPStatus);
    }

    public async Task<IReadOnlyList<ILlrpParameter>> GetRoSpecsAsync(
        LlrpReader reader, uint messageId, CancellationToken cancellationToken)
    {
        GET_ROSPECS_RESPONSE response = await reader.TransactAsync<GET_ROSPECS_RESPONSE>(
            new GET_ROSPECS(messageId), timeout: null, cancellationToken).ConfigureAwait(false);
        EnsureSuccess("GET_ROSPECS", response.LLRPStatus);
        return Array.AsReadOnly(response.ROSpecItems.Cast<ILlrpParameter>().ToArray());
    }

    public async Task AddAccessSpecAsync(
        LlrpReader reader, uint messageId, ILlrpParameter accessSpec, CancellationToken cancellationToken)
    {
        ADD_ACCESSSPEC_RESPONSE response = await reader.TransactAsync<ADD_ACCESSSPEC_RESPONSE>(
            new ADD_ACCESSSPEC(messageId, RequireAccessSpec(accessSpec)), timeout: null, cancellationToken).ConfigureAwait(false);
        EnsureSuccess("ADD_ACCESSSPEC", response.LLRPStatus);
    }

    public async Task DeleteAccessSpecAsync(
        LlrpReader reader, uint messageId, uint accessSpecId, CancellationToken cancellationToken)
    {
        DELETE_ACCESSSPEC_RESPONSE response = await reader.TransactAsync<DELETE_ACCESSSPEC_RESPONSE>(
            new DELETE_ACCESSSPEC(messageId, accessSpecId), timeout: null, cancellationToken).ConfigureAwait(false);
        EnsureSuccess("DELETE_ACCESSSPEC", response.LLRPStatus);
    }

    public async Task EnableAccessSpecAsync(
        LlrpReader reader, uint messageId, uint accessSpecId, CancellationToken cancellationToken)
    {
        ENABLE_ACCESSSPEC_RESPONSE response = await reader.TransactAsync<ENABLE_ACCESSSPEC_RESPONSE>(
            new ENABLE_ACCESSSPEC(messageId, accessSpecId), timeout: null, cancellationToken).ConfigureAwait(false);
        EnsureSuccess("ENABLE_ACCESSSPEC", response.LLRPStatus);
    }

    public async Task DisableAccessSpecAsync(
        LlrpReader reader, uint messageId, uint accessSpecId, CancellationToken cancellationToken)
    {
        DISABLE_ACCESSSPEC_RESPONSE response = await reader.TransactAsync<DISABLE_ACCESSSPEC_RESPONSE>(
            new DISABLE_ACCESSSPEC(messageId, accessSpecId), timeout: null, cancellationToken).ConfigureAwait(false);
        EnsureSuccess("DISABLE_ACCESSSPEC", response.LLRPStatus);
    }

    public async Task<IReadOnlyList<ILlrpParameter>> GetAccessSpecsAsync(
        LlrpReader reader, uint messageId, CancellationToken cancellationToken)
    {
        GET_ACCESSSPECS_RESPONSE response = await reader.TransactAsync<GET_ACCESSSPECS_RESPONSE>(
            new GET_ACCESSSPECS(messageId), timeout: null, cancellationToken).ConfigureAwait(false);
        EnsureSuccess("GET_ACCESSSPECS", response.LLRPStatus);
        return Array.AsReadOnly(response.AccessSpecItems.Cast<ILlrpParameter>().ToArray());
    }

    private static ROSpec RequireRoSpec(ILlrpParameter parameter) => parameter as ROSpec ??
        throw new ArgumentException(
            "The supplied ROSpec must be generated for LLRP 1.0.1 parameter type 177.",
            nameof(parameter));

    private static AccessSpec RequireAccessSpec(ILlrpParameter parameter) => parameter as AccessSpec ??
        throw new ArgumentException(
            "The supplied AccessSpec must be generated for LLRP 1.0.1 parameter type 207.",
            nameof(parameter));

    private static void EnsureSuccess(string operation, LLRPStatus status)
    {
        if (status.StatusCode != StatusCode.M_Success)
        {
            throw new LlrpReaderOperationException(operation, status);
        }
    }
}
