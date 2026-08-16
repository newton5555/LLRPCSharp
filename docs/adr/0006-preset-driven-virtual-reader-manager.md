# ADR 0006：预设驱动的报文级 Virtual Reader Core 与独立 Manager

- 状态：Accepted（实施中：Core、Manager、1.0.1/1.1 标准闭环已交付）
- 日期：2026-08-17

## 背景

本决策固定报文级虚拟设备的架构边界。Core、独立 Manager、精确端点绑定、注册式
Preset/Handler 管道和 1.0.1/1.1 标准设备行为已经落地；跨进程持久化和厂商设备端
profile 仍留在后续范围。

当前 `LlrpVirtualReader.Core` 是一个消息级 LLRP 1.0.1/1.1 TCP Server，能支撑 SDK
互操作测试和标准 ROSpec/AccessSpec/Tag Access 闭环；Manager 已提供多 Host 生命周期
和稳定预设目录，CLI/独立宿主也可启动普通 TCP Reader。

报文级虚拟设备需要独立于任何 Reader Studio 运行，使 SDK CLI、WPF、第三方 LLRP
客户端和抓包工具都能把它当作普通 TCP Reader。相邻 `LlrpReaderPlatform` 的进程内
Virtual Reader 是平台 Session 替身，不属于本决策范围，也不能由本 Manager 管理。

## 决定

### 1. 报文虚拟设备由独立 Manager 管理

`LLRPCSharp` 提供独立 Virtual Reader Manager。Manager 消费内置报文设备预设，创建、
启动、停止和监控一个或多个 TCP Host。Reader Studio 或其他客户端只通过 Host/Port
连接，不需要知道目标是真机还是虚拟设备。

Manager 不实现设备发现，不与 `LlrpReaderPlatform` 建立运行时依赖，也不通过 IPC
让主 WPF 接管 Host 生命周期。

### 2. 用户只选择预设和 TCP 参数

普通用户创建实例时只输入：

- 设备名称；
- 内置预设；
- 本机监听地址；
- 监听端口。

用户不能创建、编辑、复制或导入任意报文配方。身份、能力、配置初值、标签、Tag Memory、
GPIO、报告和故障行为全部由经过测试的内置预设决定。

监听端点严格服从用户保存的参数。端口或地址不可绑定时，实例以结构化错误停止；Manager
不得递增端口、选择随机端口或根据端口推断设备类型。自动化测试可以显式请求端口 `0`，
普通用户实例不允许。

### 3. Core 与 Manager 分层

目标项目边界：

```text
src/
├── LlrpVirtualReader.Core/
│   ├── TCP Listener 与连接生命周期
│   ├── LLRP Frame 解码/编码
│   ├── 设备资源和标签状态
│   ├── 请求 Handler 管道
│   └── 故障注入与报告调度
├── LlrpVirtualReader.Manager/
│   ├── 目标配置与内置 Preset Catalog
│   ├── 实例目录和端点管理
│   ├── create/new 与多 Host 启停
│   ├── restart/delete/list/status
│   ├── 连接/报文/错误状态
│   └── 控制台入口；桌面 UI 另按实际需要决策
├── LlrpVirtualReader.Extensions.Impinj/    （未来）
└── LlrpVirtualReader.Extensions.Zebra/     （未来）

tests/
├── LlrpVirtualReader.Core.Tests/
├── LlrpVirtualReader.Manager.Tests/
├── LlrpVirtualReader.Extensions.Impinj.Tests/  （未来）
├── LlrpVirtualReader.Extensions.Zebra.Tests/   （未来）
└── Interop.Tests/                              SDK 客户端端到端互操作
```

Core 依赖 `LlrpNet.Core`、标准 Protocol 和按需加载的厂商 Protocol Module；设备端实现
不得依赖客户端门面 `LlrpSdk` 或 `LlrpSdk.Extensions.*`。现有 `VirtualReaderHost` 迁入
Core，Manager 通过目标配置为每个实例创建一个 `VirtualReaderHost`；Core 不维护实例目录，
也不负责多 Host 的创建、删除或生命周期编排。

所有产品项目继续位于顶层 `src/`，所有测试项目继续集中在顶层 `tests/`。测试程序集不得
放入 `src/LlrpVirtualReader.*` 内部，也不得与 Manager/Core 产品项目处于同一项目目录。

### 4. 预设采用 Catalog/Contributor，而不是厂商枚举

每个预设具有稳定的 `PresetId`、`PresetVersion`、显示名称、分类、协议 Profile 和所需
模块。Manager 只读取 Catalog，不硬编码 Standard、Impinj、Zebra 的 `switch`。

当前内置：

- Standard LLRP 1.0.1；
- Strict Standard LLRP 1.0.1；
- Standard Tag Access；
- Standard LLRP 1.1；
- Request Timeout；
- Device Disconnect。

厂商预设当前不交付，但架构从第一阶段提供 `IVirtualReaderProtocolModule`、请求 Handler
Contributor 和 Preset Contributor。未来模块建议为：

```text
LlrpVirtualReader.Extensions.Impinj
LlrpVirtualReader.Extensions.Zebra
```

厂商模块注册对应生成 Codec，处理真实 Custom Message/Parameter，贡献能力、配置、报告
和预设。只修改 ManufacturerId 不构成厂商预设。缺失所需模块时实例显示“预设模块不可用”，
不得降级为标准设备。

### 5. Host 保持设备端语义

每个 Manager 实例拥有独立 Listener、Reader Config、ROSpec/AccessSpec、Tag Memory、GPIO、回放和
故障状态。连接数量、设备重启是否保留状态和故障触发次数由预设定义。Host 负责设备端
状态机，Manager 负责创建 Host、生命周期编排和展示，不重写报文业务。

原始 TX/RX 帧通过共享 `ILlrpFrameObserver` 和日志管道暴露，不能在网络读写路径同步执行无界 UI 或磁盘工作。
绑定非回环地址时 Manager 必须提示网络暴露和防火墙风险。

## 替代方案

1. 继续维护单 Host 命令行和代码级 Options：足够单元测试，但不能作为可管理的虚拟设备产品。
2. 允许用户编辑完整 JSON 配方：灵活，但无法保证每个组合都经过协议验收，拒绝。
3. 由 Reader Studio 内嵌并管理 TCP Host：入口集中，但把设备端协议服务器耦合到客户端应用，拒绝。
4. 用固定端口区分设备类型：端口与设备语义无关且阻碍用户部署，拒绝。

## 后果

- 需要把当前不可打包 EXE 中的 Host 核心拆成可复用类库和独立 Manager 宿主；
- `VirtualReaderHost` 的单个请求 `switch` 应逐步拆为标准/版本/厂商 Handler 注册管道；
- 当前 1.0.1 互操作测试迁移后必须保持行为和故障覆盖；
- 新增预设必须有稳定 ID、版本、自动化测试和用户可见说明；
- SDK 包发布不自动包含 Manager；Manager 作为独立工具/产物发布，是否增加桌面 UI 另行决策；
- `LlrpReaderPlatform` 与 Manager 只通过 TCP/LLRP 互操作，不共享项目引用或运行时模型。
