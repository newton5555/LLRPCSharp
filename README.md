# LLRP C# SDK

[中文](README.zh.md)

[![LLRPCSharp Architecture and Capabilities](docs/images/llrpcsharp_infographic.png)](docs/showcase.md)

A modern LLRP SDK for .NET. `LlrpSdk.LlrpReader` is the application-facing session root for one RFID reader, covering connection management, protocol negotiation, inventory, resource operations, diagnostics, and protocol extensions.

For the exact current implementation status, see [docs/status.md](docs/status.md). For planned work, see [docs/roadmap.md](docs/roadmap.md). For the long-term architecture, see [docs/architecture/overview.md](docs/architecture/overview.md).

## Current Capabilities

- SDK and CLI baselines for LLRP 1.0.1 and 1.1.
- Automatic 1.1 negotiation, with policy-based forcing of 1.0.1 or 1.1.
- `LlrpReader` connection state machine, capability initialization, keepalive auto-response, and raw/typed protocol entry points.
- Managed inventory APIs: `StartAsync`, `StopAsync`, `InventoryAsync`, `ReadTagReportsAsync`, and `TagsReported`.
- Advanced ROSpec and AccessSpec resource services.
- `Microsoft.Extensions.Logging` integration and raw TX/RX frame observation through `ILlrpFrameObserver`.
- LTK XML / YAML protocol definition import, validation, and C# code generation.
- Spectre.Console CLI for online connect, monitor, and live shell workflows, plus offline inspect/decode/encode.
- Impinj extension registration, `UseImpinj()`, and generated strongly typed codec assets.
- Minimal 1.0.1 virtual reader for capability queries and ROSpec lifecycle tests.

## Quick Start

```powershell
dotnet build LLRPCSharp.slnx --no-restore
dotnet test LLRPCSharp.slnx --no-build
```

Connect to a reader:

```csharp
await using LlrpReader reader = LlrpReader.CreateBuilder("192.0.2.10")
    .WithLoggerFactory(loggerFactory)
    .WithFrameObserver(frameObserver)
    .Build();

await reader.ConnectAsync();
```

For older devices that disconnect after receiving higher-version negotiation messages, skip auto-detection explicitly:

```csharp
await using LlrpReader reader = LlrpReader.CreateBuilder("192.0.2.10")
    .WithProtocolVersionPolicy(LlrpProtocolVersionPolicy.Force101)
    .Build();

await reader.ConnectAsync();
```

CLI equivalents:

```powershell
dotnet run --project src/LlrpCli -- connect 192.0.2.10 --llrp auto
dotnet run --project src/LlrpCli -- monitor 192.0.2.10 --llrp 1.0.1
```

Offline protocol diagnostics do not require a connected reader:

```powershell
dotnet run --project src/LlrpCli -- inspect "043E0000000A01020304"
dotnet run --project src/LlrpCli -- decode "043E0000000A01020304"
dotnet run --project src/LlrpCli -- encode get-rospecs --message-id 1
```

## Repository Layout

```text
definitions/   Machine-readable protocol definitions and extension definitions
docs/          Status, roadmap, architecture, ADRs, and source references
references/    Local standards, packet captures, and legacy references, mostly not committed
samples/       SDK usage samples
src/           Product source code
testdata/      Sanitized test frames, scenarios, and expected results
tests/         Unit, integration, and interoperability tests
tools/         Definition import, generation, validation, and test helpers
```

## Documentation

- [Current Status](docs/status.md): implemented capabilities, missing work, and current build status.
- [Roadmap](docs/roadmap.md): development order and planned work.
- [Architecture and Capability Map](docs/showcase.md): project architecture, capability boundaries, and infographic.
- [Documentation Index](docs/README.md): architecture, ADRs, and references.
- [Agent Guide](AGENTS.md): repository rules for coding agents.
- [Protocol Definitions](definitions/README.md): XML/YAML definitions and generation commands.
