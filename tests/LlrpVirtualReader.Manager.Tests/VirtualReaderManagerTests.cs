using System.Net;
using LlrpNet.Core.Protocol;
using LlrpVirtualReader;
using LlrpVirtualReader.Manager;

namespace LlrpVirtualReader.Manager.Tests;

public sealed class VirtualReaderManagerTests
{
    [Fact]
    public void Catalog_exposes_standard_versions_faults_and_registered_extensions()
    {
        var catalog = new VirtualReaderPresetCatalog();

        Assert.Contains(catalog.Presets, preset => preset.Id == VirtualReaderPresetIds.Standard101Basic);
        Assert.Contains(catalog.Presets, preset => preset.Id == VirtualReaderPresetIds.Standard101TagAccess);
        Assert.Contains(catalog.Presets, preset => preset.Id == VirtualReaderPresetIds.Standard11Basic);
        Assert.Contains(catalog.Presets, preset => preset.Id == VirtualReaderPresetIds.RequestTimeoutFault);
        Assert.Contains(catalog.Presets, preset => preset.Id == VirtualReaderPresetIds.StatusErrorFault);
        Assert.Contains(catalog.Presets, preset => preset.Id == VirtualReaderPresetIds.DeviceDisconnectFault);
    }

    [Fact]
    public async Task Lifecycle_create_start_stop_restart_delete_keeps_identity_and_releases_endpoint()
    {
        await using var manager = new VirtualReaderManager();
        VirtualReaderInstanceInfo created = await manager.CreateAsync(new VirtualReaderInstanceOptions
        {
            InstanceId = "manager-lifecycle",
            Name = "Lifecycle Reader",
            ListenAddress = IPAddress.Loopback,
            Port = 0,
        });

        Assert.Equal(VirtualReaderInstanceState.Created, created.State);
        Assert.Equal(0, created.BoundPort);

        VirtualReaderInstanceInfo started = await manager.StartAsync(created.InstanceId);
        Assert.Equal(VirtualReaderInstanceState.Running, started.State);
        Assert.InRange(started.BoundPort, 1, ushort.MaxValue);

        VirtualReaderInstanceInfo stopped = await manager.StopAsync(created.InstanceId);
        Assert.Equal(VirtualReaderInstanceState.Stopped, stopped.State);
        Assert.Equal(0, stopped.BoundPort);

        VirtualReaderInstanceInfo restarted = await manager.RestartAsync(created.InstanceId);
        Assert.Equal(created.InstanceId, restarted.InstanceId);
        Assert.Equal(VirtualReaderInstanceState.Running, restarted.State);
        Assert.InRange(restarted.BoundPort, 1, ushort.MaxValue);

        await manager.DeleteAsync(created.InstanceId);
        Assert.Empty(manager.Instances);
        Assert.False(manager.TryGet(created.InstanceId, out _));
    }

    [Fact]
    public async Task Two_instances_can_run_with_independent_endpoints_and_versions()
    {
        await using var manager = new VirtualReaderManager();
        VirtualReaderInstanceInfo first = await manager.CreateAndStartAsync(new VirtualReaderInstanceOptions
        {
            InstanceId = "reader-101",
            PresetId = VirtualReaderPresetIds.Standard101Basic,
            Port = 0,
        });
        VirtualReaderInstanceInfo second = await manager.CreateAndStartAsync(new VirtualReaderInstanceOptions
        {
            InstanceId = "reader-11",
            PresetId = VirtualReaderPresetIds.Standard11Basic,
            Port = 0,
        });

        Assert.NotEqual(first.BoundPort, second.BoundPort);
        Assert.Equal(LlrpProtocolVersion.Version101, first.ProtocolVersion);
        Assert.Equal(LlrpProtocolVersion.Version11, second.ProtocolVersion);
        Assert.Equal(2, manager.Instances.Count);
    }

    [Fact]
    public async Task Custom_preset_contributor_builds_host_without_manager_switch_changes()
    {
        var catalog = new VirtualReaderPresetCatalog(
        [
            new TestPresetContributor(),
        ]);
        await using var manager = new VirtualReaderManager(catalog);

        VirtualReaderInstanceInfo instance = await manager.CreateAndStartAsync(new VirtualReaderInstanceOptions
        {
            InstanceId = "custom-reader",
            PresetId = "test.custom",
            Port = 0,
        });

        Assert.Equal("test.custom", instance.PresetId);
        Assert.Equal(VirtualReaderInstanceState.Running, instance.State);
    }

    private sealed class TestPresetContributor : IVirtualReaderPresetContributor
    {
        public string Id => "test.custom";

        public string Description => "Test contributor.";

        public VirtualReaderHostOptions Build(VirtualReaderInstanceOptions options) => new()
        {
            ListenAddress = options.ListenAddress,
            Port = options.Port,
            ProtocolModules = options.ProtocolModules,
            ReaderOptions = options.ReaderOptions with
            {
                ReaderName = options.Name,
                ProtocolVersion = LlrpProtocolVersion.Version101,
            },
        };
    }
}
