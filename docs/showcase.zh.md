# LLRPCSharp 架构与能力图谱

[English](showcase.md)

本文是 LLRPCSharp 的项目展示文档，用于快速说明项目定位、架构边界和当前能力。当前真实实现状态以 [status.md](status.md) 为准，后续计划以 [roadmap.md](roadmap.md) 为准。

## 项目信息图

![LLRPCSharp Architecture and Capabilities Infographic](images/llrpcsharp_infographic.png)

## 核心架构优势

### 1. LTK.NET 思路的现代化改造

LLRPCSharp 保留传统 LTK.NET 工作流中有价值的部分：机器可读定义、生成的协议
类型和精确的线级编解码；同时面向现代 .NET 拆分定义模型、源码生成器、Codec
Registry、异步传输、版本 Adapter 和托管 Reader API。协议资产因此可以被离线
工具、厂商模块、SDK 和 Virtual Reader 复用，而不绑定到单一应用栈。

### 2. 现代化 .NET 底座

- **异步会话与分发模型**：底层会话、传输和事件分发围绕现代 .NET 异步模式构建，便于在长连接读写器场景下处理持续报文流。
- **面向低分配的协议边界**：传输和协议解析边界尽量使用 `ReadOnlyMemory<byte>` 等内存友好类型，降低报文处理过程中的不必要复制。

### 3. 干净的适配器边界

- **协议版本隔离**：LLRP 1.0.1、1.1 与 2.0 均通过 `ILlrpProtocolAdapter` 实现隔离；LLRP 2.0 协议资产与 SDK 适配器基线已就绪，待实机验收。
- **版本无关的上层入口**：应用层优先面对 `LlrpReader`、`InventorySettings`、ROSpec 和 AccessSpec 服务等托管 API，减少业务代码直接拼装协议报文的需要。

### 4. 可插拔的厂商扩展系统

- **厂商扩展注册**：例如 Impinj 扩展可通过 `UseImpinj()`、Zebra 扩展可通过 `UseZebra()` 接入生成的强类型 Codec 资产和扩展模块。
- **低侵入性扩展模型**：标准 LLRP 能力与厂商扩展保持分层，未启用厂商扩展时可继续使用通用 LLRP 驱动路径。

## 核心项目能力

### 1. 会话生命周期管理

- **连接与版本协商**：支持协议版本自动协商（1.0.1 / 1.1 / 2.0），并可按策略强制指定版本（1.0.1、1.1 或 2.0）。
- **有限自动重连**：提供 `LlrpAutomaticReconnectOptions` 和 `WithAutomaticReconnect(...)`，用于意外断线后的重连基线。重连成功后 SDK 会自动查询设备当前 ROSpec/AccessSpec 状态并对齐内部状态（只对齐设备现状，不重放之前的期望配置）。
- **托管状态同步**：Raw Protocol 操作后会使托管状态失效。需要观察并接管设备现有资源时使用 `SynchronizeStateAsync()`；需要强制恢复 SDK 托管时，直接把目标盘点配置传给 `StartInventoryAsync(settings)` 或带 `Inventory` 的 `ApplySettingsAsync(...)`，SDK 会删除标准资源并重建托管状态，无需先同步。

### 2. 进阶资源控制

- **ROSpec 生命周期服务**：`reader.RoSpecs` 提供 Add、Delete、Enable、Disable、Start、Stop、GetAll 等操作。
- **AccessSpec 生命周期服务**：`reader.AccessSpecs` 提供 Add、Delete、Enable、Disable、GetAll 等操作。
- **盘点入口**：`StartInventoryAsync(settings)` 部署并启动盘点，返回带独立报告流的 `InventorySession`；`StartInventoryAsync()` 启动之前已部署的盘点。无会话版的 `StartAsync` 重载已转为 internal（仅 Tag Access 与连接级流程使用）。`ReadTagReportsAsync` 与 `TagsReported` 观察整个连接；同一次盘点首次消费的报告出口取得所有权，其他出口在盘点停止前立即报错。

### 3. CLI 诊断与互操作套件

- **在线诊断**：`LlrpCli` 支持连接、监控和 Live Shell，用于快速观察设备交互。
- **离线协议工具**：支持 `inspect`、`decode`、`validate` 和 `encode`，可在不连接设备的情况下检查、解码（支持单帧与 `.pcapng` 抓包分析）、校验与构造 LLRP 报文（支持 1.0.1、1.1、2.0 及 Impinj、Zebra 扩展）。
- **原始帧观测**：`ILlrpFrameObserver` 和 `LlrpFrameJournal` 可在 Transport/Session 边界记录完整 TX/RX 帧，便于 Hex 诊断、审计和互操作分析。
