using LlrpSdk;

namespace LlrpReaderStudio.Core;

public sealed class ReaderStatusChangedEventArgs(ReaderStatus status) : EventArgs
{
    public ReaderStatus Status { get; } = status;
}

public sealed class FleetTagObservedEventArgs(ReaderProfile profile, TagReport report, TagObservation aggregate) : EventArgs
{
    public ReaderProfile Profile { get; } = profile;
    public TagReport Report { get; } = report;
    public TagObservation Aggregate { get; } = aggregate;
}

public sealed class ReaderFleetService : IAsyncDisposable
{
    private readonly IReaderSessionFactory sessionFactory;
    private readonly Dictionary<Guid, ManagedReader> readers = [];
    private readonly TagAggregateStore aggregates = new();

    public ReaderFleetService(IReaderSessionFactory? sessionFactory = null)
    {
        this.sessionFactory = sessionFactory ?? new LlrpReaderSessionFactory();
    }

    public event EventHandler<ReaderStatusChangedEventArgs>? ReaderStatusChanged;
    public event EventHandler<FleetTagObservedEventArgs>? TagObserved;

    public IReadOnlyList<ReaderStatus> Readers =>
        readers.Values.Select(static reader => reader.Status).OrderBy(static reader => reader.Profile.Name).ToArray();

    public IReadOnlyList<TagObservation> Tags => aggregates.Snapshot();

    public ReaderStatus Add(ReaderProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        profile.Validate();
        if (readers.ContainsKey(profile.Id))
        {
            throw new InvalidOperationException($"Reader profile '{profile.Id}' is already registered.");
        }

        IReaderSession session = sessionFactory.Create(profile);
        var managed = new ManagedReader(profile, session);
        session.TagReported += (_, args) => OnTagReported(managed, args.Report);
        readers.Add(profile.Id, managed);
        Publish(managed);
        return managed.Status;
    }

    public async Task RemoveAsync(Guid profileId, CancellationToken cancellationToken = default)
    {
        ManagedReader managed = Get(profileId);
        await managed.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await managed.Session.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            readers.Remove(profileId);
            managed.Gate.Release();
            managed.Gate.Dispose();
        }
    }

    public Task ConnectAsync(Guid profileId, CancellationToken cancellationToken = default) =>
        RunAsync(profileId, StudioReaderState.Connecting, async managed =>
        {
            await managed.Session.ConnectAsync(cancellationToken).ConfigureAwait(false);
            managed.Status = managed.Status with
            {
                State = StudioReaderState.Connected,
                Model = managed.Session.Identity is null
                    ? null
                    : $"{managed.Session.Identity.ManufacturerId}:{managed.Session.Identity.ModelId}",
                Firmware = managed.Session.Identity?.FirmwareVersion,
                Error = null,
            };
        }, cancellationToken);

    public Task DisconnectAsync(Guid profileId, CancellationToken cancellationToken = default) =>
        RunAsync(profileId, StudioReaderState.Disconnected, async managed =>
        {
            await managed.Session.DisconnectAsync(cancellationToken).ConfigureAwait(false);
            managed.Status = managed.Status with { State = StudioReaderState.Disconnected, Error = null };
        }, cancellationToken);

    public Task StartInventoryAsync(Guid profileId, InventorySettings settings, CancellationToken cancellationToken = default) =>
        RunAsync(profileId, StudioReaderState.Inventorying, async managed =>
        {
            await managed.Session.StartInventoryAsync(settings, cancellationToken).ConfigureAwait(false);
            managed.Status = managed.Status with { State = StudioReaderState.Inventorying, Error = null };
        }, cancellationToken);

    public Task StopInventoryAsync(Guid profileId, CancellationToken cancellationToken = default) =>
        RunAsync(profileId, StudioReaderState.Connected, async managed =>
        {
            await managed.Session.StopInventoryAsync(cancellationToken).ConfigureAwait(false);
            managed.Status = managed.Status with { State = StudioReaderState.Connected, Error = null };
        }, cancellationToken);

    public Task<ReaderSettingsSnapshot> QuerySettingsAsync(Guid profileId, CancellationToken cancellationToken = default) =>
        UseAsync(profileId, (session, token) => session.QuerySettingsAsync(token), cancellationToken);

    public Task<ReaderSettingsDefaults> GetDefaultSettingsAsync(Guid profileId, CancellationToken cancellationToken = default) =>
        UseAsync(profileId, (session, token) => session.GetDefaultSettingsAsync(token), cancellationToken);

    public Task ApplySettingsAsync(Guid profileId, ReaderSettings settings, CancellationToken cancellationToken = default) =>
        UseAsync(profileId, (session, token) => session.ApplySettingsAsync(settings, token), cancellationToken);

    public Task<TagAccessResult> ReadTagMemoryAsync(Guid profileId, ReadTagRequest request, CancellationToken cancellationToken = default) =>
        UseAsync(profileId, (session, token) => session.ReadTagMemoryAsync(request, token), cancellationToken);

    public Task<TagAccessResult> WriteTagMemoryAsync(Guid profileId, WriteTagRequest request, CancellationToken cancellationToken = default) =>
        UseAsync(profileId, (session, token) => session.WriteTagMemoryAsync(request, token), cancellationToken);

    public Task SetGpoAsync(Guid profileId, ushort portNumber, bool state, CancellationToken cancellationToken = default) =>
        UseAsync(profileId, (session, token) => session.SetGpoAsync(portNumber, state, token), cancellationToken);

    public void ClearTags() => aggregates.Clear();

    public async ValueTask DisposeAsync()
    {
        foreach (ManagedReader managed in readers.Values.ToArray())
        {
            await managed.Session.DisposeAsync().ConfigureAwait(false);
            managed.Gate.Dispose();
        }

        readers.Clear();
    }

    private async Task RunAsync(
        Guid profileId,
        StudioReaderState interimState,
        Func<ManagedReader, Task> operation,
        CancellationToken cancellationToken)
    {
        ManagedReader managed = Get(profileId);
        await managed.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            managed.Status = managed.Status with { State = interimState, Error = null };
            Publish(managed);
            await operation(managed).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            managed.Status = managed.Status with { State = StudioReaderState.Faulted, Error = exception.Message };
            throw;
        }
        finally
        {
            Publish(managed);
            managed.Gate.Release();
        }
    }

    private async Task<T> UseAsync<T>(
        Guid profileId,
        Func<IReaderSession, CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        ManagedReader managed = Get(profileId);
        await managed.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await operation(managed.Session, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            managed.Gate.Release();
        }
    }

    private async Task UseAsync(
        Guid profileId,
        Func<IReaderSession, CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        ManagedReader managed = Get(profileId);
        await managed.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await operation(managed.Session, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            managed.Gate.Release();
        }
    }

    private ManagedReader Get(Guid id) =>
        readers.TryGetValue(id, out ManagedReader? managed)
            ? managed
            : throw new KeyNotFoundException($"Reader profile '{id}' is not registered.");

    private void OnTagReported(ManagedReader managed, TagReport report)
    {
        TagObservation aggregate = aggregates.Add(managed.Status.Profile, report);
        TagObserved?.Invoke(this, new FleetTagObservedEventArgs(managed.Status.Profile, report, aggregate));
    }

    private void Publish(ManagedReader managed) =>
        ReaderStatusChanged?.Invoke(this, new ReaderStatusChangedEventArgs(managed.Status));

    private sealed class ManagedReader(ReaderProfile profile, IReaderSession session)
    {
        public IReaderSession Session { get; } = session;
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public ReaderStatus Status { get; set; } =
            new(profile, StudioReaderState.Disconnected, null, null, null);
    }
}
