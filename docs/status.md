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
| LLRP 2.0 | 协议层+SDK 适配器基线 | `V2_0` 类型/Codec/`Llrp20StandardModule` 已生成(433 文件,可复现);`Llrp20ProtocolAdapter` 六个切片已实现,`Auto`/`Force20` 协商接入,往返/翻译/事件投影/协商测试通过;未实机验收。 |
| Zebra 扩展 | 协议层+SDK 扩展基线(可信度存疑) | `LlrpNet.Protocol.Zebra` 线协议包(159 文件,可复现)+ `LlrpSdk.Extensions.Zebra`(`UseZebra()`、设置/报告选项/相位·GPS·XPC 投影,最小子集)。FX9600(161/96008,固件 3.32.37.0)真机已验证:连接、8 个能力参数强类型解码、配置查询、`zebra.configuration` 往返、`MotoTagPhase`/`BrandIDCheckStatus` 报告投影。官方 ICG 二进制页 `reserved` 位数与固件字节系统性偏移,已按抓包修正部分参数,其余报告/盘点参数(GPS/XPC/zone 等)仍缺实机字节级验证,需逐参数抓包标定。 |
| 托管 Reader SDK | 可用 | `ReaderSettings`、校验、应用、托管盘点和报告流已接入。 |
| 标准 Tag Access | 可用 | 支持读、写、锁、销毁和块擦除。 |
| Impinj 扩展 | 主线可用 | 已有扩展注册、Settings/Inventory/TagReport 管道；消息级 4/4、参数级 47/104 有 SDK 路径，R420 实测通过核心能力。详见 [coverage/impinj-extension-coverage.md](coverage/impinj-extension-coverage.md)。 |
| CLI | 可用 | Live Shell、一次性 `inventory`、简化 Settings 应用流程和离线 Codec 已稳定；实时命令可经 SDK 使用 1.0.1/1.1，离线标准 Codec 当前仅注册 1.0.1。 |
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
- `StartInventoryAsync()` 返回独立的 `InventorySession`。报告出口按一次盘点内的
  首次消费者互斥选择：`InventorySession.ReadReportsAsync()` 只接收托管盘点报告，
  或 `TagsReported` / `ReadTagReportsAsync()` 选择连接级观察；同一生命周期内混用会
  立即抛出 `InvalidOperationException`。托管独占模式下，即使设备按报告选择器省略可选的
  `ROSpecID`，Session 仍会接收没有冲突 `AccessSpecID` 的标签报告。Tag Access 使用 SDK
  内部等待器，不会抢占公开回调。
- SDK 管理保留的 ROSpec/AccessSpec 资源；应用设置后保持停止，显式启动后才
  开始盘点。
- 部署契约：带 Inventory 意图的 `ApplySettingsAsync` 或
  `StartInventoryAsync(settings)` 会先删除设备上全部 ROSpec/AccessSpec
  （LLRP id=0 语义）再部署，即 SDK 完全接管设备资源配置；共享设备请用
  两段式（先部署，后 `StartInventoryAsync()` 仅启动）。Raw Protocol 或手工资源
  操作后，带配置的上述入口也是显式强制接管入口，无需先调用
  `SynchronizeStateAsync()`；无参启动仍要求已有托管状态已同步。
- 标签访问 API 复用同一资源生命周期，不要求应用手写 AccessSpec。

### CLI

- Live Shell 提供 `connect`、`status`、`caps`、`settings`、`inventory` 和
  `tag` 等操作。
- `inventory start` 在新连接尚未读取托管资源时会先查询 `14150` ROSpec；因此设备上
  已保留的高层盘点资源可以直接启动，实际扫描范围仍以该 ROSpec 的 `AntennaIDs`
  为准。
- `settings show|edit|validate|apply|save` 提供简化的设置查看、编辑、校验和写入；
  `apply --defaults --yes` 吸收原 `settings defaults`，`validate` 承担原 `settings load`
 的"载入+校验"（`defaults`/`load` 子命令已移除，不保留别名）。`settings apply [--defaults|<file>] --yes`
  是唯一显式批量应用入口，不维护 CLI 草稿状态。Raw/手工资源操作后的 CLI 链路为：
- Live Shell 的 `status` 默认只显示连接和托管生命周期状态；`status --full` 会刷新
  Settings、ROSpec 和 AccessSpec 并展示参数树。`caps` 会重新执行
  `GET_READER_CAPABILITIES(All)`，默认展示归一化能力表，`--raw` 展示完整响应参数树，
  `--json` 输出脚本友好的归一化数据。
- `settings edit` 可编辑 Reader 级 HoldEventsAndReports、Keepalive、事件通知、天线
  RF 索引，以及既有 Inventory 的基础盘点、报告常用字段、过滤器新增、触发器、AttachedData
  和厂商扩展；Priority、InventoryParameterSpecId、报告扩展字段、过滤器动作和周期
  StartAtUtc 不开放交互编辑。
  天线 RF 索引采用单组交互，并同步写入 Reader 默认配置与托管 Inventory ROSpec。
  编辑菜单支持预览、连接设备能力校验及应用前影响提示和二次确认；批量写入统一通过
  `settings apply [--defaults|<file>] --yes`（校验后直接下发，无重复校验）。
  `sync` 查询并采用设备现状，或使用带 `Inventory` 的 `settings apply <file> --yes` /
  `settings apply --defaults --yes` 强制接管；`inventory start` 只在状态已同步或接管完成后执行。
  `inventory start --defaults|--settings <file>` 提供一段式部署+启动。
- 根级一次性 `inventory <host>` 与 Live Shell 共用 SDK 和 Settings 工作流，
  默认输出适合 Agent 使用的 JSON。
- 根级在线一次性 `status <host>` 与 `caps <host>`（均支持 `--llrp`/`--vendor`/`--output json|table`）
  连接→查询身份/协商/扩展或 GET_READER_CAPABILITIES→自动断开，默认 JSON 输出，供 Agent 在线冒烟。
- `inspect`、`decode`、`validate`、`encode` 为离线协议诊断命令，`--llrp` 支持
  auto/1.0.1/1.1/2.0 版本感知；离线 Codec 已注册 1.0.1/1.1/2.0、Impinj 与 Zebra 模块。
- `encode` 支持 `--llrp` 与 `--requested-data`，消息目录单源（`Helpers.CreateEncodeMessage`）。
- 实时命令 `--llrp`/`--vendor` 现支持 2.0 与 zebra（`"2"` 映射修正为 Force20）。
- `tag sequence` 支持结构化 `--read/--write/--erase/--lock/--kill` 旗标。
- 离线工具消重：standalone 与 Live Shell 的 `inspect`/`decode`/`validate` 合并到 `OfflineProtocolTool` 共享核，
  两个入口只做参数适配；`decode` 的 standalone `--output json` 分支保留；`encode` 消息构造已单源。
  删除了未接线的死代码 `LlrpFrameAnalyzer`（语义分析半成品，从未被调用）。

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
- `ILlrpProtocolAdapter` 是 SDK 的唯一版本边界:前向编译/翻译、反向反解析
  (`ParseManagedRoSpec`)、事件投影、标准消息构造/分类与连接前版本协商全部
  收敛在版本边界内;`LlrpReader` 门面零版本类型引用(机器强制)。
- `UseImpinj()` 提供 Impinj 扩展入口；扩展值通过强类型 Contributor 接入托管
  Settings 和 TagReport。
- `ILlrpFrameObserver`、日志和连接事件可用于诊断与监控。

## 当前缺口

- LLRP 1.0.1 完成度审计:协议层 42 消息 / 111 参数全覆盖,SDK 层 42/42 消息
  有业务路径或已定案（`CLIENT_REQUEST_OP` 经实机实测设备不支持,SDK 不接线、
  仅提供 `SupportsClientRequestOpSpec` 门控）;`ReaderExceptionEvent` 已暴露为
  `ReaderExceptionOccurred`,`TagReport.PcBits` 已投影。详见
  [coverage/llrp101-sdk-coverage.md](coverage/llrp101-sdk-coverage.md)。
- 2.0 适配器已实现(编译/反解析/事件投影/协商),但没有实机互操作闭环。
- Zebra SDK 扩展已实现(最小子集);FX9600(161/96008,固件 3.32.37.0,LLRP 1.0.1)真机已验证:8 个能力参数强类型解码、
  报告能力 `MotoTagPhase`/`BrandIDCheckStatus` 按门控开启并投影(如 `zebra.phase`),设备不支持 1.1(实测
  `GET_SUPPORTED_VERSION` 回 `M_UnsupportedVersion`,与 ICG 1.0.1 基线一致)。
- **Zebra 定义可信度风险(未闭环)**：官方 ICG(72E-131718-13EN)二进制页的 `reserved` 位数与固件实际字节
  **系统性偏移**(PDF 多算 24 位),已按实机抓包修正 8 个能力参数 + `MotoTagPhase`/`BrandIDCheckStatus`;
  但其余报告/盘点参数(如 `MotoTagGPS`、`MotoC1G2ExtendedPC`、`MotoTagReportContentSelector` 的其余标志、
  `MotoZoneInfo` 等)仍**缺乏实机字节级验证**。官方 PDF 与 `zebra.yml` 已出现偏差,不能把任何单一来源
  (PDF/SDK/yml)当权威——需逐参数抓包验证 `reserved`/字段宽度后才能在 `zebra.yml` 标定,并补 round-trip 测试。
- LLRP 2.0 与 Zebra 的 CLI 离线/实时命令已接线，但 2.0 的真实设备互操作仍需实机验收。
- 其他厂商/型号/固件的扩展能力目录仍需按实测证据补充。
- Virtual Reader 不模拟真实射频、跨进程持久化或全部设备配置行为。
- 实机验收范围仍小于自动化测试覆盖范围。

## 验证状态

截至基准日期，解决方案构建为零警告、零错误，测试项目全部通过，共 473 项。
生成管线经 `--verify` 逐字节复现验证:1.0.1 XML(364 文件)、1.1 YAML(395 文件)、
Impinj XML(267 文件)、2.0 delta(433 文件)、Zebra YAML(159 文件)均与已提交产物一致。
2026-08-14 适配器边界重构后真机复验:标准设备 `192.168.40.88` 6/6、
Impinj R420 `192.168.40.87` 12/12 通过(证据见
[acceptance/reader-interoperability.md](acceptance/reader-interoperability.md))。
其中 `LlrpSdk.Tests` 含版本边界守护测试(`ArchitectureGuardTests`):`LlrpReader`
源码零版本类型引用,以及 1.0.1/1.1 往返与等价测试(编译→反解析往返、报告翻译
双版本等价、ROSpec 线字节等价、事件投影双版本等价)。
发布前仍需按 [互操作验收标准](acceptance/reader-interoperability.md) 验证目标
设备组合。
