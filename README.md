# LLRPCSharp

[中文](README.zh.md)

![.NET 10.0](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square&logo=dotnet)
![C# 14](https://img.shields.io/badge/C%23-14.0-239120?style=flat-square&logo=c-sharp)
![Build & Tests](https://img.shields.io/badge/Build%20%26%20Tests-523%20Passed-10b981?style=flat-square)
![Protocol](https://img.shields.io/badge/LLRP-1.0.1%20%7C%201.1%20%7C%202.0-3b82f6?style=flat-square)

**LLRPCSharp** is a .NET 10 / C# 14 RFID reader toolkit. The primary product is the client-side LlrpSdk: a managed API for connecting to LLRP readers, reading capabilities and settings, configuring inventory, receiving tag reports, and performing standard C1G2 Tag Access operations.

The repository also contains the protocol foundation and a separate device-side virtual reader runtime. The virtual device is described in the final section.

## Client SDK

* **LlrpSdk** is the normal application surface. LlrpReader exposes connection state, ReaderCapabilities, ReaderSettings, InventorySession, tag reports, and Tag Access without requiring applications to build ROSpec or AccessSpec messages.
* **Vendor extension projects** add strongly typed behavior. The Impinj extension provides UseImpinj(), typed capability mappings, inventory extensions, and report projections.
* **LlrpNet** is the protocol layer with generated LLRP 1.0.1/1.1/2.0 types, codecs, registries, and asynchronous TCP transport. Exact wire control is available through reader.Protocol or direct LlrpNet use.
* **LlrpCli** is the first client application built on this SDK and follows the same connection, settings, inventory, Tag Access, and raw-protocol workflows.

The normal client workflow is: connect and negotiate a version; read capabilities and settings; obtain and edit a ReaderSettings default; apply it; start an InventorySession; consume ReadReportsAsync; then use high-level Tag Access methods when needed.

High-level inventory owns the SDK-reserved ROSpec/AccessSpec resource domain. Exact resource IDs, manual resources, and unsupported vendor messages belong to the expert RoSpecs, AccessSpecs, and Protocol surfaces. Raw or expert writes invalidate the managed-state assumption; synchronize or explicitly perform a new managed takeover before returning to high-level control.

### Public SDK example: LlrpSdk

```csharp
await using var reader = LlrpReader.CreateBuilder("192.168.1.100").Build();
await reader.ConnectAsync();
ReaderSettingsDefaults defaults = await reader.GetDefaultSettingsAsync();
await reader.ApplySettingsAsync(defaults.Settings); // deploys, does not start
await using InventorySession session = await reader.StartInventoryAsync();
await foreach (TagReport tag in session.ReadReportsAsync())
    Console.WriteLine(tag.EpcHex);
```

### Settings and reports

* QuerySettingsAsync recognizes the SDK-reserved ROSpec (14150) when present; other manual resources remain expert data.
* InventorySession.ReadReportsAsync is the isolated session stream. The connection-level TagsReported/ReadTagReportsAsync observer is an alternative and conflicting consumers are rejected.
* StopAsync removes SDK-managed inventory resources. Application-held Settings can be applied again; the reader does not persist an application draft.
* ApplySettingsAsync with Inventory = null changes reader-global configuration only. With Inventory, it takes over the managed resource domain, rebuilds the SDK ROSpec and optional AttachedData AccessSpec, and leaves the ROSpec disabled. StartInventoryAsync(settings) is the one-call deployment-and-start form.

---

## System architecture

![LLRPCSharp System Architecture](docs/images/architecture.svg)

The client side is the primary SDK surface. The device side is a separate in-process virtual reader used by tests, demos, UI development, and external-client interoperability.

    Client side
      LlrpSdk + LlrpSdk.Extensions.*  managed reader SDK
      LlrpNet                        protocol codecs and transport
      LlrpCli                        client-side CLI

    Device side
      LlrpDevice.Virtual.Hosting     virtual-device SDK facade
      LlrpVirtualDevice.Cli          virtual-device CLI

---

## Client CLI (LlrpCli)

LlrpCli is the reference client application, not a virtual-device server. Start it with dotnet run --project src/LlrpCli. Typical commands are connect HOST, caps, settings show, settings edit --from defaults, settings apply --defaults --yes, inventory start --monitor live, inventory stop, and raw send. The client CLI follows the same resource ownership and report-stream rules as LlrpSdk.

---

## Protocol and vendor compatibility

| Capability / Vendor | Support | Details |
| :--- | :--- | :--- |
| **LLRP 1.0.1** | Available | Primary client SDK and standard virtual-device path |
| **LLRP 1.1** | SDK baseline | Explicit adapter and generated types; broader real-reader coverage pending |
| **LLRP 2.0** | Protocol baseline | Generated V2_0 assets and adapter; real-device verification pending |
| **Impinj extensions** | Mainline | UseImpinj() pipeline, typed capabilities, inventory/report extensions, R420 path |
| **Zebra extensions** | Baseline | Wire package and UseZebra() pipeline; selected projections verified |

---

## Repository layout

The tree emphasizes the client SDK. Virtual-device SDK and CLI are separate device-side consumers:

    src/
      LlrpSdk/                    primary managed client SDK
      LlrpSdk.Extensions.Impinj/  Impinj client extension
      LlrpSdk.Extensions.Zebra/   Zebra client extension
      LlrpNet/                    protocol model, codecs, registry, and TCP transport
      LlrpCli/                    client-side interactive/scriptable CLI

      LlrpDevice.Abstractions/    device-side contract
      LlrpDevice.Server/           generic LLRP device-side server
      LlrpDevice.Virtual/          deterministic virtual reader
      LlrpDevice.Virtual.Hosting/  public virtual-device SDK facade
      LlrpVirtualDevice.Cli/       virtual-device server CLI

    docs/                         SDK, CLI, and virtual-device guides
    tests/                        protocol, SDK, virtual-device, interop, and hardware tests

---

## Virtual Device SDK and CLI (device-side)

The virtual device creates an LLRP endpoint that behaves like a reader. It is intended for SDK development, CI, protocol inspection, UI development, and external-client interoperability. It is not a second client API, does not replace a physical reader, and does not simulate real RF waveforms.

The public entry point is LlrpDevice.Virtual.Hosting:

* IVirtualDeviceHost owns one endpoint and exposes start/stop/restart, endpoint facts, connected clients, lifecycle events, and decoded message events.
* VirtualDeviceHostOptions selects protocol/profile, listener, report cadence, deterministic inventory data, RF-observable scenario, and relaxed or strict ROSpec lifecycle checks.
* VirtualLlrpDevice supplies deterministic tag memory, standard Tag Access, GPI/GPO state, and static/moving/noisy observable behavior.
* LlrpDevice.Server implements TCP, LLRP version dispatch, ROSpec/AccessSpec state, events, and report delivery.

The default profile is llrp1.0.1_standard. The built-in impinj.r420.llrp-1.0.1 profile adds captured Impinj capability/configuration parameters and the Impinj control-message module. Tags can be supplied through VirtualDeviceHostOptions.Inventory before start.

### Public SDK example: LlrpDevice.Virtual.Hosting

This is the device-side SDK. It creates an LLRP endpoint; an external LlrpSdk, LlrpCli, ItemTest, or other LLRP client connects to that endpoint.

```csharp
using LlrpDevice.Virtual.Hosting;

await using IVirtualDeviceHost host = VirtualLlrpDeviceHost.Create(
    new VirtualDeviceHostOptions
    {
        ProfileId = VirtualDeviceProfiles.Standard101Id,
        Port = 0,
        Inventory = new VirtualInventoryOptions
        {
            Tags =
            [
                new VirtualInventoryTag
                {
                    ElectronicProductCode = Convert.FromHexString("E28011710000020D056E9BEE")
                }
            ]
        }
    });

await host.StartAsync();
Console.WriteLine($"LLRP endpoint: 127.0.0.1:{host.BoundPort}");
// Connect a client to host.BoundPort, then stop the virtual reader when finished.
await host.StopAsync();
```

The standalone device-side CLI uses the same facade. Its lifecycle commands are server create --llrp 1.0.1, server start, server status, logs on, server stop, and server destroy. The run command starts a configured foreground service; live starts the configured device and enters the interactive shell.

The virtual-device CLI observes requests generated by an external LLRP client. Its Impinj R420 profile has been validated to accept an Impinj ItemTest 2.10.0 LLRP client connection and reach the LLRP 1.0.1 inventory-start path. ItemTest features outside LLRP, including mDNS discovery and rshell/SSH services, are outside this project scope.

See docs/guides/virtual-device-cli.md and docs/architecture/overview.md for implementation details.

---

## User guides

* SDK API Guide: docs/guides/sdk-api-guide.md
* Client CLI User Guide: docs/guides/cli-user-guide.md
* Virtual Device SDK and CLI: docs/guides/virtual-device-cli.md

## License

[MIT License](LICENSE)
