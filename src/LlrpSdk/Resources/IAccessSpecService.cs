using LlrpNet.Protocol.Parameters;

namespace LlrpSdk;

/// <summary>
/// Provides managed request/response operations for AccessSpec resources on one ready reader connection.
/// </summary>
/// <remarks>
/// This advanced API sends operations directly to the reader and does not maintain a local AccessSpec cache.
/// </remarks>
public interface IAccessSpecService
{
    /// <summary>Adds one AccessSpec parameter.</summary>
    public Task AddAsync(ILlrpParameter accessSpec, CancellationToken cancellationToken = default);

    /// <summary>Deletes one AccessSpec.</summary>
    public Task DeleteAsync(uint accessSpecId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Explicitly deletes all standard AccessSpec resources.
    /// </summary>
    /// <param name="policy">Must be <see cref="ResourceTakeoverPolicy.ReplaceAll"/>.</param>
    public Task DeleteAllAsync(ResourceTakeoverPolicy policy, CancellationToken cancellationToken = default);

    /// <summary>Enables one AccessSpec.</summary>
    public Task EnableAsync(uint accessSpecId, CancellationToken cancellationToken = default);

    /// <summary>Disables one AccessSpec.</summary>
    public Task DisableAsync(uint accessSpecId, CancellationToken cancellationToken = default);

    /// <summary>Retrieves the reader's AccessSpec resources without populating a local cache.</summary>
    public Task<IReadOnlyList<ILlrpParameter>> GetAllAsync(CancellationToken cancellationToken = default);
}
