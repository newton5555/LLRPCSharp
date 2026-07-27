# CLI C2 Command Metadata Implementation Plan

> Status: Completed

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the Live Shell use one structured command catalog for command identity, aliases, Usage, connection availability, completion candidates, help, and dispatch selection.

**Architecture:** Keep Spectre's outer CLI and `LiveCommand` as separate hosts. Expand `CommandCatalog` into the Live Shell's authoritative metadata source; it resolves a raw verb to a canonical command and dispatch route. `LiveCommand` retains its existing handlers and rendering, but selects handlers through the catalog rather than a separately maintained alias switch.

**Tech Stack:** .NET 10, C#, Spectre.Console, xUnit.

## Global Constraints

- Target framework remains `net10.0`; retain centralized package management.
- Do not edit generated `*.g.cs` protocol assets.
- C# source is UTF-8 without BOM and CRLF, as defined by `.editorconfig`.
- Preserve outer Spectre command registrations; this plan changes only the Live Shell metadata boundary.
- Preserve current command behavior, safety confirmation rules, connection constraints, and aliases.
- Do not create a Git commit automatically; repository policy requires explicit user request.

## File Structure

- Modify `src/LlrpCli/Commands/CommandCatalog.cs`: structured Live command metadata, completion data, alias resolution, connection-aware resolution.
- Modify `src/LlrpCli/Commands/LiveCommand.cs`: resolve a user-entered verb through the catalog and dispatch by route while retaining current handlers.
- Modify `tests/LlrpCli.Tests/LlrpCliApplicationTests.cs`: catalog and rendered-help regression coverage.
- Modify `docs/architecture/cli-command-system.md`: factual C2 completion boundary.

### Task 1: Establish structured Live command metadata

**Files:**

- Modify: `src/LlrpCli/Commands/CommandCatalog.cs`
- Test: `tests/LlrpCli.Tests/LlrpCliApplicationTests.cs`

**Interfaces:**

- Produce `LiveCommandRoute` with `Connect`, `Disconnect`, `Status`, `Capabilities`, `Inventory`, `Monitor`, `Frames`, `RoSpec`, `AccessSpec`, `Configuration`, `Raw`, `Synchronize`, `Inspect`, `Decode`, `Validate`, `Encode`, `Clear`, `Help`, and `Exit`.
- Produce `CommandSpec.Route`, `CommandSpec.CompletionCandidates`, and `CommandCatalog.TryResolve(string verb, bool isConnected, out CommandSpec command)`.
- Preserve `CommandCatalog.Assist(...)` and `CommandCatalog.FindCommand(...)` behavior.

- [x] **Step 1: Write failing catalog tests**

```csharp
[Fact]
public void CommandCatalog_ResolvesAliasesToOneCanonicalRoute()
{
    bool resolved = CommandCatalog.TryResolve("cls", isConnected: false, out CommandSpec command);

    Assert.True(resolved);
    Assert.Equal("clear", command.Name);
    Assert.Equal(LiveCommandRoute.Clear, command.Route);
}

[Fact]
public void CommandCatalog_HidesConnectedOnlyCommandsUntilConnected()
{
    Assert.False(CommandCatalog.TryResolve("config", isConnected: false, out _));
    Assert.True(CommandCatalog.TryResolve("config", isConnected: true, out CommandSpec command));
    Assert.Equal(LiveCommandRoute.Configuration, command.Route);
}
```

- [x] **Step 2: Verify the tests fail**

Run `dotnet test tests/LlrpCli.Tests/LlrpCli.Tests.csproj --no-restore -m:1 --filter FullyQualifiedName~CommandCatalog_`.

Expected: compilation fails because `LiveCommandRoute` and `TryResolve` do not yet exist.

- [x] **Step 3: Implement catalog-owned metadata**

Add `LiveCommandRoute` and extend `CommandSpec`:

```csharp
public sealed record CommandSpec(
    string Name,
    LiveCommandRoute Route,
    string Usage,
    string Description,
    bool RequiresConnection = false,
    IReadOnlyList<string>? CompletionCandidates = null,
    params string[] Aliases)
{
    public IReadOnlyList<string> CompletionCandidates { get; init; } = CompletionCandidates ?? [];
}
```

Move each value from `ArgumentCandidatesByCommand` to the matching command spec. Implement `TryResolve` with `FindCommand`, returning `false` if the command requires a connection that is not present. Make `GetCandidates` use the matching spec's `CompletionCandidates`; retain existing prefix filtering and ghost suffix behavior.

- [x] **Step 4: Verify the focused tests pass**

Run `dotnet test tests/LlrpCli.Tests/LlrpCli.Tests.csproj --no-restore -m:1 --filter FullyQualifiedName~CommandCatalog_`.

Expected: all catalog tests pass.

### Task 2: Route Live Shell execution through the catalog

**Files:**

- Modify: `src/LlrpCli/Commands/LiveCommand.cs`
- Test: `tests/LlrpCli.Tests/LlrpCliApplicationTests.cs`

**Interfaces:**

- Consume `CommandCatalog.TryResolve(string, bool, out CommandSpec)` and `LiveCommandRoute`.
- Produce canonical dispatch: aliases invoke the handler selected by `CommandSpec.Route`.
- Retain direct host/IP shorthand as a pre-resolution fallback because it is not a registered command.

- [x] **Step 1: Add a dispatch-contract regression test**

```csharp
[Fact]
public void CommandCatalog_ClearAliasUsesClearRoute()
{
    Assert.True(CommandCatalog.TryResolve("cls", isConnected: false, out CommandSpec command));
    Assert.Equal(LiveCommandRoute.Clear, command.Route);
}
```

- [x] **Step 2: Refactor the interactive dispatch**

Resolve `tokens[0]` before the existing switch. Switch on `command.Route` and call the same handlers with the original `tokens`. Resolve `quit`, `exit`, and `q` to `LiveCommandRoute.Exit` so the loop exit remains metadata-driven. Move the direct host/IP fallback and escaped unknown-command message into `HandleUnknownInputAsync`; preserve current text exactly.

- [x] **Step 3: Verify CLI behavior**

Run `dotnet test tests/LlrpCli.Tests/LlrpCli.Tests.csproj --no-restore -m:1`.

Expected: all current CLI tests pass, including literal `[options]` help rendering.

- [x] **Step 4: Manually smoke the interactive prompt**

Run `dotnet run --project src/LlrpCli --no-restore --`. At the prompt, run `help config`, type `config ` and press Tab, then run `cls`. Expected: escaped literal option placeholders, `get`/`apply` candidates, and no alias-routing error.

### Task 3: Document the C2 boundary

**Files:**

- Modify: `docs/architecture/cli-command-system.md`

**Interfaces:**

- Consume Tasks 1–2's catalog-owned routes and completion values.
- Produce factual status: Live Shell Usage, help, candidates, aliases, connection availability, and dispatch selection share metadata; typed outer Spectre registration and C3/C4 work remain separate.

- [x] **Step 1: Update the C2 status paragraph**

State the completed Live Shell metadata boundary and explicitly retain outer Spectre registration and `LiveSessionContext` work as future C3 scope.

- [x] **Step 2: Build and inspect the focused diff**

Run `dotnet build LLRPCSharp.slnx --no-restore -m:1`, then `git diff --check` and inspect only the four listed files. Expected: successful build, no whitespace errors, no generated-file edits, and no unrelated changes.

## Plan Self-Review

- **Coverage:** Task 1 unifies identity, aliases, Usage, connection availability, and completion. Task 2 uses the same metadata for Live routing. Task 3 documents the exact boundary.
- **Scope:** Outer Spectre registration, extracting `LiveSessionContext`, shared business handlers, and tag commands remain outside this plan.
- **Consistency:** `LiveCommandRoute`, `CommandSpec`, and `CommandCatalog.TryResolve` are introduced in Task 1 and consumed consistently by Tasks 2 and 3.

## Execution Handoff

The plan is saved at `docs/plans/2026-07-27-cli-c2-command-metadata.md`. Repository policy prohibits automatic commits, so review checkpoints replace the skill's default commit step unless the user explicitly requests a commit.
