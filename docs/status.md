# 当前状态

> 基准日期：2026-08-03
> 目的：作为仓库当前真实状态的事实源。README 面向使用者，长期规划面向设计；本文件回答“现在已经有什么、还缺什么、当前阶段的核心交付目标是什么”。

## 总结

在保持项目架构对多厂商自定义扩展（Vendor Extensions，如 Impinj/Zebra 等）以及未来 LLRP 协议版本（LLRP 1.1/2.0）具备长期解耦与可扩展性的前提下，**当前阶段的核心主线交付目标是：完成 LLRP 1.0.1 标准协议与 Impinj 默认扩展（Impinj Default Extensions）的 SDK 与 CLI 全量功能实现与设备闭环验收。**

当前源码已具备 LLRP 1.0.1 与 Impinj 扩展的核心 SDK API（连接/协商/托管盘点/标签读写/配置管理/专家 ROSpec/AccessSpec）及 CLI（Live Shell、托管命令、帧观察器、自动补全和离线 Codec 工具）。组合 Tag Access 序列已在 SDK 与 Live Shell 完成；Keepalive 超时可选监测已完成，最终实机验收仍在推进；不得将构建或单元测试通过表述为全量设备验收。

## 支持矩阵

| 能力 | 当前状态 | 说明 |
|---|---|---|
| LLRP 1.0.1 + Impinj 扩展 | **主线交付中** | 核心标准模型、Codec、Registry、Adapter、Impinj 扩展、组合 Tag Access、CLI 命令与可选 Keepalive 超时监测已可用；最终双设备验收尚未完成。 |
| 多厂商扩展架构 | 可扩展基线 | 抽象层支持 `IReaderExtension`、`ITagReportContributor`、`IReaderSettingsContributor` 等扩展点，保持对其他厂商扩展的可扩展性。 |
| LLRP 1.1 | 架构兼容 | `GET_SUPPORTED_VERSION` / `SET_PROTOCOL_VERSION` 协商、`Force11` 和回退策略已接入。 |
| LLRP 2.0 | 架构预留，Adapter 未完成 | `definitions/llrp-2.0-delta.yaml` 存在，保留未来 2.0 Adapter 的架构扩展点。 |
| 标准 Tag Access | 主线可用 | `ReadTagMemoryAsync` / `WriteTagMemoryAsync` 通过临时 AccessSpec 运行；已完成 R420 实机非破坏性读验证。 |
| Contributor 管道 | 主线可用 | Settings、TagReport 与 Inventory Contributor 已接入 SDK；Impinj Settings/TagReport 扩展属性已打通端到端验收。 |
| CLI 工具链 | 主线可用 | 包含 Live Shell、Agent/脚本友好的一次性 `inventory`、SDK 托管 Reader API、专家 ROSpec/AccessSpec/Raw 调试入口、自动补全、帧观察器与离线 Codec 工具。 |
| Reader Studio WPF | **首个应用示例可用** | 已加入解决方案；支持多读写器档案、盘点汇总、精确 EPC 标签读写、ReaderSettings 草稿/设备查询/SDK 默认值/Apply、标准过滤器、触发器、报告触发、AttachedData、TOI 和 GPO 诊断。 |
| Virtual Reader | 主线可用 | 支持 1.0.1 场景模拟、能力查询、ROSpec 生命周期、TagReport 与 AccessSpec 模拟。 |

## 已实现

### 盘点与资源服务

- `LlrpReader.StartAsync(InventorySettings)`、无参 `StartAsync()`、`StartInventoryAsync(...)`、`StopAsync()` 与 `ClearManagedSettingsAsync()`。Stop 停止并保留 SDK ROSpec `14150`（及 AttachedData `14151`），无参 Start 可再次启动；Clear 才释放资源域。`StartInventoryAsync` 返回隔离的 `InventorySession` 报告流；连接级 `ReadTagReportsAsync()` 与 `TagsReported` 保持为全量观察流。
- `ReaderSettings`、`QuerySettingsAsync()` 与 `ApplySettingsAsync()` 是托管配置与声明式盘点意图的统一入口；`ReaderSettings.Create/Edit` 与 `InventorySettings.Create/Edit` 提供直接生成同一套 record 的轻量 Builder。`ValidateSettingsAsync()` 在不发送报文的情况下返回结构化诊断，Apply 及带 Settings 的启动入口会在资源操作前执行同一校验。Apply 只部署 Inventory 资源并保持 Disabled，`StartAsync()` / `StartInventoryAsync()` 才启动它。Live Shell 使用稳定的 `settings show|edit|validate|apply|load|save|discard` 契约，不再提供 `config` 命令。
- `GetDefaultSettingsAsync()` 提供第二种托管初始化来源：它不读取或修改读写器资源，而是按已连接 Reader 的 Identity、Firmware、Capabilities 与激活扩展生成 `ReaderSettingsDefaults`（Settings、ProfileId、Source、Notes）。`ReaderSettingsDefaults.CreateGeneric()` 可在离线时生成可移植标准基线；`ReaderSettingsSerializer` 可序列化带 Profile 来源的默认文档。
- `ReadTagReportsAsync()`、`GetTagReportsAsync()` 与 `TagsReported`，并共用同一份已翻译的 `TagReport`。
- `InventorySettings` 是版本无关的声明式盘点意图模型，包含 C1G2 参数、标准 ROSpec Start/Stop Trigger（周期 Trigger 可选 `StartAtUtc` UTC 首次时间）与 `AttachedDataOptions`；`ROReportSpec` 的 `UponNTagsOrEndOfRoSpec` 配合 `ReportEveryNTags = 0` 表示 ROSpec 结束后批量报告（BatchAfterStop），其他报告 Trigger 要求 N 大于零。启用 AttachedData 时，`StartAsync` 创建并启用关联的标准 C1G2 Read AccessSpec，`StopAsync` 停用但保留它，`ClearManagedSettingsAsync()` 才清理它。临时 Tag Access 会暂停后恢复 AttachedData；在已停止的托管配置上执行 Tag Access 会复用 `14150`，结束后返回 `HighLevelConfigured`。
- 状态感知 Filter 要求同时提供 `InventorySelectFilter.StateAwareAction` 与 `InventorySettings.StateAwareSingulation`，并要求 Reader 声明 `CanDoTagInventoryStateAwareSingulation`；任一前提不满足时，SDK 明确拒绝编译，绝不降级为普通 Filter。
- `InventorySelectedFlag.All` 映射为 LLRP 1.1 的 `C1G2TagInventoryStateAwareSingulationAction.S_All=1`；LLRP 1.0.1 没有该字段，SDK 会明确拒绝该意图。
- `ReaderMetadata` 新增物理参数表（`TxPowers`、`RxSensitivities`、`TxFrequencies`、`HopTables`、`RfModes`）与门控能力标志位（`IsTagAccessAvailable`、`IsMultiwordBlockWriteAvailable`、`IsMultiwordBlockEraseAvailable`、`CanDoTagInventoryStateAwareSingulation`）。
- `IRoSpecService` 提供 Add/Delete/Enable/Disable/Start/Stop/GetAll。
- `IAccessSpecService` 提供 Add/Delete/Enable/Disable/GetAll。
- `WriteTagMemoryAsync` 在设备能力快照确认支持时自动将多字写编译为 `C1G2BlockWrite`，否则保持 `C1G2Write`。
- TagReport 翻译覆盖标准 C1G2 Read、Write、BlockWrite、Lock、Kill 与 BlockErase 的 OpSpec Result，因此所有现有单操作托管 API 都能取得成功/失败结果。
- Reader 事件已公开 `GpiChanged`、`AntennaChanged`、`KeepaliveReceived`、`KeepaliveTimedOut`（通过 `WithKeepaliveTimeout` 选择启用）、`ReportBufferWarning`（含百分比）与 `ReportBufferOverflow`。
- 托管盘点独占 SDK 保留 ROSpec/AccessSpec 资源；停止后处于 `HighLevelConfigured`，运行时处于 `HighLevelRunning`。专家资源写入必须先清除托管资源并调用 `EnterManualResourceModeAsync()`。Raw Protocol 操作后资源状态变为未知，必须通过 `SynchronizeStateAsync()` 重新识别保留资源或回到空闲状态。

### 版本协商与 Adapter

- `Llrp101ProtocolAdapter` 与 `Llrp11ProtocolAdapter`。
- `LlrpProtocolVersionPolicy.Auto`、`Force101`、`Force11`。
- `ConnectAsync()` 内部执行 1.1 协商，并可在旧设备返回不支持时回退到 1.0.1。
- CLI 支持 `--llrp auto|1.0.1|1.1`。

### 扩展生命周期

- `ILlrpProtocolModule`、`UseProtocolModule(...)`。
- `IReaderExtension`、`UseReaderExtension(...)`、`reader.Extensions`。
- `UseImpinj()` 高层扩展入口；Impinj 生成协议资产已拆分到独立的 `LlrpNet.Protocol.Impinj` 项目，Raw 协议用户无需引用 `LlrpSdk` 即可使用其报文、参数、Codec 和注册模块。
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
- 交互式在线功能由 Live Shell 提供：`tag read/write/lock/erase/kill <epc> ...` 与 `tag sequence <epc> --op ...` 均复用标准 SDK Tag Access API；若读取方没有托管盘点，会临时启动并在结束后清理。写入、擦除、锁定、销毁或含这些操作的序列均要求显式 `--yes`；省略确认时 `tag write` 只显示 dry-run 计划。根级 `inventory` 提供唯一的一次性在线工作负载；其他根命令为离线 `inspect/decode/validate/encode`。
- Live Shell 使用 `settings show|edit|validate|apply|load|save|discard` 管理托管设置；`caps` 继续显示 Tx/Rx 索引到 dBm 的能力表。专家需要直接构造 `GET_READER_CONFIG` / `SET_READER_CONFIG` 时使用 `reader.Protocol`。
- Live Shell 默认渲染全部非标签 TX/RX LLRP 帧，`RO_ACCESS_REPORT` 交给标签汇总；`inventory start [--monitor live|frames|none]` 默认进入前台聚合标签监控，`--monitor frames` 连标签报告也按底层 TX/RX 帧显示。Ctrl+C 或 `inventory start --monitor-duration <seconds>` 到期都只退出监控并返回 Prompt，盘点继续运行；`inventory stop` 停止并保留托管配置，`resources clear` 才删除托管 ROSpec。
- 2026-07-27：Live Shell 已通过 R420 的实际非破坏性 User Memory 读取验收，目标 EPC `E28011710000020D056E9BEE` 的 word 0 返回 `0000`。

### 托管 Settings 与专家配置

- `QuerySettingsAsync()` 在同一操作锁中读取版本无关配置、保留 ROSpec `14150` 与 AttachedData AccessSpec `14151`；按协商的 LLRP 1.0.1 或 1.1 参数图恢复标准 Trigger、Select Filter、报告选择、AttachedData 与实际运行状态。1.1 的 `S_All` 可还原为 `InventorySelectedFlag.All`。`ApplySettingsAsync()` 在 Settings 含 Inventory 时按清场、配置写入、重建托管资源的顺序执行。
- `14150` / `14151` 为 SDK 保留资源；`InventorySettings` 不再公开资源 ID，Live Shell 的手动 `rospec add` 必须显式提供非保留 ID。
- `reader.Protocol.TransactAsync<TResponse>()` 是 `GET_READER_CONFIG` / `SET_READER_CONFIG` 的专家入口；成功 Raw 操作后需要 `sync` 才能继续托管操作。

### Contributor 管道

- `ITagReportContributor` 会在标准协议翻译后投影厂商 Custom Parameter 到 `TagReport.Extensions`；Impinj 扩展识别 Serialized TID、RF Phase Angle、Peak RSSI、GPS、Doppler、TxPower、XPC、CR Handle、ID、Enhanced Integra 和 Endpoint IC（前提是读写器报告中包含这些字段）。
- `IReaderSettingsContributor` 可以把 `GET_READER_CONFIG_RESPONSE` 的 Custom Parameter 投影到 `ReaderConfiguration.Extensions`，并在 Apply 时生成 `SET_READER_CONFIG` 的 Custom Parameter；`IInventorySettingsContributor` 负责把保留 ROSpec 的厂商报告参数反向恢复为托管扩展值。
- `IReaderSettingsDefaultsContributor` 为已识别 Reader 生成可编辑的默认 Settings。Seuic UF40 根据能力表把实际天线和 Rx/Tx/Hop/Channel 直接写入标准 `InventorySettings.AntennaConfigurations`；编译器只读取核心标准模型，不再依赖隐藏的厂商 inventory profile extension。
- `UseImpinj()` 会在配置查询中请求 `ImpinjRequestedData(All_Configuration)`，并将可写的 `ImpinjReaderConfiguration` 投影为 `ReaderConfiguration.Extensions["impinj.configuration"]`，将区域/温度投影为只读 `impinj.facts`。目前可编译 Search Mode、频率、低占空比、GPI 防抖、Link Monitor、Report Buffer、AccessSpec 与 Advanced GPO 参数；2026-07-30 已在 R420 6.4.1.240 对当前 GPI debounce、Link Monitor、Report Buffer 与 AccessSpec 配置完成同值 `ApplySettingsAsync()` / `QuerySettingsAsync()` 回读验收。
- `IInventoryContributor` 现在可读取初始化后的身份、能力和协商版本。Impinj 已接入 `ImpinjInventoryReportOptions`（`InventorySettings.Extensions["impinj.inventoryReport"]`）及默认拒绝的能力目录；报告选择器可表达 Serialized TID、RF Phase、Peak RSSI、GPS、优化读取（最多两个 `C1G2Read`）、Doppler、TxPower、XPC、CR Handle、ID、Enhanced Integra 和 Endpoint IC，并在查询时恢复这些字段。未知型号/固件仍默认拒绝，应用确认设备支持后可显式启用 `AllowUnverifiedFields`。R420 Model `2001002` Firmware `6.4.1.x` 的 ItemTest 抓包已验证 `ImpinjTagReportContentSelector` 位于 `ROReportSpec` 时可被接受，SDK 编译器已修正为相同挂载位置。
- `LlrpSdk.Extensions.Impinj` 为 `InventorySettingsBuilder` 提供 `.Impinj(...)` 强类型入口，可配置常用报告字段、优化读取与标签数量估计，不再要求普通调用方手写扩展 Key；内部仍生成现有 `ImpinjInventoryReportOptions` / `ImpinjInventoryControlOptions`，Contributor 和序列化格式不变。
- 2026-07-27：R420 直接 SDK 盘点同时启用 `IncludeSerializedTid`、`IncludeRfPhaseAngle`、`IncludePeakRssi` 成功，收到 EPC `E28011710000020D056E9BEE` 的扩展字段 `impinj.serializedTid = E2801171200003EEADD309A0`、`impinj.rfPhaseAngle = 1276`、`impinj.peakRssi = -6700`；临时 SDK ROSpec 已在停止后确认清理。

## 未完成

### LLRP 2.0

仓库已有 2.0 Delta，但当前没有 `Llrp20ProtocolAdapter`，Reader 初始化 Adapter 列表也只有 1.0.1 与 1.1。该 Adapter 及其 2.0 Virtual Reader 互操作闭环均已排到项目最终阶段。

### 扩展 Contributor

`IReaderSettingsContributor`、`ITagReportContributor`、`IInventoryContributor` 和 `IInventorySettingsContributor` 已接入 SDK；Impinj Settings Contributor 已完成模型到协议参数的编译及 R420 6.4.1.240 同值写入/回读验收。R420 Model `2001002` Firmware `6.4.1.x` 已完成抓包、SDK 盘点与三项 TagReport 扩展字段的闭环验证；后续需扩充其他型号/固件的能力目录。

### 托管 Settings 文件

`ReaderSettings` 的 JSON 导入、导出和校验已接入 Live CLI，根文档与每个厂商扩展均带版本号。厂商扩展值只能经 `IReaderSettingsSerializationContributor` 的强类型映射读写，禁止直接序列化 `object`；Impinj 已实现可写配置、只读 Facts 与盘点报告扩展映射，不再投影已废弃的 `impinj.InventorySettings` 键。

R420 Firmware 6.4.1 的 ItemTest 抓包证明最新 Impinj 定义中的 `ImpinjTagReportContentSelector` 可用；此前 SDK 把它错误地放进 `AISpec`，导致 `M_UnsupportedParameter`。修正为 `ROReportSpec` 子项后，直接 SDK 盘点已收到 `impinj.serializedTid`、`impinj.rfPhaseAngle` 与 `impinj.peakRssi` 扩展字段，并在结束后清理临时 ROSpec。

### Virtual Reader 场景覆盖

Virtual Reader 已能生成可配置 EPC 的基础 TagReport，对 EPC bit mask 执行最小标签筛选，并以可变的 User Memory 模拟 C1G2 Read/Write AccessSpec；也支持最小 `GET/SET_READER_CONFIG` 的 Keepalive、GPO、天线和事件配置状态，以及标准 `DELETE_ACCESSSPEC(0)` / `DELETE_ROSPEC(0)` 全资源清场。`Interop.Tests` 覆盖托管模式接管手动资源、Raw 后同步、写后读、配置查询/应用回读、SDK 超时、LLRP 错误状态和主动断线后的自动重连。它仍不模拟真实射频、跨进程持久化或 LLRP 2.0。

2026-08-03 已完成协议扩展分层、轻量托管 Settings API 与 CLI 重构后的 `dotnet build LLRPCSharp.slnx --no-restore`（零警告、零错误）及 `dotnet test LLRPCSharp.slnx --no-build --no-restore`。全部测试项目通过，共 399 项；该结果验证源码和自动化场景，不替代真实设备验收。

本轮（2026-07-28）完成的主要工作：

- **CLI/SDK 边界（2026-08-03）**：SDK 将已应用的声明式 Inventory 意图持久化为 Reader 上可查询的保留资源；Live Shell 只在用户执行 `settings edit` 或 `settings load` 后保存可空的本地草稿。`show/edit/load/save/discard/validate` 不写设备，只有 `settings apply [file] --yes` 写设备。`inventory start|stop|status` 只控制或显示 Reader 已部署的 Inventory；`CurrentInventorySettings` 是 Reader 托管配置，不是草稿。
- **一次性 `inventory`（2026-08-03）**：根 Spectre 命令按当前格式提供连接、Settings 文件、duration、输出、协议和厂商选项，默认输出结构化 JSON。它与 Live Shell 共用 `ManagedSettingsWorkflow` 和 `LlrpReader` SDK，不维护第二套配置逻辑；结束时 Stop 并清除托管 Inventory 资源，已经应用的 Reader 全局 Configuration 保留。Live Shell 仍使用 `inventory start|stop|status` 管理当前连接。
- **受控配置写入修正（2026-07-30）**：托管 `ApplySettingsAsync` 内部的 `SET_READER_CONFIG` 不再按外部 Raw Protocol 调用使自身事务进入 `StateUnknown`；因此配置写入后可继续创建 SDK 保留 ROSpec。通过 `reader.Protocol` 直接发送配置报文的失效与 `sync` 要求保持不变。
- **CLI `tag` 全量对齐**：`tag lock`、`tag kill`、`tag erase` 已补全；`TagAccessRenderer` 错误字段修正为 `Error`。
- **`InventorySettingsSerializer`**：提供 JSON 序列化、反序列化、加载和保存帮助类，供盘点草稿使用；厂商 Extension 的强类型 Profile 序列化仍待扩展自身实现。
- **`CommandCatalog` 扩展**：新增 `Require`、`TryResolve(name, isConnected)`、`Assist(input, cursor, isConnected)` 方法，支持连接状态门控与末尾空格自动补全场景。
- **`LlrpCli.csproj`**：添加 `InternalsVisibleTo("LlrpCli.Tests")`，允许测试项目访问 internal 命令处理器。
- **CLI 用户指南**：[cli-user-guide.md](file:///f:/Projects/LLRP/LLRPCSharp/docs/guides/cli-user-guide.md) 全量更新，覆盖所有命令语法、参数表、settings 文件 JSON 格式示例与常见问题。
- 测试：当前解决方案级验证共 **399 项**全部通过（0 失败），包括 Settings Builder、无副作用结构化校验、Impinj 强类型 Builder、CLI Settings 契约、一次性 `inventory` 参数门控、组合 Tag Access、Keepalive 超时、资源模式接管、Raw 后同步、命令目录和协议扩展独立依赖测试。

此前发生的 Impinj 扩展类型重复定义错误已解决：`LlrpNet.ProtocolGenerator.Tool` 已增加对输出目录孤立 `*.g.cs` 文件的检测与自动清理机制，旧编号遗留文件已全部清除。

## 同步要求

- 改变公开能力时，同步本文件。
- 新增长期设计时，放入规划或 architecture 文档，不要把未来 API 写成本文件的已实现事实。
- 修复构建阻塞后，更新本文件的构建状态。
