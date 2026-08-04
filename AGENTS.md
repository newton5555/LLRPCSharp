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

Use PowerShell from the repository root.

```powershell
dotnet build LLRPCSharp.slnx --no-restore
dotnet test LLRPCSharp.slnx --no-build
```

Known status as of 2026-07-25: the solution build passes cleanly with zero errors across all projects. See `docs/status.md` for current details.

## Do Not Hand Edit Generated Code

Generated protocol assets are committed, but their source of truth is the
protocol definition plus generator.

Do not manually edit:

- `src/LlrpNet/LlrpNet.Protocol/**/*.g.cs`
- `src/LlrpNet/LlrpNet.Protocol.Impinj/**/*.g.cs`

Test projects must not write to or normalize the committed `.g.cs` files above.
Tests that need generated output should use temporary directories, in-memory
sources, or checked-in fixtures outside the committed generated protocol tree.

Change `definitions/`, importer/generator code, or the generation command
instead, then regenerate and verify.

## Useful Entry Points

- SDK facade: `src/LlrpSdk/Reader/LlrpReader.cs`
- Reader builder/options: `src/LlrpSdk/Reader/LlrpReaderBuilder.cs`,
  `src/LlrpSdk/Reader/LlrpReaderOptions.cs`
- Protocol adapters: `src/LlrpSdk/Protocol/Llrp101ProtocolAdapter.cs`,
  `src/LlrpSdk/Protocol/Llrp11ProtocolAdapter.cs`
- Transport/session core: `src/LlrpNet/LlrpNet.Core/Session/`,
  `src/LlrpNet/LlrpNet.Core/Transport/`
- CLI commands: `src/LlrpCli/Commands/`
- Virtual reader: `src/LlrpVirtualReader/`

## Current Boundaries

- LLRP 1.0.1 and 1.1 have usable adapter baselines.
- LLRP 2.0 definitions exist, but there is no `Llrp20ProtocolAdapter` yet.
- `InventorySettings` currently represents inventory intent, not a full reader
  configuration snapshot.
- `QueryConfigurationAsync`, `ApplyConfigurationAsync`, tag memory access APIs, dynamic
  YAML runtime loading, Settings Contributor, and TagReport Contributor are
  planned design areas, not currently callable public SDK APIs.
- Automatic reconnect is limited; it does not yet restore desired ROSpec,
  AccessSpec, or managed inventory state.

## Documentation Rules

- Keep `docs/status.md` factual and current. It is the single source for what is
  implemented, missing, or blocked now.
- Keep `docs/roadmap.md` about future work and priority.
- Keep `README.md` user-facing and brief.
- Mark planned APIs clearly as planned when they appear in design documents.

## Git Commit Rules

- **Do Not Automatically Commit to Git**: Coding agents must not make automatic Git commits unless explicitly requested by the user. If you believe a commit is necessary, always ask the user for permission first.
