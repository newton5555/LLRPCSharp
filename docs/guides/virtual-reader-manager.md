# Virtual Reader Manager

This guide documents the compatibility/upper-level multi-instance Manager.
For the primary single-device SDK entry and the sibling standalone CLI, see
[Virtual Device SDK and CLI](virtual-device-cli.md). The Manager is not a
dependency of that single-device path.

`LlrpDevice.Server` is the generic message-level LLRP device service. It accepts
ordinary LLRP TCP clients and uses the repository's `LlrpNet` transport, session,
codec registry, and generated protocol types. It is not an SDK-side fake session
and does not depend on `LlrpSdk`.

`LlrpDevice.Virtual` supplies the deterministic device behavior. The normal
composition is:

```csharp
ILlrpDevice device = new VirtualLlrpDevice(virtualOptions);
await using var server = new LlrpDeviceServer(serverOptions, device);
await server.StartAsync();
```

`LlrpVirtualReader.Manager` owns instance identity and lifecycle. The Manager
composes `LlrpDevice.Server` with `LlrpDevice.Virtual` and can load a versioned
local JSON document containing reader instances and declarative inventory/device
presets. Loading is explicit; the process does not discover a configuration file
or restore a previous runtime automatically.

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

## Load a local reader and inventory preset

The repository includes [`config/virtual-readers.example.json`](../../config/virtual-readers.example.json).
Validate it without binding a socket:

```powershell
dotnet run --project src/LlrpCli/LlrpCli.csproj -- virtual-reader `
  --config config/virtual-readers.example.json --validate-config
```

List built-in and local presets:

```powershell
dotnet run --project src/LlrpCli/LlrpCli.csproj -- virtual-reader `
  --config config/virtual-readers.example.json --list-presets
```

Start one explicitly selected configured instance:

```powershell
dotnet run --project src/LlrpCli/LlrpCli.csproj -- virtual-reader `
  --config config/virtual-readers.example.json --instance reader-local-1
```

The standalone Manager accepts the same `--config`, `--instance`,
`--validate-config`, and `--list-presets` options. A local preset stores the
reader identity/profile, report cadence, deterministic RF-observable scenario,
seed, detection/RSSI policy, and tag/TID/User-memory definitions. It is a
behavior preset rather than an arbitrary raw LLRP packet editor: the LLRP
client still sends `ADD_ROSPEC`/`START_ROSPEC`, and the Server produces reports
through the configured device implementation.

There is no automatic restart or recovery of active ROSpec, AccessSpec, or
managed inventory state after a process restart. Re-run the explicit start
command when a new process should be brought up.

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
connection set, ROSpec/AccessSpec graph, tag state, report scheduler, and
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

`ILlrpDevicePresetContributor` lets an application register a tested preset that
builds `LlrpDeviceServerOptions` and `VirtualDeviceOptions`. The generic
`ILlrpDeviceProtocolModule` is the device-side extension point for version-scoped
codecs and message handlers. Modules are registered before the Server accepts
clients and are evaluated before the standard profile. The legacy
`IVirtualReaderPresetContributor` and `IVirtualReaderProtocolModule` contracts
remain available through the compatibility façade.

The protocol service consumes `ILlrpDevice`; `VirtualLlrpDevice` is the current
deterministic implementation. A future physical-reader process can provide a
different `ILlrpDevice` without changing TCP, LLRP resource state, or version
translation. This seam does not itself claim to drive a real RFID module or
emulate an analog RF waveform.

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

The generic Server supports LLRP 1.0.1 and 1.1 as explicit profiles, and keeps
the current 2.0 adapter baseline. It handles
connection initialization, reader events, capabilities/configuration,
ROSpec/AccessSpec lifecycle, KEEPALIVE/ACK, close/error responses, tag reports,
and standard C1G2 Read/Write/BlockWrite/Lock/Kill/BlockErase operations. LLRP 1.1 requests are translated to a
single canonical 1.0.1 resource state through the shared codec registry; the
wire header and response types remain 1.1.

Reports are configurable with `LlrpDeviceReportOptions`:

```csharp
var serverOptions = new LlrpDeviceServerOptions
{
    Reports = new LlrpDeviceReportOptions
    {
        ReportInterval = TimeSpan.FromMilliseconds(100),
        ReportCount = 0,
        Repeat = true,
    },
};

```

The RF-observable scenarios are deterministic and operate at the tag-observation
boundary:

- `static` returns the configured tags consistently;
- `moving-tags` alternates tag presence in repeatable presence windows;
- `noisy` applies seeded detection decisions and optional RSSI jitter.

These scenarios make report streams and Tag Access behavior reproducible for
tests. They are not a physical RF or reader-antenna simulator.

`DropResponseForMessageTypes`, `ErrorResponseForMessageTypes`,
`CloseConnectionAfterRequestMessageTypes`, and
`TruncateResponseForMessageTypes` are deterministic fault-injection hooks for
tests. The Server also accepts the shared `ILlrpFrameObserver` and logger factory
from `LlrpNet`.
