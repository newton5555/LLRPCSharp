# LlrpSdk

面向 .NET 的现代化 RFID LLRP 读写器 SDK 核心包。提供完整支持 LLRP 1.0.1 协议、握手与版本自动协商、托管盘点 API、C1G2 内存读写、配置查询/应用及高级 ROSpec/AccessSpec 资源管理服务。

## 快速开始

```csharp
using LlrpSdk;

// 1. 创建并建立读写器连接
await using LlrpReader reader = LlrpReader.CreateBuilder("192.168.1.100")
    .WithPort(5084)
    .Build();

await reader.ConnectAsync();

// 2. 启动托管盘点
await reader.StartAsync(new ReaderSettings { AntennaIds = [0] });

// 3. 消费标签流
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
await foreach (TagReport report in reader.ReadTagReportsAsync(cts.Token))
{
    Console.WriteLine($"[TAG] EPC={report.EpcHex}, Antenna={report.AntennaId}, RSSI={report.PeakRssi}dBm");
}

// 4. 停止并断开
await reader.StopAsync();
await reader.DisconnectAsync();
```

---

## 协议标准与开源许可 (License & Standard Notice)

- 本 SDK 在 MIT 许可下开源发布与分发。
- LLRP (Low Level Reader Protocol) 1.0.1 / 1.1 为 GS1 / EPCglobal 发布的开放国际标准规范。
