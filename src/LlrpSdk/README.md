# LlrpSdk

`LlrpSdk` is the managed .NET API for connecting to one RFID reader and
running the normal reader workflow. It hides versioned LLRP messages and
resource lifecycles behind `LlrpReader`, `ReaderSettings`, and
`InventorySession`.

## What It Provides

- LLRP 1.0.1 and 1.1 connection and version negotiation;
- managed Reader Settings defaults, validation, query, apply, and clear;
- managed inventory start, monitoring, stop, and report streams;
- translated `TagReport` values;
- standard C1G2 tag memory read, write, lock, kill, and block erase operations;
- protocol modules and vendor extensions without an `ImpinjReader` inheritance tree.

## Quick Start

```csharp
using LlrpSdk;

await using LlrpReader reader = LlrpReader.CreateBuilder("192.0.2.10")
    .WithPort(5084)
    .Build();

await reader.ConnectAsync();

ReaderSettings settings = (await reader.GetDefaultSettingsAsync()).Settings;
await reader.ApplySettingsAsync(settings);

await using InventorySession session = await reader.StartInventoryAsync();
await foreach (TagReport report in session.ReadReportsAsync())
{
    Console.WriteLine(report.Epc);
}

await reader.StopAsync();
```

`ValidateSettingsAsync()` can be called before `ApplySettingsAsync()` and does
not send protocol messages. `ApplySettingsAsync()` deploys managed settings in
a stopped state; `StartInventoryAsync()` starts the deployed inventory and
returns an isolated report stream.

When a resource write or exact raw frame makes the device observation stale, the
desired inventory settings remain available. High-level APIs can reconcile the
SDK-reserved resources immediately; the default `PreserveForeign` policy leaves
foreign ROSpecs and AccessSpecs untouched. Use the overload accepting
`ResourceTakeoverPolicy.ReplaceAll` only when deleting every standard resource is
intentional. `SynchronizeStateAsync()` refreshes the observed resource snapshot
and is useful for inspection, but it is not required before a managed operation.
`StartExistingRoSpecAsync(id)` can attach a report session to an expert/raw-created
ROSpec without compiling, replacing, or deleting it.

For connection-wide observation, use `TagsReported` or `ReadTagReportsAsync()`;
these observer outlets are mutually exclusive with the session stream during an
active inventory. When a reader omits the optional `ROSpecID`, the SDK-owned exclusive
session still accepts non-conflicting inventory reports. Do not consume both
`InventorySession.ReadReportsAsync()` and
an observer outlet for the same inventory. For direct LLRP messages, expert
ROSpec/AccessSpec ownership, or protocol generation, use the lower-level
`LlrpNet` projects.

## License And Standard

- This SDK is distributed under the MIT License.
- LLRP 1.0.1 and 1.1 are open standards published by GS1/EPCglobal.
