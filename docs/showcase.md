# LLRPCSharp Architecture and Capability Map

[中文](showcase.zh.md)

This showcase explains the project positioning, architecture boundaries, and current capabilities of LLRPCSharp. The exact implementation status is tracked in [status.md](status.md), and planned work is tracked in [roadmap.md](roadmap.md).

## Infographic

![LLRPCSharp Architecture and Capabilities Infographic](images/llrpcsharp_infographic.png)

## Architecture Advantages

### 1. Modernizing the LTK.NET model

LLRPCSharp retains the practical parts of the traditional LTK.NET workflow:
machine-readable definitions, generated protocol types, and exact wire-level
encoding. It modernizes the surrounding design for current .NET by separating
the definition model, source generator, codec registry, asynchronous transport,
version adapters, and managed Reader API. This makes protocol assets reusable
by offline tools, vendor modules, the SDK, and the virtual reader without
coupling them to one application stack.

### 2. Modern .NET Foundation

- **Async session and dispatch model**: The transport, session, and event dispatch layers use modern .NET async patterns for continuous reader message streams.
- **Low-allocation protocol boundaries**: Transport and protocol parsing boundaries prefer memory-friendly types such as `ReadOnlyMemory<byte>` to reduce unnecessary copying.

### 3. Clean Adapter Boundary

- **Version isolation**: LLRP 1.0.1, 1.1, and 2.0 are isolated behind `ILlrpProtocolAdapter`; LLRP 2.0 protocol assets and SDK adapter baseline are implemented, with real-device acceptance pending.
- **Version-neutral application entry points**: Application code works primarily with managed APIs such as `LlrpReader`, `InventorySettings`, ROSpec services, and AccessSpec services instead of hand-assembling versioned protocol messages.

### 4. Pluggable Reader Extensions

- **Vendor extension registration**: Impinj support can be enabled through `UseImpinj()`, and Zebra support through `UseZebra()`, which register generated strongly typed codec assets and extension modules.
- **Low-intrusion extension model**: Standard LLRP behavior remains layered away from vendor extensions, so the generic LLRP path remains available when extensions are not enabled.

## Project Capabilities

### 1. Session Lifecycle Management

- **Connection and version negotiation**: Supports automatic protocol version negotiation (1.0.1 / 1.1 / 2.0), with policy-based forcing of 1.0.1, 1.1, or 2.0.
- **Limited automatic reconnect**: Provides `LlrpAutomaticReconnectOptions` and `WithAutomaticReconnect(...)` as a reconnect baseline after unexpected disconnects. After a successful reconnect the SDK queries the device's current ROSpec/AccessSpec state and realigns its internal state (observing reality rather than re-applying the previous desired configuration).
- **Managed state synchronization**: Raw Protocol operations invalidate managed state. Use `SynchronizeStateAsync()` to inspect and adopt existing resources, or pass the desired inventory settings to `StartInventoryAsync(settings)` / `ApplySettingsAsync(...)` to explicitly delete standard resources and rebuild SDK-managed state without a prior synchronization call.

### 2. Advanced Resource Control

- **ROSpec lifecycle service**: `reader.RoSpecs` provides Add, Delete, Enable, Disable, Start, Stop, and GetAll operations.
- **AccessSpec lifecycle service**: `reader.AccessSpecs` provides Add, Delete, Enable, Disable, and GetAll operations.
- **Inventory entry points**: `StartInventoryAsync(settings)` deploys and starts inventory and returns an `InventorySession` with an isolated report stream; `StartInventoryAsync()` starts the previously deployed inventory. The session-less `StartAsync` overloads are internal (tag access and connection-level flows only). `ReadTagReportsAsync` and `TagsReported` observe the whole connection, and the first report outlet consumed for an inventory owns delivery; the other outlets fail fast until that inventory stops.

### 3. CLI Diagnostics and Interop

- **Online diagnostics**: `LlrpCli` supports connect, monitor, and live shell workflows for observing reader interactions.
- **Offline protocol tools**: `inspect`, `decode`, `validate`, and `encode` inspect, decode (supporting single frames and `.pcapng` capture analysis), validate, and construct LLRP messages without a connected reader (supporting 1.0.1, 1.1, 2.0, Impinj, and Zebra).
- **Raw frame observation**: `ILlrpFrameObserver` and `LlrpFrameJournal` can capture complete TX/RX frames at the Transport/Session boundary for hex diagnostics, auditing, and interoperability analysis.
