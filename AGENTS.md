# Agent Guide

This file is the first stop for coding agents working in this repository.
It is intentionally short and operational. For user-facing documentation, start at
`README.md`; for project truth, start at `docs/status.md`.

## Project Facts

- Product: modern .NET LLRP SDK plus CLI tooling.
- Main solution: `LLRPCSharp.slnx`.
- Current implementation status: `docs/status.md`.
- Development order and open work: `docs/roadmap.md`.
- Long-range architecture: `docs/architecture/overview.md`.
- Protocol definition workflow: `definitions/README.md`.

## Build And Test

Use PowerShell from the repository root. See [`tests/README.md`](tests/README.md) for full test architecture and physical hardware acceptance test guides.

```powershell
# Automated Solution Build & Unit/Virtual Tests
dotnet build LLRPCSharp.slnx --no-restore
dotnet test LLRPCSharp.slnx --no-build

# Local Physical Hardware Acceptance Test (Requires real reader device on network)
dotnet test tests/LlrpSdk.Hardware.Tests/LlrpSdk.Hardware.Tests.csproj
```

Build/test status is tracked in `docs/status.md` — do not hard-code build-status dates here.

## Do Not Hand Edit Generated Code

Generated protocol assets are committed, but their source of truth is the
protocol definition plus generator.

Do not manually edit:

- `src/LlrpNet/LlrpNet.Protocol/**/*.g.cs`
- `src/LlrpNet/LlrpNet.Protocol.Impinj/**/*.g.cs`
- `src/LlrpNet/LlrpNet.Protocol.Zebra/**/*.g.cs`

Test projects must not write to or normalize the committed `.g.cs` files above.
Tests that need generated output should use temporary directories, in-memory
sources, or checked-in fixtures outside the committed generated protocol tree.

Change `definitions/`, importer/generator code, or the generation command
instead, then regenerate and verify.

## Multi-Version Protocol Code Convention

The protocol tree ships separate generated type sets per LLRP version
(`LlrpNet.Protocol.Messages|Parameters|Enumerations.V1_0_1` and `.V1_1`).
Version must always be explicit in SDK/CLI/tool code — never rely on a
"default" version.

- **No bare versioned namespace usings** in code that could touch more than one
  version: `using LlrpNet.Protocol.Parameters.V1_0_1;` is not allowed there.
- Use version-prefixed namespace aliases and qualify every reference:
  ```csharp
  using V101Messages = LlrpNet.Protocol.Messages.V1_0_1;      // LLRP 1.0.1
  using V101Parameters = LlrpNet.Protocol.Parameters.V1_0_1;
  using V101Enumerations = LlrpNet.Protocol.Enumerations.V1_0_1;
  using V11Messages = LlrpNet.Protocol.Messages.V1_1;          // LLRP 1.1
  using V11Parameters = LlrpNet.Protocol.Parameters.V1_1;
  using V11Enumerations = LlrpNet.Protocol.Enumerations.V1_1;

  V101Parameters.ROSpec  // 1.0.1 type
  V11Parameters.ROSpec   // 1.1 type — same concept, different version
  ```
- **No legacy camel-case type aliases** (`using RoSpec = ...V1_0_1.ROSpec;`)
  — they hide the version and collide visually with other versions.
- Single-version files may use bare references only when the file itself is
  unambiguously version-bound by name (e.g. `Llrp101InventoryCompiler.cs`).
  Any file that touches two versions (like `src/LlrpSdk/Reader/LlrpReader.cs`)
  must qualify every protocol type reference with its `V101*`/`V11*` prefix.
- When adding a new protocol version (e.g. 2.0), add `V20*` aliases and follow
  the same rule; never introduce a default-version path.

## Useful Entry Points

- SDK facade: `src/LlrpSdk/Reader/LlrpReader.cs`
- Reader builder/options: `src/LlrpSdk/Reader/LlrpReaderBuilder.cs`,
  `src/LlrpSdk/Reader/LlrpReaderOptions.cs`
- Protocol adapters: `src/LlrpSdk/Protocol/Llrp101ProtocolAdapter.cs`,
  `src/LlrpSdk/Protocol/Llrp11ProtocolAdapter.cs`
- Transport/session core: `src/LlrpNet/LlrpNet.Core/Session/`,
  `src/LlrpNet/LlrpNet.Core/Transport/`
- CLI commands: `src/LlrpCli/Commands/`
- Virtual device: `src/LlrpDevice.Server/`, `src/LlrpDevice.Virtual/`, and
  `src/LlrpDevice.Virtual.Hosting/`; device-side CLI:
  `src/LlrpVirtualDevice.Cli/`
- Live Hardware Smoke Tool: `tools/LlrpSdk.LiveSmoke/`

## Current Boundaries

- LLRP 1.0.1 and 1.1 have usable adapter baselines.
- LLRP 2.0 has a generated protocol layer (`V2_0`) and an SDK adapter baseline (`Llrp20ProtocolAdapter` with `Auto`/`Force20` negotiation); real-device interoperability is unverified. Zebra has a wire package (`LlrpNet.Protocol.Zebra`) and a minimal SDK extension (`LlrpSdk.Extensions.Zebra` with `UseZebra()`); real-device acceptance is pending.
- `InventorySettings` currently represents inventory intent, not a full reader
  configuration snapshot.
- `QueryConfigurationAsync`, `ApplyConfigurationAsync`, dynamic YAML runtime
  loading, Settings Contributor, and TagReport Contributor are planned design
  areas, not currently callable public SDK APIs. (Standard Tag Access — read,
  write, lock, kill, block erase — is implemented; see `docs/status.md`.)
- Automatic reconnect is limited; it does not yet restore desired ROSpec,
  AccessSpec, or managed inventory state.

## Documentation Rules

- Keep `docs/status.md` factual and current. It is the single source for what is
  implemented, missing, or blocked now.
- Keep `docs/roadmap.md` about future work and priority.
- Keep `README.md` user-facing and brief.
- Mark planned APIs clearly as planned when they appear in design documents.

## AI Documentation Navigation

For a new task, read in this order before acting: `AGENTS.md` → `docs/status.md`
→ the relevant guide under `docs/guides/` → `tests/README.md`. When changing
behavior, first check `docs/status.md` and the affected docs for an existing
convention; update them in the same change.

## Bilingual Documentation Convention

Files that exist in both languages (`README.md`/`README.zh.md`,
`docs/architecture/overview*.md`, `docs/showcase*.md`) keep **English as the
authoritative version** and the Chinese copy as a translation. Never let the two
versions diverge — edit both together, and treat the English version as truth on
conflicts.

## Hardware Acceptance Evidence

Real-device acceptance is executed by an engineer or agent and the outcome is
recorded by the executor into the evidence table in
`docs/acceptance/reader-interoperability.md`. `tools/LlrpSdk.LiveSmoke` is for
agent smoke checks only and is not acceptance evidence by itself; `dotnet test
tests/LlrpSdk.Hardware.Tests` silently skips when the device is unreachable, so
a recorded run must confirm the tests actually executed.

## Git Commit Rules

- **Do Not Automatically Commit to Git**: Coding agents must not make automatic Git commits unless explicitly requested by the user. If you believe a commit is necessary, always ask the user for permission first.

## Standard Release Workflow

When executing a version release (e.g. `0.6.0`), follow this strict step-by-step workflow:

1. **Create Release Branch**: Create and switch to a local release branch: `git checkout -b release/<version>`.
2. **Add Release Document & Update Version**:
   - Create the release notes file at `docs/releases/v<version>.md` (e.g. `docs/releases/v0.6.0.md`).
   - Update the project version number (e.g., `<Version>` in `Directory.Build.props`).
3. **Local Hardware Verification (Mandatory)**:
   - CI/CD only runs unit/virtual tests. Before publishing, run local hardware acceptance tests on real hardware:
     `dotnet run --project tools/LlrpSdk.LiveSmoke -- <real-reader-ip> --inventory`
4. **Local Commit**: Commit the release changes locally (e.g., `git commit -m "release: prepare <version>"`).
5. **Create Version Tag**: Create annotated Git tag directly on the release branch: `git tag -a v<version> -m "release v<version>"`.
6. **Manual Confirmation 1 (Push Release Branch & Tags)**: Ask the user for explicit confirmation before pushing the release branch and tags to `origin`: `git push origin release/<version> --tags`.
7. **Verify CI/CD Status (Actions OK)**: Wait for GitHub Actions build, test & publish workflow to complete successfully.
8. **Manual Confirmation 2 (Merge to Master)**: Ask the user for explicit confirmation before merging `release/<version>` back into `master`.
9. **Manual Confirmation 3 (Cleanup Release Branch)**: Ask the user for explicit confirmation before deleting local and remote `release/<version>` branches.
