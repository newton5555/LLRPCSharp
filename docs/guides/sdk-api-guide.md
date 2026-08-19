# SDK API 使用指南 (`LlrpSdk`)

`LlrpSdk` 是参考 **Impinj Octane SDK** 理念重新实现的托管高层 API。通过 `LlrpReader`，应用程序可直接管理连接、配置天线/功率、启动盘点以及执行标签内存操作，无需手动组装底层的 `ROSpec` 或 `AccessSpec` 消息。

如需使用底层的编解码和原始消息（**LTK.NET** 的现代化替代），请直接查阅 `LlrpNet` 协议层架构说明。

当前客户端验收边界：标准 LLRP 1.0.1 Reader 与 Impinj R420 的 SDK 连接、能力/设置读取、盘点和非破坏性 Tag Access 路径已通过；LLRP 1.1 是可用的 SDK 基线，真实型号/固件覆盖仍需单独验收。设备端虚拟读写器不在 `LlrpSdk` 内部实现，见 [Virtual Device SDK and CLI 指南](virtual-device-cli.md)。

---

## 1. 基础建立与盘点

```csharp
using LlrpSdk;

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
using LlrpSdk;

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

`TagsReported` 与 `session.ReadReportsAsync()` 是互斥的报告出口。一次盘点中，首次
开始消费的出口取得所有权；随后尝试使用另一出口会抛出
`InvalidOperationException`。需要连接级异步观察时可使用
`reader.ReadTagReportsAsync()`，它同样不能与 Session 流或 `TagsReported` 混用。

LLRP 允许设备在报告选择器中关闭 `ROSpecID` 字段。SDK 托管独占盘点时会在报告缺少
`ROSpecID` 的情况下，结合当前托管资源和 `AccessSpecID` 继续将标签报告路由到 Session；
若报告带有其他 ROSpec ID，仍会被隔离。

```csharp
reader.TagsReported += (sender, reports) =>
{
    foreach (var tag in reports)
    {
        Console.WriteLine($"收到标签: {tag.EpcHex}");
    }
};
```

### 4.1 盘点配置的恢复模式（应用层）

`InventorySettings` 是应用持有的盘点意图（类似 Impinj 模型中由应用持久化的部分），SDK 不隐式恢复。
恢复策略由应用层实现，配置来源有三种：

**① 从 SDK default 基线恢复**（无本地文件时的推荐起点）：

```csharp
ReaderSettingsDefaults defaults = await reader.GetDefaultSettingsAsync();
await using var session = await reader.StartInventoryAsync(defaults.Settings.Inventory);
```

**② 从本地持久化文件恢复**：

```csharp
// 保存：InventorySettingsSerializer.SaveToFile("inventory.json", session.Settings);
InventorySettings saved = InventorySettingsSerializer.LoadFromFile("inventory.json");
await using var session = await reader.StartInventoryAsync(saved);
```

**③ 从设备当前快照恢复**（设备上恰好存在 SDK 托管盘点时）：

```csharp
ReaderSettingsSnapshot snapshot = await reader.QuerySettingsAsync();
if (snapshot.ManagedRoSpec is { } managed)
{
    await using var session = await reader.StartInventoryAsync(managed.Inventory);
}
```

两段式流程保持不变（先部署、后启动）：

```csharp
await reader.ApplySettingsAsync(settings);          // 部署（含 Inventory 意图），不启动
await using var session = await reader.StartInventoryAsync();  // 启动已部署资源
```

**④ 设备断电重启后的恢复**：

设备上的 ROSpec/AccessSpec 是易失资源（LLRP 不强制设备持久化，Impinj 实测断电重启后不保留）。设备重启后直接调用无参
`StartInventoryAsync()` 会按预期报错（"No stopped SDK-managed inventory configuration is available to start."），
与 Impinj Octane 在设备重启后直接 `Start()` 报错的行为一致。应用需在重启后从本地配置重新部署：

```csharp
// 设备重启后：从本地文件重新部署并启动
InventorySettings saved = InventorySettingsSerializer.LoadFromFile("inventory.json");
await using var session = await reader.StartInventoryAsync(saved);
```

> ⚠️ 注意：带 Inventory 意图的部署会先删除设备上**全部** ROSpec/AccessSpec（SDK 完全接管设备资源配置），不要在共享设备上对正在运行的其他托管盘点执行部署型调用。无参 `StartInventoryAsync()` 仅启动已部署资源，不做任何部署或删除。

**⑤ 非托管操作后的强制接管**：如果应用通过 `reader.Protocol`、`reader.RoSpecs` 或
`reader.AccessSpecs` 使用了非托管资源接口，SDK 会将本地托管状态标记为未知。此时有两种选择：

- 需要保留并检查设备现有资源：调用 `SynchronizeStateAsync()`，再继续无参托管操作；
- 需要 SDK 完全覆盖设备现状：直接调用 `StartInventoryAsync(desiredInventory)`，或调用带
  `Inventory` 的 `ApplySettingsAsync(desiredSettings)`。这两个入口会删除全部标准 ROSpec/AccessSpec
  后重新部署 SDK 托管资源，不需要先同步。

这类强制接管会删除其他应用创建的标准资源；仅修改 Reader 全局配置而不提供 `Inventory` 的
`ApplySettingsAsync` 仍要求先同步，不会隐式删除资源。

### 4.2 入口选择：一段式 vs 两段式

公开盘点入口只有两个重载，按场景选择：

| 场景 | 入口 | 说明 |
|---|---|---|
| **快速/临时盘点** | `StartInventoryAsync(InventorySettings)` | 不重发设备配置（以设备为真相），仅部署盘点 ROSpec 并立即启动；适合临时巡检、快速验证、一次性盘点 |
| **受控部署 + 显式启动** | `ApplySettingsAsync(ReaderSettings)` → `StartInventoryAsync()` | 先全量下发配置与盘点意图（保持停止），应用确认后再显式启动；适合正式业务流程（与 Impinj Octane 的 `ApplySettings` + `Start()` 一致） |
| **恢复（断电重启/新会话）** | 见 4.1 三种来源 + 显式传入 | 设备重启后 ROSpec 丢失，无参 `StartInventoryAsync()` 会报错，需应用从本地/default 重新部署 |

两段式对应 Octane 官方用法：`Connect` → `QueryDefaultSettings` → `ApplySettings(settings)`（部署，保持停止）→ `Start()`（显式启动）→ `Stop()`。

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
    KillPassword = "12345678",
}, timeout: TimeSpan.FromSeconds(10));
```

---

## 6. 厂商扩展使用 (以 Impinj 为例)

通过 `.UseImpinj()` 挂载扩展，可提取 Impinj 扩展属性（TID 序列号、相位角、Peak RSSI 等）：

```csharp
using LlrpSdk;
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
    Console.WriteLine($"EPC: {tag.EpcHex}, TID: {tag.GetSerializedTidHex()}");
}
```
