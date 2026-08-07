# LLRP 1.0.1 SDK 完成度

> 审计基准:2026-08-07（dev 分支）
> 权威依据:`definitions/imports/xml/llrp-1.0.1/llrp-1x0-def.xml`（EPCglobal LLRP 1.0.1 二进制定义）
> 审计方法:协议定义 ↔ 生成代码（`*.g.cs`）↔ SDK 源码逐项核对;
> 关键能力门控与消息支持经 Impinj R430（固件 6.4.1.240）实机实测。

本文档回答一个问题:**LLRP 1.0.1 标准在 LlrpSdk 中完成到什么程度**。
它只记录事实与缺口;开发计划见 [roadmap.md](../roadmap.md),总体状态见
[status.md](../status.md)。

## 结论摘要

- 协议层（LlrpNet.Protocol）对 1.0.1 标准**全覆盖**:42 个标准消息、
  111 个标准参数全部生成、编解码并注册,与定义文件数量完全一致。
- SDK 层（LlrpSdk + LlrpCli）对 42 个标准消息 **42/42 全部有业务路径或已定案**:
  39 个完整接线;`CLOSE_CONNECTION` 接收侧、`ENABLE_EVENTS_AND_REPORTS`
  重连放行已实现（后者 R430 真机验证）;`CLIENT_REQUEST_OP` 经实测（R430 固件
  6.4.1.240）设备不支持,SDK 不接线、仅提供 `SupportsClientRequestOpSpec` 门控。
- 参数层:111 个参数全部生成;SDK 消费了其中大部分,有若干标准子参数
  生成但未投影（详见「参数消费缺口」）。

## 消息级完成度（42 个标准消息）

| 消息 | 协议层 | SDK 接线 | 说明 |
|---|---|---|---|
| GET_READER_CAPABILITIES / _RESPONSE | ✅ | ✅ | 初始化自动拉取全部能力;`Llrp101ProtocolAdapter.cs:25/46` |
| ADD / DELETE / ENABLE / DISABLE / START / STOP_ROSPEC（+RESPONSE） | ✅ | ✅ | `IRoSpecService` 全生命周期;`Llrp101ProtocolAdapter.cs:174-214` |
| GET_ROSPECS / _RESPONSE | ✅ | ✅ | `:216` |
| ADD / DELETE / ENABLE / DISABLE_ACCESSSPEC（+RESPONSE） | ✅ | ✅ | `IAccessSpecService`;`:225-255` |
| GET_ACCESSSPECS / _RESPONSE | ✅ | ✅ | `:257` |
| GET_READER_CONFIG / _RESPONSE | ✅ | ✅ | `QuerySettingsAsync` 等;`:298` |
| SET_READER_CONFIG / _RESPONSE | ✅ | ✅ | `ApplySettingsAsync`;`:397` |
| GET_REPORT | ✅ | ✅ | `GetTagReportsAsync` 主动拉取;`:164` |
| RO_ACCESS_REPORT | ✅ | ✅ | 报告翻译;`Llrp101TagReportTranslator.cs` |
| KEEPALIVE / KEEPALIVE_ACK | ✅ | ✅ | 自动应答 + 可选监控;`LlrpReader.cs:1939/1828` |
| READER_EVENT_NOTIFICATION | ✅ | ✅ | 接收 + 6 种子事件解析（含 ReaderExceptionEvent）;`LlrpReader.cs:3233` |
| ERROR_MESSAGE | ✅ | ✅ | 收到时抛操作异常;`LlrpReader.cs:3112` |
| CUSTOM_MESSAGE（1023） | ✅ | ◐ | 协议层含 `RawCustomMessage` 编解码与注册机制;SDK 无内置业务用法,可经 `Protocol`/`ConfigureProtocol` 扩展 |
| CLIENT_REQUEST_OP / CLIENT_REQUEST_OP_RESPONSE | ✅ | ⬇ | 不接线:实测 R430 固件 6.4.1.240 拒绝 ClientRequestOpSpec（M_UnsupportedParameter）;仅提供 `SupportsClientRequestOpSpec` 门控 |
| CLOSE_CONNECTION / CLOSE_CONNECTION_RESPONSE | ✅ | ◐ | 接收侧:消息泵识别设备主动关闭,回 CLOSE_CONNECTION_RESPONSE,`ConnectionChanged` 携带 `DeviceInitiatedClose`（`LlrpReader.cs:1946`）;发送侧不做,`DisconnectAsync` 直接关 TCP |
| ENABLE_EVENTS_AND_REPORTS | ✅ | ✅ | 重连后若设备配置 hold=true 则发送放行（`EnsureEventsAndReportsEnabledOnReconnectAsync`）;R430 真机验证 |

## 功能维度完成度

| 功能 | 状态 | 说明 |
|---|---|---|
| 连接 / 断开 / 自动重连 | ✅ | 重连后对齐设备 ROSpec/AccessSpec 状态;收到 CLOSE_CONNECTION 时识别设备主动关闭并回执,`ConnectionChanged` 携带 `DeviceInitiatedClose`;主动断开维持直接关 TCP |
| 能力查询 | ✅ | GeneralDeviceCapabilities、Regulatory/UHFBand、LLRPCapabilities、C1G2LLRPCapabilities 均消费 |
| Settings 应用 / 查询 | ✅ | AntennaConfiguration、KeepaliveSpec、ReaderEventNotificationSpec、GPOWriteData、EventsAndReports、Custom |
| ROSpec 生命周期 | ✅ | 编译含 ROBoundarySpec + AISpec + InventoryParameterSpec + C1G2InventoryCommand + ROReportSpec |
| AccessSpec 生命周期 | ✅ | 标准 Tag Access 复用;含 C1G2TargetTag 选择、AccessReportSpec |
| 标准 Tag Access | ✅ | Read / Write / BlockWrite（自动降级）/ Lock / Kill / BlockErase |
| KeepAlive | ✅ | 收 KEEPALIVE 自动回 ACK;liveness 监控超时仅发事件 |
| TagReport 流 | ✅ | channel 流 + 事件 + 主动 GET_REPORT 拉取 |
| 事件订阅 | ✅ | 解析 6/11 种;ReaderExceptionEvent 已暴露为 `ReaderExceptionOccurred`,其余 5 种按分析不做（可经 `ReadMessagesAsync` 取原始数据） |
| 客户端请求式访问 | ⬇ | 不接线（实测设备不支持）,仅 `SupportsClientRequestOpSpec` 门控 |

## 参数消费缺口（已生成、未投影到 SDK 模型）

- **事件子参数（已收窄）**:`ReaderExceptionEvent` 已投影为
  `ReaderExceptionOccurred` 事件（`LlrpReader.cs:3253`）;其余 5 种
  （Hopping/RFSurvey/AISpec/ConnectionAttempt/ConnectionClose）按分析不做
  （ConnectionAttempt 角色不符、ConnectionClose 与 CLOSE_CONNECTION 重叠、
  RFSurveyEvent 设备不支持、Hopping/AISpec 价值低）,可经原始 `ReadMessagesAsync`
  取得。事件订阅开关（`HoppingEventEnabled`、`ReaderExceptionEventEnabled` 等）
  Settings 层已支持。
- **TagReportData 子参数（已收窄）**:`C1G2_PC` 已投影为 `TagReport.PcBits`
  （配合 `IncludePcBits` 请求）;`C1G2_CRC`、`C1G2SingulationDetails`
  按分析低价值不做（可经原始 `ReadMessagesAsync` 拿到）。
- **RF 调查**:`RFSurveyReportData` / `FrequencyRSSILevelEntry` 只翻译
  TagReportData,RF 调查报告不翻译（`Llrp101ProtocolAdapter.cs:161-162`）。
- **GET_READER_CONFIG 响应未投影**:`ROReportSpec`、`AccessReportSpec`、
  `LLRPConfigurationStateValue`（`:322-394`）。
- **客户端请求参数**:`ClientRequestOpSpec`、`ClientRequestResponse`、
  `ClientRequestOpSpecResult` 随 CLIENT_REQUEST_OP 一并未接线。

## 缺口清单

### 已处理（不再待办）

- **CLOSE_CONNECTION 接收侧（已完成）**:消息泵识别设备主动关闭,best-effort 回
  `CLOSE_CONNECTION_RESPONSE`,`ConnectionChanged` 携带 `DeviceInitiatedClose`。
  **发送侧按设计决策不做**:主动断开维持直接关 TCP（CLOSE_CONNECTION 主要是
  Reader 通知 Client 的机制,等待回执引入超时与兼容性风险）。
- **ENABLE_EVENTS_AND_REPORTS（已接线）**:重连后若设备配置了
  `HoldEventsAndReportsUponReconnect=true`,SDK 在状态同步后发送一次 ENABLE 放行
  （`EnsureEventsAndReportsEnabledOnReconnectAsync`,内部逻辑,不新增公开 API）;
  `LlrpSdk.Hardware.Tests` 在 R430 真机验证通过（hold=true → 重连 → 报告恢复）。
- **CLIENT_REQUEST_OP（降级:不接线,仅门控）**:R430 固件 6.4.1.240 实测拒绝
  `ClientRequestOpSpec`（ADD_ACCESSSPEC 返回 `M_UnsupportedParameter`,
  `//ClientRequestOpSpec : unsupported`）,且 `SupportsClientRequestOpSpec=False`。
  SDK 不实现该访问模式,仅提供 `ReaderCapabilities.SupportsClientRequestOpSpec`
  门控（1.0.1/1.1 均提取）。探针工具 `tools/LlrpSdk.Probe.ClientRequestOp`
  可复测其他型号。
- **RF 调查报告翻译（降级:低优先级）**:R430 `CanDoRFSurvey=False`,Impinj 设备
  普遍不支持 RF 调查;SDK 提供 `ReaderCapabilities.CanDoRfSurvey` 门控,
  翻译缺口在 Impinj 设备上无触发机会。

### 剩余待办

1. **ReaderExceptionEvent（已完成，2026-08-07）**:`ReaderExceptionOccurred` 事件
   已暴露（Message + ROSpec/SpecIndex/InventoryParameterSpecID/天线/AccessSpec/
   OpSpec 上下文,1.0.1/1.1 均接线,单测覆盖）。
   其余事件子参数按分析**不做**（ConnectionAttempt 角色不符、ConnectionClose 与
   CLOSE_CONNECTION 重叠、RFSurveyEvent 设备不支持、Hopping/AISpec 价值低,
   均可经 `ReadMessagesAsync` 取原始协议对象）。
2. **TagReport 补充投影**（部分完成）:`C1G2_PC` 已投影为
   `TagReport.PcBits`（1.0.1/1.1 翻译器均提取,配合 `IncludePcBits` 请求）。
   剩余:`C1G2_CRC`、`C1G2SingulationDetails`（低价值,可暂缓或不做）。
