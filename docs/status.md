# 当前状态

> 基准日期：2026-07-28
> 目的：作为仓库当前真实状态的事实源。README 面向使用者，长期规划面向设计；本文件回答“现在已经有什么、还缺什么、当前阶段的核心交付目标是什么”。

## 总结

在保持项目架构对多厂商自定义扩展（Vendor Extensions，如 Impinj/Zebra 等）以及未来 LLRP 协议版本（LLRP 1.1/2.0）具备长期解耦与可扩展性的前提下，**当前阶段的核心主线交付目标是：完成 LLRP 1.0.1 标准协议与 Impinj 默认扩展（Impinj Default Extensions）的 SDK 与 CLI 全量功能实现与设备闭环验收。**

当前源码已具备 LLRP 1.0.1 与 Impinj 扩展的核心 SDK API（连接/协商/托管盘点/标签读写/配置管理/高级 ROSpec/AccessSpec）及 CLI（Live Shell、高层命令、帧观察器、自动补全和离线 Codec 工具）。组合 Tag Access 序列已在 SDK 与 Live Shell 完成；Keepalive 超时可选监测已完成，最终实机验收仍在推进；不得将构建或单元测试通过表述为全量设备验收。

## 支持矩阵

| 能力 | 当前状态 | 说明 |
|---|---|---|
| LLRP 1.0.1 + Impinj 扩展 | **主线交付中** | 核心标准模型、Codec、Registry、Adapter、Impinj 扩展、组合 Tag Access、CLI 命令与可选 Keepalive 超时监测已可用；最终双设备验收尚未完成。 |
| 多厂商扩展架构 | 可扩展基线 | 抽象层支持 `IReaderExtension`、`ITagReportContributor`、`IReaderSettingsContributor` 等扩展点，保持对其他厂商扩展的可扩展性。 |
| LLRP 1.1 | 架构兼容 | `GET_SUPPORTED_VERSION` / `SET_PROTOCOL_VERSION` 协商、`Force11` 和回退策略已接入。 |
| LLRP 2.0 | 架构预留，Adapter 未完成 | `definitions/llrp-2.0-delta.yaml` 存在，保留未来 2.0 Adapter 的架构扩展点。 |
| 标准 Tag Access | 主线可用 | `ReadTagMemoryAsync` / `WriteTagMemoryAsync` 通过临时 AccessSpec 运行；已完成 R420 实机非破坏性读验证。 |
| Contributor 管道 | 主线可用 | Settings、TagReport 与 Inventory Contributor 已接入 SDK；Impinj Settings/TagReport 扩展属性已打通端到端验收。 |
| CLI 工具链 | 主线可用 | 包含 Live Shell 交互式 Studio、SDK Reader API 的高层封装、原始 ROSpec/AccessSpec/Raw 调试入口、自动补全、帧观察器与离线 Codec 工具。 |
| Virtual Reader | 主线可用 | 支持 1.0.1 场景模拟、能力查询、ROSpec 生命周期、TagReport 与 AccessSpec 模拟。 |

## 已实现

### 盘点与资源服务

- `LlrpReader.StartAsync(ReaderSettings)`、`StopAsync()`、`InventoryAsync(ReaderSettings?)`。默认托管盘点以 Null Start Trigger 建立 ROSpec，随后显式发送 `START_ROSPEC`；未指定的 AISpec C1G2 参数回退到读写器天线默认配置。Seuic UF40 的兼容默认值由 `LlrpSdk.Extensions.Seuic` 提供，实机盘点验收待补。
- `ReadTagReportsAsync()`、`GetTagReportsAsync()` 与 `TagsReported`，并共用同一份已翻译的 `TagReport`。
- `ReaderSettings` 是版本无关的盘点意图模型，包含 C1G2 参数、标准 ROSpec Start/Stop Trigger 与 `AttachedDataOptions`；启用 AttachedData 时，`StartAsync` 创建并启用关联的标准 C1G2 Read AccessSpec，`StopAsync` 清理它，临时 Tag Access 会暂停后恢复它。
- `ReaderMetadata` 新增物理参数表（`TxPowers`、`RxSensitivities`、`TxFrequencies`、`HopTables`、`RfModes`）与门控能力标志位（`IsTagAccessAvailable`、`IsMultiwordBlockWriteAvailable`、`IsMultiwordBlockEraseAvailable`、`CanDoTagInventoryStateAwareSingulation`）。
- `IRoSpecService` 提供 Add/Delete/Enable/Disable/Start/Stop/GetAll。
- `IAccessSpecService` 提供 Add/Delete/Enable/Disable/GetAll。
- `WriteTagMemoryAsync` 在设备能力快照确认支持时自动将多字写编译为 `C1G2BlockWrite`，否则保持 `C1G2Write`。
- TagReport 翻译覆盖标准 C1G2 Read、Write、BlockWrite、Lock、Kill 与 BlockErase 的 OpSpec Result，因此所有现有单操作高层 API 都能取得成功/失败结果。
- Reader 事件已公开 `GpiChanged`、`KeepaliveReceived`、`KeepaliveTimedOut`（通过 `WithKeepaliveTimeout` 选择启用）、`ReportBufferWarning`（含百分比）与 `ReportBufferOverflow`。
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

### 硬件事件与 GPIO

- `GpiChanged`、`KeepaliveReceived`、`ReportBufferOverflow` 硬件事件推送接入消息 Pump 调度。
- `SetGpoAsync(portNumber, state)` 提供控制读写器 GPO 输出的便利接口。

### 标签访问 API

- `ReadTagMemoryAsync`、`WriteTagMemoryAsync`、`LockTagMemoryAsync`、`KillTagAsync`、`BlockEraseTagMemoryAsync`：版本无关的标准 C1G2 读/写/锁/销毁/块擦除操作入口。
- 自动化 ROSpec 生命周期：非盘点状态下调用标签操作时，SDK 自动创建并开启临时 ROSpec 14150，并在操作完成后自动清理临时资源与恢复状态。
- AccessSpec 由 1.0.1 / 1.1 Adapter 编译为 `C1G2Lock`、`C1G2Kill`、`C1G2BlockErase` 等底层 wire 参数；执行后自动 Disable/Delete 临时 AccessSpec。
- `TagReport` 会投影标准 C1G2 Read/Write/BlockWrite/Lock/Kill/BlockErase OpSpec Result。
- 2026-07-27：Impinj R420（LLRP 1.0.1、固件 6.4.1.240）通过直接 SDK 调用完成连接、Impinj 扩展激活、盘点和 User Memory 读；详见互操作验收文档。
- `Interop.Tests` 使用 Virtual Reader 覆盖 SDK 托管盘存、TagReport 翻译、临时 AccessSpec 读取结果与清理路径；Virtual Reader 是固定 LLRP 1.0.1，因此测试显式使用 `Force101`。
- 在线 CLI 功能统一由 Live Shell 提供：`tag read/write/lock/erase/kill <epc> ...` 与 `tag sequence <epc> --op ...` 均复用标准 SDK Tag Access API；若读取方没有托管盘点，会临时启动并在结束后清理。写入、擦除、锁定、销毁或含这些操作的序列均要求显式 `--yes`；省略确认时 `tag write` 只显示 dry-run 计划。根命令仅保留离线 `inspect/decode/validate/encode`。
- Live Shell 的 `config get` 已显示完整标准配置（天线、GPIO、事件）及已启用的 Impinj 配置摘要；`caps` 显示 Tx/Rx 索引到 dBm 的能力表，`config apply` 会校验 Tx/Rx 索引属于当前读写器报告的能力表。
- Live Shell 默认渲染全部非标签 TX/RX LLRP 帧，`RO_ACCESS_REPORT` 交给标签汇总；`inventory start [--monitor live|frames|none]` 默认进入前台聚合标签监控，`--monitor frames` 连标签报告也按底层 TX/RX 帧显示。两个模式下 Ctrl+C 只退出监控并返回 Prompt，盘点继续运行，随后可用 `inventory stop` 清理托管 ROSpec。
- 2026-07-27：Live Shell 已通过 R420 的实际非破坏性 User Memory 读取验收，目标 EPC `E28011710000020D056E9BEE` 的 word 0 返回 `0000`。

### Reader 配置查询与应用

- `LlrpReader.QueryConfigurationAsync()` 与 `ApplyConfigurationAsync(ReaderConfiguration)`，支持高层版、版本无关的 Keepalive、Antenna、GPI/GPO、以及事件通知配置的查询与应用。
- 支持 LLRP 1.0.1 和 1.1 协议适配器下的映射与配置流。
- `QueryConfigurationAsync()` 是只读 SDK 事务，不会使托管 ROSpec/AccessSpec 状态失效；`ApplyConfigurationAsync()` 仍会使其失效，之后必须显式 `SynchronizeStateAsync()`。
- CLI 命令行支持 `config get <HOST>` 和 `config apply <HOST> [options]`；Live Shell 也支持 `config get` 与复用同一映射/校验的 `config apply [options] [--dry-run] --yes`。配置写入会在连接前拒绝空写入、缺少天线上下文的天线字段，以及不完整的 GPO 写入；Live 写入必须显式使用 `--yes`。
- `config get <HOST>`/Live `config get` 直接调用 `QueryConfigurationAsync()`，读取设备当前状态；`config defaults <HOST>`/Live `config defaults` 直接调用 `GetDefaultConfigurationResult()`，展示 SDK 安全基线及选中的 Profile 来源。后者需要已初始化连接以识别设备，但不读取或写入设备配置。
- Live Shell 的 `rospec add` 会在设备当前没有任何 ROSpec 时，创建 SDK 默认的 Disabled ROSpec（ID `14150`）；不会自动启用或开始盘点，也不会覆盖设备已有 ROSpec。后续可显式执行 `rospec enable 14150` 与 `rospec start 14150`。

### Contributor 管道

- `ITagReportContributor` 会在标准协议翻译后投影厂商 Custom Parameter 到 `TagReport.Extensions`；Impinj 扩展当前识别 Serialized TID、RF Phase Angle 和 Peak RSSI（前提是读写器报告中包含这些字段）。
- `IReaderSettingsContributor` 可以把 `GET_READER_CONFIG_RESPONSE` 的 Custom Parameter 投影到 `ReaderConfiguration.Extensions`，并在 `ApplyConfigurationAsync` 时生成 `SET_READER_CONFIG` 的 Custom Parameter。
- `UseImpinj()` 会在配置查询中请求 `ImpinjRequestedData(All_Configuration)`，并将区域、温度、GPI 防抖、Link Monitor、Report Buffer 与 AccessSpec 设置投影为 `ReaderConfiguration.Extensions["impinj.readerSettings"]`。
- 2026-07-27：R420 直测返回区域 `China_920_925_MHz`、温度 `35°C`、4 路 GPI 防抖、正常 Report Buffer 以及 AccessSpec 的 FIFO 设置。该 Contributor 当前故意不生成写入参数，避免 `ApplyConfigurationAsync` 隐式修改 Impinj 私有配置。
- `IInventoryContributor` 现在可读取初始化后的身份、能力和协商版本。Impinj 已接入 `ImpinjInventoryReportOptions`（`ReaderSettings.Extensions["impinj.inventoryReport"]`）及默认拒绝的能力目录；R420 Model `2001002` Firmware `6.4.1.x` 的 ItemTest 抓包已验证 `ImpinjTagReportContentSelector` 位于 `ROReportSpec` 时可被接受，SDK 编译器已修正为相同挂载位置。
- 2026-07-27：R420 直接 SDK 盘点同时启用 `IncludeSerializedTid`、`IncludeRfPhaseAngle`、`IncludePeakRssi` 成功，收到 EPC `E28011710000020D056E9BEE` 的扩展字段 `impinj.serializedTid = E2801171200003EEADD309A0`、`impinj.rfPhaseAngle = 1276`、`impinj.peakRssi = -6700`；临时 SDK ROSpec 已在停止后确认清理。

## 未完成

### LLRP 2.0

仓库已有 2.0 Delta，但当前没有 `Llrp20ProtocolAdapter`，Reader 初始化 Adapter 列表也只有 1.0.1 与 1.1。该 Adapter 及其 2.0 Virtual Reader 互操作闭环均已排到项目最终阶段。

### 扩展 Contributor

`IReaderSettingsContributor`、`ITagReportContributor` 和 `IInventoryContributor` 已接入 SDK；Impinj Settings Contributor 已实现只读查询，Inventory Contributor 已实现默认拒绝的能力门控。R420 Model `2001002` Firmware `6.4.1.x` 已完成抓包、SDK 盘点与 TagReport 扩展字段的闭环验证；后续需扩充其他型号/固件的能力目录。受 Profile 驱动的 Impinj 设置写入仍未实现。

### Reader 默认配置 Profile

`LlrpReader.GetDefaultConfiguration()` 已可在连接初始化完成后返回不访问设备的 SDK 安全基线；`GetDefaultConfigurationResult()` 额外公开选中的 Provider/Profile 来源。两者与 `QueryConfigurationAsync()`（设备当前状态）及设备持久化配置严格分离。`IReaderConfigurationDefaultsProvider` 支持厂商/型号 Profile 注册，按最高优先级选择且同优先级冲突显式失败。`ReaderConfigurationPatch` 可通过 `ResolveConfigurationPatchAsync()` 只读预览完整结果，或由 `ApplyConfigurationPatchAsync()` 显式查询、合并并写入。当前基线不猜测任何设备相关 RF/GPO 值；Impinj 型号 Profile 待厂商资料或实测确认后加入。

R420 Firmware 6.4.1 的 ItemTest 抓包证明最新 Impinj 定义中的 `ImpinjTagReportContentSelector` 可用；此前 SDK 把它错误地放进 `AISpec`，导致 `M_UnsupportedParameter`。修正为 `ROReportSpec` 子项后，直接 SDK 盘点已收到 `impinj.serializedTid`、`impinj.rfPhaseAngle` 与 `impinj.peakRssi` 扩展字段，并在结束后清理临时 ROSpec。

### Virtual Reader 场景覆盖

Virtual Reader 已能生成可配置 EPC 的基础 TagReport，对 EPC bit mask 执行最小标签筛选，并以可变的 User Memory 模拟 C1G2 Read/Write AccessSpec；也支持最小 `GET/SET_READER_CONFIG` 的 Keepalive、GPO、天线和事件配置状态。`Interop.Tests` 覆盖写后读、配置查询/应用回读、SDK 超时、LLRP 错误状态和主动断线后的自动重连。它仍不模拟真实射频、跨进程持久化或 LLRP 2.0。

`dotnet build LLRPCSharp.slnx --no-restore` 已通过，解决方案中所有项目编译零错误、零警告。

本轮（2026-07-28）完成的主要工作：

- **CLI `inventory` 草稿化**：Live Shell 将下一次盘点意图保存在 `DesiredInventorySettings`；`inventory settings show|set|load|save|reset` 可离线维护 JSON 草稿，`inventory start [--antennas <id,id|all>]` 只按草稿启动并允许一次性天线覆盖。运行中的 `CurrentSettings` 不再被误用为会话草稿。
- **CLI `tag` 全量对齐**：`tag lock`、`tag kill`、`tag erase` 已补全；`TagAccessRenderer` 错误字段修正为 `Error`。
- **`ReaderSettingsSerializer`**：提供 JSON 序列化、反序列化、加载和保存帮助类，供盘点草稿使用；厂商 Extension 的强类型 Profile 序列化仍待扩展自身实现。
- **`CommandCatalog` 扩展**：新增 `Require`、`TryResolve(name, isConnected)`、`Assist(input, cursor, isConnected)` 方法，支持连接状态门控与末尾空格自动补全场景。
- **`LlrpCli.csproj`**：添加 `InternalsVisibleTo("LlrpCli.Tests")`，允许测试项目访问 internal 命令处理器。
- **CLI 用户指南**：[cli-user-guide.md](file:///f:/Projects/LLRP/LLRPCSharp/docs/guides/cli-user-guide.md) 全量更新，覆盖所有命令语法、参数表、settings 文件 JSON 格式示例与常见问题。
- 测试：本轮定向验证 `LlrpSdk.Tests` **53 项**、`LlrpCli.Tests` **31 项**全部通过（0 失败），包括盘点草稿、临时天线覆盖、组合 Tag Access、Keepalive 超时和命令目录测试。

此前发生的 Impinj 扩展类型重复定义错误已解决：`LlrpNet.ProtocolGenerator.Tool` 已增加对输出目录孤立 `*.g.cs` 文件的检测与自动清理机制，旧编号遗留文件已全部清除。

## 同步要求

- 改变公开能力时，同步本文件。
- 新增长期设计时，放入规划或 architecture 文档，不要把未来 API 写成本文件的已实现事实。
- 修复构建阻塞后，更新本文件的构建状态。
