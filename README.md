# LLRPCSharp

[中文](README.zh.md)

LLRPCSharp is a .NET implementation of the Low Level Reader Protocol (LLRP)
for RFID readers. It is organized as three layers so that users can choose the
right entry point without learning the whole repository.

LLRPCSharp modernizes the traditional LTK.NET approach for current .NET
applications. It keeps the useful definition-driven protocol model and wire
compatibility, while separating generated protocol assets, codecs, transport,
reader state, and application workflows into replaceable layers.

## 1. LlrpNet: protocol and networking

`LlrpNet` is the low-level LLRP protocol and networking foundation. It keeps
wire-level concerns independent from the application-facing Reader SDK.

Use this layer when you need to:

- work directly with strongly typed LLRP messages and parameters;
- encode, decode, or inspect raw protocol frames without a connected reader;
- use the registry to combine standard and vendor-specific codecs;
- implement protocol adapters or vendor-specific protocol definitions;
- generate protocol assets from validated LTK XML/YAML definitions.

Its main strengths are definition-driven code generation, explicit codec
registration, and a transport/session layer that can be tested independently
from protocol models. `LlrpNet.ProtocolModel` validates the input definitions;
the generator produces messages, parameters, enums, codecs, and registry
modules; `LlrpNet.Protocol` contains standard protocol assets; and
`LlrpNet.Protocol.Impinj` contains independent vendor wire assets.

`LlrpNet.Core` provides TCP transport and transaction primitives. Most
application code should use `LlrpSdk` instead of depending on these types
directly.

## 2. LlrpSdk: managed Reader SDK

`LlrpSdk` is the application-facing API. The main object is
`LlrpSdk.LlrpReader`, which owns one reader connection and provides the managed
reader workflow:

- connect and negotiate LLRP 1.0.1 or 1.1;
- query and apply `ReaderSettings`;
- start, monitor, stop, and clear managed inventory;
- receive translated `TagReport` values;
- perform standard tag memory access operations;
- use vendor extensions such as `UseImpinj()` when needed.

The intended application flow is:

```csharp
await using LlrpReader reader = LlrpReader.CreateBuilder("192.0.2.10")
    .Build();

await reader.ConnectAsync();
ReaderSettings settings = (await reader.GetDefaultSettingsAsync()).Settings;
await reader.ApplySettingsAsync(settings);

await using InventorySession session = await reader.StartInventoryAsync();
await foreach (TagReport report in session.ReadReportsAsync())
{
    Console.WriteLine(report.Epc);
}
```

For normal applications, start with the managed API. The raw protocol entry
point, expert ROSpec/AccessSpec services, and contributor extension contracts
are available when the managed workflow is not sufficient, but are documented
separately.

## 3. LlrpCli: operating a reader

`LlrpCli` is the command-line front end for human operation, scripts, and
agents. It uses the same `LlrpSdk` managed workflow rather than maintaining a
second reader configuration model.

Start the Live Shell:

```powershell
dotnet run --project src/LlrpCli
```

### Live Shell workflow

```text
connect 192.0.2.10
settings edit --from generic
settings show draft
settings apply --yes
inventory start
inventory status
inventory stop
```

The usual operation is:

1. `connect <host>` connects to one reader and negotiates the protocol.
2. `settings edit` creates or changes a local settings draft.
3. `settings show draft` reviews the draft without writing to the reader.
4. `settings apply --yes` deploys the managed settings and leaves inventory stopped.
5. `inventory start|status|stop` controls and observes managed inventory.

Other common Live Shell operations include:

```text
status
caps
tag read <epc> --bank user --word 0 --count 2
tag write <epc> --bank user --word 0 --data <hex-data> --yes
disconnect
```

The Live Shell is the primary interactive interface. One-shot `inventory`
commands are provided for agents and scripts, and reuse the same settings and
SDK workflow:

```powershell
dotnet run --project src/LlrpCli -- inventory 192.0.2.10 --duration 10 --yes
```

Offline `inspect`, `decode`, `validate`, and `encode` commands are secondary
protocol diagnostics and do not require a reader connection.

## Build and test

```powershell
dotnet build LLRPCSharp.slnx --no-restore
dotnet test LLRPCSharp.slnx --no-build
```

## Repository layout

```text
definitions/   Machine-readable protocol definitions and extension definitions
docs/          Status, roadmap, architecture, ADRs, and user guides
references/    Local standards, packet captures, and legacy references
src/LlrpNet/   Protocol, transport, codec, and generator projects
src/LlrpSdk/   Managed Reader SDK and vendor extensions
src/LlrpCli/   Live Shell and command-line tools
tests/         Unit, integration, and interoperability tests
tools/         Definition import, generation, validation, and test helpers
```

## Documentation

- [Current Status](docs/status.md): implemented capabilities and known gaps.
- [CLI User Guide](docs/guides/cli-user-guide.md): core command syntax.
- [SDK API Guide](docs/guides/sdk-api-guide.md): managed SDK API reference.
- [Roadmap](docs/roadmap.md): planned work and development order.
- [Architecture](docs/architecture/overview.md): long-term boundaries.
- [Protocol Definitions](definitions/README.md): definition and generation workflow.
