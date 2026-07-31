using LlrpSdk;
using LlrpSdk.Extensions.Impinj;

namespace LlrpReaderStudio.Core;

public sealed class StudioTagReportEventArgs(TagReport report) : EventArgs
{
    public TagReport Report { get; } = report;
}

public interface IReaderSession : IAsyncDisposable
{
    public bool IsConnected { get; }
    public ReaderIdentity? Identity { get; }
    public ReaderCapabilities? Capabilities { get; }
    public event EventHandler<StudioTagReportEventArgs>? TagReported;
    public Task ConnectAsync(CancellationToken cancellationToken);
    public Task DisconnectAsync(CancellationToken cancellationToken);
    public Task<ReaderSettingsSnapshot> QuerySettingsAsync(CancellationToken cancellationToken);
    public Task<ReaderSettingsDefaults> GetDefaultSettingsAsync(CancellationToken cancellationToken);
    public Task ApplySettingsAsync(ReaderSettings settings, CancellationToken cancellationToken);
    public Task StartInventoryAsync(InventorySettings settings, CancellationToken cancellationToken);
    public Task StopInventoryAsync(CancellationToken cancellationToken);
    public Task<TagAccessResult> ReadTagMemoryAsync(ReadTagRequest request, CancellationToken cancellationToken);
    public Task<TagAccessResult> WriteTagMemoryAsync(WriteTagRequest request, CancellationToken cancellationToken);
    public Task SetGpoAsync(ushort portNumber, bool state, CancellationToken cancellationToken);
}

public interface IReaderSessionFactory
{
    public IReaderSession Create(ReaderProfile profile);
}

public sealed class LlrpReaderSessionFactory : IReaderSessionFactory
{
    public IReaderSession Create(ReaderProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        profile.Validate();
        var builder = new LlrpReaderBuilder(profile.Host).WithPort(profile.Port);
        if (profile.EnableImpinjExtensions)
        {
            builder.UseImpinj();
        }

        return new LlrpReaderSession(builder.Build());
    }
}

internal sealed class LlrpReaderSession : IReaderSession
{
    private readonly LlrpReader reader;
    private InventorySession? inventorySession;

    public LlrpReaderSession(LlrpReader reader)
    {
        this.reader = reader;
        reader.TagsReported += OnTagsReported;
    }

    public bool IsConnected => reader.IsConnected;
    public ReaderIdentity? Identity => reader.Identity;
    public ReaderCapabilities? Capabilities => reader.Capabilities;
    public event EventHandler<StudioTagReportEventArgs>? TagReported;

    public Task ConnectAsync(CancellationToken cancellationToken) => reader.ConnectAsync(cancellationToken);
    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        try
        {
            await reader.DisconnectAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            inventorySession = null;
        }
    }
    public Task<ReaderSettingsSnapshot> QuerySettingsAsync(CancellationToken cancellationToken) =>
        reader.QuerySettingsAsync(cancellationToken);
    public Task<ReaderSettingsDefaults> GetDefaultSettingsAsync(CancellationToken cancellationToken) =>
        reader.GetDefaultSettingsAsync(cancellationToken);
    public Task ApplySettingsAsync(ReaderSettings settings, CancellationToken cancellationToken) =>
        reader.ApplySettingsAsync(settings, cancellationToken);

    public async Task StartInventoryAsync(InventorySettings settings, CancellationToken cancellationToken)
    {
        if (inventorySession is not null)
        {
            throw new InvalidOperationException("Inventory is already running for this reader.");
        }

        inventorySession = await reader.StartInventoryAsync(settings, cancellationToken).ConfigureAwait(false);
    }

    public async Task StopInventoryAsync(CancellationToken cancellationToken)
    {
        InventorySession? session = inventorySession;
        if (session is null)
        {
            await reader.StopAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        inventorySession = null;
        await session.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<TagAccessResult> ReadTagMemoryAsync(ReadTagRequest request, CancellationToken cancellationToken) =>
        reader.ReadTagMemoryAsync(request, TimeSpan.FromSeconds(5), cancellationToken);

    public Task<TagAccessResult> WriteTagMemoryAsync(WriteTagRequest request, CancellationToken cancellationToken) =>
        reader.WriteTagMemoryAsync(request, TimeSpan.FromSeconds(5), cancellationToken);

    public Task SetGpoAsync(ushort portNumber, bool state, CancellationToken cancellationToken) =>
        reader.SetGpoAsync(portNumber, state, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        reader.TagsReported -= OnTagsReported;
        if (inventorySession is not null)
        {
            try
            {
                await inventorySession.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                // Reader disposal remains authoritative when the connection is already gone.
            }
        }

        await reader.DisposeAsync().ConfigureAwait(false);
    }

    private void OnTagsReported(object? sender, TagReportEventArgs args) =>
        TagReported?.Invoke(this, new StudioTagReportEventArgs(args.Report));
}
