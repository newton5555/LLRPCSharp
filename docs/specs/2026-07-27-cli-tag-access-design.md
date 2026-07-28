# CLI Tag Access Design

> Status: Approved

**Goal:** Provide safe Live Shell and one-shot CLI paths for standard C1G2 tag access while preserving explicit confirmation for destructive operations.

## Scope

- `tag read` selects exactly one EPC, starts SDK-managed inventory when necessary, reads one C1G2 memory range, prints the operation result, and cleans up the SDK-managed inventory it started.
- The caller supplies EPC, memory bank, word pointer, word count, optional antenna, optional access password, and optional timeout.
- `tag write` accepts the same target information plus words. Without `--yes` it renders the derived request only; with `--yes` it creates a temporary `LlrpReader` and calls `WriteTagMemoryAsync`.
- `tag sequence` exposes multiple standard operations in one temporary AccessSpec. Non-read operations require `--yes` in the one-shot CLI.
- Live Shell reuses its current connection. The one-shot CLI creates and disposes a temporary `LlrpReader` using the existing connection-option policy.

## Explicit Safety Boundaries

- EPC input is hexadecimal and becomes an exact EPC-bank selection: bit pointer 32 and bit length equal to the supplied EPC length.
- Reads require positive word count and a connected reader. They may affect temporary SDK-managed ROSpec/AccessSpec state but do not change tag memory.
- One-shot writes require an explicit `--yes`; the default path remains an offline preview and does not connect.
- No vendor-specific access command or Impinj-specific behavior is added.

## Shape

```text
tag read options
  -> TagAccessCliRequest parser/validator
  -> ReadTagRequest + exact EPC TagSelection
  -> existing LlrpReader.ReadTagMemoryAsync
  -> result renderer

tag write options
  -> TagAccessCliRequest parser/validator
  -> WriteTagRequest
  -> preview renderer, or with --yes -> LlrpReader.WriteTagMemoryAsync
```

`TagAccessCliRequest` is an internal CLI model shared by both hosts. It centralizes EPC hex parsing, bank-name parsing, range validation, access-password parsing, and construction of `ReadTagRequest` / `WriteTagRequest`; host-specific code only obtains the reader, ensures managed inventory for a read, and renders output.

## Commands

Live Shell:

```text
tag read <epc> --bank reserved|epc|tid|user --word <address> --count <words>
         [--antenna <id>] [--password <hex>] [--timeout <seconds>]
tag write <epc> --bank reserved|epc|tid|user --word <address> --data <hex-words>
          [--antenna <id>] [--password <hex>]
```

One-shot CLI uses the same subcommands after `llrp tag`, plus `<HOST>`, `--port`, `--llrp`, and `--vendor` connection options. `tag write` takes `<HOST>` even for preview so the exact command can be confirmed by adding `--yes`.

## Result and Failures

- Successful read renders EPC, target bank/range, operation success, and returned 16-bit words as uppercase four-digit hexadecimal values.
- An AccessSpec/OpSpec failure renders the standard error returned by the SDK and exits non-zero in the one-shot host.
- If Live Shell inventory was already active, it is reused; otherwise the handler starts it before access and stops it in `finally`.
- Invalid hexadecimal, zero word count, an unknown bank, malformed words, or an inactive Live connection are usage errors before any reader operation.

## Verification

- Unit coverage for parser/validator requests, dry-run output, repeated sequence operations, and `--yes` command exposure.
- Virtual Reader integration coverage for a User-memory read and cleanup of the temporary inventory path.
- Existing R420 non-destructive User-memory read remains the physical-device acceptance path.

## Deferred Work

- Direct one-shot lock/kill/erase aliases and vendor-specific access commands. The existing `tag sequence` can already carry standard lock/kill/erase operations.
- LLRP 2.0 adapter work and a corresponding LLRP 2.0 Virtual Reader are final-phase work, after the 1.0.1/1.1 SDK/CLI and acceptance path are complete.
