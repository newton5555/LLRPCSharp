# LLRP 1.0.1 SDK 全量功能对齐与实施规范 (LLRP 1.0.1 SDK Completion Spec)

> 基准日期：2026-07-28  
> 目的：作为下一步具体开发工作的**指导与实施文档**。在最大限度保留未来 LLRP 1.1 / 2.0 协议及多厂商扩展（Impinj / Zebra 等）架构解耦的前提下，实现新的 `LLRPCSharp` SDK 与旧版 `LLRPClient` SDK 在标准 LLRP 1.0.1 功能上的 **1:1 全量功能覆盖**（已允许移除高层 `ResetToFactoryDefaults` API，报文层保留）。

---

## 一、 全量功能对齐与实施规划对照表

以下表格详细列出了旧版 SDK 的全部功能模块、在当前新 SDK 中的状态、已有接口及底层 LLRP 报文，以及待补全功能的具体实施计划与设计：

| 功能模块 | 对应功能项 | 状态 | 当前 SDK 接口 / 底层 LLRP 报文 (已实现) | 实施计划与设计 (待补全) |
|---|---|---|---|---|
| **1. 会话与连接管理** | 1.1 TCP 非加密连接 | **已实现** | `LlrpReader.CreateBuilder(host).WithPort(5084).Build().ConnectAsync()`<br>底层：`READER_EVENT_NOTIFICATION`, `GET_READER_CAPABILITIES` (General/All) | — |
| | 1.2 TLS 加密连接 (5085) | **待补全** | — | **实施计划**：在 `LlrpReaderBuilder` 中增加 `.WithTls(bool enable = true, int port = 5085)`，在 `LlrpTransport` 中注入 `SslStream` 处理 TLS 握手。 |
| | 1.3 优雅断开连接 | **已实现** | `LlrpReader.DisconnectAsync()`<br>底层：`CLOSE_CONNECTION` ➔ `CLOSE_CONNECTION_RESPONSE` | — |
| | 1.4 强行撕毁断开 | **待补全** | — | **实施计划**：增加 `LlrpReader.ForceDisconnectAsync()`，在心跳超时或网络异常时直接关闭底层 Socket 与泵 Task，不发 `CLOSE_CONNECTION`。 |
| | 1.5 协议版本协商与降级 | **已实现(超越)**| `LlrpProtocolVersionPolicy.Auto` / `Force101` / `Force11`<br>底层：`GET_SUPPORTED_VERSION` (46)，支持旧设备断开时自动降级 1.0.1 | — |
| **2. 硬件能力集解析** | 2.1 设备基础身份快照 | **已实现** | `reader.Identity` (`ManufacturerId`, `ModelId`, `FirmwareVersion`)；<br>`reader.Capabilities` (`MaxNumberOfAntennas`, `NumGpis`, `NumGpos`)<br>底层：`GET_READER_CAPABILITIES` (General) | — |
| | 2.2 发射功率表 (`TxPowers`) | **已实现** | `ReaderCapabilities.TxPowers`（索引、原始 0.01 dBm 值和 `TransmitPowerDbm`） | — |
| | 2.3 接收灵敏度表 (`RxSensitivities`) | **已实现** | `ReaderCapabilities.RxSensitivities`（索引、原始 0.01 dBm 值和 `ReceiveSensitivityDbm`） | — |
| | 2.4 频点与跳频表 | **已实现** | `ReaderCapabilities.TxFrequencies` / `HopTables` | — |
| | 2.5 C1G2 射频模式表 | **已实现** | `ReaderCapabilities.RfModes`（ModeIdentifier、DR、调制、Tari 范围等） | — |
| | 2.6 高级物理门控布尔值 | **已实现** | `IsTagAccessAvailable`、`IsMultiwordBlockWriteAvailable`、`IsMultiwordBlockEraseAvailable`、`CanDoTagInventoryStateAwareSingulation` | — |
| **3. 设备配置与物理控制** | 3.1 查询设备当前物理配置 | **已实现** | `reader.QueryConfigurationAsync()`<br>底层：`GET_READER_CONFIG` (RequestedData=All) ➔ `GET_READER_CONFIG_RESPONSE` | — |
| | 3.2 离线 SDK 安全默认配置 | **已实现** | `reader.GetDefaultConfiguration()` / `GetDefaultConfigurationResult()` | — |
| | 3.3 应用物理配置 | **已实现** | `reader.ApplyConfigurationAsync(configuration)`<br>底层：`SET_READER_CONFIG` ➔ `SET_READER_CONFIG_RESPONSE` | — |
| | 3.4 心跳 Keepalive 配置 | **已实现** | `configuration.Keepalive.TriggerType` / `IntervalMs`<br>底层：`KeepaliveSpec` | — |
| | 3.5 天线功率/灵敏度配置 | **已实现** | `configuration.Antennas`<br>底层：`AntennaConfiguration` | — |
| | 3.6 C1G2 盘点参数扩展 | **已实现（标准范围）** | `ReaderSettings.Session`、`TagPopulationEstimate`、`ModeIndex`、`Tari` 与 `StateAwareSingulation` 编译为 `C1G2SingulationControl` / `C1G2RFControl`；Target A/B 仅在能力快照明确支持时启用 | 厂商 SearchMode 保持为 Contributor/Profile 专有能力，不放入标准模型。 |
| | 3.7 ROSpec 定时器/GPI 自动触发 | **已实现** | `ReaderSettings.StartTrigger` / `StopTrigger` 支持 Immediate、Periodic、GPI、Duration 与 GPI-with-timeout，并编译到 `ROBoundarySpec` | Live Shell 暂未暴露触发器旗标，保持为 SDK Reader-first API。 |
| | 3.8 盘点附加数据配置 (`AttachedData`) | **已实现** | `ReaderSettings.AttachedData`；`StartAsync` 创建/启用关联 C1G2 Read AccessSpec，`StopAsync` 清理；临时 Tag Access 会暂停并恢复该常驻 AccessSpec | 厂商高效报告选择器仍由 Contributor 自主决定，不能替换标准语义。 |
| **4. 托管盘点与数据上报** | 4.1 启动托管盘点 | **已实现** | `reader.StartAsync(settings)`<br>底层：`ADD_ROSPEC` (14150) ➔ `ENABLE_ROSPEC` ➔ `START_ROSPEC` | — |
| | 4.2 停止托管盘点 | **已实现** | `reader.StopAsync()`<br>底层：`STOP_ROSPEC` ➔ `DISABLE_ROSPEC` ➔ `DELETE_ROSPEC` | — |
| | 4.3 实时标签数据订阅 | **已实现** | `reader.TagsReported` 事件 / `ReadTagReportsAsync()` 异步流<br>底层：`RO_NOTIFICATION` / `TagReport` | — |
| | 4.4 主动拉取模式 (`QueryTags`) | **已实现** | `reader.GetTagReportsAsync()`<br>底层：`GET_REPORT` ➔ `RO_ACCESS_REPORT` | — |
| **5. 标签 Memory 访问操作** | 5.1 自动 ROSpec 生命周期与安全协同 | **已实现（单操作）** | 无托管盘点时 `ExecuteTagAccessAsync` 创建并清理临时 ROSpec；运行中盘点直接关联其 ROSpec；AttachedData 常驻 AccessSpec 会在临时操作期间暂停后恢复 | 组合操作序列仍单列实现。 |
| | 5.2 读标签 Memory | **已实现** | `reader.ReadTagMemoryAsync(ReadTagRequest)`<br>底层：`ADD_ACCESSSPEC` (C1G2Read) ➔ `ENABLE_ACCESSSPEC` ➔ 监听结果 ➔ 清理 | — |
| | 5.3 写标签 Memory / BlockWrite | **已实现** | `reader.WriteTagMemoryAsync(WriteTagRequest)`；多字写且 `Capabilities.IsMultiwordBlockWriteAvailable` 时编译 `C1G2BlockWrite`，否则 `C1G2Write` | — |
| | 5.4 锁标签 Memory | **已实现** | `reader.LockTagMemoryAsync(LockTagRequest)` ➔ `C1G2Lock` / `C1G2LockPayload`；Result 投影为 `TagAccessResult` | — |
| | 5.5 销毁标签 (Kill Tag) | **已实现** | `reader.KillTagAsync(KillTagRequest)` ➔ `C1G2Kill`；Result 投影为 `TagAccessResult` | — |
| | 5.6 块擦除 (BlockErase) | **已实现** | `reader.BlockEraseTagMemoryAsync(BlockEraseTagRequest)` ➔ `C1G2BlockErase`；Result 投影为 `TagAccessResult` | — |
| | 5.7 组合操作序列 (`TagOpSequence`) | **已实现** | `reader.ExecuteTagAccessSequenceAsync(TagAccessSequenceRequest)`；一个 AccessSpec 内包含多个 C1G2 OpSpec，复用每项请求中的标准 BitPointer/Mask TargetTag，返回完整结果集合；Live Shell `tag sequence --op ...` 为薄封装 | 非只读操作要求 `--yes`。 |
| **6. GPIO 与硬件事件通知** | 6.1 GPO 快捷高低电平控制 | **已实现** | `reader.SetGpoAsync(port, state)` ➔ `SET_READER_CONFIG` | — |
| | 6.2 GPI 电平变化事件 | **已实现** | `reader.GpiChanged`，解析 `READER_EVENT_NOTIFICATION` | — |
| | 6.3 心跳接收与超时事件 | **已实现（观察型）** | `reader.KeepaliveReceived`；`builder.WithKeepaliveTimeout(...)` 启用后，连续静默会触发一次 `reader.KeepaliveTimedOut` | 超时不会强制断开；TLS 与显式强制断开属于后续连接增强。 |
| | 6.4 缓冲区告警与诊断事件 | **已实现** | `ErrorOccurred`、`ReportBufferOverflow`、`ReportBufferWarning`（包含读写器报告的缓冲区百分比） | — |

---

## 一、 本轮实施范围与架构隔离原则

### 1. 本轮实施范围说明
- **暂缓项目 (本轮不做，后续按需安排)**：
  - `1.2 TLS 加密连接 (5085)`
  - `1.4 强行撕毁断开 (ForceDisconnect)`
- **后续连接增强项目**：
  - TLS 与显式强制断开维持为独立安全与互操作工作，不作为本轮 1.0.1 SDK/CLI 完成条件。

---

### 2. 架构兼容与扩展预留原则

在实施上述标准 1.0.1 功能时，坚决贯彻以下架构隔离原则：

1. **协议与厂商无关的业务表达**：所有高层模型（如 `ReaderSettings` / `ReaderConfiguration` / `TagAccessRequest`）均为纯抽象接口。具体报文编译完全隔离在 `Llrp101ProtocolAdapter` 中。
2. **厂商扩展 Contributor 预留与解耦**：
   - 当前优先保障标准 LLRP 1.0.1 协议管道（如 `AttachedData` 下发标准 C1G2Read AccessSpec 1000）。
   - 保持 Contributor 接口的设计弹性：未来接入厂商扩展（如 Impinj 硬件直出）时，只需在 Contributor 中拦截对应设置并替换为厂商专属字段，无需改动高层 SDK API 或标准 1.0.1 核心逻辑。
3. **三层 API 状态互锁**：维持 `IsManagedStateSynchronized` 标记。调用第三层 Raw 报文 API 时自动置为 `false`，要求显式 `SynchronizeStateAsync()` 方可切回第一层托管 API。

---

## 三、 下一步具体实施步骤

按以下 4 个阶段顺序推进实施：

- **阶段 1：能力集与配置补全**（补全 2.2-2.6 功率/灵敏度/频点/RF Mode 表及物理门控，补全 3.6-3.8 盘点 C1G2 参数、触发器与 AttachedData）。
- **阶段 2：全量 5 大 C1G2 标签操作库**（补全 5.1-5.7 自动 ROSpec 生命周期、Lock, Kill, BlockErase, BlockWrite 优化与 TagOpSequence）。
- **阶段 3：GPIO、事件与连接增强**（补全 1.2/1.4 TLS/ForceDisconnect，6.1-6.4 GPO 快捷控制、GPI 事件、心跳事件与缓冲区告警）。
- **阶段 4：测试与实机闭环验收**（更新 `Interop.Tests` 模拟测试与实机验证，更新 API 指南与测试用例）。
