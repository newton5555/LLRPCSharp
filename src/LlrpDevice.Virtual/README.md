# LlrpDevice.Virtual

`LlrpDevice.Virtual` provides a deterministic, in-process virtual RFID device
implementation for LLRP integration tests and simulations. It exposes the
version-neutral `ILlrpDevice` contract together with deterministic inventory,
RF-observable scenarios, tag memory operations, and device events.

Install the package with:

```powershell
dotnet add package LlrpDevice.Virtual --version 1.5.0
```

The package includes `LlrpDevice.Abstractions`. TCP hosting is provided by the
separate `LlrpDevice.Virtual.Hosting` source project and is not part of this
core package.
