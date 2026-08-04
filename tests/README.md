# LLRPCSharp 全套测试用例与质量规范指南 (`tests`)

本文档是 LLRPCSharp 解决方案中全部 10 个测试项目的**官方测试清单与断言手册**。详细说明了每一个测试项目、测试类（Test Class）及具体测试用例（`[Fact]` / `[Theory]`）的**测试目标、测试场景、输入条件与成功判定标准（Pass Criteria）**。

---

## 🏛️ 测试项目全景清单

解决方案包含 10 个专门的测试项目：

1. [**`LlrpNet.ProtocolModel.Tests`**](#1-llrpnetprotocolmodeltests-协议模型定义测试)
2. [**`LlrpNet.ProtocolGenerator.Tests`**](#2-llrpnetprotocolgeneratortests-代码生成器测试)
3. [**`LlrpNet.Core.Tests`**](#3-llrpnetcoretests-网络传输与底层帧处理测试)
4. [**`LlrpNet.Protocol.Tests`**](#4-llrpnetprotocoltests-llrp-101--11-标准协议编解码测试)
5. [**`LlrpNet.Protocol.Impinj.Tests`**](#5-llrpnetprotocolimpinjtests-impinj-厂商扩展编解码测试)
6. [**`LlrpSdk.Tests`**](#6-llrpsdktests-托管-sdk-门面与状态机测试)
7. [**`LlrpSdk.Extensions.Impinj.Tests`**](#7-llrpsdkextensionsimpinjtests-sdk-impinj-扩展管道测试)
8. [**`LlrpCli.Tests`**](#8-llrpclitests-cli-命令行工具测试)
9. [**`Interop.Tests`**](#9-interoptests-虚拟读写器模拟互操作测试)
10. [**`LlrpSdk.Hardware.Tests`**](#10-llrpsdkhardwaretests-本地物理真机测试)

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
* **`[Fact] Parse_SettingsCommand_UpdatesDraftState`**
  - **测试内容**：验证 CLI 命令行草稿状态同步。
  - **输入文本**：`settings antennas 1,2`
  - **OK 判定标准**：CLI 内部暂存的 `DraftSettings.Antennas` 数组被成功更新为 `[1, 2]`。

---

### 9. `Interop.Tests` (虚拟读写器模拟互操作测试)

#### 9.1 `VirtualReaderSdkInteropTests` (虚拟读写器端到端互操作测试)
* **`[Fact] EndToEnd_InventoryWorkflow_ReceivesTagReportsFromVirtualReader`**
  - **测试内容**：验证完全离线环境下的端到端盘点全链路。
  - **输入条件**：启动本地内存 `VirtualReaderHost`。
  - **OK 判定标准**：SDK 连接 `VirtualReaderHost` -> 下发 ROSpec -> 启动盘点 -> `session.ReadReportsAsync()` 顺利接收并解包虚拟读写器推送的标签数据。
* **`[Fact] DroppedAddRoSpecResponse_ProducesSdkRequestTimeout`**
  - **测试内容**：验证异常丢包响应处理。
  - **输入条件**：配置 `VirtualReaderHost` 故意丢弃 `ADD_ROSPEC_RESPONSE`。
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

---

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
