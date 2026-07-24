# 项目文档与当前源码偏移及下一步计划

> 基准日期：2026-07-24
> 目的：把现有规划文档、README 与当前源码状态之间的偏移集中记录，作为下一轮开发的入口清单。

## 1. 当前结论

当前源码已经超过根目录 `README.md` 的“当前下一步”描述：M3/M4/M6/M7/M8 都已有部分实现，而 README 仍停留在“正在推进 M3 核心盘点路径与 M5 协议定义/生成链”的表述。整体规划文档更接近真实状态，但仍混有 API 草案、阶段目标和已实现内容，下一步开发前应先把“已完成基线”和“仍待开发项”分开。

当前最高优先级不是继续扩功能，而是先恢复仓库可构建状态：`LlrpSdk.Extensions.Impinj` 中存在重复生成类型，导致 `dotnet build LLRPCSharp.slnx --no-restore` 失败。这个问题会阻塞后续任何可验证开发。

## 2. 已实现但文档滞后的部分

### M3/M4：盘点与资源服务

源码中已经存在以下能力：

- `LlrpReader.StartAsync(ReaderSettings)`、`StopAsync()`、`InventoryAsync(ReaderSettings?)`。
- `ReadTagReportsAsync()` 与 `TagsReported`，并共用 TagReport 翻译结果。
- `ReaderSettings` 已作为版本无关的盘点意图模型存在。
- `IRoSpecService` 已提供 Add/Delete/Enable/Disable/Start/Stop/GetAll。
- `IAccessSpecService` 已提供 Add/Delete/Enable/Disable/GetAll。
- Raw Protocol 操作后会使托管状态失效，并通过 `SynchronizeStateAsync()` 恢复可继续 Managed 操作的状态。

偏移：根 README 的“当前下一步”仍把 ROSpec 参数图、Reader Settings、RO_ACCESS_REPORT/TagData 管道作为待完成项，实际应该改成“已完成基础路径，仍需完善 Settings 查询/应用、标签访问、AccessSpec 高层构造和真机回归”。

### M6：1.1 协商与 Adapter

源码中已经存在：

- `Llrp101ProtocolAdapter` 与 `Llrp11ProtocolAdapter`。
- `LlrpProtocolVersionPolicy.Auto`、`Force101`、`Force11`。
- `ConnectAsync()` 内部执行 1.1 的 `GET_SUPPORTED_VERSION` / `SET_PROTOCOL_VERSION` 协商，并可回退 1.0.1。
- CLI 解析 `--llrp auto|1.0.1|1.1` 对应策略。

偏移：文档中“1.1 与 2.0”容易被读成同一完成度。实际状态是 1.1 可用基线已接入，2.0 仍未接入 Adapter 和协商。

### M7：扩展生命周期

源码中已经存在：

- `ILlrpProtocolModule`、`UseProtocolModule(...)`。
- `IReaderExtension`、`UseReaderExtension(...)`、`reader.Extensions`。
- `UseImpinj()` 扩展入口。
- Reader Extension 基于 Manufacturer/Model/Firmware/ProtocolVersion 匹配，并检查互斥组冲突。

偏移：文档对扩展架构的目标描述基本正确，但下一步应明确“协议模块和 Reader Extension 生命周期已存在；Settings/TagReport Contributor、厂商 CLI 命令、真实型号/固件匹配规则仍未完成”。

### M8：可靠性与虚拟设备

源码中已经存在：

- `LlrpAutomaticReconnectOptions` 和 `WithAutomaticReconnect(...)`。
- 意外断线后的有限自动重连。
- `LlrpFrameJournal` 诊断基线。
- `LlrpVirtualReader` 1.0.1 TCP Server，支持能力查询与 ROSpec 生命周期。

偏移：整体规划中已说明自动重连不恢复 ROSpec/AccessSpec/托管盘点，但 README 没有体现。下一步开发应把“有限重连”和“期望状态恢复”拆开处理，避免误认为自动恢复已完成。

## 3. 文档中仍是目标但源码未完成的部分

### 3.1 Settings 查询与应用

规划中出现 `QuerySettingsAsync` 与 `ApplySettingsAsync`，但 `LlrpReader` 当前没有这两个公开方法。`ReaderSettings` 目前主要用于编译托管盘点 ROSpec，不等同于完整 Reader Config 查询/应用模型。

下一步任务：

1. 定义 `ReaderSettings` 当前范围：只表示 Inventory 设置，还是升级为完整 Reader Config 聚合模型。
2. 若保留轻量模型，新增独立的 `ReaderConfiguration` 或 `ReaderConfigSnapshot`，避免 `ReaderSettings` 承担过多语义。
3. 实现 `QuerySettingsAsync` / `ApplySettingsAsync` 时走 Adapter，避免把版本化 Message 暴露到应用层。
4. CLI 增加 `config get` / `config apply` 的最小可用路径。

### 3.2 标签访问 API

规划中列出 `ExecuteTagAccessAsync`、`ReadTagMemoryAsync`、`WriteTagMemoryAsync`，源码目前没有这些 API。AccessSpec 服务只是进阶资源生命周期操作，不是面向普通业务的标签访问能力。

下一步任务：

1. 先定义版本无关的 `TagAccessRequest` / `TagAccessResult` / `ReadTagRequest` / `WriteTagRequest`。
2. 将 AccessSpec 高层构造放入 Adapter 或 Compiler，避免业务层构造生成参数图。
3. CLI 增加 `tag read` / `tag write` 前先支持 dry-run 或 inspect 输出，降低真机风险。

### 3.3 LLRP 2.0

仓库已有 `definitions/llrp-2.0-delta.yaml`，但源码中没有 `Llrp20ProtocolAdapter`，`LlrpReader` 初始化的 Adapter 列表也只有 1.0.1 与 1.1。

下一步任务：

1. 核验 2.0 Delta 是否已经能生成 V2_0 类型与 Codec。
2. 新增 `Llrp20ProtocolAdapter`，先覆盖 Initialize、ROSpec/AccessSpec 映射和 TagReport 翻译的最小闭环。
3. 扩展协商策略，明确 1.1 与 2.0 的最高共同版本选择逻辑。
4. 增加 `--llrp 2.0` CLI 入口和互操作测试。

### 3.4 扩展 Contributor

文档描述了 Settings Contributor 与 TagReport Contributor 管道，但源码当前只看到协议模块注册和 Reader Extension 激活，尚未形成标准 Contributor 接口。

下一步任务：

1. 设计 `IReaderSettingsContributor` 和 `ITagReportContributor`，挂到已激活的 Reader Extension。
2. 让 Inventory Compiler 在生成 ROSpec/ReportSpec 时收集扩展贡献。
3. 让 TagReport 翻译后再由扩展补充 `TagData.Extensions`。
4. 为未安装、已安装未激活、已激活三种状态补回测试。

### 3.5 Virtual Reader 场景覆盖

当前 Virtual Reader 支持能力查询和 ROSpec 生命周期，但不支持报告生成、AccessSpec、故障注入和脚本化场景。

下一步任务：

1. 增加可配置 TagReport 生成，支撑 `InventoryAsync` 的端到端测试。
2. 增加 AccessSpec 最小状态机。
3. 增加断线、超时、错误状态码、非法帧等故障注入。
4. 将 Virtual Reader 接入 CI 互操作测试。

## 4. 当前构建阻塞

`dotnet build LLRPCSharp.slnx --no-restore` 当前失败在 `src/LlrpSdk.Extensions.Impinj`。错误集中表现为重复类型定义，例如：

- `ImpinjEnableEnhancedIntegraCodec`
- `ImpinjEnhancedIntegraReportCodec`
- `IMPINJ_ENABLE_EXTENSIONSCodec`
- `IMPINJ_ENABLE_EXTENSIONS_RESPONSECodec`
- `IMPINJ_SAVE_SETTINGSCodec`
- `IMPINJ_SAVE_SETTINGS_RESPONSECodec`
- `ImpinjEnhancedIntegraMode`
- `ImpinjEnhancedIntegraResultType`

初步判断：Impinj 生成输出中同名定义被写入了多组编号文件，可能来自原始 XML 重复定义、生成器去重策略不足，或旧生成文件未清理。

下一步任务：

1. 对 `LlrpSdk.Extensions.Impinj` 生成文件按类型名建立索引，确认重复来源。
2. 在 ProtocolModel Validator 或 Generator 写入前增加同名类型冲突诊断。
3. 明确 Impinj 原始定义中同 subtype 或同 name 的处理规则。
4. 清理重复生成输出后，重新运行 `dotnet build LLRPCSharp.slnx --no-restore`。

## 5. 建议开发顺序

1. 恢复构建：优先修复 Impinj 生成重复类型。
2. 更新入口文档：同步 README 的当前状态和下一步，避免继续引用过期 M3/M5 计划。
3. 固化当前 SDK 基线：为 1.0.1/1.1 的 Start/Stop/Inventory、ROSpec、AccessSpec、Raw sync 补足回归测试。
4. 完成 Settings 查询/应用：先做最小 Reader Config 闭环，再扩展 CLI。
5. 完成标签访问 API：以 AccessSpec 高层构造为核心，不暴露版本化 Message。
6. 扩展 Contributor 管道：让厂商设置贡献和报告增强真正进入 SDK 高级 API。
7. 推进 Virtual Reader：补 TagReport、AccessSpec 和故障注入，用于 CI。
8. 接入 LLRP 2.0：在 1.0.1/1.1 基线稳定后再加入 V2 Adapter。

## 6. 需要同步修订的文档

- `README.md`：更新“当前实现”和“当前下一步”，明确 M3/M4/M6/M7/M8 的真实进度。
- `docs/LLRP-CSharp-SDK-整体规划.md`：保留大规划，但把 API 草案、已实现内容、待实现内容分段标记。
- `docs/architecture/source-structure.md`：补充 `LlrpVirtualReader`、`AccessSpecService`、`ReaderExtensionCollection`、`AutomaticReconnect` 等当前源码组成。
- `docs/architecture/protocol-extension-guide.md`：去掉或标记尚不存在的动态 YAML 运行时装载示例，避免误导。
