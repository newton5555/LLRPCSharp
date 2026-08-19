# SDK 源码结构与生成边界说明 (`src/`)

> 本文档用于说明 `src/` 目录下各 SDK 子项目的职责划分、**手写核心架构**与**基于协议定义自动生成的代码**之间的边界，以及标准与厂商扩展代码生成的命名空间与目录规范。当前实现状态以 [`../status.md`](../status.md) 为准。LlrpSdk 项目单独的架构梳理与新增协议版本指南见 [llrpsdk-architecture.md](llrpsdk-architecture.md)。

---

## 1. 最终仓库结构与解决方案分组 (`LLRPCSharp.slnx` / `src/`)

```text
LLRPCSharp/
├── LLRPCSharp.slnx             [解决方案]
├── /src/                       [源代码根目录]
│   ├── LlrpCli/                [手写] 通用客户端 CLI，直接位于 src 根下
│   ├── LlrpVirtualDevice.Cli/  [手写] 虚拟设备 CLI，直接位于 src 根下
│   │
│   ├── LlrpNet/                [解决方案文件夹：通信层 + 协议层]
│   │   ├── LlrpNet.Core/       [手写] Client/accepted TCP、IO、流式帧切分、LlrpSession、FrameObserver
│   │   ├── LlrpNet.Protocol/   [生成 + 注册] LLRP 标准 Message/Parameter/Codec/Registry
│   │   ├── LlrpNet.Protocol.Impinj/ [生成] Impinj 线协议扩展与 Codec（不依赖 LlrpSdk）
│   │   ├── LlrpNet.Protocol.Zebra/  [生成] Zebra 线协议扩展与 Codec（不依赖 LlrpSdk）
│   │   ├── LlrpNet.ProtocolModel/   [手写] 协议定义模型与 LTK XML 导入器
│   │   ├── LlrpNet.ProtocolGenerator/ [手写] C# 协议源码生成引擎
│   │   └── LlrpNet.ProtocolGenerator.Tool/ [手写] 生成器命令行工具
│   │
│   ├── LlrpSdk/                [解决方案文件夹：SDK 层]
│   │   ├── LlrpSdk/            [手写] LlrpReader、Reader 会话、Inventory、Resources、TagAccess、Reports、Protocol
│   │   ├── LlrpSdk.Extensions.Abstractions/ [手写] SDK 扩展抽象
│   │   ├── LlrpSdk.Extensions.Impinj/ [手写] Impinj 高层 SDK 映射与 Contributor
│   │   ├── LlrpSdk.Extensions.Seuic/  [手写] Seuic SDK 扩展入口
│   │   └── LlrpSdk.Extensions.Zebra/  [手写] Zebra 高层 SDK 映射与 Contributor
│   │
│   ├── LlrpDevice.Abstractions/ [手写] 版本中立的设备行为合同
│   ├── LlrpDevice.Server/       [手写] 通用 LLRP 设备端服务、资源状态与版本分发
│   ├── LlrpDevice.Virtual/      [手写] 确定性标签、RF 观察和 Tag Access 实现
│   └── LlrpDevice.Virtual.Hosting/ [手写] 虚拟设备公开 SDK 门面与生命周期
│
├── /tests/                     [测试项目根目录]
│   ├── LlrpNet.*.Tests/        [通信层、协议层、生成器与厂商 Wire Codec 测试]
│   ├── LlrpSdk.*.Tests/        [SDK、扩展与硬件验收测试]
│   ├── LlrpDevice.Abstractions.Tests/ [合同与依赖边界测试]
│   ├── LlrpDevice.Server.Tests/ [通用 Server 与 Scripted Device 测试]
│   ├── LlrpDevice.Virtual.Tests/ [Virtual RF、Tag Access、隔离测试]
│   ├── LlrpDevice.Virtual.Hosting.Tests/ [设备 SDK 门面与生命周期测试]
│   ├── LlrpVirtualDevice.Cli.Tests/ [设备 CLI 测试]
│   ├── LlrpCli.Tests/          [CLI 测试]
│   └── Interop.Tests/          [互操作测试]
│
└── /tools/                     [开发辅助工具；LiveSmoke 纳入解决方案]
    ├── LlrpSdk.LiveSmoke/      [真实读写器 Smoke 工具]
    └── LlrpSdk.Probe.ClientRequestOp/ [协议客户端探针]
```

这里的 `LlrpSdk`、`LlrpDevice` 与两个 CLI 是同一解决方案下的并列能力域：设备端不
嵌套在 `src/LlrpSdk/` 物理目录中，而是由 `LlrpDevice.Server` 通过
`LlrpNet.Core` 和 `LlrpNet.Protocol` 复用通信与报文解析/编码能力；
`LlrpDevice.Virtual` 只实现 `ILlrpDevice`，`LlrpDevice.Virtual.Hosting` 负责组合
Server 与 Virtual 并提供公开生命周期门面；`LlrpCli` 只消费客户端 SDK，
`LlrpVirtualDevice.Cli` 只消费设备端 Hosting 门面。

---

## 2. 代码生成细节规范（Standard 与 Vendor 扩展）

### 2.1 LLRP 1.0.1 官方标准代码生成规范

官方标准由 `definitions/imports/xml/llrp-1.0.1/` 下的原始 XML 定义驱动生成，所有生成文件按版本后缀分隔，避免不同 LLRP 协议版本冲突：

* **版本标识映射**：LLRP `v1.0.1` 映射为版本后缀 `V1_0_1`。
* **目录与命名空间结构**：
  ```text
  LlrpNet.Protocol/
  ├── Messages/V1_0_1/    --> 命名空间: LlrpNet.Protocol.Messages.V1_0_1
  │   ├── Keepalive.cs
  │   ├── GetReaderCapabilities.cs
  │   ├── AddRoSpec.cs
  │   └── ...
  ├── Parameters/V1_0_1/  --> 命名空间: LlrpNet.Protocol.Parameters.V1_0_1
  │   ├── GeneralDeviceCapabilities.cs
  │   ├── RoSpec.cs
  │   ├── TagReportData.cs
  │   └── ...
  ├── Codecs/V1_0_1/      --> 命名空间: LlrpNet.Protocol.Codecs.V1_0_1
  │   ├── KeepaliveCodec.cs
  │   ├── GeneralDeviceCapabilitiesCodec.cs
  │   └── ...
  └── Registry/V1_0_1/    --> 命名空间: LlrpNet.Protocol.Registry.V1_0_1
      └── Llrp101StandardModule.cs (一键向 Registry 注册该版本所有标准 Codec)
  ```
* **主键查找规则**：
  - 标准 Message 按 `ProtocolVersion (1.0.1) + MessageType (1, 11, 20 等)` 查找。
  - 标准 Parameter 按 `ProtocolVersion (1.0.1) + ParameterType (137, 177, 240 等)` 查找。

---

### 2.2 厂商自定义扩展（Vendor Custom Extension，以 Impinj 为例）代码生成规范

厂商扩展（如 Impinj / Alien / Zebra）基于厂商发布的扩展定义 XML 生成；当前 Impinj 输入为
`definitions/imports/xml/extensions/impinj/Impinjdef.xml`（LTK Definition Files 10.58.0）。

* **厂商标识与命名空间规约**：
  - 线协议生成资产必须放在独立的 `LlrpNet.Protocol.Impinj` 项目中；该项目只依赖 `LlrpNet.Protocol` 与 `LlrpNet.Core`，不得依赖 `LlrpSdk`。
  - **命名空间**：`LlrpNet.Protocol.Impinj.Messages` / `Parameters` / `Codecs`。
  - `LlrpSdk.Extensions.Impinj` 只包含手写的高层映射和 Contributor，并通过项目引用使用协议扩展。
* **类型与类名规约**：
  - 类名保持厂商定义名称，前缀显式带厂商标识，如：
    - 扩展 Message：`IMPINJ_ENABLE_EXTENSIONS`
    - 扩展 Parameter：`ImpinjGen2XInventoryConfig`、`ImpinjEnableEndpointICVerification`、`ImpinjRampUpPowerBoost`
* **唯一匹配主键 (Vendor Key)**：
  - 厂商扩展参数/消息在 LLRP 报文中属于 ParameterType = 327 (Custom Parameter) 或 MessageType = 1023 (Custom Message)。
  - **定位三元组**：`VendorID (如 Impinj = 25882)` + `Subtype (如 1023, 1001)` + `TypeKind (Message 或 Parameter)`。
* **扩展注册机制 (`ImpinjProtocolModule`)**：
  - 生成器会自动产出 `ImpinjProtocolModule`（或 `ImpinjExtensionModule`）。
  - 在 `LlrpReader` 与读写器建立连接前，通过 `UseProtocolModule(ImpinjProtocolModule.Instance)` 将厂商 Custom Codec 注入到 `LlrpCodecRegistry` 中；常规集成可直接调用 `builder.UseImpinj()`。
  - **容错隔离**：若未安装或未注册 Impinj 扩展模块，收到该厂商报文时系统不会崩溃，而是自动降级解析为 `RawCustomParameter`，确保主盘点流程不受干扰。

---

## 3. 模块职责说明

### 3.1 应用层 (`src/LlrpSdk/`) —— [手写]
- **`LlrpReader`**：开发者直接调用的设备会话根对象，负责管理连接建立、断线恢复、 Keepalive 自动应答、能力协商与 ReaderIdentity / ReaderCapabilities 元数据。
- **`RoSpecService`**：高级资源服务，提供 `reader.RoSpecs.AddAsync` / `EnableAsync` / `StartAsync` / `StopAsync` / `DeleteAsync` / `GetAllAsync` 操作。
- **`AccessSpecService`**：进阶 AccessSpec 生命周期服务，提供 Add/Delete/Enable/Disable/GetAll；当前不是标签读写的高级业务 API。
- **`ReaderExtensionCollection`**：维护连接后激活的 Reader Extension，负责基于设备元数据筛选和互斥检查。
- **`LlrpAutomaticReconnectOptions`**：控制有限自动重连；重连成功后自动查询设备当前
  ROSpec/AccessSpec 状态并对齐 SDK 内部状态，不重放期望配置。

### 3.2 传输与会话层 (`src/LlrpNet/LlrpNet.Core/`) —— [手写]
- **`LlrpSession`**：底层的 LLRP 双向会话管理，负责并发 Request/Response 事务匹配、超时控制与取消广播。
- **`Framing`**：实现网络大端序二进制 Buffer Reader/Writer、Bit Reader/Writer，以及 TCP 粘包/半包和多段缓冲区的流式帧切分。
- **`ILlrpFrameObserver`**：网络边界级别的原始 LLRP 帧监听观察者接口，用于无侵入打印和捕获完整 TX/RX 报文。

### 3.3 协议定义模型与导入器 (`src/LlrpNet/LlrpNet.ProtocolModel/`) —— [手写]
- **`LtkXmlDefinitionImporter`**：直接读取与解析 LLRP 官方及 Impinj 等厂商发布的原始 LTK XML 规范文件（如 `llrp-1x0-def.xml`、`Impinjdef.xml`）。
- **`ProtocolDefinition`**：将解析后的规范标准化为可校验的协议定义模型。

### 3.4 源码生成引擎 (`src/LlrpNet/LlrpNet.ProtocolGenerator/`) —— [手写]
- **`ProtocolSourceGenerator`**：将导入的协议定义规范模型编译生成强类型的 C# 源码（包含二进制 Pack/Unpack、Bit-field 逻辑、保留位校验与 Codec 注册绑定）。

### 3.5 协议二进制编解码集 (`src/LlrpNet/LlrpNet.Protocol/`) —— [自动生成]
- **`Messages/`**：所有的 LLRP Message 强类型对象（如 `GetReaderCapabilities`、`AddRoSpec`、`RO_ACCESS_REPORT` 等）。
- **`Parameters/`**：所有的 LLRP Parameter 强类型对象（如 `GeneralDeviceCapabilities`、`RoSpec`、`TagReportData`、`EPCData` 等）。
- **`Enumerations/`**：协议中所有的枚举定义（如 `LlrpStatusCode`、`AirProtocolID` 等）。
- **`Codecs/`**：每个 Message 和 Parameter 对应的二进制 Codec 编解码逻辑。
- **`Registry/`**：将所有生成 Codec 批量绑定到 `LlrpCodecRegistry` 的模块类（如 `Llrp101StandardModule`）。

### 3.6 终端诊断工具 (`src/LlrpCli/`) —— [手写]
- 基于 `Spectre.Console` 和 `Spectre.Console.Cli` 构建的 Live Shell，提供指令补全提示链、灰色 Ghost 后缀、平滑光标控制以及深层 LLRP 报文树状分析器。

### 3.6.1 虚拟设备 CLI (`src/LlrpVirtualDevice.Cli/`) —— [手写]
- 只消费 `LlrpDevice.Virtual.Hosting`，负责前台虚拟 LLRP 设备生命周期。
- 提供前台 `run`/`start`、设备 JSON `validate` 和内置 `presets`；Ctrl+C
  对应停止，不引入跨进程多设备管理协议。

### 3.7 通用设备端 (`src/LlrpDevice.*`) —— [手写]
- `LlrpDevice.Abstractions` 定义 `ILlrpDevice`、`IInventoryExecution`、设备身份/能力/配置、
  版本中立 Tag Access、观察结果和结构化事件；只依赖 BCL。
- `LlrpDevice.Server` 复用 `LlrpNet` accepted transport、Session、Codec Registry 和
  Frame Observer；拥有 1.0.1/1.1/2.0 分发、ROSpec/AccessSpec 状态、KEEPALIVE、报告、
  标准 Tag Access 映射、故障注入和注册式设备端协议模块。
- `LlrpDevice.Virtual` 只实现 `ILlrpDevice`，维护独立的标签/内存/锁/销毁状态，提供
  `static`、`moving-tags`、`noisy` 三种确定性 RF 可观察场景，但不模拟真实 RF 波形。
- `LlrpDevice.Virtual.Hosting` 提供 `IVirtualDeviceHost`、
  `VirtualDeviceHostOptions` 与 `VirtualLlrpDeviceHost.Create(...)`，把
  `VirtualLlrpDevice` 和 `LlrpDeviceServer` 组合成上层应用可直接启动/停止/重启的
  设备端入口，并允许启动前注入标签；旧接口仅作为迁移兼容路径保留。

---

## 4. 手写与生成的区分原则总结

| 类别 | 包含模块 / 目录 | 修改与维护原则 |
|---|---|---|
| **手写核心逻辑** | `LlrpSdk`, `LlrpNet/LlrpNet.Core`, `LlrpNet/LlrpNet.ProtocolModel`, `LlrpNet/LlrpNet.ProtocolGenerator`, `LlrpDevice.*`, `LlrpCli`, `LlrpVirtualDevice.Cli` | 正常的 C# 逻辑代码，随需求功能演进手写维护；本阶段客户端和共享协议产品代码冻结。 |
| **自动生成代码** | `LlrpNet/LlrpNet.Protocol` 与 `LlrpNet/LlrpNet.Protocol.Impinj` (`Messages`, `Parameters`, `Codecs`, `Registry`) | 不手写 C# 代码；通过更新 `definitions/` 下的 XML 定义并调用生成工具更新。高层 `LlrpSdk.Extensions.Impinj` 不存放生成的线协议类型。 |
