# SDK 源码结构与生成边界说明 (`src/`)

> 本文档用于说明 `src/` 目录下各 SDK 子项目的职责划分、**手写核心架构**与**基于协议定义自动生成的代码**之间的边界，以及标准与厂商扩展代码生成的命名空间与目录规范。当前实现状态以 [`../status.md`](../status.md) 为准。LlrpSdk 项目单独的架构梳理与新增协议版本指南见 [llrpsdk-architecture.md](llrpsdk-architecture.md)。

---

## 1. 架构总览与分层关系 (`src/`)

```text
src/
├── LlrpNet/                    [底层协议栈]
│   ├── LlrpNet.Core/           [手写] 协议网络与传输层 (IO、TCP 流式帧切分、LlrpSession、FrameObserver)
│   ├── LlrpNet.ProtocolModel/  [手写] 协议定义模型与 LTK XML 导入器 (LtkXmlDefinitionImporter)
│   ├── LlrpNet.ProtocolGenerator/ [手写] C# 源码生成引擎 (ProtocolSourceGenerator)
│   ├── LlrpNet.Protocol/       [生成] LLRP 标准消息/参数强类型类与 Codec 编解码器 (由 LTK XML 自动生成)
│   └── LlrpNet.Protocol.Impinj/ [生成] Impinj 厂商私有报文/参数/Codec 与协议注册模块（不依赖 LlrpSdk）
│   └── LlrpNet.Protocol.Zebra/  [生成] Zebra(Moto)厂商私有报文/参数/Codec 与协议注册模块（不依赖 LlrpSdk）
├── LlrpSdk/                    [手写] 应用层 SDK（按职责分文件夹）
│   ├── Reader/                  Reader 会话门面、连接状态与元数据
│   ├── Settings/                ReaderSettings、配置模型与序列化
│   ├── Inventory/               InventorySettings、编译器与盘点会话
│   ├── Resources/               ROSpec/AccessSpec 专家资源服务
│   ├── TagAccess/               标签访问编译与操作模型
│   ├── Reports/                 报告模型、翻译器与时间戳
│   ├── Protocol/                LLRP 版本适配器与版本切片组件(反解析/事件投影/消息工厂/版本协商),门面零版本引用
│   └── Extensions/              SDK 扩展注册集合
├── LlrpSdk.Extensions.Impinj/  [手写扩展] Impinj 高层 SDK 映射、Settings/Inventory Contributor 与 UseImpinj()
│   ├── Registration/            UseImpinj 与扩展注册
│   ├── Settings/                Impinj 高层设置映射
│   ├── Inventory/               Impinj 盘点扩展模型
│   └── Reports/                 Impinj 报告扩展
├── LlrpCli/                    [手写] 交互式终端 Shell、智能提示链与 LLRP 报文树状分析器
└── LlrpVirtualReader/          [手写] 最小 1.0.1 虚拟读写器，用于本地互操作和回归测试
```

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

### 3.7 虚拟读写器 (`src/LlrpVirtualReader/`) —— [手写]
- 最小 1.0.1 TCP Server，用于本地互操作和回归测试。
- 当前支持能力查询、ROSpec 生命周期、确定性 TagReport、最小 AccessSpec、标签筛选、User Memory 读写模拟和部分故障注入场景；不模拟真实射频。

---

## 4. 手写与生成的区分原则总结

| 类别 | 包含模块 / 目录 | 修改与维护原则 |
|---|---|---|
| **手写核心逻辑** | `LlrpSdk`, `LlrpNet/LlrpNet.Core`, `LlrpNet/LlrpNet.ProtocolModel`, `LlrpNet/LlrpNet.ProtocolGenerator`, `LlrpCli` | 正常的 C# 逻辑代码，随需求功能演进手写维护。 |
| **自动生成代码** | `LlrpNet/LlrpNet.Protocol` 与 `LlrpNet/LlrpNet.Protocol.Impinj` (`Messages`, `Parameters`, `Codecs`, `Registry`) | 不手写 C# 代码；通过更新 `definitions/` 下的 XML 定义并调用生成工具更新。高层 `LlrpSdk.Extensions.Impinj` 不存放生成的线协议类型。 |
