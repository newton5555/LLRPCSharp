# LlrpSdk.Extensions.Impinj

Impinj RFID 读写器（Speedway R420, Revolution R700 等）针对 `LlrpSdk` 的高层扩展插件包。提供 `.UseImpinj()` 主动扩展激活、报告扩展字段投影，以及手写高层 `ImpinjReaderConfiguration` 到协议参数的 Contributor 映射。底层强类型报文、参数和 Codec 位于独立的 `LlrpNet.Protocol.Impinj` 协议包。

`ImpinjReaderConfiguration` 是扩展包的高层意图模型，不是由 XML 自动生成；XML 只生成实际的 LLRP custom parameter 类型。将它放在 `ReaderConfiguration.Extensions[ImpinjReaderConfiguration.ExtensionKey]` 后，`ApplySettingsAsync()` 会经 Contributor 编译成相应的 Impinj `SET_READER_CONFIG` 参数。

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

- `LlrpNet.Protocol.Impinj` 包含基于 Impinj Octane LTK Definition Files 自动生成的报文、参数和编解码资产；本包只包含高层 SDK 映射。
- **Impinj Protocol Definition Notice**: Portions of this package incorporate protocol parameter and message definitions derived from Impinj LTK Definition Files. Copyright © Impinj, Inc. All rights reserved.
- 本 SDK 项目在 MIT 许可下开源发布与分发，用户可自由用于商业与非商业项目。
