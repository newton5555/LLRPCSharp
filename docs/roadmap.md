# 路线图

> 基准日期：2026-08-17

本文档只保留未完成事项；当前实现事实见 [status.md](status.md)。

## 已交付基线

- LLRP 1.0.1/1.1 的 SDK、CLI、协议 Codec 和设备互操作基线。
- LLRP 2.0 协议层与 SDK Adapter 基线；真实设备验收仍未完成。
- 通用设备端架构：`LlrpDevice.Abstractions` 定义 `ILlrpDevice`，`LlrpDevice.Server`
  承载通用 LLRP 服务与资源状态，`LlrpDevice.Virtual` 提供确定性内存、Tag Access 和
  `static` / `moving-tags` / `noisy` RF 可观察场景，`LlrpDevice.Virtual.Hosting` 提供
  单台设备的 SDK 生命周期门面。
- 客户端 1.0.1 设备端对齐：Server/Virtual 已覆盖客户端使用的 Capabilities、Config、
  ROSpec/AccessSpec、Null/Immediate/Periodic/GPI/Duration 触发、Select/状态感知
  Singulation、报告 selector/缓冲/GET_REPORT、Hold/Release、Reader Event、附加数据和
  标准 C1G2 Tag Access；未接线的 CLIENT_REQUEST_OP/RF Survey 保持能力门控。
- 版本化单设备 JSON 配置：设备端点、标签/TID/User memory、报告节奏和寻卡行为预设可通过
  设备端 CLI 的 `validate`、`presets` 与显式配置启动加载；不自动恢复进程状态。
- `LlrpVirtualDevice.Cli` 与 `LlrpCli` 平级，前者只负责单台设备端生命周期，后者只负责
  客户端连接与操作；旧 `LlrpVirtualReader.*` 兼容层和 `virtual-reader` 命令已移除。
- `LlrpDevice.Virtual.Hosting.Tests`、`LlrpVirtualDevice.Cli.Tests` 和 `Interop.Tests`
  覆盖单台设备端点、生命周期、版本协商、报告、扩展 Handler、SDK 端到端和故障场景。

## 近期

1. 完成 LLRP 1.0.1 目标设备的最终实机验收，并补充失败场景记录。
2. 补充 LLRP 1.1 目标 Reader 的实机互操作验收，覆盖版本协商、标准 Settings、Inventory、
   TagReport 和 Tag Access，并记录具体型号/固件证据。
3. 体系化扩充 `LlrpSdk.Hardware.Tests`：多天线配置、真实标签 Memory Bank 读写、高并发稳定性
   和厂商扩展字段校验。
4. 根据真实设备证据扩充 Impinj/Zebra 及其他型号/固件能力目录；继续标定 Zebra 定义中与
   固件抓包不一致的 reserved 位和字段宽度。
5. 继续完善 CLI Settings 编辑器和 Agent 输出能力，保持已有命令语义稳定。
6. 继续完善 2.0 Adapter，并对支持的真实设备做互操作验收。

## LLRP Device Server / Virtual Device 下一阶段

完整实施顺序、冻结范围、目标项目结构、接口合同、迁移阶段和验收门禁见
[LLRP Device Server 与 Virtual Device 下一阶段实施计划](architecture/llrp-device-server-virtual-device-migration-plan.md)。
该计划已完整实施：Virtual Device 已从设备端核心调整为 `ILlrpDevice` 的一种具体实现；
`LlrpSdk` 客户端产品代码和 `LlrpNet` 产品代码保持冻结。

本专项决策来源为 [ADR 0007](adr/0007-llrp-device-server-device-abstraction.md) 和
[ADR 0008](adr/0008-single-virtual-device-sdk-and-cli.md)。它只负责
报文级 TCP/LLRP 虚拟设备；`LlrpReaderPlatform` 的进程内 Session 替身仍属于平台仓库，
不与本运行时共享依赖。

### 已交付架构

```text
LlrpVirtualDevice.Cli / VirtualLlrpDeviceHost
  └─ one composition
       ├─ LlrpDevice.Server
       │    ├─ accepted TCP + LlrpSession
       │    ├─ version-explicit Codec/Handler dispatch
       │    ├─ canonical ROSpec/AccessSpec/resource state
       │    └─ KeepAlive/report/fault pipeline
       └─ ILlrpDevice
            └─ VirtualLlrpDevice + deterministic RF/Tag Access behavior
```

`LlrpDevice.Server` 维护协议资源状态和报文行为，`VirtualLlrpDevice` 只维护设备行为；
`VirtualLlrpDeviceHost` 负责一台 Server/Device 组合和显式本地配置加载。端口只表示监听
位置，绑定失败不会自动换端口。普通客户端不需要虚拟设备专用分支。

### 后续工作

- 如果未来 UI 需要管理独立后台进程，再单独设计跨进程注册、控制和守护服务；当前单台
  CLI 不引入多设备服务、跨进程 `status/stop/restart` 或自动恢复。
- 更完整的 Reader 配置快照、原始寻卡报文配方编辑、RF Survey/CLIENT_REQUEST_OP
  以及全部厂商消息覆盖；当前本地预设覆盖 Reader 端点、报告节奏、标签内存和 RF
  可观察行为，LLRP 客户端仍负责发送 `ADD_ROSPEC`/`START_ROSPEC`。
- 真实 RFID 模块驱动和真实 RF 波形模拟；当前 `ILlrpDevice` 定义设备行为接缝，Virtual
  实现只在标签观察边界产生确定性数据。
- 延迟、吞吐、连接重置等更多一次性故障预设，以及设备端 CLI 的帧日志导出和运行摘要。
- `LlrpDevice.Server` 的 Impinj/Zebra 等厂商设备端模块；接口已经落地，但必须有协议资料
  或真机抓包证据后才能发布预设。
- LLRP 2.0 Virtual Device 的独立协议验收；通用 Server 与新的单台 CLI 已可选择 2.0
  基线，但真实设备/完整设备端互操作仍待验收。
- Reader Studio 图形化项目；不是当前 SDK 仓库阶段目标。

### 已交付：单台 Virtual Device SDK 与 CLI

- `LlrpDevice.Virtual.Hosting` 提供公开的 `IVirtualLlrpDeviceHost` 和
  `VirtualLlrpDeviceHost`，把一台 `VirtualLlrpDevice` 与一台 `LlrpDeviceServer`
  组合成 Start/Stop/Restart 生命周期入口。
- `src/LlrpVirtualDevice.Cli` 与 `src/LlrpCli` 平级；前者是设备端 CLI，一个进程只运行
  一台虚拟设备，默认进入交互 Shell，支持 `server create/start/stop/restart/status/destroy`
  生命周期命令、`run`/`start`、自动创建并启动后进入 Shell 的 `live`、单设备 JSON
  `validate` 和 `presets`。
- `config/virtual-device.example.json` 定义单设备配置格式；本地配置显式加载，不保存或
  恢复运行中的 ROSpec/AccessSpec 图。
- 旧 `LlrpVirtualReader.*` 兼容层已移除；未来 UI 直接引用 Hosting 门面，客户端 UI
  仍通过 `LlrpSdk` 连接设备端点。

### 约束

- 新的客户端托管能力进入 `LlrpSdk`；设备端单台生命周期能力进入
  `LlrpDevice.Virtual.Hosting`，CLI 只负责输入、展示和前台进程编排。
- 单台 Virtual Device 的 SDK 门面不维护实例目录；多 Host 编排属于未来单独的上层 UI/服务，
  不进入 Server/Virtual 核心。
- 本地 JSON 只在显式命令下加载；不得把配置加载误解为进程重启后的自动恢复机制。
- 报文设备端 Server 不依赖客户端 `LlrpSdk`；Virtual 与未来厂商设备模块不依赖客户端
  厂商扩展包。
- 生成协议代码只能通过 `definitions/`、生成器或生成脚本修改，禁止手工编辑 `.g.cs`。
