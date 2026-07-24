using LlrpNet.Core.Protocol;
using LlrpNet.Core.Transactions;
using LlrpNet.Protocol.Messages.V1_0_1;
using LlrpNet.Protocol.Parameters;
using LlrpNet.Protocol.Parameters.V1_0_1;
using LlrpNet.Protocol.Registry;
using ROSpec = LlrpNet.Protocol.Parameters.V1_0_1.ROSpec;

namespace LlrpSdk;

internal sealed class RoSpecService : IRoSpecService
{
    private const ushort RoSpecParameterType = 177;
    private readonly LlrpMessageIdGenerator _messageIds;
    private readonly LlrpReader _reader;
    private readonly LlrpCodecRegistry _registry;

    public RoSpecService(
        LlrpReader reader,
        LlrpMessageIdGenerator messageIds,
        LlrpCodecRegistry registry)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _messageIds = messageIds ?? throw new ArgumentNullException(nameof(messageIds));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public async Task AddAsync(
        ILlrpParameter roSpec,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(roSpec);
        ValidateRoSpecForRequest(roSpec);
        if (roSpec is not ROSpec typedRoSpec)
        {
            throw new ArgumentException(
                $"The supplied ROSpec must be an instance of {nameof(ROSpec)} for the negotiated protocol version.",
                nameof(roSpec));
        }

        var request = new AddRoSpec(_messageIds.Next(), typedRoSpec);
        AddRoSpecResponse response = await _reader
            .TransactAsync<AddRoSpecResponse>(request, timeout: null, cancellationToken)
            .ConfigureAwait(false);
        EnsureSuccess("ADD_ROSPEC", response.LLRPStatus);
    }

    public async Task DeleteAsync(
        uint roSpecId,
        CancellationToken cancellationToken = default)
    {
        var request = new DeleteRoSpec(_messageIds.Next(), roSpecId);
        DeleteRoSpecResponse response = await _reader
            .TransactAsync<DeleteRoSpecResponse>(request, timeout: null, cancellationToken)
            .ConfigureAwait(false);
        EnsureSuccess("DELETE_ROSPEC", response.LLRPStatus);
    }

    public async Task EnableAsync(
        uint roSpecId,
        CancellationToken cancellationToken = default)
    {
        var request = new EnableRoSpec(_messageIds.Next(), roSpecId);
        EnableRoSpecResponse response = await _reader
            .TransactAsync<EnableRoSpecResponse>(request, timeout: null, cancellationToken)
            .ConfigureAwait(false);
        EnsureSuccess("ENABLE_ROSPEC", response.LLRPStatus);
    }

    public async Task DisableAsync(
        uint roSpecId,
        CancellationToken cancellationToken = default)
    {
        var request = new DisableRoSpec(_messageIds.Next(), roSpecId);
        DisableRoSpecResponse response = await _reader
            .TransactAsync<DisableRoSpecResponse>(request, timeout: null, cancellationToken)
            .ConfigureAwait(false);
        EnsureSuccess("DISABLE_ROSPEC", response.LLRPStatus);
    }

    public async Task StartAsync(
        uint roSpecId,
        CancellationToken cancellationToken = default)
    {
        var request = new StartRoSpec(_messageIds.Next(), roSpecId);
        StartRoSpecResponse response = await _reader
            .TransactAsync<StartRoSpecResponse>(request, timeout: null, cancellationToken)
            .ConfigureAwait(false);
        EnsureSuccess("START_ROSPEC", response.LLRPStatus);
    }

    public async Task StopAsync(
        uint roSpecId,
        CancellationToken cancellationToken = default)
    {
        var request = new StopRoSpec(_messageIds.Next(), roSpecId);
        StopRoSpecResponse response = await _reader
            .TransactAsync<StopRoSpecResponse>(request, timeout: null, cancellationToken)
            .ConfigureAwait(false);
        EnsureSuccess("STOP_ROSPEC", response.LLRPStatus);
    }

    public async Task<IReadOnlyList<ILlrpParameter>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        var request = new GetRoSpecs(_messageIds.Next());
        GetRoSpecsResponse response = await _reader
            .TransactAsync<GetRoSpecsResponse>(request, timeout: null, cancellationToken)
            .ConfigureAwait(false);
        EnsureSuccess("GET_ROSPECS", response.LLRPStatus);

        ILlrpParameter[] roSpecs = response.ROSpecItems.Cast<ILlrpParameter>().ToArray();
        foreach (ILlrpParameter roSpec in roSpecs)
        {
            ValidateRoSpecFromResponse(roSpec);
        }

        return Array.AsReadOnly(roSpecs);
    }

    private void ValidateRoSpecForRequest(ILlrpParameter roSpec)
    {
        ushort wireType;
        try
        {
            wireType = GetWireType(roSpec);
        }
        catch (Exception exception) when (
            exception is ArgumentException or LlrpProtocolException or NotSupportedException)
        {
            throw new ArgumentException(
                $"The supplied ROSpec must be encodable as LLRP parameter type {RoSpecParameterType} " +
                "for the reader's negotiated protocol version.",
                nameof(roSpec),
                exception);
        }

        if (wireType != RoSpecParameterType)
        {
            throw new ArgumentException(
                $"A ROSpec operation requires an LLRP parameter with wire type {RoSpecParameterType}, " +
                $"but the supplied parameter uses type {wireType}.",
                nameof(roSpec));
        }
    }

    private void ValidateRoSpecFromResponse(ILlrpParameter roSpec)
    {
        ushort wireType = GetWireType(roSpec);
        if (wireType != RoSpecParameterType)
        {
            throw new LlrpProtocolException(
                LlrpProtocolErrorCode.InvalidParameterEncoding,
                $"GET_ROSPECS_RESPONSE returned parameter type {wireType} where ROSpec type " +
                $"{RoSpecParameterType} was required.");
        }
    }

    private ushort GetWireType(ILlrpParameter parameter)
    {
        byte[] encoded = _registry.EncodeParameter(_reader.NegotiatedVersion, parameter);
        if ((encoded[0] & 0x80) != 0)
        {
            return (ushort)(encoded[0] & 0x7F);
        }

        return LlrpTlvParameterHeader.Decode(encoded).ParameterType;
    }

    private static void EnsureSuccess(string operation, LlrpStatus status)
    {
        if (status.StatusCode != LlrpStatusCode.M_Success)
        {
            throw new LlrpReaderOperationException(operation, status);
        }
    }
}
