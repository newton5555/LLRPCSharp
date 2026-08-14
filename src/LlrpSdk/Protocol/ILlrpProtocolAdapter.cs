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

    public Task<ReaderIdentity> FetchIdentityAsync(
        LlrpReader reader,
        uint messageId,
        CancellationToken cancellationToken);

    public Task<ReaderCapabilities> FetchCapabilitiesAsync(
        LlrpReader reader,
        uint messageId,
        CancellationToken cancellationToken);

    public ILlrpParameter CompileInventory(
        InventorySettings settings,
        uint roSpecId,
        InventoryCustomItems customItems,
        bool supportsStateAwareSingulation);

    public ILlrpParameter CompileTagAccess(uint accessSpecId, uint roSpecId, TagAccessRequest request, bool useBlockWrite = false);

    public ILlrpParameter CompileTagAccessSequence(
        uint accessSpecId,
        uint roSpecId,
        IReadOnlyList<TagAccessRequest> requests,
        bool useBlockWrite = false);

    public Task<IReadOnlyList<TranslatedTagReport>> FetchReportsAsync(
        LlrpReader reader,
        uint messageId,
        CancellationToken cancellationToken);

    public IReadOnlyList<TranslatedTagReport> TranslateTagReports(ILlrpMessage message);

    /// <summary>Reverse-compiles one SDK-managed ROSpec into its version-independent inventory snapshot.</summary>
    public ManagedRoSpecSnapshot ParseManagedRoSpec(
        LlrpReader reader,
        ILlrpParameter roSpec,
        IReadOnlyList<ILlrpParameter> accessSpecs);

    /// <summary>Returns whether the supplied ROSpec parameter is the SDK-managed inventory ROSpec.</summary>
    public bool IsManagedRoSpec(ILlrpParameter item);

    /// <summary>Returns whether the supplied AccessSpec list contains the SDK-managed attached-data AccessSpec.</summary>
    public bool HasAttachedDataAccessSpec(IReadOnlyList<ILlrpParameter> accessSpecs);

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

    public Task<TranslatedReaderConfiguration> QueryConfigurationAsync(
        LlrpReader reader,
        uint messageId,
        IReadOnlyList<ILlrpParameter> customItems,
        CancellationToken cancellationToken);

    public Task ApplyConfigurationAsync(
        LlrpReader reader,
        uint messageId,
        ReaderConfiguration configuration,
        IReadOnlyList<ILlrpParameter> customItems,
        CancellationToken cancellationToken);
}
