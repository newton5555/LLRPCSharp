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
| LLRP 1.1 | 可用基线 | SDK 支持自动协商、强制版本策略和对应 Adapter；真实 Reader 型号/固件覆盖仍需持续验证。 |
| LLRP 2.0 | 预留 | 有定义 Delta，但尚无 `Llrp20ProtocolAdapter`。 |
| 托管 Reader SDK | 可用 | `ReaderSettings`、校验、应用、托管盘点和报告流已接入。 |
| 标准 Tag Access | 可用 | 支持读、写、锁、销毁和块擦除。 |
| Impinj 扩展 | 主线可用 | 已有扩展注册、Settings/Inventory/TagReport 管道；消息级 4/4、参数级 47/104 有 SDK 路径，R420 实测通过核心能力。详见 [coverage/impinj-extension-coverage.md](coverage/impinj-extension-coverage.md)。 |
| CLI | 可用 | Live Shell、一次性 `inventory`、settings 草稿和离线 Codec 已稳定；实时命令可经 SDK 使用 1.0.1/1.1，离线标准 Codec 当前仅注册 1.0.1。 |
| Virtual Reader | 测试基线 | 覆盖核心 1.0.1 生命周期、报告和部分 AccessSpec 场景，不模拟真实射频。 |

## 已实现的应用能力

### 托管 Reader SDK

- `LlrpReader` 负责连接、协议协商、能力初始化和生命周期管理。
- `ReaderCapabilities` 暴露 LLRP 标准能力门控:`SupportsClientRequestOpSpec`
  （客户端请求式访问）与 `CanDoRfSurvey`（RF 调查）。SDK 本身不实现这两种
  访问模式，仅提供门控;R430 固件 6.4.1.240 实测两者均为 false。
- 收到设备主动关闭（`CLOSE_CONNECTION` 消息）时回 `CLOSE_CONNECTION_RESPONSE`
  并在 `ConnectionChanged` 事件携带 `DeviceInitiatedClose` 标记,应用可区分
  “设备主动关闭”与“网络故障”。主动断开仍直接关 TCP。
- `ReaderExceptionOccurred` 事件暴露 Reader 内部异常（ReaderExceptionEvent）:
  Message + ROSpec/SpecIndex/天线/AccessSpec 上下文,用于故障诊断。
- `TagReport.PcBits` 暴露标签 PC 字（C1G2_PC）:EPC 长度/编码类型信息,
  变长 EPC 场景必须依赖它;配合 `InventoryReportSettings.IncludePcBits` 请求。
- `ReaderSettings` 是托管配置模型；支持 Reader Defaults、Generic Defaults、
  查询事实、编辑、校验、应用、序列化和清理。
- Reader 级 `AntennaConfiguration` 查询和应用会完整保留 `RFTransmitter` 的
  `HopTableID`、`ChannelIndex` 与 `TransmitPower`，避免查询快照回写时把跳频表
  ID 降为零。
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
- `inspect` 只解析任意版本的 Header；`decode`/`validate`/`encode` 的标准消息
  Codec 当前仅注册 LLRP 1.0.1，1.1 消息主体会显示为 `UnknownMessage` 或暂不支持构造。

### LLRP 1.1 Reader 连接边界

- `Auto` 连接先建立 TCP，再用 LLRP 1.1 `GET_SUPPORTED_VERSION` 查询；协商成功后
  发送 `SET_PROTOCOL_VERSION(1.1)` 并切换到 1.1 Adapter，明确不支持时回退 1.0.1。
- `Force101` 跳过版本协商；`Force11` 协商失败、超时或 Reader 不支持时连接初始化失败，
  不会静默回退到 1.0.1。
- 连接成功后，标准 Settings、Inventory、TagReport 和 Tag Access 使用协商版本的
  消息/参数类型；1.1 的真实设备互操作尚未由当前自动化测试完整覆盖。
- 重连成功后，SDK 会自动查询设备当前的 ROSpec/AccessSpec 状态并对齐内部状态：
  若 SDK 托管 ROSpec 仍在则保留会话继续接收报告；若已丢失（如设备重启清空配置）
  则结束旧会话并回到 Idle，由应用显式重建期望状态。只对齐设备现状，不会重放
  应用之前的期望配置。
- 重连后若设备配置了 `HoldEventsAndReportsUponReconnect=true`，SDK 会在状态同步
  完成后发送 `ENABLE_EVENTS_AND_REPORTS` 释放被挂起的事件/报告（内部逻辑，不新增
  公开 API；hold 未配置时不发送）。已由 `LlrpSdk.Hardware.Tests` 真机验证
  （Impinj R430 固件 6.4.1.240）。

### 协议与扩展

- 协议定义通过 XML/YAML 导入、校验和生成器维护，生成的 `.g.cs` 不手工编辑。
- `UseImpinj()` 提供 Impinj 扩展入口；扩展值通过强类型 Contributor 接入托管
  Settings 和 TagReport。
- `ILlrpFrameObserver`、日志和连接事件可用于诊断与监控。

## 当前缺口

- LLRP 1.0.1 完成度审计:协议层 42 消息 / 111 参数全覆盖,SDK 层 42/42 消息
  有业务路径或已定案（`CLIENT_REQUEST_OP` 经实机实测设备不支持,SDK 不接线、
  仅提供 `SupportsClientRequestOpSpec` 门控）;`ReaderExceptionEvent` 已暴露为
  `ReaderExceptionOccurred`,`TagReport.PcBits` 已投影。详见
  [coverage/llrp101-sdk-coverage.md](coverage/llrp101-sdk-coverage.md)。
- 没有 LLRP 2.0 Adapter 和完整互操作闭环。
- 其他厂商/型号/固件的扩展能力目录仍需按实测证据补充。
- Virtual Reader 不模拟真实射频、跨进程持久化或全部设备配置行为。
- 实机验收范围仍小于自动化测试覆盖范围。

## 验证状态

截至基准日期，解决方案构建为零警告、零错误，测试项目全部通过，共 399 项。
发布前仍需按 [互操作验收标准](acceptance/reader-interoperability.md) 验证目标
设备组合。
