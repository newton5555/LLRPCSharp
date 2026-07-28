using LlrpSdk;

namespace LlrpCli.Commands;

/// <summary>
/// Executes tag access operations on an <see cref="LlrpReader"/>.
/// </summary>
internal static class TagAccessOperations
{
    public static async Task<TagAccessResult> ReadAsync(
        LlrpReader reader,
        ReadTagRequest request,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        return await reader.ReadTagMemoryAsync(request, timeout, cancellationToken);
    }

    public static async Task<TagAccessResult> WriteAsync(
        LlrpReader reader,
        WriteTagRequest request,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        return await reader.WriteTagMemoryAsync(request, timeout, cancellationToken);
    }

    public static async Task<TagAccessResult> LockAsync(
        LlrpReader reader,
        LockTagRequest request,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        return await reader.LockTagMemoryAsync(request, timeout, cancellationToken);
    }

    public static async Task<TagAccessResult> KillAsync(
        LlrpReader reader,
        KillTagRequest request,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        return await reader.KillTagAsync(request, timeout, cancellationToken);
    }

    public static async Task<TagAccessResult> BlockEraseAsync(
        LlrpReader reader,
        BlockEraseTagRequest request,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        return await reader.BlockEraseTagMemoryAsync(request, timeout, cancellationToken);
    }
}
