using LlrpReaderStudio.Core;
using LlrpSdk;

namespace LlrpReaderStudio.Tests;

public sealed class ReaderFleetServiceTests
{
    [Fact]
    public async Task ConnectAndInventory_RouteToProfileSession()
    {
        var session = new FakeSession();
        await using var fleet = new ReaderFleetService(new FakeFactory(session));
        var profile = new ReaderProfile { Name = "Reader 1", Host = "192.0.2.10" };
        fleet.Add(profile);

        await fleet.ConnectAsync(profile.Id);
        await fleet.StartInventoryAsync(profile.Id, new InventorySettings { Session = 2 });

        Assert.True(session.ConnectCalled);
        Assert.Equal((byte)2, session.StartedInventory?.Session);
        Assert.Equal(StudioReaderState.Inventorying, Assert.Single(fleet.Readers).State);
    }

    [Fact]
    public async Task Reports_ArePublishedAsFleetAggregates()
    {
        var session = new FakeSession();
        await using var fleet = new ReaderFleetService(new FakeFactory(session));
        var profile = new ReaderProfile { Name = "Reader 1", Host = "192.0.2.10" };
        fleet.Add(profile);
        FleetTagObservedEventArgs? observed = null;
        fleet.TagObserved += (_, args) => observed = args;

        session.Emit(new TagReport(
            Convert.FromHexString("300833B2"),
            14150,
            1,
            1,
            1,
            -50,
            1,
            null,
            null,
            4,
            null));

        Assert.NotNull(observed);
        Assert.Equal("300833B2", observed.Aggregate.Epc);
        Assert.Equal(4, observed.Aggregate.ReadCount);
    }

    private sealed class FakeFactory(FakeSession session) : IReaderSessionFactory
    {
        public IReaderSession Create(ReaderProfile profile) => session;
    }

    private sealed class FakeSession : IReaderSession
    {
        public bool ConnectCalled { get; private set; }
        public InventorySettings? StartedInventory { get; private set; }
        public bool IsConnected => ConnectCalled;
        public ReaderIdentity? Identity => null;
        public ReaderCapabilities? Capabilities => null;
        public event EventHandler<StudioTagReportEventArgs>? TagReported;

        public Task ConnectAsync(CancellationToken cancellationToken)
        {
            ConnectCalled = true;
            return Task.CompletedTask;
        }

        public Task DisconnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<ReaderSettingsSnapshot> QuerySettingsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new ReaderSettingsSnapshot(new ReaderSettings(), null));
        public Task<ReaderSettingsDefaults> GetDefaultSettingsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(ReaderSettingsDefaults.CreateGeneric());
        public Task ApplySettingsAsync(ReaderSettings settings, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StartInventoryAsync(InventorySettings settings, CancellationToken cancellationToken)
        {
            StartedInventory = settings;
            return Task.CompletedTask;
        }

        public Task StopInventoryAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<TagAccessResult> ReadTagMemoryAsync(ReadTagRequest request, CancellationToken cancellationToken) =>
            Task.FromException<TagAccessResult>(new NotSupportedException());
        public Task<TagAccessResult> WriteTagMemoryAsync(WriteTagRequest request, CancellationToken cancellationToken) =>
            Task.FromException<TagAccessResult>(new NotSupportedException());
        public Task SetGpoAsync(ushort portNumber, bool state, CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void Emit(TagReport report) =>
            TagReported?.Invoke(this, new StudioTagReportEventArgs(report));
    }
}
