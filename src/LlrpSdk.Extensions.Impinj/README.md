# LlrpSdk.Extensions.Impinj

`LlrpSdk.Extensions.Impinj` adds managed Impinj support to `LlrpSdk` for
readers such as Speedway R420 and Revolution R700. It provides active
extension initialization, typed managed settings contributors, and Impinj
fields projected into `TagReport.Extensions`.

The generated wire-level messages, parameters, and codecs are kept in the
independent `LlrpNet.Protocol.Impinj` project. This package contains the
application-facing mapping and extension lifecycle.

## Quick Start

```csharp
using LlrpSdk;
using LlrpSdk.Extensions.Impinj;

await using LlrpReader reader = LlrpReader.CreateBuilder("192.0.2.10")
    .WithPort(5084)
    .UseImpinj()
    .Build();

await reader.ConnectAsync();
ReaderSettings settings = (await reader.GetDefaultSettingsAsync()).Settings;
await reader.ApplySettingsAsync(settings);

await using InventorySession session = await reader.StartInventoryAsync();
await foreach (TagReport report in session.ReadReportsAsync())
{
    if (report.Extensions.TryGetValue("impinj.serializedTid", out object? tid))
    {
        Console.WriteLine($"Serialized TID: {tid}");
    }
}
```

Common report options can be expressed with the typed inventory builder:

```csharp
InventorySettings inventory = InventorySettings.Create(settings => settings
    .Antennas(1, 2)
    .Impinj(impinj => impinj
        .IncludeSerializedTid()
        .IncludeRfPhaseAngle()
        .IncludePeakRssi()));
```

Use only options verified for the target reader model and firmware. Unknown or
unverified vendor features are rejected by default.

## Package Boundaries

- `LlrpSdk.Extensions.Impinj`: managed extension activation, Settings and
  Inventory contributors, and TagReport projections.
- `LlrpNet.Protocol.Impinj`: generated Impinj wire assets and codec registry
  module for low-level protocol users.

## License And Definition Notice

- This SDK extension is distributed under the MIT License.
- Generated Impinj protocol assets are derived from Impinj LTK Definition Files.
  Copyright © Impinj, Inc. All rights reserved. The local definition source is
  not redistributed by this package.
