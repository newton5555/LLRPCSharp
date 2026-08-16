# Virtual Reader Manager

`LlrpVirtualReader.Manager` is the SDK repository's message-level TCP virtual
reader host. It is independent from `LlrpSdk` and from `LlrpReaderPlatform`;
clients connect to it as an ordinary LLRP 1.0.1 Reader.

## Start a host

```powershell
dotnet run --project src/LlrpVirtualReader.Manager/LlrpVirtualReader.Manager.csproj -- --port 5085
```

The endpoint can be selected explicitly:

```powershell
dotnet run --project src/LlrpVirtualReader.Manager/LlrpVirtualReader.Manager.csproj -- `
  --listen 127.0.0.1 --port 5085 --strict
```

`--listen` accepts an IP address. The requested address and port are bound
exactly; a bind failure is reported and the Manager does not choose a fallback
port. User-facing startup requires port `1` through `65535`; port `0` remains
available only to in-process automated tests through `VirtualReaderHostOptions`.

The current Manager milestone hosts one TCP instance. The Manager is the
planned owner of instance creation and lifecycle; Core remains responsible for
one `VirtualReaderHost` and its device-side LLRP behavior.

The following lifecycle surface is planned, but is not callable yet:

```text
create/new  <target-config>  create an inactive instance
start       <instance-id>
stop        <instance-id>
restart     <instance-id>
delete      <instance-id>
list / status
```

Preset Catalog, target configuration resolution, and registered protocol
Handler management are staged in VR4–VR5 of the [Virtual Reader roadmap](../roadmap.md),
after the Manager instance lifecycle baseline in VR3.

The original `src/LlrpVirtualReader` command remains as a compatibility
launcher for loopback-only `--port` usage.
