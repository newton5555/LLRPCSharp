# LLRPCSharp

[中文](README.zh.md)

![.NET 10.0](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square&logo=dotnet)
![C# 14](https://img.shields.io/badge/C%23-14.0-239120?style=flat-square&logo=c-sharp)
![Build & Tests](https://img.shields.io/badge/Build%20%26%20Tests-486%20Passed-10b981?style=flat-square)
![Protocol](https://img.shields.io/badge/LLRP-1.0.1%20%7C%201.1%20%7C%202.0-3b82f6?style=flat-square)
![License](https://img.shields.io/badge/License-MIT-blue?style=flat-square)

**LLRPCSharp** is a modern RFID (UHF) Reader LLRP protocol development kit and command-line tool built on **.NET 10.0 / C# 14**.

The project is structured around two core design pillars:
* **`LlrpNet`**: A modern replacement for traditional **LTK.NET**, handling LLRP 1.0.1 / 1.1 / 2.0 binary encoding/decoding, protocol type definitions, and async TCP transport.
* **`LlrpSdk`**: A managed high-level API inspired by the **Impinj Octane SDK**, abstracting low-level `ROSpec` and `AccessSpec` details into intuitive connection, configuration, and tag reporting workflows.

---

## 🏛️ System Architecture

![LLRPCSharp System Architecture](docs/images/architecture.svg)

---

## ⚡ Quick Start

### 1. Basic Inventory Example (Print & Apply Default Settings)

```csharp
using LlrpSdk;

// 1. Create and connect to reader
await using var reader = LlrpReader.CreateBuilder("192.168.1.100").Build();
await reader.ConnectAsync();

// 2. Fetch recommended default settings, print info, and apply to reader
ReaderSettingsDefaults defaults = await reader.GetDefaultSettingsAsync();
Console.WriteLine($"Loaded default profile: {defaults.ProfileId}");
Console.WriteLine($"Default antennas: {string.Join(", ", defaults.Settings.Inventory.AntennaIds)} (0 = all antennas)");

await reader.ApplySettingsAsync(defaults.Settings);

// 3. Start managed inventory session and consume tag reports
await using var session = await reader.StartInventoryAsync();
await foreach (TagReport tag in session.ReadReportsAsync())
{
    Console.WriteLine($"[Antenna {tag.AntennaId}] EPC: {tag.EpcHex} | RSSI: {tag.PeakRssi} dBm");
}
```

### 2. Configure Antennas & Impinj Extensions (Mode/Power Index mapped in `reader.Capabilities`)

```csharp
using LlrpSdk;
using LlrpSdk.Extensions.Impinj;

await using var reader = LlrpReader.CreateBuilder("192.168.1.100")
    .UseImpinj()
    .Build();

await reader.ConnectAsync();

// Note: Physical meanings of ModeIndex and TransmitPowerIndex are queryable in reader.Capabilities
ReaderSettings customSettings = ReaderSettings.Create(builder => builder
    .Inventory(inv => inv
        .Antennas(1, 2)
        .Mode(modeIndex: 1000)      // RF Mode Index (Query actual rate in reader.Capabilities.RfModes)
        .Session(2)                 // Gen2 Session (0~3)
        .Population(128)            // Tag population estimate
        .ReportEveryTag()
        .Impinj(imp => imp.IncludeSerializedTid())));

await reader.ApplySettingsAsync(customSettings);

await using var session = await reader.StartInventoryAsync();
await foreach (TagReport tag in session.ReadReportsAsync())
{
    Console.WriteLine($"EPC: {tag.EpcHex}, TID: {tag.GetSerializedTidHex()}");
}
```

---

## 🛠️ CLI Diagnostic Tool (`LlrpCli`)

Provides an interactive Live Shell and single-line command executions for field debugging:

```powershell
# Launch interactive Live Shell
dotnet run --project src/LlrpCli
```

```text
connect 192.168.1.100   # Connect to reader
status                  # View current connection status and protocol version
caps                    # Query reader antenna count, TransmitPowerIndex table, & ModeIndex table
settings edit           # Interactively edit and apply settings to reader
settings show           # View currently deployed settings
inventory start         # Start inventory scan
inventory stop          # Stop inventory
```

Single-line script command:

```powershell
# Run a 10-second inventory scan and output JSON tag stream
dotnet run --project src/LlrpCli -- inventory 192.168.1.100 --duration 10 --yes
```

### Standalone single-device SDK and CLI

The device-side SDK exposes `IVirtualDeviceHost` and
`VirtualDeviceHostOptions` from `LlrpDevice.Virtual.Hosting`. They are the
public entry point for one virtual device and compose one `VirtualLlrpDevice`
with one `LlrpDeviceServer`; tags can be supplied before the host starts:

The default device is created with `server create --llrp 1.0.1`. This selects
the `llrp1.0.1_standard` capability profile and the independent `default`
inventory source. The Hosting facade also supports the
`impinj.r420.llrp-1.0.1` profile. Listen address and port are creation-time
options, not persisted device configuration.

```powershell
dotnet run --project src/LlrpVirtualDevice.Cli/LlrpVirtualDevice.Cli.csproj -- `
  run --config src/LlrpDevice.Virtual/config/virtual-device.example.json
```

The standalone `LlrpVirtualDevice.Cli` is a sibling of the general
`LlrpCli`. Starting it without arguments enters an interactive shell without
creating a device. Use `server create`, `server start`, `server status`,
`server stop`, `server restart`, and `server destroy` to control one device
host; `logs on|off|status` controls lifecycle, client, and decoded `RX`/`TX`
events. `validate --config <PATH>` validates the single-device behavior document,
`presets` lists behavior presets, and `caps` lists capability profiles.
A WPF or other UI can reference the same Hosting facade directly, configure
`VirtualDeviceHostOptions.Inventory`, and start the host without depending on
the CLI or the compatibility Manager.

Use `live` when the shell should automatically create and start the device:

```powershell
dotnet run --project src/LlrpVirtualDevice.Cli/LlrpVirtualDevice.Cli.csproj -- `
  live --config src/LlrpDevice.Virtual/config/virtual-device.example.json
```

`live` then enters the same shell with event output enabled. Use `run` for the
quiet, non-interactive foreground service mode. The device CLI observes traffic
from an LLRP client; it does not generate client commands on its own.

---

## 📋 Protocol & Vendor Compatibility

| Capability / Vendor | Support | Details |
| :--- | :--- | :--- |
| **LLRP 1.0.1** | Available (Verified) | Full SDK, client CLI, virtual device server, and standard ROSpec / AccessSpec operations |
| **LLRP 1.1** | Available Baseline | Protocol version auto-negotiation and `Llrp11ProtocolAdapter` baseline |
| **LLRP 2.0** | Protocol & Adapter Baseline | Generated `V2_0` assets and `Llrp20ProtocolAdapter` implemented; real-device verification pending |
| **Impinj Extensions** | Available (Mainline) | Strongly typed `UseImpinj()` pipeline, Contributor model (TID, Phase, RSSI) verified on R420/R430 |
| **Zebra Extensions** | Extension Baseline | Wire package and `UseZebra()` pipeline; FX9600 verified for capabilities and Phase/Brand-ID |

---

## 📁 Repository Layout

```text
src/
  LlrpSdk/    Managed high-level API (Inspired by Impinj Octane SDK)
  LlrpNet/    Protocol codecs & TCP transport (Modernized LTK.NET)
  LlrpCli/    General client-side command-line tooling & Live Shell
  LlrpVirtualDevice.Cli/  Single-device virtual LLRP device CLI
docs/
  guides/     SDK, CLI, and Virtual Device guides
```

---

## 📚 User Guides

* [SDK API Guide](docs/guides/sdk-api-guide.md)
* [CLI User Guide](docs/guides/cli-user-guide.md)

---

## 📄 License

[MIT License](LICENSE)
