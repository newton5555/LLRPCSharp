# LLRPCSharp SDK API 开发指南 (Developer API Reference)

本文档面向基于 `LLRPCSharp` SDK 进行二代开发应用（如 RFID 业务中间件、MES 系统、仓储盘点服务）的开发者。详细阐述 SDK 的设计理念、`LlrpReader` 的完整生命周期、分类 API 规格以及代码示例。

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

### 4. 高层托管盘点 API (Managed Inventory)

#### `Task StartAsync(ReaderSettings settings, CancellationToken cancellationToken = default)`
- **说明**：根据输入的盘点意图 (`ReaderSettings`) 启动 SDK 托管盘点。
- **参数**：
  - `settings.AntennaIds`：天线 ID 数组（`[0]` 为全天线）。
  - `settings.ReportEveryNTags`：上报颗粒度。
  - `settings.Extensions`：厂商延伸选项（如 Impinj 盘点报告选项）。
- **底层行为**：编译规范 ROSpec 14150 -> 下发 `ADD_ROSPEC` -> `ENABLE_ROSPEC` -> `START_ROSPEC`。

#### `Task StopAsync(CancellationToken cancellationToken = default)`
- **说明**：停止当前托管盘点。下发 `STOP_ROSPEC` -> `DISABLE_ROSPEC` -> `DELETE_ROSPEC` 并清理临时资源。

#### 标签接收机制：
1. **异步流 API**：`IAsyncEnumerable<TagReport> ReadTagReportsAsync(CancellationToken cancellationToken = default)`
   ```csharp
   await foreach (TagReport report in reader.ReadTagReportsAsync(cts.Token))
   {
       Console.WriteLine($"EPC: {report.EpcHex}, RSSI: {report.PeakRssi} dBm");
   }
   ```
2. **事件驱动 API**：`event EventHandler<TagReportEventArgs> TagsReported`
   ```csharp
   reader.TagsReported += (sender, e) =>
   {
       foreach (var report in e.Reports) { /* 处理标签 */ }
   };
   ```

---

### 5. 标签 Memory 读写 API (Tag Memory Access)

#### `Task<TagAccessResult> ReadTagMemoryAsync(ReadTagRequest request, TimeSpan? timeout = null, CancellationToken cancellationToken = default)`
- **说明**：针对特定 EPC 标签执行 C1G2 内存块读取。
- **参数**：
  - `request.TargetEpc`：目标 EPC 字节序列。
  - `request.MemoryBank`：目标内存 Bank（`Reserved`, `EPC`, `TID`, `User`）。
  - `request.WordPointer`：起始 Word 偏移。
  - `request.WordCount`：读取 Word 数量（1 Word = 2 Bytes）。
  - `request.AccessPassword`：访问密码（默认 `0x00000000`）。
- **底层行为**：自动创建并激活临时 AccessSpec (ID 24000+)，在接收到对应 OpSpecResult 后自动清理。

---

### 6. 高级资源管理与原始协议透传 API (Advanced Services)

#### `IRoSpecService RoSpecs`
- 显式管理读写器上的 ROSpec 实体资源：
  - `GetAllAsync()`：发送 `GET_ROSPECS` 查询读写器现存的所有 ROSpec 列表。
  - `EnableAsync(uint rospecId)` / `DisableAsync(uint rospecId)`
  - `StartAsync(uint rospecId)` / `StopAsync(uint rospecId)`
  - `DeleteAsync(uint rospecId)` / `AddDefaultAsync(ReaderSettings settings)`

#### `IAccessSpecService AccessSpecs`
- 显式管理读写器上的 AccessSpec 实体资源：
  - `GetAllAsync()`：发送 `GET_ACCESSSPECS` 查询列表。
  - `EnableAsync(uint accessSpecId)` / `DisableAsync(uint accessSpecId)`
  - `DeleteAsync(uint accessSpecId)`

#### `ILlrpProtocolAdapter Protocol` (底层逃生与诊断)
- 当公开 SDK API 无法满足极端特殊需求时，提供原始二进制 Hex 报文透传：
  - `SendRawAsync(ReadOnlyMemory<byte> hexFrame)`
  - `TransactRawAsync(ReadOnlyMemory<byte> requestFrame, Func<LlrpMessageHeader, ReadOnlyMemory<byte>, bool> responsePredicate)`
- **恢复同步**：调用 `Task SynchronizeStateAsync(CancellationToken cancellationToken = default)` 向设备拉取最新 ROSpec/AccessSpec 列表，重置 `IsManagedStateSynchronized = true`。

---

## 四、 快速上手代码示例

```csharp
using LlrpSdk;
using LlrpSdk.Extensions.Impinj;

// 1. 创建并构建 Reader 实例
await using LlrpReader reader = LlrpReader.CreateBuilder("192.168.1.100")
    .WithPort(5084)
    .WithConnectTimeout(TimeSpan.FromSeconds(5))
    .UseImpinj() // 启用 Impinj 扩展
    .Build();

// 2. 建立连接并完成双阶段协商与激活
await reader.ConnectAsync();
Console.WriteLine($"已连接到 {reader.Identity?.ManufacturerId} - {reader.Identity?.ModelId} ({reader.Identity?.FirmwareVersion})");

// 3. 启动托管盘点（全天线）
await reader.StartAsync(new ReaderSettings { AntennaIds = [0] });

// 4. 异步消费标签数据流
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
try
{
    await foreach (TagReport report in reader.ReadTagReportsAsync(cts.Token))
    {
        string epc = Convert.ToHexString(report.ElectronicProductCode.Span);
        Console.WriteLine($"[TAG] EPC={epc}, Antenna={report.AntennaId}, RSSI={report.PeakRssi}dBm");
    }
}
catch (OperationCanceledException)
{
    // 盘点时间到
}

// 5. 停止盘点并优雅断开
await reader.StopAsync();
await reader.DisconnectAsync();
```
