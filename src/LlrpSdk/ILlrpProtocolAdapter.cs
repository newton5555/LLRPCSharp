using LlrpNet.Core.Protocol;
using LlrpNet.Protocol.Messages;
using LlrpNet.Protocol.Parameters;
using LlrpNet.Protocol.Registry;

namespace LlrpSdk;

/// <summary>Maps one wire-protocol version to the version-independent SDK inventory boundary.</summary>
internal interface ILlrpProtocolAdapter
{
    public LlrpProtocolVersion Version { get; }

    public void RegisterStandardCodecs(LlrpCodecRegistry registry);

    public Task<ReaderMetadataSnapshot> InitializeAsync(
        LlrpReader reader,
        uint messageId,
        CancellationToken cancellationToken);

    public ILlrpParameter CompileInventory(ReaderSettings settings);

    public IReadOnlyList<TagReport> TranslateTagReports(ILlrpMessage message);

    public Task AddRoSpecAsync(
        LlrpReader reader,
        uint messageId,
        ILlrpParameter roSpec,
        CancellationToken cancellationToken);

    public Task DeleteRoSpecAsync(
        LlrpReader reader,
        uint messageId,
        uint roSpecId,
        CancellationToken cancellationToken);

    public Task EnableRoSpecAsync(
        LlrpReader reader,
        uint messageId,
        uint roSpecId,
        CancellationToken cancellationToken);

    public Task DisableRoSpecAsync(
        LlrpReader reader,
        uint messageId,
        uint roSpecId,
        CancellationToken cancellationToken);

    public Task StartRoSpecAsync(
        LlrpReader reader,
        uint messageId,
        uint roSpecId,
        CancellationToken cancellationToken);

    public Task StopRoSpecAsync(
        LlrpReader reader,
        uint messageId,
        uint roSpecId,
        CancellationToken cancellationToken);

    public Task<IReadOnlyList<ILlrpParameter>> GetRoSpecsAsync(
        LlrpReader reader,
        uint messageId,
        CancellationToken cancellationToken);

    public Task AddAccessSpecAsync(
        LlrpReader reader,
        uint messageId,
        ILlrpParameter accessSpec,
        CancellationToken cancellationToken);

    public Task DeleteAccessSpecAsync(
        LlrpReader reader,
        uint messageId,
        uint accessSpecId,
        CancellationToken cancellationToken);

    public Task EnableAccessSpecAsync(
        LlrpReader reader,
        uint messageId,
        uint accessSpecId,
        CancellationToken cancellationToken);

    public Task DisableAccessSpecAsync(
        LlrpReader reader,
        uint messageId,
        uint accessSpecId,
        CancellationToken cancellationToken);

    public Task<IReadOnlyList<ILlrpParameter>> GetAccessSpecsAsync(
        LlrpReader reader,
        uint messageId,
        CancellationToken cancellationToken);
}
