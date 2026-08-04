# LLRPCSharp 测试体系指南 (`tests`)

本文档详细说明 LLRPCSharp 的全套测试体系、10 个测试项目的具体测试内容与职责划分，以及如何运行这些测试。

---

## 🏛️ 测试项目架构与详细测试项说明

解决方案包含 10 个专门的测试项目，覆盖从底层二进制协议编解码到真实物理硬件联调的完整质量链路：

---

### 1. `LlrpNet.ProtocolModel.Tests`（协议模型定义测试）
* **测试目标**：验证 LLRP 协议元模型（Meta Model）与定义解析器。
* **具体测试项**：
  - 验证 `llrp-1.1.yaml` / XML 导入器能否正确解析消息 (Messages)、参数 (Parameters) 和枚举 (Enums)。
  - 验证消息 Type Num / Parameter Subtype 的重名与冲突检测校验逻辑。
  - 验证定义架构（Schema Validation）字段约束。

---

### 2. `LlrpNet.ProtocolGenerator.Tests`（代码生成器测试）
* **测试目标**：验证根据 YAML/XML 协议定义自动生成 C# 强类型协议代码的生成器 (`ProtocolGenerator`)。
* **具体测试项**：
  - 验证生成器输出的 `.g.cs` 强类型 Class / Record 语法结构。
  - 验证嵌套 Parameter、数组列表与可选字段的编解码代码生成正确性。
  - 验证生成的枚举与位域 Flag 解析代码。

---

### 3. `LlrpNet.Core.Tests`（网络传输与核心帧处理测试）
* **测试目标**：验证网络层 `LlrpNet.Core` 的异步 TCP 连接生命周期与二进制帧处理。
* **具体测试项**：
  - **拆包与粘包 (Frame Framing)**：验证在 TCP 流式传输中，高并发/分片网络包能否按 LLRP 头部 Header Length 正确切帧。
  - **超时与重连机制**：验证请求超时 (`TimeoutException`) 触发与连接断开清理。
  - **帧观测器 (LlrpFrameJournal)**：验证收发原始 LLRP 二进制帧的捕获与日志记录。

---

### 4. `LlrpNet.Protocol.Tests`（LLRP 1.0.1 / 1.1 标准协议编解码测试）
* **测试目标**：验证 LLRP 1.0.1 及 1.1 标准二进制协议消息与参数的 Encode/Decode 准确性。
* **具体测试项**：
  - 验证 `ADD_ROSPEC`, `ENABLE_ROSPEC`, `START_ROSPEC`, `RO_ACCESS_REPORT` 等核心 LLRP 消息序列化成 `byte[]` 后再反序列化回强类型对象的一致性。
  - 验证位域 (Bit fields)、TLV / TV 格式参数的内存对齐与编码规范。
  - 验证 `CodecRegistry` 编解码器注册表的正确查找。

---

### 5. `LlrpNet.Protocol.Impinj.Tests`（Impinj 厂商扩展编解码测试）
* **测试目标**：验证 Impinj 扩展协议（Vendor Extension 1000）二进制编解码。
* **具体测试项**：
  - 验证 Impinj Custom Parameter (如 `ImpinjSerializedTID`, `ImpinjRFPhaseAngle`, `ImpinjPeakRSSI`) 的二进制解析。
  - 验证 Impinj Custom Message (如 `IMPINJ_ENABLE_EXTENSIONS`) 的编解码正确性。

---

### 6. `LlrpSdk.Tests`（托管 SDK 门面与状态机测试）
* **测试目标**：验证高层托管 SDK (`LlrpReader`) 的状态控制、参数校验与高层 API。
* **具体测试项**：
  - **状态机控制**：验证在未配置 `ApplySettingsAsync` 的情况下调用 `StartInventoryAsync` 抛出 `InvalidOperationException`。
  - **Settings 校验**：验证 `ReaderSettingsValidator` 对非法天线 ID、超出范围的功率/速率索引的拦截。
  - **默认配置合成**：验证 `ReaderSettingsDefaults.CreateGeneric()` 生成合法的默认 ROSpec 配置。

---

### 7. `LlrpSdk.Extensions.Impinj.Tests`（SDK Impinj 扩展管道测试）
* **测试目标**：验证 `LlrpSdk.Extensions.Impinj` 给 `LlrpReader` 挂载扩展的能力。
* **具体测试项**：
  - 验证调用 `.UseImpinj()` 后，扩展管线自动注入 `IMPINJ_ENABLE_EXTENSIONS` 消息。
  - 验证在 TagReport 中提取 `ImpinjTagReportData` 扩展数据（TID 序列号、相位角、RSSI）的强类型映射。

---

### 8. `LlrpCli.Tests`（CLI 命令行工具测试）
* **测试目标**：验证 `LlrpCli` 终端交互命令与自动化脚本指令。
* **具体测试项**：
  - 验证 Live Shell 命令解析（如 `connect`, `inventory`, `read`, `settings`）。
  - 验证设置草稿暂存、差异对比 (`diff`) 与配置文件导出/导入。

---

### 9. `Interop.Tests`（虚拟读写器模拟互操作测试）
* **测试目标**：配合内存中运行的虚拟读写器 (`LlrpVirtualReader`)，在无硬件环境下验证完整的 SDK-读写器交互流程。
* **具体测试项**：
  - 模拟正常盘点链路：`Connect` -> `GetCapabilities` -> `ADD_ROSPEC` -> `ENABLE_ROSPEC` -> `START_ROSPEC` -> 接收 `RO_ACCESS_REPORT`。
  - 模拟异常链路：读写器丢失响应、返回 `ROSpec` 校验失败错误消息时 SDK 的异常抛出与处理。

---

### 10. `LlrpSdk.Hardware.Tests`（物理真机测试）
* **测试目标**：连接网络中真实的 RFID 读写器硬件，进行标准一致性与真实射频（RF）环境下的联调测试。
* **对齐标准**：参阅 **LLRP 1.1 Conformance Specification** ([`../docs/references/standards/llrp-1.1/llrp_1_1-conformance-20101013.pdf`](../docs/references/standards/llrp-1.1/llrp_1_1-conformance-20101013.pdf))。
* **具体测试项**：
  - **Capabilities 真实握手**：验证真实设备的硬件能力（天线数、功率表 `TxPowers`、RF 模式 `RfModes`）提取成功。
  - **真机盘点生命周期**：验证真实设备下发的 ROSpec 被硬件接收，且现场标签能触发产生 `TagReport` 数据流。
  - **厂商扩展真机验证**：验证 Impinj 物理读写器开启扩展后，现场标签的 TID 序列号和相位角能真实解包输出。

---

## 🏃 运行测试

### 1. 运行自动化全套测试 (CI/CD 模式)

```powershell
dotnet build LLRPCSharp.slnx --no-restore
dotnet test LLRPCSharp.slnx --no-build
```

### 2. 仅运行本地物理真机测试 (`LlrpSdk.Hardware.Tests`)

配置 `tests/LlrpSdk.Hardware.Tests/appsettings.local.json` 启用测试并填入你的读写器 IP：

```json
{
  "HardwareTest": {
    "Enabled": true,
    "TargetReader": {
      "Ip": "192.168.1.100",
      "Port": 5084,
      "Vendor": "Impinj",
      "Antennas": [1, 2],
      "SupportsImpinjExtensions": true
    }
  }
}
```

运行命令：

```powershell
dotnet test tests/LlrpSdk.Hardware.Tests/LlrpSdk.Hardware.Tests.csproj
```
