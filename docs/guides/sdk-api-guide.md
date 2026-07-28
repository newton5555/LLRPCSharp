# LLRPCSharp SDK API 开发指南 (Developer API Reference)

本文档面向基于 `LLRPCSharp` SDK 进行二次开发应用（如 RFID 业务中间件、MES 系统、仓储盘点服务）的开发者。详细阐述 SDK 的设计理念、`LlrpReader` 的完整生命周期、分类 API 规格以及代码示例。

---

## 一、 设计理念与三层 API 架构 (Three-Tier API Architecture)

`LLRPCSharp` 提供现代 C# 面向对象的二代 RFID 读写器 SDK，支持 LLRP 1.0.1、LLRP 1.1 以及 Impinj 等厂商扩展。

为了同时满足**开箱即用业务集成**、**精细化资源控制**以及**协议级底盘逃生/诊断**的需求，SDK 设计了**无冲突的三层 API 体系（Three-Tier API Architecture）**：

```text
                                       应用层代码 (App Code)
                                                │
         ┌──────────────────────────────────────┼──────────────────────────────────────┐
         ▼                                      ▼                                      ▼
【第一层：高层托管封装 API】            【第二层：高级资源操控服务 API】          【第三层：底层 Raw 报文 API】
 (High-Level Managed API)               (Advanced Resource Services API)         (Low-Level Raw Message API)
 隐藏协议细节，托管 ROSpec/               直接掌控 ROSpec/AccessSpec               直接透传二进制 LLRP 帧与
 AccessSpec 生命周期与标签解析             资源生命周期与物理配置                   自定义 Custom Message 报文
  ├─ reader.StartAsync / StopAsync       ├─ reader.RoSpecs (IRoSpecService)      ├─ reader.Protocol.TransactAsync<T>
  ├─ reader.ReadTagMemoryAsync           ├─ reader.AccessSpecs (IAccessSpecService)├─ reader.Protocol.SendRawAsync
  └─ reader.TagsReported / ReadTagReports └─ reader.QueryConfigurationAsync / ApplyConfigurationAsync    └─ reader.Protocol.TransactRawAsync
```

---

### 三层 API 互不冲突与状态互锁机制（核心验收标准）

三层 API 可以在同一个 `LlrpReader` 实例的生命周期内协同工作，其**互不冲突与安全互锁机制**已作为 SDK 的核心验收标准：

1. **状态互锁防护 (State Synchronization Guard)**：
   - **托管状态 (`IsManagedStateSynchronized = true`)**：当使用第一层托管 API（如 `StartAsync` / `ReadTagMemoryAsync`）时，SDK 维持对读写器 ROSpec/AccessSpec 生命周期的完全追踪；
   - **Raw/直接操控追踪**：一旦开发者调用了第三层 Raw 报文 API（如 `SendRawAsync`）或第二层修改了全局配置 (`ApplyConfigurationAsync`)，SDK 会**自动将 `IsManagedStateSynchronized` 标记为 `false`**；
   - **防踩踏断言**：在 `IsManagedStateSynchronized == false` 时，若再次调用第一层托管 API，SDK 会抛出明确异常，防止托管逻辑与裸报文操作产生混乱或状态覆盖。
2. **恢复机制 (`SynchronizeStateAsync`)**：
   - 开发者完成第三层 Raw 报文调试或自定义操作后，只需显式调用 `await reader.SynchronizeStateAsync()`，SDK 就会重新向设备拉取现存的 ROSpec/AccessSpec 列表，恢复托管状态同步。

---

## 二、 `LlrpReader` 完整生命周期

```text
[构建 Builder] ──► [ConnectAsync 握手] ──► [Ready 就绪] ──► [业务操作 (配置/盘点/读写)] ──► [Disconnect / Dispose 释放]
                        │                       │
                        ▼                       ▼
               (自动协商 1.1/1.0.1             (若调用 Raw/ApplyConfiguration
                + 双阶段 Impinj 扩展激活)         标记失效 需 SynchronizeStateAsync)
```

1. **构建阶段 (Builder)**：配置连接目标 Host/Port、超时参数、重连策略、帧观察器与厂商扩展（如 `.UseImpinj()`）。
2. **握手与初始化阶段 (Connect)**：执行 TCP 建立、LLRP 1.1 协议版本自动协商/回退、**双阶段身份与能力获取**（先识别厂商为 Impinj 发送 `IMPINJ_ENABLE_EXTENSIONS` 激活扩展，再拉取全量 Capability 快照），启动后台消息接收泵。
3. **就绪与业务操作阶段 (Ready)**：支持高层托管盘点 (`StartAsync`)、标签读写 (`ReadTagMemoryAsync`)、配置查询/应用以及高级资源管理。
4. **托管状态同步 (Sync)**：当进行了 Raw 报文透传或配置应用后，SDK 标记 `IsManagedStateSynchronized = false`，需要调用 `SynchronizeStateAsync()` 恢复。
5. **断开与销毁 (Disconnect / Dispose)**：发送 `CLOSE_CONNECTION` 并安全释放套接字及后台 Task。

---

## 三、 分类 SDK API 规格指南

### 0. 两类设置与一个运行中快照

`ReaderConfiguration` 和 `ReaderSettings` 不是同一类对象：

| 对象 | 用途 | 生命周期 |
|---|---|---|
| `ReaderConfiguration` / `ReaderConfigurationPatch` | 设备硬件、事件与厂商配置；对应 `GET/SET_READER_CONFIG` | 设备状态与显式写入 |
| `ReaderSettings`（计划规范名：`InventorySettings`） | 盘点意图；SDK 编译为 ROSpec 和必要的托管资源 | 每次 `StartAsync` 的输入快照 |
| `CurrentSettings`（计划规范名：`CurrentInventorySettings`） | SDK 当前托管盘点的实际输入 | 仅在盘点运行期间有效 |

`GetDefaultConfiguration()` 只是不发送配置查询报文；目前仍需 Reader 已连接并完成初始化，才能依照身份、能力和激活扩展解析 Profile。它不是设备当前配置，也不是完全离线 API。

配置 API 统一使用 `QueryConfigurationAsync` / `ApplyConfigurationAsync`；未发布版本不保留含糊的旧名称。

`ReaderSettings` 表示一次托管盘点的意图。除天线、C1G2 Session、标签数量、RF Mode 和 Tari 外，它还可通过 `StartTrigger` / `StopTrigger` 表达标准 ROSpec 的周期、GPI 或时长触发；这些字段由协商的协议 Adapter 编译，应用层不需要引用版本化协议类型。

`AttachedData.Enabled` 会让 SDK 为该托管 ROSpec 创建标准 C1G2 Read AccessSpec；停止盘点时该资源会被清理。若调用单次 Read/Write/Lock/Kill/Erase，SDK 会暂时禁用该常驻读取并在操作结束后恢复，以避免两个 AccessSpec 竞争。

需要在一个目标标签上执行多个标准操作时，使用 `ExecuteTagAccessSequenceAsync(new TagAccessSequenceRequest { Operations = [...] })`。每项操作必须使用相同的 `TagSelection` 与天线；SDK 将它们编译到同一个 AccessSpec，并返回按 OpSpec ID 排序的完整结果集合。

若使用 `ReaderSettings.StateAwareSingulation` 指定 C1G2 Target A/B，SDK 会先检查 `ReaderCapabilities.CanDoTagInventoryStateAwareSingulation`；读写器未声明支持时启动将明确失败，绝不静默下发会被忽略的状态感知参数。

### 1. 构建与连接管理 API

#### `LlrpReader.CreateBuilder(string host)`
- **说明**：创建 `LlrpReaderBuilder` 构建器实例。
- **参数**：`host` - 读写器 IP 地址或主机名。

#### Builder 配置扩展方法：
- `.WithPort(int port)`：设置 LLRP 端口（默认 `5084`）。
- `.WithConnectTimeout(TimeSpan timeout)`：连接建立超时时间。
- `.WithRequestTimeout(TimeSpan timeout)`：LLRP 报文事务响应超时时间。
- `.WithKeepaliveTimeout(TimeSpan? timeout)`：可选的读写器 KEEPALIVE 静默监测；超时触发 `KeepaliveTimedOut`，不强制断连。
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
| `OperationState` | `ReaderOperationState` | 当前托管盘点状态：`Idle`, `Starting`, `Inventorying`, `Stopping`, `Faulted` |
| `IsConnected` | `bool` | 当前读写器是否在线且就绪 (`ConnectionState == Ready && Session.IsConnected`) |
| `NegotiatedVersion` | `LlrpProtocolVersion` | 连接建立后实际协商确定的协议版本 (`Version101` 或 `Version11`) |
| `Identity` | `ReaderIdentity?` | 读写器身份信息：`ManufacturerName`, `ModelName`, `FirmwareVersion` |
| `Capabilities` | `ReaderCapabilities?` | 读写器能力快照：支持最大天线数、GPI/GPO 数量、天线灵敏度表等 |
| `IsManagedStateSynchronized` | `bool` | 本地托管状态与设备是否同步（若为 `false` 需调用 `SynchronizeStateAsync`） |
| `ConnectionChanged` | `event EventHandler<ReaderConnectionChangedEventArgs>` | 连接状态转换事件 |
| `ErrorOccurred` | `event EventHandler<ReaderErrorEventArgs>` | 读写器后台泵或连接发生异常的通知事件 |
| `KeepaliveTimedOut` | `event EventHandler<KeepaliveTimeoutEventArgs>` | 仅在 `WithKeepaliveTimeout` 启用后，连续静默达到阈值时触发一次 |

---

### 3. 设备配置 API (Configuration Management)

#### `Task<ReaderConfiguration> QueryConfigurationAsync(CancellationToken cancellationToken = default)`
- **说明**：向读写器发送 `GET_READER_CONFIG`（包含 Impinj 查询扩展），获取设备当前运行参数。
- **返回**：`ReaderConfiguration` 对象。对于 Impinj 读写器，扩展配置存储在 `configuration.Extensions["impinj.readerSettings"]`（类型为 `ImpinjReaderSettings`），包含区域、温度 Celsius、GPI 防抖、Link Monitor 等。

#### `ReaderConfiguration GetDefaultConfiguration()` / `ReaderConfigurationDefaultsResult GetDefaultConfigurationResult()`
- **说明**：获取 SDK 推荐的离线安全配置基线（不向设备发送报文）。

#### `Task ApplyConfigurationAsync(ReaderConfiguration configuration, CancellationToken cancellationToken = default)`
- **说明**：向设备发送 `SET_READER_CONFIG` 应用配置。执行后将使 `IsManagedStateSynchronized` 标为 `false`。

---

### 4. 托管盘点 API (Managed Inventory)

#### `Task StartAsync(ReaderSettings? settings = null, CancellationToken cancellationToken = default)`
- **说明**：以指定的 `ReaderSettings` 启动 SDK 托管的 RFID 盘点。若 `settings` 为空，使用默认设置。
- **报文流**：默认以 Null Start Trigger 创建 ROSpec，再发送 `ADD_ROSPEC` (ID 14150) -> `ENABLE_ROSPEC` -> `START_ROSPEC`。未显式给出的 AISpec C1G2 参数由读写器的天线默认配置提供；若指定 `StartTrigger` 为 Periodic 或 GPI，则保留该标准自动触发语义，不发送 `START_ROSPEC`。

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

#### `Task<TagAccessResult> ReadTagMemoryAsync(ReadTagRequest request, TimeSpan? timeout = null, CancellationToken cancellationToken = default)`
- **说明**：读取指定 EPC 标签的 Memory 区（如 EPC, TID, User Memory, Reserved）。
- **参数**：
  - `request.Selection`：目标标签的标准 BitPointer/Mask/Data 选择条件；EPC 选择通常使用 EPC bank、BitPointer=32。
  - `request.MemoryBank`：存储区类型 (`ElectronicProductCode`, `Tid`, `User`, `Reserved`)。
  - `request.WordPointer`：起始 Word 偏移量。
  - `request.WordCount`：读取 Word 数量。
  - `request.AccessPassword`：访问密码（可选）。
- **机制**：SDK 自动创建临时 AccessSpec (ID 24000+)，等待 OpSpec 结果后自动注销清理。

#### `Task<TagAccessResult> WriteTagMemoryAsync(WriteTagRequest request, TimeSpan? timeout = null, CancellationToken cancellationToken = default)`
- **说明**：向指定 EPC 标签写入数据。

同一套高层入口还提供 `LockTagMemoryAsync`、`KillTagAsync`、`BlockEraseTagMemoryAsync` 及 `ExecuteTagAccessSequenceAsync`。它们返回 `TagAccessResult` 或 `TagAccessSequenceResult`；结果从标准 C1G2 OpSpec Result 投影。

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

## 四、 核心代码使用示例 (以标准 LLRP 1.0.1 读写器为例)

以下代码全面展示基于标准 LLRP 1.0.1 读写器连接时，三层 API 的典型用法与协同模式：

---

### 示例 1：【第一层】高层托管封装 API (托管盘点与 C1G2 标签读取)

> **适用场景**：最常见的业务集成，开发者无需关心 LLRP 的 ROSpec/AccessSpec 的添加、使能、启动与删除细节。

```csharp
using LlrpSdk;

// 1. 构建标准 LLRP 1.0.1 读写器（纯标准模式，无需厂商扩展）
LlrpReader reader = LlrpReader.CreateBuilder("192.168.1.148")
    .WithPort(5084)
    .WithProtocolVersionPolicy(LlrpProtocolVersionPolicy.Force101)
    .Build();

// 2. 订阅标签数据上报
reader.TagsReported += (sender, e) =>
{
    TagReport tag = e.Report;
    Console.WriteLine($"[TAG] EPC: {tag.EPC}, 天线端口: {tag.AntennaId}, 时间: {tag.Timestamp}");
};

// 3. 建立连接
await reader.ConnectAsync();
Console.WriteLine($"已连通标准 1.0.1 读写器: {reader.Identity?.ManufacturerId}, 固件: {reader.Identity?.FirmwareVersion}");

// 4. 启动托管盘点（SDK 自动下发、使能并启动临时 ROSpec 14150）
await reader.StartAsync();
await Task.Delay(TimeSpan.FromSeconds(5));

// 5. 停止托管盘点（SDK 自动停止、禁用并清理临时 ROSpec 14150）
await reader.StopAsync();

// 6. 执行 C1G2 标签 User Memory 读取（SDK 自动下发并清理临时 AccessSpec）
byte[] epc = Convert.FromHexString("E28011710000020D056E9BEE");
TagAccessResult response = await reader.ReadTagMemoryAsync(new ReadTagRequest
{
    Selection = new TagSelection
    {
        MemoryBank = TagMemoryBank.ElectronicProductCode,
        BitPointer = 32,
        BitLength = checked((ushort)(epc.Length * 8)),
        Mask = Enumerable.Repeat((byte)0xFF, epc.Length).ToArray(),
        Data = epc,
    },
    MemoryBank = TagMemoryBank.User,
    WordPointer = 0,
    WordCount = 1,
});
Console.WriteLine(response.Operation.Success
    ? $"读取成功: Data={string.Concat(response.Operation.ReadData.Select(static word => word.ToString(\"X4\")))}"
    : $"读取失败: {response.Operation.Error}");

// 7. 断开与销毁
await reader.DisconnectAsync();
await reader.DisposeAsync();
```

---

### 示例 2：【第二层】高级资源操控服务 API (显式 ROSpec/AccessSpec 控制)

> **适用场景**：面向熟悉 LLRP 规范的高级开发者，需要手动创建、使能、触发或清理特定的 ROSpec/AccessSpec 资源。

```csharp
await reader.ConnectAsync();

// 1. 查询设备中当前安装的所有 ROSpec 资源列表
IReadOnlyList<global::LlrpNet.Protocol.Parameters.ILlrpParameter> installedRoSpecs = 
    await reader.RoSpecs.GetAllAsync();
Console.WriteLine($"设备当前存在 {installedRoSpecs.Count} 个 ROSpec 资源。");

// 2. 显式创建 SDK 默认的 Disabled ROSpec (ID 14150)
var settings = new ReaderSettings();
await reader.RoSpecs.AddDefaultAsync(settings);

// 3. 显式掌控 ROSpec 生命周期的每一个步骤
uint targetRoSpecId = settings.RoSpecId;
await reader.RoSpecs.EnableAsync(targetRoSpecId);  // 使能 ROSpec
await reader.RoSpecs.StartAsync(targetRoSpecId);   // 手动触发 Start
await Task.Delay(TimeSpan.FromSeconds(5));
await reader.RoSpecs.StopAsync(targetRoSpecId);    // 手动 Stop
await reader.RoSpecs.DisableAsync(targetRoSpecId); // 禁用 ROSpec
await reader.RoSpecs.DeleteAsync(targetRoSpecId);  // 删除 ROSpec

// 4. 显式查询设备运行物理配置 (GET_READER_CONFIG)
ReaderConfiguration config = await reader.QueryConfigurationAsync();
Console.WriteLine($"Keepalive 模式: {config.Keepalive.TriggerType}, 天线数量: {config.Antennas.Count}");

await reader.DisconnectAsync();
```

---

### 3. 【第三层】底层 Raw 报文与帧 API (透传、自定义帧与状态恢复)

> **适用场景**：发送厂家私有自定义报文、调试原始帧、或进行协议完整性验证。

```csharp
await reader.ConnectAsync();

// 1. 使用协议层发送强类型自定义/原始报文
ushort nextMessageId = reader.Protocol.NextMessageId();
var rawCapabilitiesMsg = new GET_READER_CAPABILITIES(nextMessageId, RequestedData.General_Device_Capabilities);

GET_READER_CAPABILITIES_RESPONSE response = 
    await reader.Protocol.TransactAsync<GET_READER_CAPABILITIES_RESPONSE>(rawCapabilitiesMsg);
Console.WriteLine($"收到响应: Status={response.LLRPStatus.StatusCode}");

// 2. 使用 Hex 字节直接透传原始二进制 LLRP 帧
byte[] rawFrameBytes = Convert.FromHexString("04010000000B0000000101");
ReadOnlyMemory<byte> rawResponseBytes = await reader.Protocol.TransactRawAsync(rawFrameBytes);
Console.WriteLine($"透传成功，接收二进制响应长度: {rawResponseBytes.Length} 字节");

// 3. 检查托管状态同步标记（Raw 操作后会自动标为 false）
if (!reader.IsManagedStateSynchronized)
{
    Console.WriteLine("警告: 底层 Raw 操作已执行，托管状态已标记为未同步。");
    
    // 4. 显式重新同步设备状态，恢复第一层托管 API 操作能力
    await reader.SynchronizeStateAsync();
    Console.WriteLine("托管状态已重新同步！");
}

await reader.DisconnectAsync();
```

---

### 4. 【三层混合安全协同示例】验证三层互不冲突的完整闭环

```csharp
await reader.ConnectAsync();

// 阶段 A：使用第一层托管 API 启动盘点
await reader.StartAsync();
await Task.Delay(2000);
await reader.StopAsync();

// 阶段 B：使用第二层高级服务查询设备 ROSpec
var roSpecs = await reader.RoSpecs.GetAllAsync();

// 阶段 C：使用第三层 Raw API 透传底层查询
var customMsg = new GET_READER_CONFIG(reader.Protocol.NextMessageId(), ...);
await reader.Protocol.TransactAsync<GET_READER_CONFIG_RESPONSE>(customMsg);

// 阶段 D：安全重新同步后，无缝切回第一层托管 API 读标签
await reader.SynchronizeStateAsync();
// 使用上方示例的完整 ReadTagRequest；TagAccessResult 可提供 OpSpec 成功状态和读取字。

await reader.DisconnectAsync();
```

---

## 五、 SDK 后续演进与增补建议

1. **Impinj 配置写入生成器 (BuildApplyParameters)**：目前 `ImpinjReaderExtension.BuildApplyParameters` 返回空列表。后续在确定特定型号 Profile 和恢复策略后，可开放 `ImpinjReaderSettings` 到 `SET_READER_CONFIG` 的写入映射。
2. **型能表 (Capability Catalog) 扩充**：目前针对 R420 (Model `2001002`, FW `6.4.1.x`) 已完成完备验证，后续可根据实测抓包扩充 R700 / Speedway xPortal 等型号的专属门控配置。
3. **Tag Write 写入接口安全审计**：针对 `WriteTagMemoryAsync` 的破坏性写入，补充显式确认与 Dry-run 请求预览模型。
