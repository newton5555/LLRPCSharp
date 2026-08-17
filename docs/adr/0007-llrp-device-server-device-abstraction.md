# ADR 0007: Generic LLRP Device Server and `ILlrpDevice` Boundary

- Status: Accepted and implemented; legacy compatibility layer retired by ADR 0008
- Date: 2026-08-17

## Context

The original message-level Virtual Reader combined the TCP listener, LLRP
version dispatch, ROSpec/AccessSpec state, report scheduling, and virtual tag
behavior in one Core host. That shape was useful for the first test endpoint,
but it made a future physical reader implementation depend on a virtual-reader
host instead of sharing the device-side service boundary.

The required direction is to make a virtual device one implementation of the
same device behavior contract that a future real RFID module can implement.
The existing `LlrpSdk` client product and `LlrpNet` transport/protocol product
must remain unchanged while this boundary is introduced.

## Decision

The device-side runtime is split into three projects:

```text
LlrpDevice.Abstractions   version-neutral device behavior contract
        |
        +--> LlrpDevice.Server   TCP/LLRP service and protocol resources
        |
        +--> LlrpDevice.Virtual  deterministic in-memory implementation
```

The single-device application entry is supplied by a fourth composition
project:

```text
LlrpDevice.Virtual.Hosting  one VirtualLlrpDevice + one LlrpDeviceServer facade
```

- `ILlrpDevice` exposes identity, capabilities, configuration, inventory
  execution, standard C1G2 Tag Access, and structured device events. It does
  not expose generated LLRP types, ROSpec/AccessSpec CRUD, TCP sessions, or
  protocol-version namespaces.
- `LlrpDevice.Server` owns the listener, accepted connections, framing/session
  integration, explicit 1.0.1/1.1/2.0 dispatch, resource state, KeepAlive,
  report composition, standard status mapping, fault hooks, and device-side
  extension registration.
- `LlrpDevice.Virtual` owns fake tag state, deterministic `static`,
  `moving-tags`, and `noisy` observation behavior, memory mutations, lock/kill
  state, and per-instance isolation. It references only
  `LlrpDevice.Abstractions`.
- `LlrpDevice.Virtual.Hosting` exposes `IVirtualLlrpDeviceHost` and
  `VirtualLlrpDeviceHost` as the public single-device composition root. It
  owns one server, one virtual device, and Start/Stop/Restart lifecycle.
- `LlrpVirtualReader.Manager` remains an upper-level compatibility composition
  root for the previous multi-instance API. Local JSON is an explicit
  configuration/preset source; it is not a cross-process registry and does not
  restore active resources after a restart.
- `VirtualReaderHost` remains only as a compatibility façade. It maps legacy
  options/events and delegates to `LlrpDeviceServer`; it does not retain a
  second listener, dispatcher, resource state machine, or report loop.

No product code under `src/LlrpSdk/**`, `src/LlrpNet/**`, or `definitions/**`
is changed by this decision. Generated protocol files remain governed by the
definition/generator workflow.

## Consequences

The same Server can be started with a scripted device in tests, with
`VirtualLlrpDevice` for deterministic CI, or with a future hardware-backed
`ILlrpDevice` implementation. ROSpec/AccessSpec behavior and protocol-version
mapping do not need to be duplicated for each device implementation.

The current implementation does not provide a real RFID driver, analog RF
waveform simulation, or automatic process-restart recovery. Those require
separate hardware and persistence decisions and remain future work.
