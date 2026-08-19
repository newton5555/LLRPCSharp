# LlrpDevice.Virtual

`LlrpDevice.Virtual` is the deterministic, in-process implementation used by
the public `LlrpDevice.Virtual.Hosting` facade. It exposes the
version-neutral `ILlrpDevice` contract together with deterministic inventory,
RF-observable scenarios, tag memory operations, and device events.

Applications that need a TCP/LLRP virtual device should install the Hosting
package instead:

```powershell
dotnet add package LlrpDevice.Virtual.Hosting --version 2.0.0
```

`LlrpDevice.Virtual.Hosting` is the 2.0.0 public entry point and bundles the
server, virtual-device, Impinj profile, and protocol runtime needed by the host.
