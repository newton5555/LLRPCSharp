# Virtual Reader Manager

`LlrpVirtualReader.Core` is a message-level LLRP device. It accepts ordinary
LLRP TCP clients and uses the repository's `LlrpNet` transport, session, codec
registry, and generated protocol types. It is not an SDK-side fake session and
does not depend on `LlrpSdk`.

`LlrpVirtualReader.Manager` owns instance identity and lifecycle. Its first
release manages multiple Core hosts in one process; configuration persistence
across processes is intentionally outside this milestone.

## Run the standalone Manager host

```powershell
dotnet run --project src/LlrpVirtualReader.Manager/LlrpVirtualReader.Manager.csproj -- --port 5085
```

Select the exact bind address, protocol profile, and display name:

```powershell
dotnet run --project src/LlrpVirtualReader.Manager/LlrpVirtualReader.Manager.csproj -- `
  --listen 127.0.0.1 --port 5085 --llrp 1.1 --name test-reader
```

`--strict` selects `llrp.standard101.strict`. `--listen` and `--port` are
bound exactly; a bind failure is reported and no fallback port is selected.
The process entry point accepts ports `1` through `65535`. Port `0` is reserved
for in-process tests through `VirtualReaderHostOptions` or the Manager API.

The compatibility executable remains available:

```powershell
dotnet run --project src/LlrpVirtualReader/LlrpVirtualReader.csproj -- --port 5085
```

## Use the Manager API

The Manager API keeps instance lifecycle separate from the device protocol:

```csharp
await using var manager = new VirtualReaderManager();

VirtualReaderInstanceInfo reader = await manager.CreateAndStartAsync(
    new VirtualReaderInstanceOptions
    {
        InstanceId = "reader-11",
        Name = "LLRP 1.1 test reader",
        PresetId = VirtualReaderPresetIds.Standard11Basic,
        ListenAddress = IPAddress.Loopback,
        Port = 5086,
    });

Console.WriteLine($"{reader.InstanceId}: {reader.ListenAddress}:{reader.BoundPort}");
await manager.StopAsync(reader.InstanceId);
await manager.StartAsync(reader.InstanceId);
await manager.DeleteAsync(reader.InstanceId);
```

`CreateAsync` creates an inactive instance. `StartAsync`, `StopAsync`,
`RestartAsync`, `DeleteAsync`, `Get`, `TryGet`, and `Instances` provide the
stable lifecycle/status surface. Every instance owns its own listener,
connection set, ROSpec/AccessSpec graph, tag source, report scheduler, and
fault configuration.

## Built-in presets

The catalog is registration-based; the Manager does not switch on device type.
The built-ins are:

- `llrp.standard101.basic`
- `llrp.standard101.strict`
- `llrp.standard101.tag-access`
- `llrp.standard11.basic`
- `llrp.fault.request-timeout`
- `llrp.fault.status-error`
- `llrp.fault.device-disconnect`

`IVirtualReaderPresetContributor` lets an application register a tested preset
that returns `VirtualReaderHostOptions`. `IVirtualReaderProtocolModule` is the
device-side extension point for version-scoped codecs and message handlers.
Modules are registered before the host accepts clients and are evaluated before
the standard profile.

## Run from the main CLI

The main CLI exposes the same Core behavior without requiring a graphical
tool:

```powershell
dotnet run --project src/LlrpCli/LlrpCli.csproj -- virtual-reader `
  --listen 127.0.0.1 --port 5087 --llrp 1.1 --name ci-reader --interval-ms 50
```

Use `--tag <EPC>` to replace the deterministic tag and `--count <N>` to limit
the number of report messages (`0` means repeat while the ROSpec is active).
Press Ctrl+C to stop.

## Protocol behavior

The Core host supports LLRP 1.0.1 and 1.1 as explicit profiles. It handles
connection initialization, reader events, capabilities/configuration,
ROSpec/AccessSpec lifecycle, KEEPALIVE/ACK, close/error responses, tag reports,
and standard C1G2 read/write operations. LLRP 1.1 requests are translated to a
single canonical 1.0.1 resource state through the shared codec registry; the
wire header and response types remain 1.1.

Reports are configurable with `VirtualReaderReportOptions`:

```csharp
ReaderOptions = new VirtualReaderOptions
{
    Reports = new VirtualReaderReportOptions
    {
        ReportInterval = TimeSpan.FromMilliseconds(100),
        ReportCount = 0,
        Repeat = true,
    },
};
```

`DropResponseForMessageTypes`, `ErrorResponseForMessageTypes`,
`CloseConnectionAfterRequestMessageTypes`, and
`TruncateResponseForMessageTypes` are deterministic fault-injection hooks for
tests. The host also accepts the shared `ILlrpFrameObserver` and logger factory
from `LlrpNet`.
