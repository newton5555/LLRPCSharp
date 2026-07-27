using LlrpSdk;

namespace LlrpCli.Commands;

/// <summary>Runs a tag read while owning only inventory it started itself.</summary>
internal static class TagAccessOperations
{
    public static async Task<TagAccessResult> ReadAsync(
        LlrpReader reader,
        ReadTagRequest request,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        bool startedInventory = false;
        if (reader.OperationState != ReaderOperationState.Inventorying)
        {
            await reader.StartAsync(new ReaderSettings { AntennaIds = [request.AntennaId] }, cancellationToken);
            startedInventory = true;
        }

        try
        {
            return await reader.ReadTagMemoryAsync(request, timeout, cancellationToken);
        }
        finally
        {
            if (startedInventory && reader.IsConnected && reader.OperationState == ReaderOperationState.Inventorying)
            {
                await reader.StopAsync(CancellationToken.None);
            }
        }
    }
}
