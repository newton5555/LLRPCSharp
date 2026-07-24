using LlrpNet.Core.Protocol;
using LlrpNet.Core.Transactions;
using LlrpNet.Protocol.Enumerations.V1_0_1;
using LlrpNet.Protocol.Messages.V1_0_1;
using LlrpNet.Protocol.Parameters;
using LlrpNet.Protocol.Parameters.V1_0_1;
using LlrpNet.Protocol.Registry;
using AccessSpec = LlrpNet.Protocol.Parameters.V1_0_1.AccessSpec;

namespace LlrpSdk;

/// <summary>
/// Implements the 1.0.1 baseline AccessSpec operations for one reader connection.
/// </summary>
internal sealed class AccessSpecService : IAccessSpecService
{
    private const ushort AccessSpecParameterType = 207;
    private readonly LlrpMessageIdGenerator _messageIds;
    private readonly LlrpReader _reader;
    private readonly LlrpCodecRegistry _registry;

    public AccessSpecService(
        LlrpReader reader,
        LlrpMessageIdGenerator messageIds,
        LlrpCodecRegistry registry)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _messageIds = messageIds ?? throw new ArgumentNullException(nameof(messageIds));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public async Task AddAsync(ILlrpParameter accessSpec, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(accessSpec);
        ValidateAccessSpecForRequest(accessSpec);
        if (accessSpec is not AccessSpec typedAccessSpec)
        {
            throw new ArgumentException(
                $"The supplied AccessSpec must be an instance of {nameof(AccessSpec)} for the negotiated protocol version.",
                nameof(accessSpec));
        }

        var request = new ADD_ACCESSSPEC(_messageIds.Next(), typedAccessSpec);
        ADD_ACCESSSPEC_RESPONSE response = await _reader
            .TransactAsync<ADD_ACCESSSPEC_RESPONSE>(request, timeout: null, cancellationToken)
            .ConfigureAwait(false);
        EnsureSuccess("ADD_ACCESSSPEC", response.LLRPStatus);
    }

    public async Task DeleteAsync(uint accessSpecId, CancellationToken cancellationToken = default)
    {
        var request = new DELETE_ACCESSSPEC(_messageIds.Next(), accessSpecId);
        DELETE_ACCESSSPEC_RESPONSE response = await _reader
            .TransactAsync<DELETE_ACCESSSPEC_RESPONSE>(request, timeout: null, cancellationToken)
            .ConfigureAwait(false);
        EnsureSuccess("DELETE_ACCESSSPEC", response.LLRPStatus);
    }

    public async Task EnableAsync(uint accessSpecId, CancellationToken cancellationToken = default)
    {
        var request = new ENABLE_ACCESSSPEC(_messageIds.Next(), accessSpecId);
        ENABLE_ACCESSSPEC_RESPONSE response = await _reader
            .TransactAsync<ENABLE_ACCESSSPEC_RESPONSE>(request, timeout: null, cancellationToken)
            .ConfigureAwait(false);
        EnsureSuccess("ENABLE_ACCESSSPEC", response.LLRPStatus);
    }

    public async Task DisableAsync(uint accessSpecId, CancellationToken cancellationToken = default)
    {
        var request = new DISABLE_ACCESSSPEC(_messageIds.Next(), accessSpecId);
        DISABLE_ACCESSSPEC_RESPONSE response = await _reader
            .TransactAsync<DISABLE_ACCESSSPEC_RESPONSE>(request, timeout: null, cancellationToken)
            .ConfigureAwait(false);
        EnsureSuccess("DISABLE_ACCESSSPEC", response.LLRPStatus);
    }

    public async Task<IReadOnlyList<ILlrpParameter>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var request = new GET_ACCESSSPECS(_messageIds.Next());
        GET_ACCESSSPECS_RESPONSE response = await _reader
            .TransactAsync<GET_ACCESSSPECS_RESPONSE>(request, timeout: null, cancellationToken)
            .ConfigureAwait(false);
        EnsureSuccess("GET_ACCESSSPECS", response.LLRPStatus);

        ILlrpParameter[] accessSpecs = response.AccessSpecItems.Cast<ILlrpParameter>().ToArray();
        foreach (ILlrpParameter accessSpec in accessSpecs)
        {
            ValidateAccessSpecFromResponse(accessSpec);
        }

        return Array.AsReadOnly(accessSpecs);
    }

    private void ValidateAccessSpecForRequest(ILlrpParameter accessSpec)
    {
        ushort wireType = GetWireType(accessSpec);
        if (wireType != AccessSpecParameterType)
        {
            throw new ArgumentException(
                $"An AccessSpec operation requires LLRP parameter type {AccessSpecParameterType}, " +
                $"but the supplied parameter uses type {wireType}.",
                nameof(accessSpec));
        }
    }

    private void ValidateAccessSpecFromResponse(ILlrpParameter accessSpec)
    {
        ushort wireType = GetWireType(accessSpec);
        if (wireType != AccessSpecParameterType)
        {
            throw new LlrpProtocolException(
                LlrpProtocolErrorCode.InvalidParameterEncoding,
                $"GET_ACCESSSPECS_RESPONSE returned parameter type {wireType} where AccessSpec type " +
                $"{AccessSpecParameterType} was required.");
        }
    }

    private ushort GetWireType(ILlrpParameter parameter)
    {
        try
        {
            return _registry.GetParameterWireIdentity(_reader.NegotiatedVersion, parameter).ParameterType;
        }
        catch (Exception exception) when (
            exception is ArgumentException or LlrpProtocolException or NotSupportedException)
        {
            throw new ArgumentException(
                $"The supplied AccessSpec must be encodable for the reader's negotiated protocol version.",
                nameof(parameter),
                exception);
        }
    }

    private static void EnsureSuccess(string operation, LLRPStatus status)
    {
        if (status.StatusCode != StatusCode.M_Success)
        {
            throw new LlrpReaderOperationException(operation, status);
        }
    }
}
