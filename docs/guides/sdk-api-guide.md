# LLRPCSharp SDK API 开发指南 (Developer API Reference)

本文档面向基于 `LLRPCSharp` SDK 进行二次开发应用（如 RFID 业务中间件、MES 系统、仓储盘点服务）的开发者。详细阐述 SDK 的设计理念、`LlrpReader` 的完整生命周期、分类 API 规格以及代码示例。

---

## 一、 设计理念与架构分层

`LLRPCSharp` 提供现代 C# 面向对象的二代 RFID 读写器 SDK，支持 LLRP 1.0.1、LLRP 1.1 以及 Impinj 厂商扩展。

SDK 遵循 **双层 API 入口架构**：

```text
                             应用代码 (App Code)
                                    │
       ┌────────────────────────────┴────────────────────────────┐
       ▼                                                         ▼
【高层托管封装 (High-Level API)】                     【高级资源服务 (Advanced Services)】
 隐藏协议细节、托管生命周期、投影 TagReport            保留标准 ROSpec/AccessSpec 显式掌控力
  └─ ConnectAsync                                       ├─ reader.RoSpecs (IRoSpecService)
  ├─ QuerySettingsAsync / ApplySettingsAsync            ├─ reader.AccessSpecs (IAccessSpecService)
  ├─ StartAsync / StopAsync / InventoryAsync            └─ reader.Protocol (Raw 透传与诊断)
  ├─ ReadTagMemoryAsync / WriteTagMemoryAsync
  └─ TagsReported / ReadTagReportsAsync
```

---

## 二、 `LlrpReader` 完整生命周期

```text
[构建 Builder] ──► [ConnectAsync 握手] ──► [Ready 就绪] ──► [业务操作 (配置/盘点/读写)] ──► [Disconnect / Dispose 释放]
                        │                       │
                        ▼                       ▼
               (自动协商 1.1/1.0.1             (若调用 Raw/ApplySettings
                + 双阶段 Impinj 扩展激活)         标记失效 需 SynchronizeStateAsync)
```

1. **构建阶段 (Builder)**：配置连接目标 Host/Port、超时参数、重连策略、帧观察器与厂商扩展（如 `.UseImpinj()`）。
2. **握手与初始化阶段 (Connect)**：执行 TCP 建立、LLRP 1.1 协议版本自动协商/回退、**双阶段身份与能力获取**（先识别厂商为 Impinj 发送 `IMPINJ_ENABLE_EXTENSIONS` 激活扩展，再拉取全量 Capability 快照），启动后台消息接收泵。
3. **就绪与业务操作阶段 (Ready)**：支持高层托管盘点 (`StartAsync`)、标签读写 (`ReadTagMemoryAsync`)、配置查询/应用以及高级资源管理。
4. **托管状态同步 (Sync)**：当进行了 Raw 报文透传或配置应用后，SDK 标记 `IsManagedStateSynchronized = false`，需要调用 `SynchronizeStateAsync()` 恢复。
5. **断开与销毁 (Disconnect / Dispose)**：发送 `CLOSE_CONNECTION` 并安全释放套接字及后台 Task。

---

## 三、 分类 SDK API 规格指南

### 1. 构建与连接管理 API

#### `LlrpReader.CreateBuilder(string host)`
- **说明**：创建 `LlrpReaderBuilder` 构建器实例。
- **参数**：`host` - 读写器 IP 地址或主机名。

#### Builder 配置扩展方法：
- `.WithPort(int port)`：设置 LLRP 端口（默认 `5084`）。
- `.WithConnectTimeout(TimeSpan timeout)`：连接建立超时时间。
- `.WithRequestTimeout(TimeSpan timeout)`：LLRP 报文事务响应超时时间。
- `.WithProtocolVersionPolicy(LlrpProtocolVersionPolicy policy)`：协议协商策略 (`Auto`, `Force101`, `Force11`)。
- `.WithAutomaticReconnect(LlrpAutomaticReconnectOptions options)`：开启意外断开后的有限自动重连。
- `.WithFrameObserver(ILlrpFrameObserver observer)`：注入底层 TX/RX 帧观察器。
- `.UseImpinj()`：注册 Impinj LLRP 1.0.1 编解码器、能力表与三大 Contributor。
- `.Build()`：生成配置好的 `LlrpReader` 实例（初始处于 `Disconnected` 状态）。

#### 连接控制方法：
- `Task ConnectAsync(CancellationToken cancellationToken = default)`：异步建立连接并完成双阶段初始化。
- `Task DisconnectAsync(CancellationToken cancellationToken = default)`：优雅发送 `CLOSE_CONNECTION` 并断开。
- `ValueTask DisposeAsync()`：释放读写器资源（实现 `IAsyncDisposable`）。

---

### 2. 状态与属性查询 API

| 属性 / 方法 | 类型 | 说明 |
|---|---|---|
| `ConnectionState` | `ReaderConnectionState` | 当前连接状态：`Disconnected`, `Connecting`, `Ready`, `Disconnecting`, `Faulted`, `Reconnecting`, `Disposed` |
| `OperationState` | `ReaderOperationState` | 当前托管盘点状态：`Idle`, `InventoryRunning` |
| `IsConnected` | `bool` | 当前读写器是否在线且就绪 (`ConnectionState == Ready && Session.IsConnected`) |
| `NegotiatedVersion` | `LlrpProtocolVersion` | 连接建立后实际协商确定的协议版本 (`Version101` 或 `Version11`) |
| `Identity` | `ReaderIdentity?` | 读写器身份信息：`ManufacturerName`, `ModelName`, `FirmwareVersion` |
| `Capabilities` | `ReaderCapabilities?` | 读写器能力快照：支持最大天线数、GPI/GPO 数量、天线灵敏度表等 |
| `IsManagedStateSynchronized` | `bool` | 本地托管状态与设备是否同步（若为 `false` 需调用 `SynchronizeStateAsync`） |
| `ConnectionChanged` | `event EventHandler<ReaderConnectionChangedEventArgs>` | 连接状态转换事件 |
| `ErrorOccurred` | `event EventHandler<ReaderErrorEventArgs>` | 读写器后台泵或连接发生异常的通知事件 |

---

### 3. 设备配置 API (Configuration Management)

#### `Task<ReaderConfiguration> QuerySettingsAsync(CancellationToken cancellationToken = default)`
- **说明**：向读写器发送 `GET_READER_CONFIG`（包含 Impinj 查询扩展），获取设备当前运行参数。
- **返回**：`ReaderConfiguration` 对象。对于 Impinj 读写器，扩展配置存储在 `configuration.Extensions["impinj.readerSettings"]`（类型为 `ImpinjReaderSettings`），包含区域、温度 Celsius、GPI 防抖、Link Monitor 等。

#### `ReaderConfiguration GetDefaultConfiguration()` / `ReaderConfigurationDefaultsResult GetDefaultConfigurationResult()`
- **说明**：获取 SDK 推荐的离线安全配置基线（不向设备发送报文）。

#### `Task ApplySettingsAsync(ReaderConfiguration configuration, CancellationToken cancellationToken = default)`
- **说明**：向设备发送 `SET_READER_CONFIG` 应用配置。执行后将使 `IsManagedStateSynchronized` 标为 `false`。

---

### 4. 托管盘点 API (Managed Inventory)

#### `Task StartAsync(ReaderSettings? settings = null, CancellationToken cancellationToken = default)`
- **说明**：以指定的 `ReaderSettings` 启动 SDK 托管的 RFID 盘点。若 `settings` 为空，使用默认设置。
- **报文流**：发送 `ADD_ROSPEC` (ID 14150) -> `ENABLE_ROSPEC` -> `START_ROSPEC`。

#### `Task StopAsync(CancellationToken cancellationToken = default)`
- **说明**：停止当前托管盘点。
- **报文流**：发送 `STOP_ROSPEC` -> `DISABLE_ROSPEC` -> `DELETE_ROSPEC` 并清理状态。

#### `Task<IReadOnlyList<TagReport>> InventoryAsync(ReaderSettings? settings = null, CancellationToken cancellationToken = default)`
- **说明**：一次性盘点快捷 API（启动盘点 -> 收集数据 -> 停止盘点）。

#### 标签数据订阅：
- **事件模式**：`event EventHandler<TagReportEventArgs> TagsReported`
- **异步流模式**：`IAsyncEnumerable<TagReport> ReadTagReportsAsync(CancellationToken cancellationToken = default)`
- **`TagReport` 数据结构**：
  - `EPC`：字符串形式的 EPC 十六进制（如 `"E28011710000020D056E9BEE"`）。
  - `AntennaId`：触发天线端口号。
  - `Timestamp`：时间戳。
  - `Extensions`：厂商扩展字典：
    - `"impinj.serializedTid"`：Serialized TID 字符串。
    - `"impinj.rfPhaseAngle"`：射频相位角 (ushort)。
    - `"impinj.peakRssi"`：峰值 RSSI 信号强度 (short)。

---

### 5. C1G2 标签 Memory 访问 API (Tag Access)

#### `Task<ReadTagResponse> ReadTagMemoryAsync(ReadTagRequest request, CancellationToken cancellationToken = default)`
- **说明**：读取指定 EPC 标签的 Memory 区（如 EPC, TID, User Memory, Reserved）。
- **参数**：
  - `request.TargetEpc`：目标标签 EPC。
  - `request.MemoryBank`：存储区类型 (`EPC`, `TID`, `User`, `Reserved`)。
  - `request.WordAddress`：起始 Word 偏移量。
  - `request.WordCount`：读取 Word 数量。
  - `request.AccessPassword`：访问密码（可选）。
- **机制**：SDK 自动创建临时 AccessSpec (ID 24000+)，等待 OpSpec 结果后自动注销清理。

#### `Task<WriteTagResponse> WriteTagMemoryAsync(WriteTagRequest request, CancellationToken cancellationToken = default)`
- **说明**：向指定 EPC 标签写入数据。

---

### 6. 高级资源服务与 Raw 逃生口 API

#### `reader.RoSpecs` (`IRoSpecService`)
提供对标准 LLRP ROSpec 的显式增删改查与使能控制：
- `AddAsync(ILlrpParameter roSpec)` / `DeleteAsync(uint roSpecId)`
- `EnableAsync(uint roSpecId)` / `DisableAsync(uint roSpecId)`
- `StartAsync(uint roSpecId)` / `StopAsync(uint roSpecId)`
- `GetAllAsync()`

#### `reader.AccessSpecs` (`IAccessSpecService`)
提供对标准 LLRP AccessSpec 的显式生命周期控制。

#### `reader.Protocol` (`IReaderProtocolAccess`)
- `Task<TResponse> TransactAsync<TResponse>(ILlrpMessage request, TimeSpan? timeout, CancellationToken cancellationToken)`
- **说明**：原始 LLRP 报文透传接口。应用可通过此接口发送任何自定义报文。发送后 `IsManagedStateSynchronized` 会变为 `false`。

#### `Task SynchronizeStateAsync(CancellationToken cancellationToken = default)`
- **说明**：重新查询设备的 ROSpec / AccessSpec，同步 SDK 本地托管状态。

---

## 四、 核心代码使用示例

### 示例 1：基础连接、Impinj 扩展使能与标签盘点

```csharp
using LlrpSdk;
using LlrpSdk.Extensions.Impinj;

// 1. 创建 Builder 并启用 Impinj 扩展
LlrpReader reader = LlrpReader.CreateBuilder("192.168.1.100")
    .WithPort(5084)
    .WithConnectTimeout(TimeSpan.FromSeconds(5))
    .UseImpinj() // 注册 Impinj 编解码器及扩展管道
    .Build();

// 2. 订阅标签接收事件
reader.TagsReported += (sender, e) =>
{
    TagReport tag = e.Report;
    Console.WriteLine($"[TAG] EPC: {tag.EPC}, Antenna: {tag.AntennaId}");
    
    // 获取 Impinj 扩展属性 (前提是 ReaderSettings 中配置了响应选项)
    if (tag.Extensions.TryGetValue("impinj.serializedTid", out object? tid))
        Console.WriteLine($"      TID: {tid}");
    if (tag.Extensions.TryGetValue("impinj.peakRssi", out object? rssi))
        Console.WriteLine($"      RSSI: {rssi} dBm");
};

// 3. 建立连接
await reader.ConnectAsync();
Console.WriteLine($"已连通读写器: {reader.Identity?.ModelName}, 固件: {reader.Identity?.FirmwareVersion}");

// 4. 配置 Impinj 盘点报告选项（请求 Serialized TID 与 Peak RSSI）
var settings = new ReaderSettings();
settings.Extensions[ImpinjInventoryReportOptions.ExtensionKey] = new ImpinjInventoryReportOptions
{
    IncludeSerializedTid = true,
    IncludePeakRssi = true,
    IncludeRfPhaseAngle = true,
};

// 5. 启动托管盘点
await reader.StartAsync(settings);
await Task.Delay(TimeSpan.FromSeconds(10)); // 盘点 10 秒

// 6. 停止盘点并断开连接
await reader.StopAsync();
await reader.DisconnectAsync();
await reader.DisposeAsync();
```

### 示例 2：读取标签 User Memory 存储区

```csharp
await reader.ConnectAsync();

// 建立标签读取请求
var request = new ReadTagRequest(
    targetEpc: "E28011710000020D056E9BEE",
    memoryBank: MemoryBank.User,
    wordAddress: 0,
    wordCount: 2
);

ReadTagResponse response = await reader.ReadTagMemoryAsync(request);
if (response.IsSuccess)
{
    Console.WriteLine($"读取成功！Data Hex: {response.DataHex}");
}
else
{
    Console.WriteLine($"读取失败: {response.Status}");
}

await reader.DisconnectAsync();
```

---

## 五、 SDK 后续演进与增补建议

1. **Impinj 配置写入生成器 (BuildApplyParameters)**：目前 `ImpinjReaderExtension.BuildApplyParameters` 返回空列表。后续在确定特定型号 Profile 和恢复策略后，可开放 `ImpinjReaderSettings` 到 `SET_READER_CONFIG` 的写入映射。
2. **型能表 (Capability Catalog) 扩充**：目前针对 R420 (Model `2001002`, FW `6.4.1.x`) 已完成完备验证，后续可根据实测抓包扩充 R700 / Speedway xPortal 等型号的专属门控配置。
3. **Tag Write 写入接口安全审计**：针对 `WriteTagMemoryAsync` 的破坏性写入，补充显式确认与 Dry-run 请求预览模型。
