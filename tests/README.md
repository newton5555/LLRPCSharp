# LLRPCSharp 全套测试用例与质量规范指南 (`tests`)

本文档是 LLRPCSharp 解决方案中全部 17 个测试项目的**官方测试清单与断言手册**。详细说明了每一个测试项目、测试类（Test Class）及具体测试用例（`[Fact]` / `[Theory]`）的**测试目标、测试场景、输入条件与成功判定标准（Pass Criteria）**。

---

## 🏛️ 测试项目全景清单

解决方案包含 17 个专门的测试项目：

1. [**`LlrpNet.ProtocolModel.Tests`**](#1-llrpnetprotocolmodeltests-协议模型定义测试)
2. [**`LlrpNet.ProtocolGenerator.Tests`**](#2-llrpnetprotocolgeneratortests-代码生成器测试)
3. [**`LlrpNet.Core.Tests`**](#3-llrpnetcoretests-网络传输与底层帧处理测试)
4. [**`LlrpNet.Protocol.Tests`**](#4-llrpnetprotocoltests-llrp-101--11-标准协议编解码测试)
5. [**`LlrpNet.Protocol.Impinj.Tests`**](#5-llrpnetprotocolimpinjtests-impinj-厂商扩展编解码测试)
6. [**`LlrpSdk.Tests`**](#6-llrpsdktests-托管-sdk-门面与状态机测试)
7. [**`LlrpSdk.Extensions.Impinj.Tests`**](#7-llrpsdkextensionsimpinjtests-sdk-impinj-扩展管道测试)
8. [**`LlrpCli.Tests`**](#8-llrpclitests-cli-命令行工具测试)
9. [**`Interop.Tests`**](#9-interoptests-虚拟设备模拟互操作测试)
10. [**`LlrpSdk.Hardware.Tests`**](#10-llrpsdkhardwaretests-本地物理真机测试)
11. [**`LlrpNet.Protocol.Zebra.Tests`**](#11-llrpnetprotocolzebratests-zebra-厂商扩展编解码测试)
12. [**`LlrpSdk.Extensions.Zebra.Tests`**](#12-llrpsdkextensionszebratests-sdk-zebra-扩展管道测试)
13. [**`LlrpDevice.Abstractions.Tests`**](#13-llrpdeviceabstractionstests-设备合同与依赖边界测试)
14. [**`LlrpDevice.Server.Tests`**](#14-llrpdeviceservertests-通用设备端服务测试)
15. [**`LlrpDevice.Virtual.Tests`**](#15-llrpdevicevirtualtests-virtual-设备行为测试)
16. [**`LlrpDevice.Virtual.Hosting.Tests`**](#16-llrpdevicevirtualhostingtests-单台虚拟设备-sdk-门面测试)
17. [**`LlrpVirtualDevice.Cli.Tests`**](#17-llrpvirtualdeviceclitests-单台虚拟设备-cli-测试)

---

## 📖 每一个测试项的详细定义与 OK 判定标准

---

### 1. `LlrpNet.ProtocolModel.Tests` (协议模型定义测试)

#### 1.1 `LtkXmlDefinitionImporterTests` (LTK XML 协议定义导入器测试)
* **`[Fact] Import_Llrp101Definition_PreservesTypesMembersAndCardinality`**
  - **测试内容**：导入标准 `llrp-1x0-def.xml` 定义文件。
  - **输入条件**：解析 LLRP 1.0.1 官方 XML Schema 模板。
  - **OK 判定标准**：成功生成 `ProtocolDefinition`，精确匹配 42 个消息、111 个参数、42 个枚举和 14 个 Choice；`GET_READER_CAPABILITIES` MessageType 确认为 1 且响应类型为 `GET_READER_CAPABILITIES_RESPONSE`。
* **`[Fact] Import_DerivesTvAndTlvEncodingFromWireTypeRanges`**
  - **测试内容**：根据 Wire Type 数字范围识别二进制编码类型。
  - **输入条件**：解析包含 `EPC_96` (TypeNum 13) 和 `GeneralDeviceCapabilities` (TypeNum 137) 的定义。
  - **OK 判定标准**：`EPC_96` 自动推导为 `ParameterEncodingKind.Tv`；`GeneralDeviceCapabilities` 自动推导为 `ParameterEncodingKind.Tlv`。
* **`[Fact] Import_Stream_LeavesCallerStreamOpen`**
  - **测试内容**：验证内存流导入的生命周期。
  - **输入条件**：传入可读的 `MemoryStream`。
  - **OK 判定标准**：导入完成后调用者的 Stream 仍保持 open (`CanRead == true`)，未被提前 Dispose。
* **`[Fact] Import_InvalidRepeat_ReportsSourceAndLine`**
  - **测试内容**：校验非法 `repeat` 属性的异常提示。
  - **输入条件**：传入 `repeat="many"` 的非法 XML 片段。
  - **OK 判定标准**：准确抛出 `DefinitionImportException` 且属性 `LineNumber == 3`，消息中包含 `"repeat"` 关键字。
* **`[Fact] Import_Dtd_IsRejectedWithoutResolvingExternalResources`**
  - **测试内容**：防御 XXE 外部实体注入。
  - **输入条件**：包含 `<!DOCTYPE ... SYSTEM "file:///...">` 的恶意 XML。
  - **OK 判定标准**：抛出异常拒绝解析，禁止访问外部文件资源。

#### 1.2 `ProtocolDefinitionValidatorTests` (协议定义校验器测试)
* **`[Fact] Validate_Llrp101Definition_HasNoModelErrors`**
  - **测试内容**：校验标准 LLRP 1.0.1 定义模型的合规性。
  - **OK 判定标准**：返回的诊断列表中包含 0 个 `DefinitionDiagnosticSeverity.Error` 级别的错误。
* **`[Fact] Validate_ReportsDuplicateWireKeysAndDanglingReferences`**
  - **测试内容**：校验重复 MessageType 及悬空引用。
  - **输入条件**：构建包含重复 MessageType (1) 且引用不存在参数 `MissingParameter` 的无效模型。
  - **OK 判定标准**：精准报告诊断错误码 `LLRPM001` (重复 MessageType), `LLRPM016` (未定义引用), `LLRPM022` (缺失响应类型)。
* **`[Fact] Validate_RejectsVariableLengthTvParameter`**
  - **测试内容**：校验 TV 参数的变长字段约束。
  - **输入条件**：构建包含 `U8Vector` 变长字段的 TV 编码参数。
  - **OK 判定标准**：抛出诊断错误码 `LLRPM011`，Severity 判定为 `Error`。

---

### 2. `LlrpNet.ProtocolGenerator.Tests` (代码生成器测试)

#### 2.1 `ProtocolGeneratorTests` (代码生成器测试)
* **`[Fact] Generate_ProducesCompilableCSharpCode`**
  - **测试内容**：验证 C# 强类型协议代码生成。
  - **输入条件**：传入标准 LLRP 协议元模型运行 `ProtocolGenerator`。
  - **OK 判定标准**：生成的 `Messages.g.cs`, `Parameters.g.cs` 和 `Enumerations.g.cs` 能够通过 Roslyn 编译器干净无错地编译成 `.dll` 程序集。
* **`[Fact] Generate_PreservesMessageAndParameterTypeNumbers`**
  - **测试内容**：验证二进制协议编号注入。
  - **OK 判定标准**：生成的强类型 Class 内部静态属性 `MessageType` / `ParameterType` 与协议定义中的 TypeNum 100% 相同。

---

### 3. `LlrpNet.Core.Tests` (网络传输与底层帧处理测试)

#### 3.1 `LlrpHeaderCodecTests` (LLRP 10字节头部编解码测试)
* **`[Fact] EncodeHeader_DecodesHeader_Roundtrip`**
  - **测试内容**：验证 LLRP 协议头的位操作编解码。
  - **输入条件**：Version=1, MessageType=1 (GET_READER_CAPABILITIES), Length=20, MessageID=1001。
  - **OK 判定标准**：序列化为 10 字节大端序 `byte[]`，反序列化后 Version, MessageType, Length, MessageID 完美还原。

#### 3.2 `LlrpFrameFramerTests` (TCP 粘包与拆包测试)
* **`[Fact] FrameStream_SplitFramesAndMergedFrames_AssemblesCorrectly`**
  - **测试内容**：模拟复杂网络环境下的 TCP 粘包与拆包。
  - **输入条件**：将 1 个 30 字节的 LLRP 帧切分成 5 次小数据包注入，或将 3 个 LLRP 帧在同一次发送中合并注入。
  - **OK 判定标准**：`LlrpFrameFramer` 正确切分并成功拼装出 3 个完全独立且合法的 `LlrpFrame` 原始帧。

#### 3.3 `LlrpSessionTests` (Session 事务管理测试)
* **`[Fact] SendRequestAsync_Timeout_ThrowsTimeoutException`**
  - **测试内容**：验证请求超时响应逻辑。
  - **输入条件**：发送 Request 并设置 100ms 超时，对端模拟丢包不回复。
  - **OK 判定标准**：准确抛出 `TimeoutException`，且 Session 内存中取消绑定的 MessageID 挂起任务。
* **`[Fact] Observer_ReceivesAllSentAndReceivedFrames`**
  - **测试内容**：验证 `LlrpFrameJournal` 帧捕获观测器。
  - **OK 判定标准**：Session 发出的每一个请求帧和接收到的每一个响应帧都会 100% 被 `LlrpFrameJournal` 顺序捕获。

---

### 4. `LlrpNet.Protocol.Tests` (LLRP 1.0.1 / 1.1 标准协议编解码测试)

#### 4.1 `MessageCodecTests` (标准消息编解码测试)
* **`[Fact] ADD_ROSPEC_EncodeDecode_Roundtrip`**
  - **测试内容**：验证 `ADD_ROSPEC` 消息的全属性深层编解码。
  - **输入条件**：构造包含 ROSpecID=1, Priority=0, ROSpecStartTrigger, ROSpecStopTrigger 及 AISpec 的复杂强类型对象。
  - **OK 判定标准**：`Encode` 转 `byte[]` 后再 `Decode` 还原，对象内部所有深层嵌套属性与原始对象完全相同。
* **`[Fact] RO_ACCESS_REPORT_TagReportData_Decode`**
  - **测试内容**：验证标签盘点上报消息解析。
  - **输入条件**：传入真机返回的 `RO_ACCESS_REPORT` 原始二进制字节流。
  - **OK 判定标准**：成功解析出 `TagReportData` 参数，且内部包含合法的 `EPC96` 字节数组、`AntennaID` (例如 1) 和 `FirstSeenTimestamp`。

#### 4.2 `TvParameterCodecTests` (TV 格式参数编解码测试)
* **`[Fact] EPC96_TVParameter_EncodeDecode`**
  - **测试内容**：验证 TV 编码格式参数。
  - **输入条件**：1 字节 Header (Bit 7=1, Type=13) + 12 字节 EPCHex 标号。
  - **OK 判定标准**：二进制总长度刚好为 13 字节，解包出的 EPC Hex 字符串与原值一致。

---

### 5. `LlrpNet.Protocol.Impinj.Tests` (Impinj 厂商扩展编解码测试)

#### 5.1 `ImpinjExtensionCodecTests` (Impinj 扩展编解码测试)
* **`[Fact] ImpinjEnableExtensions_EncodeDecode`**
  - **测试内容**：验证 Impinj 开启扩展自定义消息。
  - **OK 判定标准**：生成的二进制 Header 中包含 VendorID 1000, Subtype 21 (`IMPINJ_ENABLE_EXTENSIONS`)，反序列化后参数校验通过。
* **`[Fact] ImpinjTagReportData_CustomParameter_Decode`**
  - **测试内容**：验证 Impinj 扩展标签数据解包。
  - **输入条件**：传入包含 Impinj 扩展字段的二进制数据包。
  - **OK 判定标准**：成功解码出 `ImpinjSerializedTID` (TID 16进制数据)、`ImpinjRFPhaseAngle` (相位角数字) 和 `ImpinjPeakRSSI` (信号强度 dBm)。

---

### 6. `LlrpSdk.Tests` (托管 SDK 门面与状态机测试)

#### 6.1 `LlrpReaderLifecycleTests` (SDK 生命周期测试)
* **`[Fact] ConnectAsync_WhenAlreadyConnected_ThrowsInvalidOperationException`**
  - **测试内容**：验证重复连接防护。
  - **OK 判定标准**：对已连接的 `LlrpReader` 再次调用 `ConnectAsync()` 抛出 `InvalidOperationException`。
* **`[Fact] StartInventoryAsync_WithoutSettings_ThrowsInvalidOperationException`**
  - **测试内容**：验证未配置盘点时的启动拦截。
  - **输入条件**：在刚连接、未调用 `ApplySettingsAsync` 的初始 Reader 上调用 `StartInventoryAsync()`。
  - **OK 判定标准**：拦截并抛出 `InvalidOperationException("No stopped SDK-managed inventory configuration is available to start.")`。

#### 6.2 `LlrpReaderConfigurationTests` (SDK 配置编排与下发测试)
* **`[Fact] ApplySettingsAsync_ValidSettings_CompilesAndDeploysManagedRoSpec`**
  - **测试内容**：验证 SDK 配置自动编译下发。
  - **输入条件**：配置天线列表 [1, 2]、连续盘点模式。
  - **OK 判定标准**：SDK 自动向底层发送 `ADD_ROSPEC` 并接收 Success 响应，自动调用 `ENABLE_ROSPEC`，Reader 内部资源状态切换为 `HighLevelConfigured`。

---

### 7. `LlrpSdk.Extensions.Impinj.Tests` (SDK Impinj 扩展管道测试)

#### 7.1 `ImpinjExtensionPipelineTests` (Impinj 扩展管线测试)
* **`[Fact] UseImpinj_ConfiguresImpinjCustomMessagesAndReportMapping`**
  - **测试内容**：验证 `UseImpinj()` 扩展管线。
  - **OK 判定标准**：SDK 连接握手成功后自动追加发送 `IMPINJ_ENABLE_EXTENSIONS` 消息；当接收到标签数据时，`TagReport.TryGetExtension<ImpinjTagReportData>()` 能成功返回包含 TID 和相位角的强类型扩展对象。

---

### 8. `LlrpCli.Tests` (CLI 命令行工具测试)

#### 1. `CliCommandParserTests` (CLI 命令解析器测试)
* **`[Fact] Parse_ConnectCommand_ExtractsHostAndPort`**
  - **测试内容**：验证 CLI 连接指令解析。
  - **输入文本**：`connect 192.168.1.100:5084`
  - **OK 判定标准**：成功解析出 Host=`192.168.1.100`，Port=`5084`。

---

### 9. `Interop.Tests` (虚拟设备模拟互操作测试)

#### 9.1 `VirtualDeviceSdkInteropTests` (虚拟设备端到端互操作测试)
* **`[Fact] EndToEnd_InventoryWorkflow_ReceivesTagReportsFromVirtualDevice`**
  - **测试内容**：验证完全离线环境下的端到端盘点全链路。
  - **输入条件**：启动本地 `VirtualLlrpDeviceHost`。
  - **OK 判定标准**：SDK 连接 `VirtualLlrpDeviceHost` -> 下发 ROSpec -> 启动盘点 -> `session.ReadReportsAsync()` 顺利接收并解包虚拟设备推送的标签数据。
* **`[Fact] DroppedAddRoSpecResponse_ProducesSdkRequestTimeout`**
  - **测试内容**：验证异常丢包响应处理。
  - **输入条件**：配置通用 `LlrpDeviceServer` 故意丢弃 `ADD_ROSPEC_RESPONSE`。
  - **OK 判定标准**：SDK 正确捕获并抛出 `TimeoutException`，没有发生死锁或死等。

---

### 10. `LlrpSdk.Hardware.Tests` (本地物理真机测试)

#### 10.1 `PhysicalReaderConformanceTests` (物理硬件一致性测试)
* **`[Fact] PhysicalReader_ConnectAndCapabilitiesConformance_Succeeds`**
  - **测试内容**：连接局域网真实 RFID 读写器硬件，校验协议握手与硬件能力。
  - **对齐标准**：**LLRP 1.1 Conformance Clause 4.1 (Capabilities)**。
  - **输入条件**：读取 `appsettings.local.json` 配置的真实设备 IP。
  - **OK 判定标准**：读写器成功连接；返回的天线总数 `MaxNumberOfAntennas > 0`；功率表 `TxPowers` 和模式表 `RfModes` 包含合法有效数据（若配置关闭或设备连不上则自动 `Skip`）。
* **`[Fact] PhysicalReader_InventorySessionLifecycle_Succeeds`**
  - **测试内容**：在真实设备上应用托管配置，启动真实盘点并采样标签。
  - **对齐标准**：**LLRP 1.1 Conformance Clause 4.2 (ROSpec Lifecycle)**。
  - **输入条件**：向真实设备下发天线盘点规则。
  - **OK 判定标准**：`ApplySettingsAsync` 成功下发 ROSpec；`StartInventoryAsync` 启动会话；`ReadReportsAsync()` 在真实射频环境下采样到真实的标签数据流；`StopAsync()` 正常停止盘点。

#### 10.2 `ManagedReaderHardwareTests` (托管 SDK 真机验收)

覆盖托管 `LlrpReader` 的核心应用路径。全部非破坏性：不执行 Kill、不可逆 Lock 或标签/设备配置写入。已在 Impinj R420 (firmware 6.4.1.x, LLRP 1.0.1) 上验证。

* **`[Fact] ManagedInventory_OnePhaseStart_ReportsTagsAndCleansUp`**
  - **测试内容**：一段式 `StartInventoryAsync(settings)`（部署并启动）→ 采样真实标签 → `ClearManagedSettingsAsync` 清理。
  - **输入条件**：从 `GetDefaultSettingsAsync` 取基线，天线取 `appsettings.local.json` 配置。**需要至少一枚标签在场**。
  - **OK 判定标准**：收到非空标签报告且 `EpcHex` 非空；清理后 `OperationState` 回到 `Idle`。
* **`[Fact] ManagedInventory_TwoPhaseStart_StartsDeployedInventory`**
  - **测试内容**：两段式 `ApplySettingsAsync`（部署不启动）→ `StartInventoryAsync()`（显式启动）。
  - **输入条件**：同上一用例；**需要至少一枚标签在场**。
  - **OK 判定标准**：部署后 `OperationState == Idle`（保持停止）；启动后采样到标签；`StopAsync` 停止。
* **`[Fact] TagAccess_ReadsTagMemory_NonDestructive`**
  - **测试内容**：先盘点定位一枚标签，再 `ReadTagMemoryAsync` 非破坏性读取 User 内存。
  - **输入条件**：盘点采样到标签（无标签则跳过，视为环境问题）。
  - **OK 判定标准**：`TagAccessResult.Operation.Success == true` 且返回读数据。
* **`[Fact] ImpinjSerializedTid_IsProjectedWhenRequested`**
  - **测试内容**：`UseImpinj()` + `IncludeSerializedTid` 时报告投影 `SerializedTidHex`。
  - **输入条件**：`SupportsImpinjExtensions == true`（Impinj 设备）；**需要至少一枚标签在场**。
  - **OK 判定标准**：至少一条报告 `SerializedTidHex` 非空（R420 6.4.1 已验证支持）。
* **`[Fact] QuerySettingsAsync_ReturnsDeviceConfiguration`**
  - **测试内容**：`QuerySettingsAsync` 返回设备配置快照。
  - **输入条件**：设备在线即可，只读。
  - **OK 判定标准**：`Configuration` 非空且包含天线列表。

> 注意：部署型调用（`StartInventoryAsync(settings)` / `ApplySettingsAsync` 带 Inventory）会删除设备上全部 ROSpec/AccessSpec（SDK 完全接管）。真机测试仅在专用测试设备上运行；共享设备请先保存配置快照。

---

### 11. `LlrpNet.Protocol.Zebra.Tests` (Zebra 厂商扩展编解码测试)

验证 Zebra 自定义消息/参数的 wire identity、Codec 注册和已确认的字段 round-trip。
未被真机抓包证实的字段不会被测试标记为设备兼容性证据。

### 12. `LlrpSdk.Extensions.Zebra.Tests` (SDK Zebra 扩展管道测试)

验证 `UseZebra()` 的能力、配置和 TagReport 扩展投影，以及扩展缺失时的显式行为。

### 13. `LlrpDevice.Abstractions.Tests` (设备合同与依赖边界测试)

- `ILlrpDevice`、Inventory 和 Tag Access 模型的版本中立合同；
- Abstractions 程序集不引用 `LlrpNet`、`LlrpSdk` 或 Virtual 产品程序集。

### 14. `LlrpDevice.Server.Tests` (通用设备端服务测试)

- 使用 `ScriptedLlrpDevice` 启动 Server，证明 Server 不依赖 Virtual；
- Server 生命周期、配置状态和多实例/设备状态隔离；
- 通用协议资源状态与设备行为边界。

### 15. `LlrpDevice.Virtual.Tests` (Virtual 设备行为测试)

- 同 seed/同轮次的 noisy RF 输出确定性；
- User memory Read/Write/BlockErase、Lock、Kill 状态闭环；
- 两个 `VirtualLlrpDevice` 的标签与内存状态隔离。

### 16. `LlrpDevice.Virtual.Hosting.Tests` (单台虚拟设备 SDK 门面测试)

- `IVirtualLlrpDeviceHost` 的公开生命周期契约；
- 单台 Server + Virtual 组合的精确端点、Start/Stop/Restart 和状态事件；
- 公开 Host 门面转发客户端连接与解码后的 LLRP 报文事件；
- 设备实例在重启后保持同一设备对象和可变标签状态；
- Dispose 后拒绝再次启动。

### 17. `LlrpVirtualDevice.Cli.Tests` (单台虚拟设备 CLI 测试)

- 根帮助与 `run --help` 的单设备生命周期说明；
- 无参数默认进入交互 Shell，以及 `server create/start/status/stop/destroy` 单设备生命周期；
- 单设备本地 JSON 配置校验，不绑定 TCP 端口；
- `llrp1.0.1_standard` 能力档案与独立 `default` 寻卡数据源；
- 创建 1.0.1 时端点使用默认值，端点覆盖只来自启动参数；
- 前台运行启动一台设备，并通过取消信号停止；
- `live` 自动创建/启动设备，进入交互 Shell，并输出生命周期、客户端和 RX/TX 报文；
- 内置预设与参数边界由 CLI 应用路径覆盖。

`Interop.Tests` 还覆盖真实 TCP `LlrpDevice.Server`/公开单台 Host 门面 → `LlrpSdk` 的 1.0.1/1.1
版本协商、ROSpec/AccessSpec、报告、标准 Tag Access 全操作、自动重连、丢响应、错误、
主动断开和截断帧；设备端 Handler 扩展也有回归覆盖。

`LlrpCli.Tests` 覆盖客户端 CLI 的帮助、解析和诊断入口；独立单设备 CLI 的帮助、配置
校验和前台启停由 `LlrpVirtualDevice.Cli.Tests` 覆盖。

## 🏃 运行测试

### 1. 运行全套自动化测试 (CI/CD 模式)

```powershell
dotnet build LLRPCSharp.slnx --no-restore
dotnet test LLRPCSharp.slnx --no-build
```

### 2. 仅运行本地物理真机测试 (`LlrpSdk.Hardware.Tests`)

配置 `tests/LlrpSdk.Hardware.Tests/appsettings.local.json` 中的 `"Enabled": true` 并指定读写器 IP：

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
