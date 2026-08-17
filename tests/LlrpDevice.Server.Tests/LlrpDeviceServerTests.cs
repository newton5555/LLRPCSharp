using System.Net;
using System.Net.Sockets;
using LlrpDevice.Abstractions;
using LlrpDevice.Server;

namespace LlrpDevice.Server.Tests;

public sealed class LlrpDeviceServerTests
{
    [Fact]
    public async Task Server_can_host_a_non_virtual_scripted_device()
    {
        await using var device = new ScriptedLlrpDevice();
        await using var server = new LlrpDeviceServer(
            new LlrpDeviceServerOptions { Port = 0 },
            device);

        await server.StartAsync();

        Assert.True(server.IsRunning);
        Assert.InRange(server.Port, 1, ushort.MaxValue);
        Assert.Equal("scripted", device.Identity.Name);

        await server.StopAsync();
        Assert.Equal(LlrpDeviceServerLifecycleState.Stopped, server.State);
    }

    [Fact]
    public async Task Server_keeps_device_configuration_and_resource_behavior_separate()
    {
        await using var device = new ScriptedLlrpDevice();
        await using var server = new LlrpDeviceServer(
            new LlrpDeviceServerOptions { Port = 0 },
            device);

        LlrpDeviceOperationResult result = await device.ApplyConfigurationAsync(new LlrpDeviceConfigurationUpdate
        {
            Gpos = [new LlrpDeviceGpoState { PortNumber = 4, State = true }],
        });

        Assert.True(result.Succeeded);
        Assert.Equal((ushort)4, Assert.Single(device.Configuration.Gpos).PortNumber);
    }

    [Fact]
    public async Task Client_disconnect_event_preserves_remote_endpoint_after_transport_disposal()
    {
        await using var device = new ScriptedLlrpDevice();
        await using var server = new LlrpDeviceServer(
            new LlrpDeviceServerOptions { Port = 0 },
            device);
        var connected = new TaskCompletionSource<LlrpDeviceClientInfo>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var disconnected = new TaskCompletionSource<LlrpDeviceClientInfo>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        server.ClientChanged += (_, args) =>
        {
            if (args.Connected)
            {
                connected.TrySetResult(args.Client);
            }
            else
            {
                disconnected.TrySetResult(args.Client);
            }
        };

        await server.StartAsync();
        using (var client = new TcpClient())
        {
            await client.ConnectAsync(IPAddress.Loopback, server.Port);
            LlrpDeviceClientInfo accepted = await connected.Task.WaitAsync(TimeSpan.FromSeconds(5));
            client.Close();

            LlrpDeviceClientInfo ended = await disconnected.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.NotNull(accepted.RemoteEndPoint);
            Assert.Equal(accepted.RemoteEndPoint, ended.RemoteEndPoint);
        }
    }

    private sealed class ScriptedLlrpDevice : ILlrpDevice
    {
        private LlrpDeviceConfiguration _configuration = new()
        {
            Gpos = [new LlrpDeviceGpoState { PortNumber = 1, State = false }],
        };

        public LlrpDeviceIdentity Identity { get; } = new() { Name = "scripted" };

        public LlrpDeviceCapabilities Capabilities { get; } = new();

        public LlrpDeviceConfiguration Configuration => _configuration;

        public event EventHandler<LlrpDeviceEvent>? EventRaised;

        public ValueTask<LlrpDeviceOperationResult> ApplyConfigurationAsync(
            LlrpDeviceConfigurationUpdate update,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _configuration = _configuration with { Gpos = update.Gpos };
            EventRaised?.Invoke(this, new LlrpDeviceEvent { Name = "configuration.changed" });
            return ValueTask.FromResult(LlrpDeviceOperationResult.Success());
        }

        public ValueTask<IInventoryExecution> StartInventoryAsync(
            LlrpInventoryPlan plan,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<IInventoryExecution>(new ScriptedInventoryExecution(plan));
        }

        public ValueTask<IReadOnlyList<LlrpTagAccessResult>> ExecuteTagAccessAsync(
            LlrpTagAccessRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<IReadOnlyList<LlrpTagAccessResult>>([]);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ScriptedInventoryExecution : IInventoryExecution
    {
        public ScriptedInventoryExecution(LlrpInventoryPlan plan) => Plan = plan;

        public LlrpInventoryPlan Plan { get; }

        public ValueTask<InventoryObservationBatch> ObserveAsync(
            LlrpInventoryRound round,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new InventoryObservationBatch());
        }

        public ValueTask StopAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
