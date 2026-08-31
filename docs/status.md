# 当前状态

> 基准日期：2026-08-31

本文档只记录当前实现事实。开发计划见 [roadmap.md](roadmap.md)，用户入口见
根目录 [README](../README.zh.md)。

## 总结

项目当前提供四个相互协作的能力域：

- `LlrpNet`：LLRP 1.0.1/1.1/2.0 的协议模型、编解码、传输和扩展基础；2.0
  已有独立版本类型、Codec 和 Adapter 基线，真实设备互操作仍单列验收。
- `LlrpSdk`：面向应用的托管 `LlrpReader`，包含连接、Settings、Inventory、
  TagReport 和标准 Tag Access。
- `LlrpDevice.Abstractions`、`LlrpDevice.Server`、`LlrpDevice.Virtual`、
  `LlrpDevice.Virtual.Hosting`：版本中立设备合同、通用 LLRP 设备端服务、确定性 Virtual
  设备实现和公开生命周期门面。
- `LlrpCli`：通用客户端 Live Shell、一次性 `inventory`、标签操作和离线协议工具；
  `LlrpVirtualDevice.Cli`：虚拟设备的独立服务 CLI。

当前已完成 LLRP 1.0.1 与 Impinj 扩展的设备闭环，并提供 1.1/2.0 与 Zebra 的可用基线；
构建和自动化测试通过不等同于所有型号和协议版本的实机验收。

## 支持矩阵

| 能力 | 状态 | 说明 |
|---|---|---|
| LLRP 1.0.1 | 可用 | SDK 客户端与通用设备端 Server/Virtual 已完成对齐闭环；Virtual 标准默认设备暴露 4 根逻辑天线，并内置基于实机采集的 RF capability tables（Tx Index 1..193、2 项 Rx、41 项 RF Mode、16 个跳频点）；客户端未接线的 CLIENT_REQUEST_OP/RF Survey 仍按能力门控处理。详见 [设备端对齐表](coverage/llrp101-device-server-coverage.md)。 |
| LLRP 1.1 | 可用基线 | SDK 支持自动协商、强制版本策略和对应 Adapter；真实 Reader 型号/固件覆盖仍需持续验证。 |
| LLRP 2.0 | 协议层+SDK 适配器基线 | `V2_0` 类型/Codec/`Llrp20StandardModule` 已生成(433 文件,可复现);`Llrp20ProtocolAdapter` 六个切片已实现,`Auto`/`Force20` 协商接入,往返/翻译/事件投影/协商测试通过;未实机验收。 |
| Zebra 扩展 | 协议层+SDK 扩展基线(可信度存疑) | `LlrpNet.Protocol.Zebra` 线协议包(159 文件,可复现)+ `LlrpSdk.Extensions.Zebra`(`UseZebra()`、设置/报告选项/相位·GPS·XPC 投影,最小子集)。FX9600(161/96008,固件 3.32.37.0)真机已验证:连接、8 个能力参数强类型解码、配置查询、`zebra.configuration` 往返、`MotoTagPhase`/`BrandIDCheckStatus` 报告投影。官方 ICG 二进制页 `reserved` 位数与固件字节系统性偏移,已按抓包修正部分参数,其余报告/盘点参数(GPS/XPC/zone 等)仍缺实机字节级验证,需逐参数抓包标定。 |
| 托管 Reader SDK | 可用 | `ReaderSettings`、校验、应用、托管盘点和报告流已接入。 |
| 标准 Tag Access | 可用 | 支持读、写、锁、销毁和块擦除。 |
| Impinj 扩展 | 主线可用 | 已有扩展注册、Settings/Inventory/TagReport 管道；消息级 4/4、参数级 47/104 有 SDK 路径，R420 实测通过核心能力。详见 [coverage/impinj-extension-coverage.md](coverage/impinj-extension-coverage.md)。 |
| CLI | 可用 | Live Shell、一次性 `inventory`、Settings 应用流程和离线 Codec 已稳定；实时与离线命令均按显式版本支持 1.0.1/1.1/2.0，离线注册表还包含 Impinj 与 Zebra 扩展。 |
| Virtual Device | SDK 门面 + 独立设备端 CLI 已可用 | `LlrpDevice.Virtual.Hosting` 提供稳定的 `IVirtualDeviceHost`、`VirtualDeviceHostOptions` 和 `VirtualLlrpDeviceHost.Create(...)`，组合 `LlrpDevice.Server` 与 `VirtualLlrpDevice`，支持 Start/Stop/Restart、端点、客户端状态、解码报文事件，以及启动前注入标签。旧 `IVirtualLlrpDeviceHost`/底层属性保留为迁移兼容路径。内置 `llrp1.0.1_standard` 与 `impinj.r420.llrp-1.0.1` profile；后者由 `LlrpDevice.Virtual.Impinj` 提供 Impinj 能力/配置扩展。`LlrpVirtualDevice.Cli` 位于 `src` 根下，与客户端 `LlrpCli` 平级，支持默认交互 Shell、`server create/start/stop/restart/status/destroy` 生命周期命令、`run`/`live`/`validate`/`presets`、1.0.1/1.1/2.0、版本化 JSON、确定性 RF 和标准 Tag Access。Hosting/CLI 默认采用宽松 ROSpec 状态检查，`--strict` 可切换严格校验。1.0.1 标准默认设备暴露 4 根逻辑天线，RF capability tables 基于实机采集（Tx Index 1..193、Rx Index 1..2、41 项 RF Mode、16 个跳频点）。`live` 会自动创建/启动设备并进入 Shell，默认输出生命周期、客户端和 `RX/TX` 报文，但不会自己生成 LLRP 客户端指令。旧 `LlrpCli virtual-reader` 命令已移除。真实 RFID 模块/真实 RF 波形模拟、运行态重启自动恢复仍未交付。详见 [设备端对齐表](coverage/llrp101-device-server-coverage.md)。 |

默认值补充：已识别的 Impinj R420 6.4.1、Zebra FX9600 3.32.37.0 和 Seuic profile
均可在核心能力基线之上提供强类型厂商默认扩展；未知型号只保留能力可验证的标准值，
不会由 UI 或 CLI 猜测厂商频点、报告字段或设备参数。

Virtual Device 的当前默认组合还包括能力档案 `llrp1.0.1_standard`、Impinj R420
档案 `impinj.r420.llrp-1.0.1` 和独立的 `default` 寻卡数据源；标准档案落在
`src/LlrpDevice.Virtual/config/llrp/caps/`，Impinj 设备端扩展落在
`src/LlrpDevice.Virtual.Impinj/`，数据源落在
`src/LlrpDevice.Virtual/config/llrp/data-sources/`；
`src/LlrpDevice.Virtual/config/virtual-device.example.json` 只组合这些对象和行为参数，监听地址、端口、连接数
仍由 create/run 启动参数提供，不写入配置文件。默认数据源包含 6 张分布在 4 根逻辑天线上的
确定性标签；默认静态寻卡每轮以 85% 概率返回 1 张、其余轮次最多返回 2 张，并按 seed/轮次轮换标签。
`singleTagProbability` 与 `maxTagsPerRound` 可在行为配置或 CLI 启动参数中调整。

标准 `llrp1.0.1_standard` 档案使用通用身份：`ManufacturerId=0`、`ModelId=0`、
`FirmwareVersion=virtual-1.0.1`；其中的 RF 表只表示虚拟设备的能力数据，不代表
虚拟设备属于某个厂商。

虚拟设备的外部 LLRP 互操作已补充验证：Impinj R420 profile 可以接受 Impinj
ItemTest 2.10.0 的 LLRP 客户端连接，并在 LLRP 1.0.1 下进入盘点启动流程。该结论
只覆盖 LLRP 连接与盘点路径，不代表实现 ItemTest 的 mDNS、rshell/SSH 或其他非
LLRP 服务。

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
  查询事实、编辑、校验、应用、序列化和清理。连接后的
  `GetDefaultSettingsAsync()` 使用当前 `Identity`、协商版本和 `Capabilities`
  生成设备相关基线：RF Mode/Tari 取能力表第一项、接收灵敏度取第一项、发射功率取
  最高功率值对应的索引，并将解析出的天线与 RF 参数同时写入 Reader 配置和 Inventory 意图。
  Impinj、Zebra、Seuic 扩展可继续通过默认值 contributor 加入型号/固件相关的强类型
  厂商参数；未知型号只返回保守的标准值，不伪造厂商能力。未连接时的
  `ReaderSettingsDefaults.CreateGeneric()` 仍是离线、非设备特定的便携基线。
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
- SDK 自有资源清理对已 inactive/disabled 或已不存在资源的设备响应按幂等成功处理，
  仅限带有匹配操作和明确状态/缺失描述的保留资源请求；其他操作和无关错误仍会抛出。
- 部署契约：持久 DesiredState 与设备 Observed 快照分离。带 Inventory 意图的
  `ApplySettingsAsync` 或 `StartInventoryAsync(settings)` 默认使用
  `ResourceTakeoverPolicy.PreserveForeign`，只替换 SDK 自有 ROSpec 14150、
  AccessSpec 14151 和可追踪临时资源；Raw/专家写入只使观测 Stale/Unknown，
  不清空 DesiredState。显式 `ReplaceAll` 才使用 LLRP id=0 删除全部标准资源。
  `SynchronizeStateAsync()` 只刷新设备快照，不是托管 API 的前置条件。
- `PrepareInventoryTakeoverAsync(ResourceTakeoverPolicy.ReplaceAll)` 提供显式的
  全量盘点接管前置步骤：先用 LLRP id=0 清理全部标准 ROSpec/AccessSpec，再交给上层
  查询或部署新的托管盘点；传入 `PreserveForeign` 会直接拒绝，避免误用破坏性接管。
- 高层事务的默认请求超时为 30 秒；超时异常会包含请求消息 ID、请求/期望响应类型和
  未收到匹配响应的诊断信息，便于定位慢设备或丢响应。
- `StartExistingRoSpecAsync(roSpecId)` 可将 Raw/专家创建的 ROSpec 接入独立
  `InventorySession`，复用报告、停止和生命周期逻辑，停止时保留调用者资源。
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
  是唯一显式批量应用入口，不维护 CLI 草稿状态。默认 PreserveForeign；`--replace-all` 才会显式删除
  foreign ROSpec/AccessSpec。`sync` 只是刷新快照，专家资源操作后仍可直接使用 `inventory start` 或
  settings apply。`caps` 同时展示 `ReaderCapabilities.ResourceLimits`（未知容量保持 nullable，设备明确返回 0 则保留为 0）。
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
  `sync` 查询并刷新设备现状但保留 DesiredState；`inventory start` 可在 Stale/Unknown
  观测下直接协调 SDK 资源。需要删除全部标准资源时必须由 SDK API 显式选择 ReplaceAll。
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

### 通用 LLRP Device Server 与 Virtual Device

- `LlrpDevice.Abstractions` 定义 `ILlrpDevice`、`IInventoryExecution`、设备身份/能力/
  配置、标准 C1G2 Tag Access、观察结果和结构化事件；不引用 `LlrpNet`、`LlrpSdk`、
  生成协议类型或 Virtual 项目。
- `LlrpDevice.Server` 是独立设备端 TCP 服务，复用 `LlrpNet` 的 accepted-TCP transport、
  `LlrpSession`、`LlrpCodecRegistry` 和 Frame Observer；不引用 `LlrpDevice.Virtual`，
  负责 1.0.1/1.1/2.0 dispatch、初始化、Capabilities、GET/SET_READER_CONFIG、完整
  ROSpec/AccessSpec 状态迁移、KEEPALIVE/ACK、CLOSE_CONNECTION、RO_ACCESS_REPORT、
  标准 C1G2 Read/Write/BlockWrite/Lock/Kill/BlockErase 和故障注入。ROSpec 中的
  `ROReportSpec`、`TagReportContentSelector`、ROBoundarySpec 触发器、报告 Hold/Release、
  `GET_REPORT` 和 `ENABLE_EVENTS_AND_REPORTS` 已接入；虚拟 Host 的报告间隔/次数只是
  测试调度参数，不替代 LLRP 报告字段。
- `LlrpDevice.Virtual` 只实现 `ILlrpDevice`，提供确定性标签/内存/锁/销毁状态、
  `static`/`moving-tags`/`noisy` 观察策略、天线过滤、RSSI 抖动和多实例隔离；不拥有
  LLRP 资源状态机或协议版本类型。
- `LlrpDevice.Virtual.Hosting` 提供 `IVirtualDeviceHost`、
  `VirtualDeviceHostOptions` 和 `VirtualLlrpDeviceHost.Create(...)`，是上层应用启动、停止、
  重启虚拟 LLRP 设备并在启动前注入标签的稳定入口；它不维护多设备目录或跨进程恢复。
- `ILlrpDeviceProtocolModule` 和 `ILlrpDeviceMessageHandler` 是设备端扩展边界；模块在
  接受连接前注册 Codec 和 Handler，Handler 先于标准 profile 处理匹配报文。
- `VirtualDeviceConfiguration` 读取版本化本地 JSON，保存能力档案选择、独立寻卡数据源引用
  和设备/寻卡行为预设（报告节奏、TID/User memory、RF 可观察场景和随机种子）。
  监听地址、端口和连接数只接受 create/run 的启动参数，不落入配置文件；配置只在显式
  `--config` 启动或校验时加载，不自动扫描文件，也不保存运行中的 ROSpec/AccessSpec 图。
  当前内置能力档案为 `llrp1.0.1_standard` 与 `impinj.r420.llrp-1.0.1`，默认寻卡数据源为
  `default`。SDK/CLI 详见 [Virtual Device SDK and CLI guide](guides/virtual-device-cli.md)。

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
- `UseImpinj()` 提供 Impinj 扩展入口，但它只是注册 Codec 和候选 Reader Extension；连接后只有在
  标准身份与协商版本匹配（当前为 Impinj + LLRP 1.0.1）时才激活。普通设备连接仍按标准 SDK 工作，
  不发送 `IMPINJ_ENABLE_EXTENSIONS`，也不会产生 Impinj 默认值、配置查询或 TagReport 投影。
  应用在使用 Impinj Settings 前应检查 `reader.Extensions.Get<ImpinjReaderExtension>()`；当前版本未激活
  时不会发送厂商字段，因此成功应用标准部分不代表 Impinj 选项已被设备接受。
- `ILlrpFrameObserver`、日志和连接事件可用于诊断与监控。

## 当前缺口

独立虚拟设备 CLI 现在默认进入设备交互 Shell：可以显式执行
`server create`、`server start`、`server stop`、`server restart`、
`server status` 和 `server destroy`，并用 `logs on|off|status` 控制生命周期、
客户端连接和解码后的 RX/TX LLRP 报文输出。`live` 会自动创建并启动虚拟
设备后进入同一个 Shell；它观察外部 LLRP 客户端产生的流量，不会自己生成
客户端指令；`run` 仍是安静的前台服务模式。
单客户端默认配置采用“新会话替换旧会话”，因此 WPF/SDK 的 Probe、Activate、
Settings 短连接可以在断开后立即重连，而不会被旧会话的异步清理窗口误拒绝。

- LLRP 1.0.1 完成度审计:协议层 42 消息 / 111 参数全覆盖,SDK 层 42/42 消息
  有业务路径或已定案（`CLIENT_REQUEST_OP` 经实机实测设备不支持,SDK 不接线、
  仅提供 `SupportsClientRequestOpSpec` 门控）;`ReaderExceptionEvent` 已暴露为
  `ReaderExceptionOccurred`,`TagReport.PcBits` 已投影。详见
  [coverage/llrp101-sdk-coverage.md](coverage/llrp101-sdk-coverage.md)。
- 1.0.1 虚拟设备端对齐已完成：通用 Server/Virtual 覆盖客户端已使用的能力（包括
  Regulatory/RF Mode 能力表）、配置、
  ROSpec/AccessSpec、触发器、过滤器、报告 selector/缓冲、事件、Hold/Release、标准
  C1G2 Tag Access 和主动关闭；未接线的 CLIENT_REQUEST_OP/RF Survey 保持明确门控。
  报告字段和触发控制来自 LLRP ROSpec/Reader Config；Simulation 只生成每轮可观察标签。
  `AISpecStopTrigger` 的独立时间线、SET_READER_CONFIG 级 ROReportSpec 作为新 ROSpec 默认值、
  以及基于 Tx/Rx/RF Mode/Tari/信道的物理 RF 计算仍未实现。
  详见 [llrp101-device-server-coverage.md](coverage/llrp101-device-server-coverage.md)。
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
- 未激活厂商扩展的高层 Settings 当前不会自动 fail-fast；应用必须先检查 active extension。后续可在
  核心 Settings 校验阶段增加“扩展未激活/无 contributor”诊断，避免调用方遗漏检查。
- Virtual Device 当前不驱动真实 RFID 模块、不模拟真实 RF 波形，也不自动恢复进程重启前的
  ROSpec/AccessSpec/托管盘点状态；这些不是本轮本地 JSON 预设的目标。通用
  `LlrpDevice.Server`、`ILlrpDevice` 合同、确定性 RF/Tag Access、Host 生命周期和
  独立设备端 CLI 已经交付；厂商设备端 profile、完整原始 LLRP 配方编辑和更多故障预设仍属
  后续工作，边界见 [ADR 0007](adr/0007-llrp-device-server-device-abstraction.md) 与
  [ADR 0008](adr/0008-single-virtual-device-sdk-and-cli.md)。
- 实机验收范围仍小于自动化测试覆盖范围。

## 验证状态

截至基准日期，解决方案构建为零警告、零错误，共 546 项自动化测试全部通过；其中
`LlrpDevice.Abstractions.Tests` 覆盖合同模型和依赖边界，`LlrpDevice.Server.Tests` 覆盖
  非 Virtual Scripted Device、生命周期和配置隔离，`LlrpDevice.Virtual.Tests` 覆盖确定性
  RF、Tag Access 和实例隔离，`LlrpDevice.Virtual.Hosting.Tests` 覆盖 SDK 门面生命周期，
`LlrpVirtualDevice.Cli.Tests` 覆盖独立 CLI 的帮助、默认交互 Shell、设备配置校验、
  server 生命周期和前台启停，
  `LlrpCli.Tests` 覆盖客户端 CLI 的帮助、解析和诊断入口，
`Interop.Tests` 覆盖通用 Server/Virtual Device Host 的 1.0.1/1.1/2.0 SDK 端到端能力（含 Regulatory/RF Mode 表）、盘点与标准 Tag Access、
报告触发/缓冲、状态感知过滤、附加数据、GPI/Reader Event、主动关闭、故障注入和设备端 Handler。
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
