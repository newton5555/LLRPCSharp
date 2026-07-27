# 路线图

> 基准日期：2026-07-24  
> 目的：记录下一步开发顺序。当前真实状态见 `status.md`。

## 当前优先级

1. 完成标签访问 API：以 AccessSpec 高层构造为核心，不暴露版本化 Message。
2. 扩展 Contributor 管道：让厂商设置贡献和报告增强真正进入 SDK 高级 API。
3. 推进 Virtual Reader：补 TagReport、AccessSpec 和故障注入，用于 CI。
4. 接入 LLRP 2.0：在 1.0.1/1.1 基线稳定后再加入 V2 Adapter。

## 任务拆分

### 1. 恢复构建（已完成）

- 对 `LlrpSdk.Extensions.Impinj` 生成文件按类型名建立索引，确认重复来源。
- 在 ProtocolModel Validator 或 Generator 写入前增加同名类型冲突诊断。
- 明确 Impinj 原始定义中同 subtype 或同 name 的处理规则。
- 清理重复生成输出后，重新运行 `dotnet build LLRPCSharp.slnx --no-restore`。

### 2. Reader 配置查询与应用（已完成）

- 定义 `ReaderSettings` 当前范围：只表示 Inventory 设置，还是升级为完整 Reader Config 聚合模型。
- 若保留轻量模型，新增独立的 `ReaderConfiguration` 或 `ReaderConfigSnapshot`。
- 实现 `QuerySettingsAsync` / `ApplySettingsAsync` 时走 Adapter，避免把版本化 Message 暴露到应用层。
- CLI 增加 `config get` / `config apply` 的最小可用路径。

### 3. 标签访问 API

- 定义版本无关的 `TagAccessRequest` / `TagAccessResult` / `ReadTagRequest` / `WriteTagRequest`。
- 将 AccessSpec 高层构造放入 Adapter 或 Compiler。
- CLI 增加 `tag read` / `tag write` 前先支持 dry-run 或 inspect 输出，降低真机风险。

### 4. LLRP 2.0

- 核验 2.0 Delta 是否已经能生成 V2_0 类型与 Codec。
- 新增 `Llrp20ProtocolAdapter`，先覆盖 Initialize、ROSpec/AccessSpec 映射和 TagReport 翻译的最小闭环。
- 扩展协商策略，明确 1.1 与 2.0 的最高共同版本选择逻辑。
- 增加 `--llrp 2.0` CLI 入口和互操作测试。

### 5. 扩展 Contributor

- 设计 `IReaderSettingsContributor` 和 `ITagReportContributor`，挂到已激活的 Reader Extension。
- 让 Inventory Compiler 在生成 ROSpec/ReportSpec 时收集扩展贡献。
- 让 TagReport 翻译后再由扩展补充 `TagData.Extensions`。
- 为未安装、已安装未激活、已激活三种状态补回测试。

### 6. Virtual Reader

- 增加可配置 TagReport 生成，支撑 `InventoryAsync` 的端到端测试。
- 增加 AccessSpec 最小状态机。
- 增加断线、超时、错误状态码、非法帧等故障注入。
- 将 Virtual Reader 接入 CI 互操作测试。

### 7. CLI 命令系统与提示链

- 详细规划见 [`architecture/cli-command-system.md`](architecture/cli-command-system.md)。
- 保留 Spectre 外层 CLI 与 Live Shell 两种宿主，逐步建立共享命令定义和业务 Handler。
- 已完成第一步连接选项对齐：外层 `connect` / `monitor` 与 Live Shell `connect` 共享 LLRP/Vendor 策略解析。
- 下一步让 Usage、Help、选项解析、连接状态约束和输入候选来自同一份命令元数据。
- 提取 `LiveSessionContext`，逐步拆分 `LiveCommand` 中的连接、监控、盘点和渲染职责。
- 在 SDK API 稳定后，将 `config` 与 `tag` 命令安全地接入 Live Shell。

### 8. Reader 默认配置 Profile

- 作为 SDK 配置模型升级能力推进，不放入本轮 CLI 重构。
- 设计 `GetDefaultConfiguration` / `QueryDefaultSettingsAsync` 之类入口前，先明确“离线默认配置”“设备当前配置”“持久化配置”的边界。
- Profile 匹配应至少包含厂商、型号、固件范围和依赖的 LLRP 标准版本；厂商扩展可以建立在某个标准版本之上。
