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

    /// <summary>Deletes one AccessSpec, or all AccessSpecs when the identifier is zero.</summary>
    public Task DeleteAsync(uint accessSpecId, CancellationToken cancellationToken = default);

    /// <summary>Enables one AccessSpec, or all AccessSpecs when the identifier is zero.</summary>
    public Task EnableAsync(uint accessSpecId, CancellationToken cancellationToken = default);

    /// <summary>Disables one AccessSpec, or all AccessSpecs when the identifier is zero.</summary>
    public Task DisableAsync(uint accessSpecId, CancellationToken cancellationToken = default);

    /// <summary>Retrieves the reader's AccessSpec resources without populating a local cache.</summary>
    public Task<IReadOnlyList<ILlrpParameter>> GetAllAsync(CancellationToken cancellationToken = default);
}
