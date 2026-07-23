# SDK 源码结构与生成边界说明 (`src/`)

> 本文档用于说明 `src/` 目录下各 SDK 子项目的职责划分，以及**手写核心架构**与**基于协议定义自动生成的代码**之间的边界。

---

## 1. 架构总览与分层关系 (`src/`)

```text
src/
├── LlrpSdk/                    [手写] 应用层 SDK 根对象 (LlrpReader、RoSpecService)
├── LlrpNet.Core/               [手写] 协议网络与传输层 (IO、TCP 流式帧切分、LlrpSession、FrameObserver)
├── LlrpNet.ProtocolModel/      [手写] 协议定义模型与 LTK XML 导入器 (LtkXmlDefinitionImporter)
├── LlrpNet.ProtocolGenerator/  [手写] C# 源码生成引擎 (ProtocolSourceGenerator)
├── LlrpNet.Protocol/           [生成] LLRP 消息/参数强类型类与 Codec 编解码器 (由 LTK XML 自动生成)
└── LlrpCli/                    [手写] 交互式终端 Shell、智能提示链与 LLRP 报文树状分析器
```

---

## 2. 模块职责说明

### 2.1 应用层 (`src/LlrpSdk/`) —— [手写]
- **`LlrpReader`**：开发者直接调用的设备会话根对象，负责管理连接建立、断线恢复、 Keepalive 自动应答、能力协商与 ReaderIdentity / ReaderCapabilities 元数据。
- **`RoSpecService`**：高级资源服务，提供 `reader.RoSpecs.AddAsync` / `EnableAsync` / `StartAsync` / `StopAsync` / `DeleteAsync` / `GetAllAsync` 操作。

### 2.2 传输与会话层 (`src/LlrpNet.Core/`) —— [手写]
- **`LlrpSession`**：底层的 LLRP 双向会话管理，负责并发 Request/Response 事务匹配、超时控制与取消广播。
- **`Framing`**：实现网络大端序二进制 Buffer Reader/Writer、Bit Reader/Writer，以及 TCP 粘包/半包和多段缓冲区的流式帧切分。
- **`ILlrpFrameObserver`**：网络边界级别的原始 LLRP 帧监听观察者接口，用于无侵入打印和捕获完整 TX/RX 报文。

### 2.3 协议定义模型与导入器 (`src/LlrpNet.ProtocolModel/`) —— [手写]
- **`LtkXmlDefinitionImporter`**：直接读取与解析 LLRP 官方及 Impinj 等厂商发布的原始 LTK XML 规范文件（如 `llrporg.lld.xml`、`impinj.lld.xml`）。
- **`ProtocolDefinition`**：将解析后的规范标准化为可校验的协议定义模型。

### 2.4 源码生成引擎 (`src/LlrpNet.ProtocolGenerator/`) —— [手写]
- **`ProtocolSourceGenerator`**：将导入的协议定义规范模型编译生成强类型的 C# 源码（包含二进制 Pack/Unpack、Bit-field 逻辑、保留位校验与 Codec 注册绑定）。

### 2.5 协议二进制编解码集 (`src/LlrpNet.Protocol/`) —— [自动生成]
- **`Messages/`**：所有的 LLRP Message 强类型对象（如 `GetReaderCapabilities`、`AddRoSpec`、`RO_ACCESS_REPORT` 等）。
- **`Parameters/`**：所有的 LLRP Parameter 强类型对象（如 `GeneralDeviceCapabilities`、`RoSpec`、`TagReportData`、`EPCData` 等）。
- **`Enumerations/`**：协议中所有的枚举定义（如 `LlrpStatusCode`、`AirProtocolID` 等）。
- **`Codecs/`**：每个 Message 和 Parameter 对应的二进制 Codec 编解码逻辑。
- **`Registry/`**：将所有生成 Codec 批量绑定到 `LlrpCodecRegistry` 的模块类（如 `Llrp101StandardModule`）。
> **注意**：`LlrpNet.Protocol` 内的报文类与 Codec 均由生成器批量自动产出，**无需也不应当手动修改这部分 C# 代码**。如果协议有新增字段或厂商扩展，只需在 `definitions/` 目录下添加或更新对应的 XML 定义文件重新生成即可。

### 2.6 终端诊断工具 (`src/LlrpCli/`) —— [手写]
- 基于 `Spectre.Console` 和 `Spectre.Console.Cli` 构建的 Live Shell，提供指令补全提示链、灰色 Ghost 后缀、平滑光标控制以及深层 LLRP 报文树状分析器。

---

## 3. 手写与生成的区分原则总结

| 类别 | 包含模块 / 目录 | 修改与维护原则 |
|---|---|---|
| **手写核心逻辑** | `LlrpSdk`, `LlrpNet.Core`, `LlrpNet.ProtocolModel`, `LlrpNet.ProtocolGenerator`, `LlrpCli` | 正常的 C# 逻辑代码，随需求功能演进手写维护。 |
| **自动生成代码** | `LlrpNet.Protocol` (`Messages`, `Parameters`, `Codecs`, `Registry`) | 不手写 C# 代码；通过更新 `definitions/` 下的 XML 定义并调用生成工具更新。 |
