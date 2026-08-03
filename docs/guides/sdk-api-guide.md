# Managed SDK API Guide

`LlrpSdk` is the application layer of LLRPCSharp. Use `LlrpReader` when an
application needs to connect to one RFID reader, configure managed inventory,
and consume translated tag reports. The generated protocol types in `LlrpNet`
are not required for this workflow.

## Basic Lifecycle

```csharp
await using LlrpReader reader = LlrpReader.CreateBuilder("192.0.2.10")
    .Build();

await reader.ConnectAsync();

ReaderSettingsDefaults defaults = await reader.GetDefaultSettingsAsync();
SettingsValidationResult validation =
    await reader.ValidateSettingsAsync(defaults.Settings);
validation.ThrowIfInvalid();

await reader.ApplySettingsAsync(defaults.Settings);
await using InventorySession session = await reader.StartInventoryAsync();

await foreach (TagReport report in session.ReadReportsAsync())
{
    Console.WriteLine(report.Epc);
}
```

The normal lifecycle is:

1. Build a reader with `LlrpReader.CreateBuilder(...)`.
2. Connect with `ConnectAsync()`; protocol negotiation is automatic by default.
3. Obtain or build `ReaderSettings`.
4. Validate and apply settings.
5. Start a managed `InventorySession` and consume `TagReport` values.
6. Stop the session and dispose the reader.

For older devices, select a protocol explicitly with
`WithProtocolVersionPolicy(LlrpProtocolVersionPolicy.Force101)` or
`Force11`.

## Managed Settings

`ReaderSettings` is the single model for managed reader configuration. It can
be created from three sources:

```csharp
ReaderSettingsDefaults readerDefaults = await reader.GetDefaultSettingsAsync();
ReaderSettings generic = ReaderSettingsDefaults.CreateGeneric().Settings;
ReaderSettings current = (await reader.QuerySettingsAsync()).Settings;
```

- Reader defaults use the connected reader identity, firmware, capabilities,
  and activated extensions.
- Generic defaults are portable and do not require a connected reader.
- Queried settings represent the reader's current managed state.

Edit records directly or use the lightweight builders. Both produce the same
`ReaderSettings` and `InventorySettings` records:

```csharp
ReaderSettings settings = ReaderSettings.Create(reader => reader
    .Inventory(inventory => inventory
        .Antennas(1, 2)
        .Session(2)
        .Population(64)
        .ReportEveryTag()));

await reader.ApplySettingsAsync(settings);
```

`ValidateSettingsAsync()` is side-effect free. Applying settings deploys the
managed resources in a stopped state; `StartAsync()` or
`StartInventoryAsync()` starts RF inventory. `StopAsync()` stops inventory while
keeping the managed settings available for a later start. Use
`ClearManagedSettingsAsync()` when the application wants to release those
managed resources.

## Inventory And Reports

Use `StartInventoryAsync()` when the caller wants an isolated report stream:

```csharp
await using InventorySession session = await reader.StartInventoryAsync(
    new InventorySettings
    {
        AntennaIds = [1, 2],
        Session = 2,
        TagPopulationEstimate = 64,
    });

await foreach (TagReport report in session.ReadReportsAsync())
{
    ReadOnlySpan<byte> epc = report.ElectronicProductCode.Span;
    Console.WriteLine(Convert.ToHexString(epc));
}
```

Use `TagsReported` or `ReadTagReportsAsync()` when the application needs to
observe reports from the whole connection rather than one managed session.

Inventory settings can express antennas, RF indexes, singulation, filters,
report triggers, start/stop triggers, attached data, and vendor extension
options. Unsupported combinations are returned as structured validation
diagnostics before resources are written.

## Tag Access

The managed SDK exposes standard C1G2 operations without requiring the caller
to construct an AccessSpec:

- `ReadTagMemoryAsync`
- `WriteTagMemoryAsync`
- `LockTagMemoryAsync`
- `KillTagAsync`
- `BlockEraseTagMemoryAsync`

When needed, the SDK manages the temporary resource lifecycle and returns the
operation result through the translated report model.

## Vendor Extensions

Register an extension while building the reader:

```csharp
await using LlrpReader reader = LlrpReader.CreateBuilder("192.0.2.10")
    .UseImpinj()
    .Build();
```

Common Impinj inventory options are available through the typed builder:

```csharp
InventorySettings inventory = InventorySettings.Create(settings => settings
    .Antennas(1, 2)
    .Impinj(impinj => impinj
        .IncludeSerializedTid()
        .IncludeRfPhaseAngle()
        .IncludePeakRssi()));
```

Use extension-specific documentation only when the reader model and firmware
are known to support the requested fields. Unverified vendor features are
rejected by default.

## When To Go Lower

Use `LlrpNet` or the expert SDK APIs only when the managed Reader workflow is
not enough, such as implementing a new protocol adapter, inspecting raw
frames, or owning ROSpec/AccessSpec resources yourself. Those APIs are outside
the normal application path and are described in the architecture documents.
