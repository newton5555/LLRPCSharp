# CLI Tag Access Implementation Plan

> Status: Superseded on 2026-07-28. The current architecture is Live Shell-only for online commands; see [`../architecture/cli-command-system.md`](../architecture/cli-command-system.md).

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Provide safe standard tag-memory access in both CLI hosts: actual non-destructive reads and write dry-runs only.

**Architecture:** An internal `TagAccessCliRequest` owns every command-input conversion and creates version-neutral SDK requests. `TagAccessOperations` owns the shared lifecycle rule: reuse active managed inventory, or start/stop temporary managed inventory around a read. One-shot and Live handlers only bind their host input, call these shared units, and render result/request data.

**Tech Stack:** .NET 10, C#, Spectre.Console, LlrpSdk, Virtual Reader integration tests.

## Global Constraints

- Do not hand edit generated `*.g.cs` files.
- C# source must remain UTF-8 without BOM and CRLF according to repository configuration.
- `tag write` is dry-run only: it must not create a reader, start inventory, add an AccessSpec, or call `WriteTagMemoryAsync`.
- Do not create a Git commit unless the user explicitly requests one.

---

### Task 1: Shared tag access input and operations

**Files:**
- Create: `src/LlrpCli/Commands/TagAccessCliRequest.cs`
- Create: `src/LlrpCli/Commands/TagAccessOperations.cs`
- Test: `tests/LlrpCli.Tests/TagAccessCliRequestTests.cs`

**Interfaces:**
- Produces: `TagAccessCliRequest ParseRead(string epc, string bank, ushort wordPointer, ushort wordCount, ushort antennaId, string? password, uint? timeoutSeconds)`.
- Produces: `ReadTagRequest ToReadRequest()` and `WriteTagRequest ToWriteRequest(IReadOnlyList<ushort> words)`.
- Produces: `Task<TagAccessResult> ReadAsync(LlrpReader reader, ReadTagRequest request, TimeSpan? timeout, CancellationToken cancellationToken)`.

- [ ] **Step 1: Add parser tests**

```csharp
[Fact]
public void ReadRequest_UsesExactEpcSelection()
{
    var request = TagAccessCliRequest.ParseRead("E2801171", "user", 4, 2, 0, null, null);
    Assert.Equal(TagMemoryBank.User, request.ToReadRequest().MemoryBank);
    Assert.Equal((ushort)32, request.ToReadRequest().Selection.BitPointer);
    Assert.Equal((ushort)32, request.ToReadRequest().Selection.BitLength);
}
```

Also assert invalid odd-length EPC, zero word count, unknown bank, malformed password, and odd-length write data throw `CliUsageException`.

- [ ] **Step 2: Implement parsing and lifecycle operations**

`ParseRead` parses uppercase/lowercase hex EPC, converts bank names `reserved|epc|tid|user`, accepts hexadecimal `--password`, and validates every numeric range before creating an exact EPC-bank selection. `ReadAsync` starts `new ReaderSettings { AntennaIds = [request.AntennaId] }` only when the reader is not already inventorying, invokes `ReadTagMemoryAsync`, and stops only inventory it started in `finally`.

- [ ] **Step 3: Run parser tests**

Run: `dotnet test tests/LlrpCli.Tests/LlrpCli.Tests.csproj --no-restore -m:1`

Expected: all tests pass.

### Task 2: Add one-shot CLI commands

**Files:**
- Create: `src/LlrpCli/Commands/TagReadCommand.cs`
- Create: `src/LlrpCli/Commands/TagWriteDryRunCommand.cs`
- Modify: `src/LlrpCli/LlrpCliApplication.cs`
- Modify: `tests/LlrpCli.Tests/LlrpCliApplicationTests.cs`

**Interfaces:**
- Consumes: `TagAccessCliRequest` and `TagAccessOperations` from Task 1.
- Produces: `llrp tag read <HOST> <EPC> ...` and `llrp tag write <EPC> ...`.

- [ ] **Step 1: Add CLI registration tests**

Assert `llrp tag --help` exposes `read` and `write`, and `llrp tag write E2801171 --bank user --word 0 --data 0001` reports a dry-run without attempting a connection.

- [ ] **Step 2: Implement commands and register the `tag` branch**

`TagReadCommand` binds host/port/LLRP options, connects with the established policy, delegates to `TagAccessOperations.ReadAsync`, renders the result, and disconnects. `TagWriteDryRunCommand` validates input and renders the derived request with the header `TAG WRITE DRY RUN — NO TAG MEMORY WAS WRITTEN`.

- [ ] **Step 3: Run CLI tests**

Run: `dotnet test tests/LlrpCli.Tests/LlrpCli.Tests.csproj --no-build --no-restore -m:1`

Expected: all tests pass.

### Task 3: Add Live Shell tag route

**Files:**
- Create: `src/LlrpCli/Commands/LiveTagAccessHandler.cs`
- Modify: `src/LlrpCli/Commands/CommandCatalog.cs`
- Modify: `src/LlrpCli/Commands/LiveCommand.cs`
- Modify: `docs/architecture/cli-command-system.md`
- Modify: `docs/roadmap.md`

**Interfaces:**
- Consumes: `LiveSessionContext.Reader`, `TagAccessCliRequest`, and `TagAccessOperations`.
- Produces: `Task HandleAsync(string[] tokens, CancellationToken cancellationToken)`.

- [ ] **Step 1: Add `tag` metadata and route**

Add `LiveCommandRoute.TagAccess` and `CommandSpec("tag", ..., "tag read|write ...", ..., RequiresConnection: true)` with candidates `read`, `write`, `--bank`, `--word`, `--count`, `--data`, `--antenna`, `--password`, and `--timeout`.

- [ ] **Step 2: Implement and route `LiveTagAccessHandler`**

Parse the Live token sequence into the shared request. `read` reuses the current reader and `TagAccessOperations.ReadAsync`; `write` uses only the dry-run renderer. Preserve the Live Shell's exception-to-error rendering and no reader write behavior.

- [ ] **Step 3: Update docs and run verification**

Run:

```powershell
dotnet build LLRPCSharp.slnx --no-restore -m:1
dotnet test tests/LlrpCli.Tests/LlrpCli.Tests.csproj --no-build --no-restore -m:1
@('help tag', 'q') | dotnet run --project src/LlrpCli --no-build --no-restore --
git diff --check
```

Expected: zero build errors, all CLI tests pass, Live help displays tag usage, and no whitespace errors.
