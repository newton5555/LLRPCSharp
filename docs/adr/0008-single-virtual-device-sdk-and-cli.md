# ADR 0008: Single Virtual Device SDK Facade and Sibling CLI

- Status: Accepted, implemented, and legacy compatibility retired
- Date: 2026-08-17

## Context

The device-side Server and Virtual implementation are reusable, but composing
them directly requires every application to know the two lower-level objects.
The repository also had a multi-instance Manager and a general client CLI,
which made the intended entry point for one virtual device unclear.

The primary use cases are a single virtual-device process for SDK/integration
testing and a future UI that references the same SDK directly. Process restart
recovery and a cross-process multi-device control service are not current goals.

## Decision

- Add `LlrpDevice.Virtual.Hosting` with the public
  `IVirtualLlrpDeviceHost` contract and `VirtualLlrpDeviceHost` implementation.
- The host composes exactly one `VirtualLlrpDevice` and one
  `LlrpDeviceServer`, and exposes Start/Stop/Restart, lifecycle state, endpoint,
  client count, decoded message observations, and the version-neutral device
  contract.
- Add `src/LlrpVirtualDevice.Cli` as a sibling of `src/LlrpCli`.
  `LlrpVirtualDevice.Cli` is the first consumer of the host facade and runs one
  device in the foreground. With no arguments it enters an interactive shell
  without creating a device; the shell owns the single-device create/start/
  stop/restart/status/destroy lifecycle. Its `live` mode automatically starts
  that one device and enters the same shell with lifecycle, client, and RX/TX
  observations enabled; it does not manufacture client traffic or manage an
  instance directory.
- Keep `src/LlrpCli` as the general client-side CLI. It operates LLRP readers
  and does not create or manage virtual-device servers. `LlrpVirtualDevice.Cli`
  is the only device-side CLI; the old `LlrpVirtualReader.*` compatibility
  projects and the old `virtual-reader` command are retired.
- Use a separate versioned single-device JSON document for the new CLI. It is
  loaded explicitly and contains repeatable device/RF/tag behavior only; it
  does not persist or restore active ROSpec/AccessSpec runtime state.

## Consequences

Applications and a future UI can use the same stable host contract without
depending on command-line parsing or a multi-instance Manager. Multiple hosts
can still be created by an upper-level application when needed, but
multi-process orchestration, IPC, daemon status, and automatic recovery remain
separate future concerns.
