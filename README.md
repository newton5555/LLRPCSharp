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

### Message-level Virtual Reader

Run a real TCP LLRP device endpoint for SDK/CLI development and offline CI:

```powershell
dotnet run --project src/LlrpCli/LlrpCli.csproj -- virtual-reader `
  --port 5085 --llrp 1.1 --name ci-reader
```

The standalone Manager also exposes registered presets and in-process
multi-instance lifecycle APIs. See the [Virtual Reader Manager guide](docs/guides/virtual-reader-manager.md).

---

## 📋 Protocol & Vendor Compatibility

| Capability / Vendor | Support | Details |
| :--- | :--- | :--- |
| **LLRP 1.0.1** | Available (Verified) | Full SDK, CLI, Virtual Reader, and standard ROSpec / AccessSpec operations |
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
  LlrpCli/    Command-line tooling & Live Shell
docs/
  guides/     SDK API Guide and CLI User Guide
```

---

## 📚 User Guides

* [SDK API Guide](docs/guides/sdk-api-guide.md)
* [CLI User Guide](docs/guides/cli-user-guide.md)

---

## 📄 License

[MIT License](LICENSE)
