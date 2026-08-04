# 当前状态

> 基准日期：2026-08-03

本文档只记录当前实现事实。开发计划见 [roadmap.md](roadmap.md)，用户入口见
根目录 [README](../README.zh.md)。

## 总结

项目当前提供三层能力：

- `LlrpNet`：LLRP 1.0.1/1.1 的协议模型、编解码、传输和扩展基础。
- `LlrpSdk`：面向应用的托管 `LlrpReader`，包含连接、Settings、Inventory、
  TagReport 和标准 Tag Access。
- `LlrpCli`：Live Shell、一次性 `inventory`、标签操作和离线协议工具。

当前主线是 LLRP 1.0.1 与 Impinj 扩展的设备闭环完善；构建和自动化测试通过
不等同于所有型号的实机验收。

## 支持矩阵

| 能力 | 状态 | 说明 |
|---|---|---|
| LLRP 1.0.1 | 可用 | SDK、CLI、Virtual Reader 和主要标准资源/标签操作已覆盖。 |
| LLRP 1.1 | 可用基线 | 支持协商、强制版本策略和对应 Adapter；设备覆盖仍需持续验证。 |
| LLRP 2.0 | 预留 | 有定义 Delta，但尚无 `Llrp20ProtocolAdapter`。 |
| 托管 Reader SDK | 可用 | `ReaderSettings`、校验、应用、托管盘点和报告流已接入。 |
| 标准 Tag Access | 可用 | 支持读、写、锁、销毁和块擦除。 |
| Impinj 扩展 | 主线可用 | 已有扩展注册、Settings/Inventory/TagReport 管道；能力目录仍需扩充。 |
| CLI | 可用 | Live Shell、一次性 `inventory`、settings 草稿和离线 Codec 已稳定。 |
| Virtual Reader | 测试基线 | 覆盖核心 1.0.1 生命周期、报告和部分 AccessSpec 场景，不模拟真实射频。 |

## 已实现的应用能力

### 托管 Reader SDK

- `LlrpReader` 负责连接、协议协商、能力初始化和生命周期管理。
- `ReaderSettings` 是托管配置模型；支持 Reader Defaults、Generic Defaults、
  查询事实、编辑、校验、应用、序列化和清理。
- `StartInventoryAsync()` 返回独立的 `InventorySession`；
  `TagsReported` 和 `ReadTagReportsAsync()` 可观察连接级报告。
- SDK 管理保留的 ROSpec/AccessSpec 资源；应用设置后保持停止，显式启动后才
  开始盘点。
- 部署契约：带 Inventory 意图的 `ApplySettingsAsync` 或
  `StartInventoryAsync(settings)` 会先删除设备上全部 ROSpec/AccessSpec
  （LLRP id=0 语义）再部署，即 SDK 完全接管设备资源配置；共享设备请用
  两段式（先部署，后 `StartInventoryAsync()` 仅启动）。
- 标签访问 API 复用同一资源生命周期，不要求应用手写 AccessSpec。

### CLI

- Live Shell 提供 `connect`、`status`、`caps`、`settings`、`inventory` 和
  `tag` 等操作。
- `settings show|edit|validate|apply|load|save|discard` 区分设备事实、本地草稿
  和写入动作。
- 根级一次性 `inventory <host>` 与 Live Shell 共用 SDK 和 Settings 工作流，
  默认输出适合 Agent 使用的 JSON。
- `inspect`、`decode`、`validate`、`encode` 为离线协议诊断命令。

### 协议与扩展

- 协议定义通过 XML/YAML 导入、校验和生成器维护，生成的 `.g.cs` 不手工编辑。
- `UseImpinj()` 提供 Impinj 扩展入口；扩展值通过强类型 Contributor 接入托管
  Settings 和 TagReport。
- `ILlrpFrameObserver`、日志和连接事件可用于诊断与监控。

## 当前缺口

- 没有 LLRP 2.0 Adapter 和完整互操作闭环。
- 其他厂商/型号/固件的扩展能力目录仍需按实测证据补充。
- Virtual Reader 不模拟真实射频、跨进程持久化或全部设备配置行为。
- 自动重连不会自动恢复应用之前的 ROSpec、AccessSpec 或托管盘点状态。
- 实机验收范围仍小于自动化测试覆盖范围。

## 验证状态

截至基准日期，解决方案构建为零警告、零错误，测试项目全部通过，共 399 项。
发布前仍需按 [互操作验收标准](acceptance/reader-interoperability.md) 验证目标
设备组合。
