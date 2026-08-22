# LLRPCSharp

[中文](README.zh.md)

![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square&logo=dotnet)
![LLRP](https://img.shields.io/badge/LLRP-1.0.1%20%7C%201.1%20%7C%202.0-2563eb?style=flat-square)
![License](https://img.shields.io/badge/license-MIT-16a34a?style=flat-square)

LLRPCSharp is a modern .NET toolkit for building applications that communicate with RFID readers over LLRP. Its main product is **LlrpSdk**, a managed reader API for connection lifecycle, capability discovery, settings, inventory, tag reports, and standard C1G2 Tag Access.

The repository also contains the lower-level protocol stack, optional vendor extensions, client and device-side command-line tools, and a deterministic TCP/LLRP virtual reader. These parts share one protocol foundation but keep client and device responsibilities separate.

## Choose the right entry point

| You want to… | Use |
|---|---|
| Build an application that controls a physical LLRP reader | **LlrpSdk** |
| Enable typed Impinj or Zebra features | **LlrpSdk.Extensions.Impinj** or **LlrpSdk.Extensions.Zebra** |
| Send exact messages, inspect frames, or work with generated protocol types | **LlrpNet.Core** and **LlrpNet.Protocol** |
| Operate or diagnose a reader from a terminal | **LlrpCli** |
| Host a deterministic reader endpoint for tests or UI development | **LlrpDevice.Virtual.Hosting** |
| Run that virtual endpoint as a standalone process | **LlrpVirtualDevice.Cli** |

## What the SDK provides

### Managed reader API

One **LlrpReader** represents one reader connection. It owns protocol negotiation, initialization, the underlying session, keepalive handling, unsolicited-message processing, and extension activation.

Applications normally work with version-neutral models:

- **ReaderCapabilities** and **ReaderIdentity** for device facts;
- **ReaderSettings** and **InventorySettings** for configuration intent;
- **InventorySession** and **TagReport** for streamed observations;
- high-level read, write, lock, kill, and block-erase requests for standard Tag Access;
- connection, operation, resource, error, GPI, antenna, and buffer events.

LLRP 1.0.1, 1.1, and 2.0 differences are contained behind protocol adapters. Ordinary application code does not need generated version-specific message or parameter types.

### Two control planes

LLRPCSharp exposes two ownership models:

1. **Managed control plane** — settings, managed inventory, reports, and Tag Access using SDK domain models. The
   SDK owns reserved ROSpec 14150, AttachedData AccessSpec 14151, and temporary Tag Access resources.
2. **Expert protocol control plane** — explicit ROSpec/AccessSpec operations through **reader.RoSpecs** and
   **reader.AccessSpecs**, alongside typed or exact-frame transactions through **reader.Protocol**. These are
   direct protocol conveniences; callers own their lifecycle.

The two control planes share one operation lock. Expert writes are available whenever the reader is Ready; they end
the current managed session and mark ObservedState stale while retaining DesiredSettings. `SynchronizeStateAsync()`
refreshes the device snapshot for inspection, and managed APIs can immediately reconcile their reserved resources.
The default `PreserveForeign` policy keeps foreign resources, while `ReplaceAll` is an explicit destructive choice.
Reader capabilities expose nullable resource limits so callers can plan capacity before expert writes or managed
deployment.

## Quick start

Requirements:

- .NET 10 SDK;
- an LLRP reader reachable over TCP, normally on port 5084.

Install the core package:

~~~powershell
dotnet add package LlrpSdk
~~~

Connect, start inventory, read one report, and stop:

~~~csharp
using LlrpSdk;

await using LlrpReader reader = LlrpReader
    .CreateBuilder("192.168.1.100")
    .Build();

await reader.ConnectAsync();

InventorySettings settings = new InventorySettingsBuilder()
    .Antennas(1)
    .ReportEvery(1)
    .Build();

await using InventorySession inventory =
    await reader.StartInventoryAsync(settings);

await foreach (TagReport report in inventory.ReadReportsAsync())
{
    Console.WriteLine(
        $"EPC={report.EpcHex} Antenna={report.AntennaId} RSSI={report.PeakRssi}");
    break;
}

await reader.StopAsync();
~~~

For device-derived defaults and a two-stage deploy/start workflow:

~~~csharp
ReaderSettingsDefaults defaults = await reader.GetDefaultSettingsAsync();
await reader.ApplySettingsAsync(defaults.Settings); // deploy, do not start

await using InventorySession inventory = await reader.StartInventoryAsync();
~~~

Use **QuerySettingsAsync** when you need the reader's current configuration rather than an SDK-generated default profile.

### Impinj extension

~~~powershell
dotnet add package LlrpSdk.Extensions.Impinj
~~~

~~~csharp
using LlrpSdk;
using LlrpSdk.Extensions.Impinj;

await using LlrpReader reader = LlrpReader
    .CreateBuilder("192.168.1.100")
    .UseImpinj()
    .Build();
~~~

The extension registers Impinj codecs before connection, activates only when the connected reader identity matches, and contributes typed settings, inventory options, and report fields without adding vendor types to the core SDK.

## Protocol and device support

Support is separated from hardware acceptance. Generated types and passing virtual-device tests prove a software path; they do not prove interoperability with every reader model and firmware.

| Area | Current state |
|---|---|
| **LLRP 1.0.1** | Mainline client and virtual-device path; physical-reader workflows are accepted on the maintained baseline devices. |
| **LLRP 1.1** | Generated protocol, SDK adapter, negotiation, CLI, virtual server, and automated interoperability baseline; broader physical-reader coverage remains device-specific. |
| **LLRP 2.0** | Generated protocol and codecs, SDK adapter, Auto/Force20 negotiation, CLI, virtual server, and automated round-trip coverage; physical-reader interoperability is not yet accepted. |
| **Impinj** | Mainline extension path. The R420 baseline covers connection, capabilities/settings, inventory, report extensions, and non-destructive Tag Access workflows. |
| **Zebra** | Wire package and SDK extension baseline. Selected FX9600 capability/configuration and report mappings have physical evidence; remaining custom parameters still require byte-level validation. |
| **Seuic** | Device-profile/defaults extension over the standard protocol path; there is no separate custom wire package. |
| **Virtual device** | Deterministic LLRP 1.0.1/1.1/2.0 endpoint with resource lifecycle, reports, standard Tag Access, fault hooks, and standard/Impinj profiles. It is not a physical RF simulator. |

See [current implementation status](docs/status.md) and the [reader interoperability record](docs/acceptance/reader-interoperability.md) for the authoritative boundary.

## Architecture

![LLRPCSharp architecture](docs/images/architecture.svg)

The repository is divided into two product sides:

~~~text
Client applications
  -> LlrpSdk + LlrpSdk.Extensions.*
  -> LlrpNet.Core + LlrpNet.Protocol
  -> physical or virtual LLRP endpoint

Device-side tools
  -> LlrpDevice.Virtual.Hosting
  -> LlrpDevice.Server + LlrpDevice.Virtual
  -> TCP/LLRP clients
~~~

Key boundaries:

- **LlrpNet.Core** owns TCP transport, framing, transactions, timeout/cancellation, and frame observation.
- **LlrpNet.Protocol** owns generated versioned messages, parameters, enums, codecs, registries, and raw/unknown wire values.
- **LlrpSdk** owns the application-facing reader lifecycle and version-neutral workflows.
- **LlrpSdk.Extensions.\*** adds vendor behavior through protocol modules and reader extensions.
- **LlrpDevice.Server** owns device-side LLRP session and resource behavior.
- **LlrpDevice.Virtual** implements deterministic device behavior behind a version-neutral device contract.

Generated protocol files are committed build assets, but their source of truth is under **definitions/** together with the importer and generator. Do not hand-edit generated **.g.cs** files.

## Command-line tools

Run the client CLI:

~~~powershell
dotnet run --project src/LlrpCli/LlrpCli.csproj -- --help
~~~

LlrpCli provides an interactive live shell, one-shot inventory and Tag Access commands, settings workflows, and offline encode/decode/inspect tools.

Run a virtual reader in interactive live mode:

~~~powershell
dotnet run --project src/LlrpVirtualDevice.Cli/LlrpVirtualDevice.Cli.csproj -- live --config src/LlrpDevice.Virtual/config/virtual-device.example.json
~~~

The virtual-device CLI hosts a reader endpoint. It does not generate client requests; connect with LlrpSdk, LlrpCli, or another LLRP client to drive it.

## Repository map

~~~text
src/
  LlrpNet/                         transport, protocol model, generator, and codecs
  LlrpSdk/                         managed client SDK
  LlrpSdk.Extensions.Abstractions/ extension contracts
  LlrpSdk.Extensions.Impinj/       Impinj SDK extension
  LlrpSdk.Extensions.Zebra/        Zebra SDK extension
  LlrpSdk.Extensions.Seuic/        Seuic profile/defaults extension
  LlrpCli/                         client CLI

  LlrpDevice.Abstractions/         version-neutral device contract
  LlrpDevice.Server/               generic device-side LLRP server
  LlrpDevice.Virtual/              deterministic device implementation
  LlrpDevice.Virtual.Hosting/      public virtual-device facade
  LlrpDevice.Virtual.Impinj/       Impinj virtual-device profile
  LlrpVirtualDevice.Cli/           standalone virtual-device CLI

definitions/                       protocol definitions and generation inputs
docs/                              status, architecture, guides, and acceptance
tests/                             unit, architecture, interop, virtual, and hardware tests
tools/                             live smoke and protocol probes
~~~

## Build and test

~~~powershell
dotnet restore LLRPCSharp.slnx
dotnet build LLRPCSharp.slnx --no-restore
dotnet test LLRPCSharp.slnx --no-build
~~~

Physical-reader acceptance is separate from automated tests:

~~~powershell
dotnet test tests/LlrpSdk.Hardware.Tests/LlrpSdk.Hardware.Tests.csproj
~~~

Hardware tests may skip when no configured reader is reachable. A release acceptance record must confirm that the intended tests actually ran and must be entered in the interoperability document.

## Documentation

- [Documentation index](docs/README.md)
- [SDK API guide](docs/guides/sdk-api-guide.md)
- [Client CLI guide](docs/guides/cli-user-guide.md)
- [Virtual Device SDK and CLI](docs/guides/virtual-device-cli.md)
- [Architecture overview](docs/architecture/overview.md)
- [Protocol extension guide](docs/architecture/protocol-extension-guide.md)
- [Current status](docs/status.md)
- [Roadmap](docs/roadmap.md)
- [Test architecture and hardware acceptance](tests/README.md)

## License

[MIT](LICENSE)
