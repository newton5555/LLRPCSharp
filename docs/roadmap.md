# 路线图

> 基准日期：2026-08-03
> 目的：记录阶段性与中长期开发顺序。当前真实状态见 `status.md`。

## 当前优先级

1. **当前主线交付推进（LLRP 1.0.1 完善收尾）**：按照规范文档 [`specs/2026-07-28-llrp101-sdk-completion-spec.md`](specs/2026-07-28-llrp101-sdk-completion-spec.md)，完成标准 LLRP 1.0.1 的全量功能覆盖对齐（包含自动 ROSpec 生命周期接管、5 大 C1G2 标签 Access 操作库、物理能力表暴露、快捷 GPIO 及托管 Reader 事件通知）。
2. **硬件/扩展能力补充（按需）**：根据更多型号/固件实测证据扩充 Impinj Contributor 管道的能力目录及相关基线 Profile。
3. **桌面应用独立演进（SDK 外部）**：Reader Studio 已迁移到同级独立项目；后续补充完整 Settings 树、Tag Access 高级操作、运行场景和日志统计，不将 WPF 依赖带回 LLRP SDK。
4. **长期可扩展阶段**：按需接入其他厂商扩展（如 Zebra 扩展等）。
5. **长期可扩展阶段**：接入 `Llrp20ProtocolAdapter` 及 LLRP 2.0 完整互操作闭环。

## 任务拆分

### 1. 恢复构建（已完成）
- 在 ProtocolModel Validator 或 Generator 写入前增加同名类型冲突诊断。
- 明确 Impinj 原始定义中同 subtype 或同 name 的处理规则。
- 清理重复生成输出后，重新运行 `dotnet build LLRPCSharp.slnx --no-restore`。

### 2. Reader 配置查询与应用（已完成）

- 定义 `InventorySettings` 当前范围：只表示 Inventory 设置，还是升级为完整 Reader Config 聚合模型。
- 若保留轻量模型，新增独立的 `ReaderConfiguration` 或 `ReaderConfigSnapshot`。
- 以 `ReaderSettings.QuerySettingsAsync` / `ApplySettingsAsync` 维持版本无关托管模型；专家 `GET_READER_CONFIG` / `SET_READER_CONFIG` 继续走 `reader.Protocol`。
- 完成 `settings` 文件的厂商强类型、版本化扩展映射；不恢复 `config` CLI 命令。

### 3. 标签访问 API（标准基线已完成）

- 已定义版本无关的 `TagAccessRequest` / `TagAccessResult` / `ReadTagRequest` / `WriteTagRequest`。
- 已将 AccessSpec 托管构造放入 1.0.1 / 1.1 Adapter，且通过 R420 完成非破坏性读取验证。
- 已完成：CLI 已增加实际执行的非破坏性 `tag read`，以及不连接设备、不调用写入 API 的 `tag write` dry-run。设计见 [`specs/2026-07-27-cli-tag-access-design.md`](specs/2026-07-27-cli-tag-access-design.md)；C6 已覆盖连接门控与非法十六进制输入，后续补完整引号/选项顺序矩阵和 Virtual Reader 命令级集成测试。

### 4. LLRP 2.0（最终阶段）

- 核验 2.0 Delta 是否已经能生成 V2_0 类型与 Codec。
- 新增 `Llrp20ProtocolAdapter`，先覆盖 Initialize、ROSpec/AccessSpec 映射和 TagReport 翻译的最小闭环。
- 扩展协商策略，明确 1.1 与 2.0 的最高共同版本选择逻辑。
- 增加 `--llrp 2.0` CLI 入口和互操作测试。

### 5. 扩展 Contributor（部分完成）

- `IReaderSettingsContributor` 和 `ITagReportContributor` 已挂到已激活的 Reader Extension；标准配置和 TagReport 保持可用。
- Impinj Settings Contributor 已完成只读查询：通过 `ImpinjRequestedData(All_Configuration)` 请求并投影厂商配置；写入必须等 Profile 与恢复流程完成后才开放。
- Inventory Compiler 已可在生成 ROSpec/ROReportSpec 时收集扩展贡献，并将身份、能力、协议版本交给 Contributor。Impinj 已建立默认拒绝的型号/固件能力表；R420 Model `2001002` Firmware `6.4.1.x` 已由 ItemTest 抓包验证支持 `ImpinjTagReportContentSelector`，SDK 已修正为将其挂到 `ROReportSpec`。
- R420 已启用 `ImpinjInventoryReportOptions` 并确认 `TagReport.Extensions` 的 `impinj.serializedTid`、`impinj.rfPhaseAngle` 与 `impinj.peakRssi` 端到端可见；下一步扩充其他型号/固件的能力目录。
- 为未安装、已安装未激活、已激活三种状态补回测试。

### 6. Virtual Reader（最终阶段）

- 已增加确定性 TagReport 生成，支撑 SDK 托管盘存与 Tag Access 读取的端到端测试；也已支持针对指定请求返回截断响应并关闭连接，用于接收循环协议错误测试。
- 已增加 AccessSpec 最小状态机、可配置标签、EPC 筛选和 User Memory 的 C1G2 Read/Write 模拟；下一步补配置流与故障注入。
- 已支持按请求消息类型静默丢弃响应、注入带描述的 LLRP 错误状态、截断响应并主动关闭连接；自动重连测试确认不会隐式恢复 ROSpec/AccessSpec。后续扩展全部 Virtual Reader 场景均放在项目最终阶段。
- 已接入 `Interop.Tests` 的 1.0.1 SDK 互操作测试；后续扩展到配置、写入和故障注入场景。

### 7. CLI 命令系统与提示链

- 详细规划见 [`architecture/cli-command-system.md`](architecture/cli-command-system.md)。
- 根 Spectre CLI 承载离线 `inspect/decode/validate/encode` 与唯一的一次性在线 `inventory`；其他在线业务收敛到 Live Shell。根级 `inventory <host>` 与 Live Shell 共用 Settings 工作流与 SDK，不复制配置逻辑。
- C2 已完成 Live Shell 的命令元数据收敛：Usage、`help <command>`、别名、连接可用性、输入候选和执行路由均从 `CommandCatalog` 获取。
- C3 已完成：`LiveSessionContext` 集中连接、监控与盘点状态；连接、盘点、监控和离线协议诊断分别由专用 Handler 处理，`LiveCommand` 保持为 Live Shell 宿主与路由层。
- `settings` 已取代 `config` 并接入 Live Shell；专家配置继续由 `raw transact` / `reader.Protocol` 处理。
- **CLI Settings 契约已稳定**：`settings show|edit|validate|apply|load|save|discard` 分离只读、本地草稿与设备写入；后续只扩展编辑器分组、选项或子命令，不改变现有语义。完整 JSON 仍是可移植、可自动化的高级表示。
- **后期 CLI 引导优化**：分组编辑器已覆盖天线/RF、Singulation、报告、Filters、触发器、Attached Data、Reader Configuration 与 Impinj 扩展。后续可补 `connect` 引导和更多厂商字段；`settings apply`、根级 `inventory`、Raw 和破坏性 Tag 操作继续要求 `--yes`。

### 8. Reader 默认配置 Profile

- 原默认配置/Profile 与 Patch 公开 API 已由统一 `ReaderSettings` 模型取代；后续 Profile 只能作为托管 Settings Contributor 的实现细节。
- 已定义可注册的 `IReaderConfigurationDefaultsProvider`；上下文包括厂商、型号、固件、协商版本、能力和激活扩展，最高优先级获选，同级冲突失败。
- 已完成 `ReaderConfigurationPatch` 的只读解析和显式 Apply 合并；下一步仅在有资料或实测依据后增加 Impinj 型号 Profile，不放入本轮 CLI 重构。

## 最终互操作验收

发布前必须按 [最终互操作验收标准](acceptance/reader-interoperability.md) 完成：LLRP 1.0.1 使用 Impinj R420/R700，LLRP 1.1 使用 Zebra FX9600，LLRP 2.0 使用 Virtual Reader。
