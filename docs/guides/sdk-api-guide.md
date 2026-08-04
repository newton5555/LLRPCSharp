# SDK API 使用指南 (`LlrpSdk`)

`LlrpSdk` 是参考 **Impinj Octane SDK** 理念重新实现的托管高层 API。通过 `LlrpReader`，应用程序可直接管理连接、配置天线/功率、启动盘点以及执行标签内存操作，无需手动组装底层的 `ROSpec` 或 `AccessSpec` 消息。

如需使用底层的编解码和原始消息（**LTK.NET** 的现代化替代），请直接查阅 `LlrpNet` 协议层架构说明。

---

## 1. 基础建立与盘点

```csharp
using LlrpSdk.Reader;
using LlrpSdk.Settings;
using LlrpSdk.Model;

// 1. 创建并连接读写器
await using var reader = LlrpReader.CreateBuilder("192.168.1.100").Build();
await reader.ConnectAsync();

// 2. 查询设备推荐默认配置，打印配置信息并下发到读写器
ReaderSettingsDefaults defaults = await reader.GetDefaultSettingsAsync();
Console.WriteLine($"已加载设备默认配置 Profile: {defaults.ProfileId}");
Console.WriteLine($"默认盘点天线列表: {string.Join(", ", defaults.Settings.Inventory.AntennaIds)} (0代表全部天线)");

await reader.ApplySettingsAsync(defaults.Settings);

// 3. 启动盘点并消费标签数据
await using var session = await reader.StartInventoryAsync();
await foreach (TagReport tag in session.ReadReportsAsync())
{
    Console.WriteLine($"[天线 {tag.AntennaId}] EPC: {tag.EpcHex} | RSSI: {tag.PeakRssi} dBm");
}
```

---

## 2. 设备能力与参数索引 (`Capabilities`)

在 LLRP 协议中，RF 模式 (`ModeIndex`)、发射功率 (`TransmitPowerIndex`) 等均为数字索引 (Index)。**索引对应的实际速率、调制模式或以 dBm 为单位的功率映射表均存储在 `reader.Capabilities` 中**。

连接建立后，可通过 `reader.Capabilities` 查询设备硬件事实：

```csharp
ReaderCapabilities caps = reader.Capabilities!;

// 查看硬件支持的天线数量
Console.WriteLine($"读写器天线数量: {caps.MaxAntennas}");

// 查看支持的 RF 模式表 (ModeIndex -> 速率与调制模式)
foreach (var mode in caps.RfModes)
{
    Console.WriteLine($"RF Mode Index {mode.ModeIndex}: {mode.Description}");
}

// 查看发射功率表 (TransmitPowerIndex -> 实际 dBm 功率)
foreach (var pwr in caps.TransmitPowerTable)
{
    Console.WriteLine($"Power Index {pwr.Index}: {pwr.TransmitPowerDbm / 100.0} dBm");
}
```

---

## 3. 配置与参数管理 (`ReaderSettings`)

`ReaderSettings` 提供了强类型配置管理，支持配置天线列表、RF 模式索引、Gen2 Session 与标签上报策略。

```csharp
using LlrpSdk.Settings;

ReaderSettings defaultSettings = (await reader.GetDefaultSettingsAsync()).Settings;

ReaderSettings customSettings = defaultSettings.Edit(builder => builder
    .Inventory(inv => inv
        .Antennas(1, 2, 3, 4)       // 启用天线 1~4
        .Mode(modeIndex: 1000)      // 设置指定的 RF 模式索引 (根据 caps.RfModes 查询)
        .Session(2)                 // Gen2 Session = 2
        .Population(128)            // 预计标签数量
        .ReportEveryTag()));        // 实时上报每一条标签

// 校验并应用到读写器
await reader.ApplySettingsAsync(customSettings);
```

---

## 4. 标签实时盘点 (Inventory Stream)

通过 `StartInventoryAsync()` 获取独立的 `InventorySession`，该会话支持通过 `await foreach` 异步迭代器实时消费 `TagReport`：

```csharp
await using InventorySession session = await reader.StartInventoryAsync();

await foreach (TagReport tag in session.ReadReportsAsync())
{
    Console.WriteLine($"EPC Hex: {tag.EpcHex}");
    Console.WriteLine($"天线: {tag.AntennaId}, 频点: {tag.ChannelIndex}, RSSI: {tag.PeakRssi} dBm");
}
```

全局事件监听：

```csharp
reader.TagsReported += (sender, reports) =>
{
    foreach (var tag in reports)
    {
        Console.WriteLine($"收到标签: {tag.EpcHex}");
    }
};
```

---

## 5. 标签内存读写与锁定 (Tag Memory Access)

提供高层 EPC C1G2 标签内存操作 API，自动管理底层 AccessSpec 声明与资源清理。

### 5.1 读取标签内存 (User / TID)

```csharp
// 先构造标签选择条件:按 EPC 全匹配(96 位,12 字节)
TagSelection selection = new()
{
    MemoryBank = TagMemoryBank.ElectronicProductCode,
    BitPointer = 32,
    BitLength = 96,
    Mask = Enumerable.Repeat((byte)0xFF, 12).ToArray(),
    Data = Convert.FromHexString("E28011910000000000000001"),
};

TagAccessResult result = await reader.ReadTagMemoryAsync(new ReadTagRequest
{
    Selection = selection,
    MemoryBank = TagMemoryBank.Tid,
    WordPointer = 0,
    WordCount = 4,
}, timeout: TimeSpan.FromSeconds(10));

Console.WriteLine(
    $"Success: {result.Operation.Success}, " +
    $"Words: [{string.Join(", ", result.Operation.ReadData.Select(word => word.ToString("X4")))}], " +
    $"Error: {result.Operation.Error}");
```

### 5.2 写入标签内存

```csharp
TagAccessResult result = await reader.WriteTagMemoryAsync(new WriteTagRequest
{
    Selection = selection,          // 同上:按 EPC 选择标签
    MemoryBank = TagMemoryBank.User,
    WordPointer = 0,
    WriteData = [0xA1B2, 0xC3D4],   // 16 位字列表
}, timeout: TimeSpan.FromSeconds(10));

Console.WriteLine($"Success: {result.Operation.Success}, WordsWritten: {result.Operation.WordsWritten}");
```

### 5.3 标签锁定与销毁(均为不可逆/危险操作,请谨慎)

```csharp
// 锁定 User 内存为 SecuredWrite 模式(锁后不可再写,需谨慎)
TagAccessResult lockResult = await reader.LockTagMemoryAsync(new LockTagRequest
{
    Selection = selection,
    UserMemoryLockMode = TagLockMode.SecuredWrite,
}, timeout: TimeSpan.FromSeconds(10));

// Kill 标签(永久销毁,不可逆;生产环境默认禁止)
TagAccessResult killResult = await reader.KillTagAsync(new KillTagRequest
{
    Selection = selection,
    KillPassword = 0x12345678,
}, timeout: TimeSpan.FromSeconds(10));
```

---

## 6. 厂商扩展使用 (以 Impinj 为例)

通过 `.UseImpinj()` 挂载扩展，可提取 Impinj 扩展属性（TID 序列号、相位角、Peak RSSI 等）：

```csharp
using LlrpSdk;
using LlrpSdk.Reader;
using LlrpSdk.Extensions.Impinj;

await using LlrpReader reader = LlrpReader.CreateBuilder("192.168.1.100")
    .UseImpinj()
    .Build();

await reader.ConnectAsync();

ReaderSettings settings = ReaderSettings.Create(builder => builder
    .Inventory(inv => inv
        .Antennas(1)
        .Impinj(imp => imp
            .IncludeSerializedTid()
            .IncludeRfPhaseAngle()
            .IncludePeakRssi())));

await reader.ApplySettingsAsync(settings);

await using var session = await reader.StartInventoryAsync();
await foreach (TagReport tag in session.ReadReportsAsync())
{
    Console.WriteLine($"EPC: {tag.EpcHex}, TID: {tag.SerializedTidHex}");
}
```
