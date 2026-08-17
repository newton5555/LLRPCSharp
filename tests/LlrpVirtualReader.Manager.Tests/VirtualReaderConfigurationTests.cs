using System.Net;
using System.Net.Sockets;
using LlrpVirtualReader.Manager;

namespace LlrpVirtualReader.Manager.Tests;

public sealed class VirtualReaderConfigurationTests
{
    [Fact]
    public async Task Local_json_configuration_loads_as_an_inactive_manager_instance_until_explicit_start()
    {
        string path = Path.Combine(Path.GetTempPath(), $"llrpcsharp-vr-{Guid.NewGuid():N}.json");
        try
        {
            VirtualReaderConfiguration.Save(
                path,
                new VirtualReaderConfigurationDocument
                {
                    Presets =
                    [
                        new VirtualReaderLocalPreset
                        {
                            Id = "local.noisy",
                            Description = "Local noisy preset",
                            RfScenario = "noisy",
                            DetectionProbability = 1,
                            RssiJitterDb = 2,
                        },
                    ],
                    Instances =
                    [
                        new VirtualReaderLocalInstance
                        {
                            InstanceId = "local-reader",
                            Name = "Local reader",
                            PresetId = "local.noisy",
                            ListenAddress = "127.0.0.1",
                            Port = GetAvailablePort(),
                        },
                    ],
                });

            VirtualReaderConfiguration configuration = VirtualReaderConfiguration.Load(path);
            await using var manager = new VirtualReaderManager(configuration.CreateCatalog());
            VirtualReaderInstanceInfo created = await manager.CreateAsync(
                configuration.BuildInstanceOptions("local-reader"));

            Assert.Equal(VirtualReaderInstanceState.Created, created.State);
            Assert.Equal("local.noisy", created.PresetId);

            VirtualReaderInstanceInfo started = await manager.StartAsync(created.InstanceId);
            Assert.Equal(VirtualReaderInstanceState.Running, started.State);
            await manager.StopAsync(created.InstanceId);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void Local_json_configuration_rejects_unknown_preset_references()
    {
        string path = Path.Combine(Path.GetTempPath(), $"llrpcsharp-vr-invalid-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(
                path,
                """
                {
                  "schemaVersion": 1,
                  "presets": [],
                  "instances": [
                    {
                      "instanceId": "reader-1",
                      "name": "Reader",
                      "presetId": "missing",
                      "listenAddress": "127.0.0.1",
                      "port": 5085
                    }
                  ]
                }
                """);

            Assert.Throws<InvalidDataException>(() => VirtualReaderConfiguration.Load(path));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void Local_json_configuration_can_reference_a_built_in_preset()
    {
        string path = Path.Combine(Path.GetTempPath(), $"llrpcsharp-vr-built-in-{Guid.NewGuid():N}.json");
        try
        {
            VirtualReaderConfiguration.Save(
                path,
                new VirtualReaderConfigurationDocument
                {
                    Instances =
                    [
                        new VirtualReaderLocalInstance
                        {
                            InstanceId = "built-in-reader",
                            Name = "Built-in reader",
                            PresetId = VirtualReaderPresetIds.Standard101Basic,
                            ListenAddress = "127.0.0.1",
                            Port = 5085,
                        },
                    ],
                });

            VirtualReaderConfiguration configuration = VirtualReaderConfiguration.Load(path);

            Assert.Equal(
                VirtualReaderPresetIds.Standard101Basic,
                configuration.BuildInstanceOptions("built-in-reader").PresetId);
            Assert.NotNull(configuration.CreateCatalog().Get(VirtualReaderPresetIds.Standard101Basic));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static int GetAvailablePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
