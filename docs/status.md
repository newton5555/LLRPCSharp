# 当前状态

> 基准日期：2026-07-24  
> 目的：作为仓库当前真实状态的事实源。README 面向使用者，长期规划面向设计；本文件回答“现在已经有什么、还缺什么、什么会阻塞开发”。

## 总结

当前源码已经超过旧 README 的 M3/M5 描述：M3/M4/M6/M7/M8 都已有部分实现。1.0.1/1.1 SDK 基线、Reader 配置流和扩展主动初始化已经固定下来。

当前最高优先级：在已建立的 Contributor 管道上补 Impinj Inventory Contributor，并推进 Virtual Reader 场景。

## 支持矩阵

| 能力 | 当前状态 | 说明 |
|---|---|---|
| LLRP 1.0.1 | 可用基线 | 标准模型、Codec、Registry、Reader Adapter 和 CLI 路径已存在。 |
| LLRP 1.1 | 可用基线 | `GET_SUPPORTED_VERSION` / `SET_PROTOCOL_VERSION` 协商、`Force11` 和回退策略已接入。 |
| LLRP 2.0 | 定义已入库，Adapter 未完成 | `definitions/llrp-2.0-delta.yaml` 存在，但没有 `Llrp20ProtocolAdapter`、协商和互操作闭环。 |
| Impinj 扩展 | 可用基线 | `UseImpinj()` 和扩展注册入口可用，生成资产构建已恢复正常。 |
| 标准 Tag Access | 可用基线 | `ReadTagMemoryAsync` / `WriteTagMemoryAsync` 通过临时 AccessSpec 运行；R420 已完成非破坏性读验证。 |
| Contributor 管道 | 部分可用 | Settings、TagReport 与 Inventory Contributor 已接入 SDK；Impinj Settings 的只读查询投影、TagReport 投影及报告选择器能力目录已实现，R420 已完成 Serialized TID、RF Phase Angle 与 Peak RSSI 端到端验收。 |
| CLI | 可用诊断入口 | 支持在线连接、监控、Live Shell、离线 inspect/decode/encode。 |
| Virtual Reader | 可用最小 1.0.1 Server | 支持能力查询、ROSpec 生命周期、基础 TagReport 与临时 AccessSpec 读/写结果；故障注入和 2.0 仍待补。 |

## 已实现

### 盘点与资源服务

- `LlrpReader.StartAsync(ReaderSettings)`、`StopAsync()`、`InventoryAsync(ReaderSettings?)`。
- `ReadTagReportsAsync()` 与 `TagsReported`，并共用同一份已翻译的 `TagReport`。
- `ReaderSettings` 作为版本无关的盘点意图模型存在。
- `IRoSpecService` 提供 Add/Delete/Enable/Disable/Start/Stop/GetAll。
- `IAccessSpecService` 提供 Add/Delete/Enable/Disable/GetAll。
- Raw Protocol 操作后会使托管状态失效，并通过 `SynchronizeStateAsync()` 恢复可继续 Managed 操作的状态。

### 版本协商与 Adapter

- `Llrp101ProtocolAdapter` 与 `Llrp11ProtocolAdapter`。
- `LlrpProtocolVersionPolicy.Auto`、`Force101`、`Force11`。
- `ConnectAsync()` 内部执行 1.1 协商，并可在旧设备返回不支持时回退到 1.0.1。
- CLI 支持 `--llrp auto|1.0.1|1.1`。

### 扩展生命周期

- `ILlrpProtocolModule`、`UseProtocolModule(...)`。
- `IReaderExtension`、`UseReaderExtension(...)`、`reader.Extensions`。
- `UseImpinj()` 扩展入口。
- Reader Extension 基于 Manufacturer/Model/Firmware/ProtocolVersion 匹配，并检查互斥组冲突。
- **两阶段能力获取与主动连接初始化 (ADR 0002)**：重构 SDK 握手连接逻辑，实现“读取基础身份 -> 匹配并激活运行对应扩展的主动初始化（如 Impinj 自动发送 `IMPINJ_ENABLE_EXTENSIONS`）-> 读取包含厂商专属能力的完整 Capability 快照”的双阶段流，解决因扩展未使能而无法查询厂商扩展能力的限制。

### 可靠性与诊断

- `LlrpAutomaticReconnectOptions` 和 `WithAutomaticReconnect(...)`。
- 意外断线后的有限自动重连。
- `LlrpFrameJournal` 诊断基线。
- `ILlrpFrameObserver` 可从底层 Transport/Session 注入完整 TX/RX 帧观测。

### Reader 配置查询与应用

- `LlrpReader.QuerySettingsAsync()` 与 `ApplySettingsAsync(ReaderConfiguration)`，支持高层版、版本无关的 Keepalive、Antenna、GPI/GPO、以及事件通知配置的查询与应用。
- 支持 LLRP 1.0.1 和 1.1 协议适配器下的映射与配置流。
- `QuerySettingsAsync()` 是只读 SDK 事务，不会使托管 ROSpec/AccessSpec 状态失效；`ApplySettingsAsync()` 仍会使其失效，之后必须显式 `SynchronizeStateAsync()`。
- CLI 命令行支持 `config get <HOST>` 和 `config apply <HOST> [options]`；Live Shell 也支持 `config get` 与复用同一映射/校验的 `config apply [options] [--dry-run] --yes`。配置写入会在连接前拒绝空写入、缺少天线上下文的天线字段，以及不完整的 GPO 写入；Live 写入必须显式使用 `--yes`。

### 标签访问 API

- `ReadTagMemoryAsync(ReadTagRequest)` 与 `WriteTagMemoryAsync(WriteTagRequest)`：版本无关的标准 C1G2 读写入口。
- AccessSpec 由 1.0.1 / 1.1 Adapter 编译；调用要求已有 SDK 托管盘点，执行后自动 Disable/Delete 临时 AccessSpec。
- `TagReport` 会投影标准 C1G2 Read/Write OpSpec Result。
- 2026-07-27：Impinj R420（LLRP 1.0.1、固件 6.4.1.240）通过直接 SDK 调用完成连接、Impinj 扩展激活、盘点和 User Memory 读；详见互操作验收文档。
- `Interop.Tests` 使用 Virtual Reader 覆盖 SDK 托管盘存、TagReport 翻译、临时 AccessSpec 读取结果与清理路径；Virtual Reader 是固定 LLRP 1.0.1，因此测试显式使用 `Force101`。

### Contributor 管道

- `ITagReportContributor` 会在标准协议翻译后投影厂商 Custom Parameter 到 `TagReport.Extensions`；Impinj 扩展当前识别 Serialized TID、RF Phase Angle 和 Peak RSSI（前提是读写器报告中包含这些字段）。
- `IReaderSettingsContributor` 可以把 `GET_READER_CONFIG_RESPONSE` 的 Custom Parameter 投影到 `ReaderConfiguration.Extensions`，并在 `ApplySettingsAsync` 时生成 `SET_READER_CONFIG` 的 Custom Parameter。
- `UseImpinj()` 会在配置查询中请求 `ImpinjRequestedData(All_Configuration)`，并将区域、温度、GPI 防抖、Link Monitor、Report Buffer 与 AccessSpec 设置投影为 `ReaderConfiguration.Extensions["impinj.readerSettings"]`。
- 2026-07-27：R420 直测返回区域 `China_920_925_MHz`、温度 `35°C`、4 路 GPI 防抖、正常 Report Buffer 以及 AccessSpec 的 FIFO 设置。该 Contributor 当前故意不生成写入参数，避免 `ApplySettingsAsync` 隐式修改 Impinj 私有配置。
- `IInventoryContributor` 现在可读取初始化后的身份、能力和协商版本。Impinj 已接入 `ImpinjInventoryReportOptions`（`ReaderSettings.Extensions["impinj.inventoryReport"]`）及默认拒绝的能力目录；R420 Model `2001002` Firmware `6.4.1.x` 的 ItemTest 抓包已验证 `ImpinjTagReportContentSelector` 位于 `ROReportSpec` 时可被接受，SDK 编译器已修正为相同挂载位置。
- 2026-07-27：R420 直接 SDK 盘点同时启用 `IncludeSerializedTid`、`IncludeRfPhaseAngle`、`IncludePeakRssi` 成功，收到 EPC `E28011710000020D056E9BEE` 的扩展字段 `impinj.serializedTid = E2801171200003EEADD309A0`、`impinj.rfPhaseAngle = 1276`、`impinj.peakRssi = -6700`；临时 SDK ROSpec 已在停止后确认清理。

## 未完成

### LLRP 2.0

仓库已有 2.0 Delta，但当前没有 `Llrp20ProtocolAdapter`，Reader 初始化 Adapter 列表也只有 1.0.1 与 1.1。

### 扩展 Contributor

`IReaderSettingsContributor`、`ITagReportContributor` 和 `IInventoryContributor` 已接入 SDK；Impinj Settings Contributor 已实现只读查询，Inventory Contributor 已实现默认拒绝的能力门控。R420 Model `2001002` Firmware `6.4.1.x` 已完成抓包、SDK 盘点与 TagReport 扩展字段的闭环验证；后续需扩充其他型号/固件的能力目录。受 Profile 驱动的 Impinj 设置写入仍未实现。

### Reader 默认配置 Profile

`LlrpReader.GetDefaultConfiguration()` 已可在连接初始化完成后返回不访问设备的 SDK 安全基线；`GetDefaultConfigurationResult()` 额外公开选中的 Provider/Profile 来源。两者与 `QuerySettingsAsync()`（设备当前状态）及设备持久化配置严格分离。`IReaderConfigurationDefaultsProvider` 支持厂商/型号 Profile 注册，按最高优先级选择且同优先级冲突显式失败。`ReaderConfigurationPatch` 可通过 `ResolveConfigurationPatchAsync()` 只读预览完整结果，或由 `ApplyConfigurationPatchAsync()` 显式查询、合并并写入。当前基线不猜测任何设备相关 RF/GPO 值；Impinj 型号 Profile 待厂商资料或实测确认后加入。

R420 Firmware 6.4.1 的 ItemTest 抓包证明最新 Impinj 定义中的 `ImpinjTagReportContentSelector` 可用；此前 SDK 把它错误地放进 `AISpec`，导致 `M_UnsupportedParameter`。修正为 `ROReportSpec` 子项后，直接 SDK 盘点已收到 `impinj.serializedTid`、`impinj.rfPhaseAngle` 与 `impinj.peakRssi` 扩展字段，并在结束后清理临时 ROSpec。

### Virtual Reader 场景覆盖

Virtual Reader 已能生成可配置 EPC 的基础 TagReport，对 EPC bit mask 执行最小标签筛选，并以可变的 User Memory 模拟 C1G2 Read/Write AccessSpec；也支持最小 `GET/SET_READER_CONFIG` 的 Keepalive、GPO、天线和事件配置状态。`Interop.Tests` 覆盖写后读、配置查询/应用回读、SDK 超时、LLRP 错误状态和主动断线后的自动重连。它仍不模拟真实射频、跨进程持久化或 LLRP 2.0。

## 当前构建状态

`dotnet build LLRPCSharp.slnx --no-restore` 已通过，解决方案中所有项目（包含 `src/LlrpSdk.Extensions.Impinj`）编译零错误；当前测试基线全部通过。

此前发生的 Impinj 扩展类型重复定义错误已解决：`LlrpNet.ProtocolGenerator.Tool` 已增加对输出目录孤立 `*.g.cs` 文件的检测与自动清理机制，旧编号遗留文件已全部清除。

## 同步要求

- 改变公开能力时，同步本文件。
- 新增长期设计时，放入规划或 architecture 文档，不要把未来 API 写成本文件的已实现事实。
- 修复构建阻塞后，更新本文件的构建状态。
