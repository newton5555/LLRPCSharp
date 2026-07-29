# LlrpSdk.Extensions.Impinj

Impinj RFID 读写器（Speedway R420, Revolution R700 等）针对 `LlrpSdk` 的官方扩展插件包。提供 `.UseImpinj()` 主动扩展激活、强类型 Impinj Codec 资产、以及 Serialized TID、RF Phase Angle、Peak RSSI 等专属扩展属性投影。

## 快速开始

```csharp
using LlrpSdk;
using LlrpSdk.Extensions.Impinj;

// 注册并激活 Impinj 扩展
await using LlrpReader reader = LlrpReader.CreateBuilder("192.168.1.100")
    .WithPort(5084)
    .UseImpinj() // 启用 Impinj 扩展插件
    .Build();

await reader.ConnectAsync();

// 启动盘点，自动上报 Impinj 专属扩展属性
await reader.StartAsync(new InventorySettings { AntennaIds = [0] });

await foreach (TagReport report in reader.ReadTagReportsAsync(cts.Token))
{
    // 获取 Impinj Serialized TID
    if (report.Extensions.TryGetValue("impinj.serializedTid", out var tid))
    {
        Console.WriteLine($"[Impinj] Serialized TID: {tid}");
    }
}
```

---

## 协议定义与版权声明 (License & Copyright Notice)

- 本扩展包中包含基于 Impinj Octane LTK Definition Files 自动生成编解码资产 (`src/LlrpSdk.Extensions.Impinj/**/*.g.cs`)。
- **Impinj Protocol Definition Notice**: Portions of this package incorporate protocol parameter and message definitions derived from Impinj LTK Definition Files. Copyright © Impinj, Inc. All rights reserved.
- 本 SDK 项目在 MIT 许可下开源发布与分发，用户可自由用于商业与非商业项目。
