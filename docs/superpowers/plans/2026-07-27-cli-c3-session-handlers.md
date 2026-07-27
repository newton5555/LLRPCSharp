# CLI C3 Session Handler Split Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Split Live Shell connection, inventory, and monitoring lifecycle work into focused handlers while retaining the current commands, output, and session behavior.

**Architecture:** `LiveSessionContext` remains the single mutable state holder for one Live Shell session. `LiveConnectionHandler` owns reader creation, frame-observer installation, connect/disconnect disposal, and window title updates; it composes `LiveInventoryHandler` so a reconnect or disconnect first stops managed inventory. `LiveInventoryHandler` owns managed-inventory start/stop/report pumping, while `LiveMonitorHandler` owns frame/table monitor scopes and their aggregation callback. `LiveCommand` remains the host: it parses and routes commands, renders shell help/status, and delegates lifecycle work.

**Tech Stack:** .NET 10, C#, Spectre.Console, LlrpSdk, existing `LlrpCli.Tests`.

## Global Constraints

- Do not hand edit generated `*.g.cs` protocol or Impinj extension files.
- Preserve existing Live Shell command text, connection options, protocol behavior, and safety checks.
- Keep C# source UTF-8 without BOM and CRLF according to `.editorconfig` / `.gitattributes`.
- Do not create a Git commit unless the user explicitly requests one.

---

### Task 1: Extract managed inventory lifecycle

**Files:**
- Create: `src/LlrpCli/Commands/LiveInventoryHandler.cs`
- Modify: `src/LlrpCli/Commands/LiveCommand.cs`

**Interfaces:**
- Consumes: `LiveSessionContext.Reader`, `InventoryCancellation`, and `InventoryPumpTask`.
- Produces: `Task HandleAsync(string[] tokens, CancellationToken cancellationToken)` and `Task StopAsync(CancellationToken cancellationToken)`.
- Used by: `LiveConnectionHandler` before replacing or disconnecting a reader, and `LiveCommand` for the `inventory` route.

- [ ] **Step 1: Move the inventory command and report pump to `LiveInventoryHandler`**

```csharp
internal sealed class LiveInventoryHandler(IAnsiConsole console, LiveSessionContext session)
{
    public Task HandleAsync(string[] tokens, CancellationToken cancellationToken);
    public Task StopAsync(CancellationToken cancellationToken);
}
```

Keep `inventory start [antenna-id] | stop | status`, the existing connection checks, task cancellation, `ReaderSettings.AntennaIds`, and tag-line rendering byte-for-byte equivalent in user-visible text.

- [ ] **Step 2: Replace `LiveCommand` inventory implementations with delegation**

```csharp
case LiveCommandRoute.Inventory:
    await _inventoryHandler.HandleAsync(tokens, cancellationToken);
    break;
```

Remove the now-relocated `HandleInventoryAsync`, `StopInventoryAsync`, and `PumpTagReportsAsync` from `LiveCommand`.

- [ ] **Step 3: Build the CLI project**

Run: `dotnet build src/LlrpCli/LlrpCli.csproj --no-restore -m:1`

Expected: zero build errors.

### Task 2: Extract passive monitor and live tag-table scopes

**Files:**
- Create: `src/LlrpCli/Commands/LiveMonitorHandler.cs`
- Modify: `src/LlrpCli/Commands/LiveCommand.cs`

**Interfaces:**
- Consumes: `LiveSessionContext.Reader`, `IsMonitoring`, `IsMonitoringTable`, and `MonitorFrameCallback`; the frame observer installed by the connection handler invokes those state flags.
- Produces: `Task HandleAsync(string[] tokens, CancellationToken cancellationToken)`.
- Used by: `LiveCommand` for the `monitor` route.

- [ ] **Step 1: Move monitor mode parsing, raw-frame timing, table aggregation, and `TagStat` into `LiveMonitorHandler`**

```csharp
internal sealed class LiveMonitorHandler(IAnsiConsole console, LiveSessionContext session)
{
    public Task HandleAsync(string[] tokens, CancellationToken cancellationToken);
}
```

Keep the `monitor [seconds] [--table | --frames]` behavior, ignored non-report frames, and `finally` cleanup that clears monitoring flags and callbacks.

- [ ] **Step 2: Delegate the monitor route from `LiveCommand`**

```csharp
case LiveCommandRoute.Monitor:
    await _monitorHandler.HandleAsync(tokens, cancellationToken);
    break;
```

Remove `HandleMonitorAsync` and the nested `TagStat` from `LiveCommand`.

- [ ] **Step 3: Run the existing CLI test project**

Run: `dotnet test tests/LlrpCli.Tests/LlrpCli.Tests.csproj --no-build --no-restore -m:1`

Expected: all existing tests pass.

### Task 3: Extract connection lifecycle

**Files:**
- Create: `src/LlrpCli/Commands/LiveConnectionHandler.cs`
- Modify: `src/LlrpCli/Commands/LiveCommand.cs`

**Interfaces:**
- Consumes: `IAnsiConsole`, `LiveSessionContext`, and `LiveInventoryHandler.StopAsync`.
- Produces: `Task ConnectAsync(CliConnectionOptions options, CancellationToken cancellationToken)`, `Task DisconnectAsync(CancellationToken cancellationToken)`, and `Task DisposeAsync()`.
- Used by: automatic startup connection, explicit `connect`, raw-host shorthand, explicit `disconnect`, and shell shutdown.

- [ ] **Step 1: Move reader lifecycle and observer installation to `LiveConnectionHandler`**

```csharp
internal sealed class LiveConnectionHandler(
    IAnsiConsole console,
    LiveSessionContext session,
    LiveInventoryHandler inventory)
{
    public Task ConnectAsync(CliConnectionOptions options, CancellationToken cancellationToken);
    public Task DisconnectAsync(CancellationToken cancellationToken);
    public Task DisposeAsync();
}
```

Install the same `DelegateFrameObserver`: raw monitoring renders frames, table monitoring invokes `MonitorFrameCallback`. Preserve the five-second connect timeout, vendor-mode rendering, negotiation-frame output, failure cleanup, and restricted-terminal-safe title update. Do not have the handler render status; the host retains `HandleStatus()` immediately after a successful connection, as today.

- [ ] **Step 2: Make `LiveCommand` a routing host for lifecycle operations**

Construct the three handlers in `LiveCommand(IAnsiConsole)`. Replace automatic, interactive, shorthand, disconnect, and shutdown lifecycle calls with handler methods. Keep `HandleStatus`, `HandleCaps`, `HandleFrames`, help, parsing, and all unrelated resource/configuration handlers in `LiveCommand`.

- [ ] **Step 3: Update C3 status documentation**

Modify `docs/architecture/cli-command-system.md` and `docs/roadmap.md` to state that C3 has extracted connection, inventory, monitoring, and offline-diagnostics handlers, while `LiveCommand` remains the interactive host and routing layer.

- [ ] **Step 4: Verify compilation and Live Shell routing**

Run:

```powershell
dotnet test tests/LlrpCli.Tests/LlrpCli.Tests.csproj --no-build --no-restore -m:1
dotnet build LLRPCSharp.slnx --no-restore -m:1
@('help inventory', 'help monitor', 'q') | dotnet run --project src/LlrpCli --no-build --no-restore --
git diff --check
```

Expected: tests/build pass; `help` renders without Spectre markup-style errors; diff check has no whitespace errors.
