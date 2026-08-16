# 路线图

> 基准日期：2026-08-16

本文档只保留未完成事项；当前实现事实见 [status.md](status.md)。

## 近期

1. 完成 LLRP 1.0.1 目标设备的最终实机验收，并补充失败场景记录。
2. 补充 LLRP 1.1 目标 Reader 的实机互操作验收，覆盖版本协商、标准 Settings、
   Inventory、TagReport 和 Tag Access，并记录具体型号/固件证据。
3. 体系化设计与扩充 `LlrpSdk.Hardware.Tests` 真机测试用例集（包含多天线配置、真实标签 Memory Bank 读写、高并发稳定性及厂商扩展字段校验）。
4. 根据真实设备证据扩充 Impinj 型号/固件能力目录。
5. 完善独立的 Reader Studio 项目，但不把 WPF 依赖带回 SDK 仓库。
6. CLI 离线协议工具（`inspect` / `decode` / `validate` / `encode`）已补齐 LLRP 1.1/2.0
   支持：`Helpers.CreateRegistry()` 已注册 1.0.1/1.1/2.0、Impinj、Zebra 模块；
   `encode --llrp` 选择构造版本；`inspect` 仍作为版本无关的 Header 检查工具。
   2.0 的真实设备互操作仍需实机验收。
7. 离线工具消重：standalone 与 Live Shell 的 `inspect`/`decode`/`validate` 合并到 `OfflineProtocolTool` 共享核；
   未接线的死代码 `LlrpFrameAnalyzer`（语义分析半成品）已删除。
8. LLRP 1.0.1 收尾:`ENABLE_EVENTS_AND_REPORTS` 重连放行已实现并真机验证
   （R430）;`CLIENT_REQUEST_OP` 经实机实测（R430 固件 6.4.1.240）设备不支持,
   SDK 不接线、仅提供 `SupportsClientRequestOpSpec` 门控。事件子参数投影已完成
   `ReaderExceptionEvent`（`ReaderExceptionOccurred` 事件）;其余事件子参数按
   分析不做（ConnectionAttempt/ConnectionClose/RFSurveyEvent/Hopping/AISpec,
   可经 `ReadMessagesAsync` 取原始协议对象）。TagReport 补充投影已完成
   `C1G2_PC`（`TagReport.PcBits`）;`C1G2_CRC`/`C1G2SingulationDetails`
   低价值可暂缓。
缺口明细见
   [coverage/llrp101-sdk-coverage.md](coverage/llrp101-sdk-coverage.md)。

## 中期

1. 增加更多厂商扩展，保持扩展与核心协议、托管 SDK 解耦。
2. 扩充 Virtual Reader 的配置流、故障注入和互操作场景。
3. 根据实际使用反馈补充 CLI Settings 编辑器和 Agent 输出能力，保持已有
   命令语义稳定。

## 长期

1. 实现 `Llrp20ProtocolAdapter`，覆盖协商、ROSpec、AccessSpec 和 TagReport
   的最小闭环。
2. 增加 LLRP 2.0 Virtual Reader 和真实设备互操作测试。
3. 继续完善多厂商协议定义、生成和能力验证工具链。

## 长期专项：Virtual Reader Core 与独立 Manager（已启动，分阶段实施）

> 决策来源：[ADR 0006](adr/0006-preset-driven-virtual-reader-manager.md)。本工作包只负责
> 报文级 TCP/LLRP 虚拟设备。`LlrpReaderPlatform` 的进程内 Session 替身由平台仓库自己管理，
> 两者不共享运行时依赖。2026-08-16 已按用户明确要求启动本专项；当前完成 VR1/VR2 基线，
> VR3～VR8 仍按依赖顺序实施。完整的多 Host、Preset 和 Handler 能力尚未交付。

### 产品边界

```text
LlrpVirtualReader.Manager
  ├─ create/new：保存目标配置（名称、预设、监听地址、端口）
  ├─ start/stop/restart/delete/list/status：管理实例生命周期
  └─ 为每个实例创建一个 LlrpVirtualReader.Core TCP Host
      └─ SDK CLI、Reader Studio 或第三方客户端按普通 Reader 连接
```

用户不编辑完整配方，不配置能力、标签、故障或厂商参数。预设是随软件发布并经过测试的
开发资产。端口只表示监听位置，不表示设备类型；指定端点无法绑定时明确失败且不自动换端口。
Core 只负责一个虚拟 Reader 的设备状态、报文处理和 TCP 生命周期；实例注册、创建/删除、
多 Host 编排和目标配置归 Manager 负责。

### VR1：现有行为冻结与 Core 拆分

- 为当前 `VirtualReaderHost` 的能力查询、GET/SET_CONFIG、ROSpec、AccessSpec、Tag Access、
  报告和故障注入建立行为清单；
- 将网络 Host、设备状态和报文处理迁入 `LlrpVirtualReader.Core` 类库；
- 保留当前控制台启动方式作为兼容入口，迁移期间现有 `Interop.Tests` 不降级；
- Core 只依赖 `LlrpNet.Core`/Protocol，不依赖 `LlrpSdk` 客户端门面。

出口：现有 SDK 互操作测试全部通过，单 Host 命令行行为保持兼容。

目标目录结构遵循现有解决方案约定：Core、Manager 和未来厂商模块放入顶层 `src/`；
`LlrpVirtualReader.Core.Tests`、`LlrpVirtualReader.Manager.Tests` 和未来厂商测试放入顶层
`tests/`；现有 `tests/Interop.Tests` 继续承担真实 TCP Host → `LlrpSdk` 的端到端互操作。
不得在任何 `src/LlrpVirtualReader.*` 项目目录下创建测试工程。

### VR2：Host 生命周期和精确 TCP 绑定

- `VirtualReaderHostOptions` 支持明确的本机监听地址和端口；
- 用户实例端口限制为 1～65535，端口 0 只保留给自动化测试；
- 地址不属于本机、端口占用、权限和套接字错误映射为结构化启动结果；
- 不递增端口、不随机回退、不按端口推断预设；
- 每个 Host 独立维护 Listener、连接、资源和 Tag Memory 状态；
- 定义单控制客户端默认语义，额外连接的处理必须有确定测试。

出口：精确端点绑定、占用失败和停止释放端口测试通过；多 Host 不串状态由 VR3 验证。

当前进度：`LlrpVirtualReader.Core` 已提供 `VirtualReaderHostOptions` 的显式监听地址/端口；
`LlrpVirtualReader.Manager` 已提供独立单 Host CLI，端口占用不回退；Core 专项测试已覆盖
精确回环端口绑定和占用失败。Manager 的多 Host 实例生命周期与结构化启动结果进入 VR3。

### VR3：Manager 实例模型与生命周期

- 定义 Manager 持有的实例记录：`InstanceId`、名称、目标配置/预设、监听地址、端口、
  启用意图和运行状态；
- `create/new` 创建一个未启动实例，并由 Manager 为它构造一个独立的 `VirtualReaderHost`；
- `start`、`stop`、`restart`、`delete`、`list`、`status` 全部由 Manager 提供；
- 每个实例拥有独立的 Host、Listener、设备状态和端点；一个实例失败不能影响其他实例；
- 端口冲突、地址不可绑定和配置错误返回结构化失败，不自动换端口；
- 第一版先在同一个 Manager 进程内管理多个 Host，不拆分子进程；
- 先用最小 Standard LLRP 1.0.1 目标配置跑通生命周期，不开放任意 JSON 报文配方。

出口：一个 Manager 进程可创建、启动、停止、重启和删除多个独立的 LLRP TCP 服务，
并有 Manager 专项测试覆盖状态隔离、端口冲突和退出清理。

### VR4：内置 Preset Catalog 与目标配置

- 定义稳定的 `PresetId`、`PresetVersion`、DisplayName、Category、Description、
  ProtocolProfileId 和 RequiredModules；
- 定义 `IVirtualReaderPresetContributor`，标准预设也通过 Contributor 注册；
- Manager 的 `create/new` 只选择经过测试的预设和 TCP 参数，不包含厂商枚举或预设 `switch`；
- 普通用户界面不提供配方 JSON 导入、编辑、复制或能力参数修改；
- 缺失预设或所需模块时保留实例记录并显示不可启动原因，不静默替换。

首批预设：

1. `llrp.standard101.basic`；
2. `llrp.standard101.strict`；
3. `llrp.standard101.tag-access`；
4. `llrp.fault.request-timeout`；
5. `llrp.fault.device-disconnect`。

出口：每个用户可见预设都有独立自动化验收，升级后稳定 ID 可恢复；Manager 能把预设
解析为 Core 可运行的目标配置。

### VR5：报文 Handler 与版本 Profile 管道

- 把 `VirtualReaderHost` 中集中的请求 `switch` 拆为注册式 Handler；
- 定义 `IVirtualReaderProtocolModule`，负责 Codec 注册、请求 Handler、设备扩展状态和预设贡献；
- 标准 1.0.1 Handler 覆盖连接、Capabilities、Config、ROSpec、AccessSpec、TagReport 和 GPIO；
- 异步事件、KEEPALIVE、ReaderException 和设备主动断开进入统一设备端调度；
- Profile 决定协议和行为，TCP 端口不参与分发。

出口：标准 1.0.1 功能与现有 SDK 编译/反解析器完成端到端往返，原始帧可观测。

### VR6：帧观察、日志和故障场景

- 在设备端提供有界 TX/RX Frame Observer，不阻塞网络读写；
- 支持按实例导出帧日志和运行摘要；
- 故障预设覆盖丢响应、LLRPStatus 错误、截断响应、设备主动断开和延迟；
- 故障触发次数、是否一次性和重启复位语义由内置预设确定，不开放任意用户脚本。

出口：CLI/SDK FrameObserver 与设备端日志可对齐同一 MessageID 和帧方向。

### VR7：厂商预设扩展架构

本阶段先完成接口和守护测试，不要求首版交付厂商预设。

规划模块：

```text
LlrpVirtualReader.Extensions.Impinj
LlrpVirtualReader.Extensions.Zebra
```

- 厂商设备端模块依赖对应 `LlrpNet.Protocol.*` 生成协议包，不依赖客户端
  `LlrpSdk.Extensions.*`；
- 模块注册真实 Custom Codec、消息/参数 Handler、能力、配置和报告字段；
- 厂商预设必须具备协议资料或真机抓包证据，只修改 ManufacturerId 不可发布；
- Manager 从 Contributor 自动获得厂商预设，无需修改 UI 或 Core；
- 缺失厂商模块时对应实例不可启动，不降级为 Standard；
- LLRP 1.1、2.0 与厂商组合沿用同一版本显式规则，不引入默认版本命名空间。

出口：使用测试模块证明第三方 Contributor 可注册 Codec、Handler 和预设，且核心源码无厂商分支。

### VR8：跨客户端验收与交付

- Manager 使用非默认端口启动 Standard 1.0.1 和 Strict Standard 两台设备；
- `LlrpSdk`、`LlrpCli` 和 Reader Studio 分别按普通 TCP Reader 连接；
- 验证 Probe/Capabilities、Settings、Inventory、Tag Access、Stop/清理和断开；
- 用 Wireshark loopback 或 FrameObserver 保存一次完整报文证据；
- 占用配方端口后再次启动，必须得到明确失败且原端口值保持不变；
- Manager 作为独立工具/产物发布，不并入 `LlrpSdk` NuGet 主包。

出口：任何客户端都不需要 VirtualReader 专用分支；真实 Reader 与报文虚拟 Reader 使用同一 SDK TCP 路径。

### 依赖顺序

```text
VR1 → VR2 → VR3 → VR4 → VR5 → VR6 → VR8
                              └──────→ VR7（接口先落地，厂商实现可后置）
```

## 约束

- 新的托管能力优先进入 `LlrpSdk`，CLI 只负责输入、展示和流程编排。
- CLI 与一次性命令共用 SDK 工作流，不维护第二套配置或资源生命周期。
- 新增公开能力必须同步更新 [status.md](status.md) 和对应用户指南。
- 生成协议代码只能通过 `definitions/`、生成器或生成脚本修改。
- Virtual Reader Manager 只暴露受测内置预设；新增用户可见预设必须附自动化验收。
- 多 Host 实例的创建、启停、重启、删除和状态编排只属于 Manager；Core 不维护实例目录。
- 报文虚拟设备必须严格绑定实例保存的本机 TCP 端点，绑定失败不得静默改端口。
- 报文设备端 Core 不依赖客户端 `LlrpSdk`；厂商虚拟模块不依赖客户端厂商扩展包。
