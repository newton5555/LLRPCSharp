using LlrpNet.Protocol.Parameters;

namespace LlrpSdk;

/// <summary>
/// Provides managed request/response operations for ROSpec resources on one ready reader connection.
/// </summary>
/// <remarks>
/// The service sends each operation to the reader immediately and does not maintain a local ROSpec cache.
/// </remarks>
public interface IRoSpecService
{
    /// <summary>
    /// Adds one ROSpec.
    /// </summary>
    /// <param name="roSpec">A ROSpec parameter whose LLRP wire type is 177.</param>
    /// <param name="cancellationToken">Cancels the send or pending response transaction.</param>
    /// <returns>A task representing the reader operation.</returns>
    public Task AddAsync(
        ILlrpParameter roSpec,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes one ROSpec, or all ROSpecs when <paramref name="roSpecId"/> is zero.
    /// </summary>
    /// <param name="roSpecId">The ROSpec identifier; zero selects all ROSpecs.</param>
    /// <param name="cancellationToken">Cancels the send or pending response transaction.</param>
    /// <returns>A task representing the reader operation.</returns>
    public Task DeleteAsync(
        uint roSpecId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Enables one ROSpec, or all ROSpecs when <paramref name="roSpecId"/> is zero.
    /// </summary>
    /// <param name="roSpecId">The ROSpec identifier; zero selects all ROSpecs.</param>
    /// <param name="cancellationToken">Cancels the send or pending response transaction.</param>
    /// <returns>A task representing the reader operation.</returns>
    public Task EnableAsync(
        uint roSpecId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Disables one ROSpec, or all ROSpecs when <paramref name="roSpecId"/> is zero.
    /// </summary>
    /// <param name="roSpecId">The ROSpec identifier; zero selects all ROSpecs.</param>
    /// <param name="cancellationToken">Cancels the send or pending response transaction.</param>
    /// <returns>A task representing the reader operation.</returns>
    public Task DisableAsync(
        uint roSpecId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts one enabled ROSpec.
    /// </summary>
    /// <param name="roSpecId">The ROSpec identifier.</param>
    /// <param name="cancellationToken">Cancels the send or pending response transaction.</param>
    /// <returns>A task representing the reader operation.</returns>
    public Task StartAsync(
        uint roSpecId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops one active ROSpec.
    /// </summary>
    /// <param name="roSpecId">The ROSpec identifier.</param>
    /// <param name="cancellationToken">Cancels the send or pending response transaction.</param>
    /// <returns>A task representing the reader operation.</returns>
    public Task StopAsync(
        uint roSpecId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the reader's current ROSpec resources without populating a local cache.
    /// </summary>
    /// <param name="cancellationToken">Cancels the send or pending response transaction.</param>
    /// <returns>An immutable snapshot of the ROSpec parameters returned by the reader.</returns>
    public Task<IReadOnlyList<ILlrpParameter>> GetAllAsync(
        CancellationToken cancellationToken = default);
}
