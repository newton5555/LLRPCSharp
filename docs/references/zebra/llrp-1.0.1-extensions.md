# Zebra FX9600 LLRP 1.0.1 厂商扩展参考与验证状态

> 用途：记录 `definitions/extensions/zebra.yml`、生成协议模块和 SDK 映射的 PDF 核对依据。
> **偏差与验证状态**见 [llrp-1.0.1-definition-drift.md](llrp-1.0.1-definition-drift.md)：官方 ICG `reserved` 位数与固件字节系统性偏移，仅部分参数经实机字节级标定。
> 本文不是生成后的协议定义；当前 SDK 已提供 Zebra 协议模块和最小设置/报告扩展，
> 但未验证的字段仍按下文的实机证据状态处理。
>
> 资料：`docs/references/zebra/interface-control-guide-en.pdf`，Zebra *RFID Reader Software Interface Control Guide*，Part 72E-131718-13EN，Revision A，2025-03。
> PDF 第 19 页明确写明 FX 系列读写器支持 EPCglobal LLRP **v1.0.1**，同时支持 LLRP custom extensions。

## 1. 结论与边界

### 1.1 FX9600 的协议基线

- 这份 Zebra 文档把 FX9600 的 LLRP 基线定义为 **LLRP 1.0.1**，不是 LLRP 1.1。
- 下面的扩展是“1.0.1 标准报文 + Zebra 自定义消息/参数”，不能当作 LLRP 1.1 的版本增量。
- 扩展定义应生成到 `V1_0_1` 版本命名空间，并与现有 1.0.1 标准模型组合。
- PDF 的产品矩阵同时列出 FX7400、FX7500、FX9500、FX9600、ATR7000 和 MC3000/MC9000；本文的 `Y/N` 只解释 **FX9600 列**。
- 当前仓库没有 Zebra LTK `def.xml`；协议模块已由 `definitions/extensions/zebra.yml`
  生成，`LlrpSdk.Extensions.Zebra` 已提供最小 SDK 映射。官方 PDF 与固件抓包仍有
  `reserved` 位宽偏差，未标定字段不得视为已完成。

### 1.2 公共线格式

| 项目 | 值 | 备注 |
|---|---:|---|
| Custom message type | `1023` | LLRP 1.0.1 `CUSTOM_MESSAGE` |
| Custom parameter type | `1023` | LLRP 1.0.1 `Custom` TLV |
| Vendor ID | `161` | PDF 对所有 Zebra/Moto 扩展均使用该值 |
| Message subtype | 8-bit | 由 `MessageSubtype` 承载 |
| Parameter subtype | 32-bit | 由 `ParameterSubtype` 承载 |

`zebra.yml` 的外层应类似下面的定义（仅示意，不是可直接生成的完整 YAML）：

```yaml
vendors:
  - name: Zebra
    vendorId: 161

customMessages:
  - name: MOTO_GET_TAG_EVENT_REPORT
    vendor: Zebra
    subtype: 2
    namespace: llrp
    members: []
```

标准 `CUSTOM_MESSAGE`/`Custom` 的 Vendor ID、Subtype 和 payload 由生成器处理；`members` 只描述该 subtype 的内容。字段类型应使用仓库定义支持的 `u1/u2/u4/u8/u12/u16/u32/u64/s16/u1v/u16v/utf8v` 等写法。标准参数引用（例如 `ROSpec`、`AccessSpec`、`LLRPStatus`、`AntennaID`）应使用 `parameter` 成员，而不是复制标准字段。

## 2. FX9600 支持的自定义消息

| 消息 | subtype | FX9600 | 作用与 payload |
|---|---:|:---:|---|
| `MOTO_GET_TAG_EVENT_REPORT` | 2 | Y | 客户端请求累积的标签事件；读写器返回标准 `RO_ACCESS_REPORT`，每个 `TagReportData` 可带 `MotoTagEventList`，发送后清除对应事件列表。无自定义字段。 |
| `MOTO_PURGE_TAGS` | 3 | Y | `PurgeTagEventStateOnly`（u1）和可选标签选择数据 `Data`。FX9600 不支持按显式标签列表 purge；空/省略列表表示全部。`Data` 的标准 1.0.1 类型需在 YAML 实现时对照 `Custom`/消息 payload 再确认。 |
| `MOTO_PURGE_TAGS_RESPONSE` | 4 | Y | `LLRPStatus`（1）。 |
| `MOTO_TAG_EVENT_NOTIFY` | 5 | Y | 事件通知消息，无自定义字段；当 `MotoTagReportMode=Report Notifications` 时异步发送。收到后客户端通常再发 subtype 2 获取标签事件报告。 |
| `MOTO_UPDATE_RADIO_FIRMWARE` | 10 | N | `FirmwareFilePath`（UTF-8）；FX9600 不支持。 |
| `MOTO_UPDATE_RADIO_FIRMWARE_RESPONSE` | 11 | N | `LLRPStatus`；FX9600 不支持。 |
| `MOTO_UPDATE_RADIO_CONFIG` | 12 | N | `ConfigFilePath`（UTF-8）；FX9600 不支持。 |
| `MOTO_UPDATE_RADIO_CONFIG_RESPONSE` | 13 | N | `LLRPStatus`；FX9600 不支持。 |
| `MOTO_GET_RADIO_UPDATE_STATUS` | 14 | N | 无自定义字段；FX9600 不支持。 |
| `MOTO_GET_RADIO_UPDATE_STATUS_RESPONSE` | 15 | N | `LLRPStatus` + `MotoRadioUpdateStatusInfo`；FX9600 不支持。 |

实现建议：协议层可以保留 PDF 中全部消息定义，以便解码未知/转发；FX9600 的高层能力判断必须拒绝 subtype 10–15，而不是尝试发送。

## 3. FX9600 参数支持矩阵

以下为 PDF Table 4 中 FX9600 列为 `Y` 的参数，按功能分组。Subtype 数字是 Zebra wire identity，不能重新编号。

### 3.1 能力、版本、配置与过滤

| 参数 | subtype | 典型上下文 | FX9600 |
|---|---:|---|:---:|
| `MotoGeneralRequestCapabilities` | 50 | `GET_READER_CAPABILITIES` 请求 | Y |
| `MotoGeneralCapabilities` | 1 | `GET_READER_CAPABILITIES_RESPONSE` | Y |
| `MotoAutonomousCapabilities` | 100 | `GET_READER_CAPABILITIES_RESPONSE` | Y |
| `MotoTagEventsGenerationCapabilities` | 120 | `GET_READER_CAPABILITIES_RESPONSE` | Y |
| `MotoFilterCapabilities` | 200 | `GET_READER_CAPABILITIES_RESPONSE` | Y |
| `MotoPersistenceCapabilities` | 300 | `GET_READER_CAPABILITIES_RESPONSE` | Y |
| `MotoAdvancedCapabilities` | 110 | `GET_READER_CAPABILITIES_RESPONSE` | Y |
| `MotoC1G2LLRPCapabilities` | 400 | `GET_READER_CAPABILITIES_RESPONSE` | Y |
| `MotoVersion` | 256 | `MotoVersionList` 子项 | Y |
| `MotoVersionList` | 504 | `GET_READER_CONFIG_RESPONSE` | Y |
| `MotoGeneralGetParams` | 51 | `GET_READER_CONFIG` 请求 | Y |
| `MotoAutonomousState` | 101 | `SET_READER_CONFIG` / 配置响应 | Y |
| `MotoRadioPowerState` | 500 | `SET_READER_CONFIG` / 配置响应 | Y |
| `MotoRadioTransmitDelay` | 511 | `SET_READER_CONFIG` / 配置响应 | Y |
| `MotoCustomCommandOptions` | 466 | `SET_READER_CONFIG` / 配置响应 | Y |
| `MotoPersistenceSaveParams` | 350 | `SET_READER_CONFIG` / 配置响应 | Y |
| `MotoDefaultSpec` | 102 | `SET_READER_CONFIG` / 配置响应 | Y |
| `MotoFilterRule` | 254 | `MotoFilterList` 子项 | Y |
| `MotoFilterTimeOfDay` | 251 | `MotoFilterTimeRange` 子项 | Y |
| `MotoFilterTimeRange` | 252 | `MotoFilterRule` 子项 | Y |
| `MotoUTCTimestamp` | 250 | `MotoFilterTimeRange` 子项 | Y |
| `MotoFilterRSSIRange` | 253 | `MotoFilterRule` 子项 | Y |
| `MotoFilterTagList` | 258 | `MotoFilterRule` 子项 | Y |
| `MotoFilterList` | 255 | `SET_READER_CONFIG` / 配置响应 | Y |

### 3.2 事件、报告与天线

| 参数 | subtype | 典型上下文 | FX9600 |
|---|---:|---|:---:|
| `MotoTagEventSelector` | 121 | `ROReportSpec` 子参数 | Y |
| `MotoTagReportMode` | 122 | `ROReportSpec` 子参数 | Y |
| `MovingStationaryTagReport` | **126** | `ROReportSpec` 子参数 | Y |
| `MotoROReportTrigger` | 125 | `ROReportSpec` 子参数 | Y |
| `MotoTagEventList` | 123 | `TagReportData` 子参数 | Y |
| `MotoTagEventEntry` | 124 | `MotoTagEventList` 子项 | Y |
| `MotoTagReportContentSelector` | 708 | `ROReportSpec` 子参数 | Y |
| `MotoC1G2ExtendedPC` | 450 | `TagReportData` | Y |
| `MotoTagGPS` | 1000 | `TagReportData`（由内容选择器启用） | Y |
| `MotoTagPhase` | 709 | `TagReportData`（由内容选择器启用） | Y |
| `MotoAntennaConfig` | 703 | `C1G2InventoryCommand` | Y |
| `MotoAntennaStopCondition` | 704 | `MotoAntennaConfig` 子项 | Y |
| `MotoAntennaPhysicalPortConfig` | 705 | `MotoAntennaConfig` 子项 | Y |
| `MotoAntennaQueryConfig` | 710 | `MotoAntennaConfig` 子项 | Y |
| `NXPBrandIDCheckConfig` | 711 | `C1G2InventoryCommand` | Y |
| `BrandIDCheckStatus` | 712 | `TagReportData` | Y |
| `MotoNXPEASAlarmSpec` | 463 | `InventoryParameterSpec` | Y |
| `MotoNXPEASAlarmNotification` | 464 | `ReaderEventNotificationData` | Y |
| `MotoConnectionFailureReason` | 465 | `ReaderEventNotificationData` | Y |

### 3.3 C1G2、NXP、Impinj 与 Gen2v2 访问操作

这些参数的请求项通常嵌在标准 `C1G2TagSpec`/`AccessCommand` 的 OpSpec 列表中；结果项返回在 `TagReportData` 中。

| 请求/配置参数 | subtype | 结果参数 | subtype | FX9600 |
|---|---:|---|---:|:---:|
| `MotoC1G2ExtendedPC` | 450 | — | — | Y |
| `MotoC1G2Recommission`（已废弃） | 451 | `MotoC1G2RecommissionOpSpecResult` | 452 | Y |
| `MotoC1G2BlockPermalock` | 453 | `MotoC1G2BlockPermalockOpSpecResult` | 454 | Y |
| `MotoNXPChangeEAS` | 455 | `MotoNXPChangeEASOpSpecResult` | 456 | Y |
| `MotoNXPSetQuiet` | 457 | `MotoNXPSetQuietOpSpecResult` | 458 | Y |
| `MotoNXPResetQuiet` | 459 | `MotoNXPResetQuietOpSpecResult` | 460 | Y |
| `MotoNXPCalibrate` | 461 | `MotoNXPCalibrateOpSpecResult` | 462 | Y |
| `MotoNXPChangeConfig` | 485 | `MotoNXPChangeConfigOpSpecResult` | 486 | Y |
| `MotoImpinjQT` | 487 | `MotoImpinjQTOpSpecResult` | 489 | Y |
| `MotoC1G2Authenticate` | 490 | `MotoC1G2AuthenticateOpSpecResult` | 491 | Y |
| `MotoC1G2ReadBuffer` | 492 | `MotoC1G2ReadBufferOpSpecResult` | 493 | Y |
| `MotoC1G2Untraceable` | 494 | `MotoC1G2UntraceableOpSpecResult` | 495 | Y |
| `MotoC1G2Crypto` | 496 | `MotoC1G2CryptoOpSpecResult` | 497 | Y |
| `QTData`（`MotoImpinjQT` 子项） | 488 | — | — | Y |

### 3.4 Zebra 专用 ROSpec 触发器

| 参数 | subtype | 结构 | FX9600 |
|---|---:|---|:---:|
| `ZebraROTriggerSpec` | 801 | 可选 `ZebraROSpecStartTrigger`、`ZebraROSpecStopTrigger` | Y |
| `ZebraROSpecStartTrigger` | 802 | 可选 `ZebraTimelapseStart`、`ZebraDistance` | Y |
| `ZebraTimelapseStart` | 803 | `TimeOfDay`（可变长度 UTF-8）+ `Period`（秒） | Y |
| `ZebraDistance` | 804 | `Value`（GPS 距离阈值） | Y |
| `ZebraROSpecStopTrigger` | 805 | 可选 `ZebraTimelapseStop` | Y |
| `ZebraTimelapseStop` | 806 | `TotalDuration` + `PeriodicDuration`（秒） | Y |

PDF 的语义章节没有明确写出 `ZebraROTriggerSpec` 应挂在哪一个标准父参数下；二进制页只描述了它自己的嵌套关系。写 `allowedIn` 前必须用 Zebra 真机抓包或更具体的 API 示例确认，不能仅凭名称猜测。

### 3.5 PDF 定义但 FX9600 不支持的项目

以下项目不要在 FX9600 高层配置中暴露为可用能力：

- `MotoLocationCapabilities` (130)、`MotoFindItem` (270)、`MotoLocationResult` (271)。
- `MotoRadioUpdateStatusInfo` (501)、`MotoRadioDutyCycle` (502)、`MotoRadioDutyCycleTable` (503)、`MotoSledBatteryStatus` (508)。
- 所有 Fujitsu 参数 `MotoFujitsu*` subtype **467–484**。
- 自定义消息 `MOTO_UPDATE_RADIO_FIRMWARE`/`_RESPONSE` (10–11)、`MOTO_UPDATE_RADIO_CONFIG`/`_RESPONSE` (12–13)、`MOTO_GET_RADIO_UPDATE_STATUS`/`_RESPONSE` (14–15)。

## 4. 字段模型速查

下面的字段摘要足以开始 YAML skeleton；精确 reserved bit 和 vector 计数仍以 PDF 二进制页和生成器验证为准。

### 4.1 能力和配置

| 参数 | 字段 |
|---|---|
| `MotoGeneralRequestCapabilities` (50) | `RequestedData:u8`；0 All，1 General，2 Autonomous，3 Tag events，4 Filtering，5 Persistence，6 C1G2 v1.2，7 Tag locating，8 Radio duty cycle，9 Versions，10 Advanced。 |
| `MotoGeneralCapabilities` (1) | `Version:u32`；`CanGetGeneralParams`、`CanReportPartNumber`、`CanReportRadioVersion`、`CanSupportRadioPowerState`、`CanSupportRadioTransmitDelay`、`CanSupportZebraTrigger`：u1。 |
| `MotoAutonomousCapabilities` (100) | `Version:u32`、`CanSupportAutonomousMode:u1`。 |
| `MotoTagEventsGenerationCapabilities` (120) | `Version:u32`；`CanSelectTagEvents`、`CanSelectTagReportingFormat`、`CanSelectMovingEvent`：u1。 |
| `MotoFilterCapabilities` (200) | `Version:u32`；RSSI、time-of-day、UTC timestamp 过滤能力三个 u1。 |
| `MotoPersistenceCapabilities` (300) | `Version:u32`；`CanSaveConfiguration`、`CanSaveTags`、`CanSaveEvents`：u1。 |
| `MotoAdvancedCapabilities` (110) | `Version:u32`；phase、GPS、zone、antenna RF、periodic tag report、sled battery、logical antenna 能力：u1。PDF 对最后一个字段的描述疑似复制错误。 |
| `MotoC1G2LLRPCapabilities` (400) | `Version:u32`；BlockPermalock、Recommissioning、UMI、NXP custom、Fujitsu custom、G2V2 能力：u1。PDF 把 `Custom` 拼成 `Cuxtom`，源字段命名需单独决定。 |
| `MotoVersion` (256) | `ModuleName:utf8v`、`ModuleVersion:utf8v`。 |
| `MotoVersionList` (504) | `MotoVersion` 列表 0-N。 |
| `MotoGeneralGetParams` (51) | `RequestedData:u8`；0 All，1 autonomous state，2 filter list，3 persistence，4 default spec，5 radio power，6 duty cycle，7 custom command options，9 sled battery。 |
| `MotoAutonomousState` (101) | `AutonomousModeState:u1`。 |
| `MotoRadioPowerState` (500) | `RadioPowerState:u1`；0 Off，1 On。PDF 语义小节标题误写成 `MobileRadioPowerState`。 |
| `MotoRadioTransmitDelay` (511) | `RadioTransmitDelay:u8`；0 Off，1 On_No_Tag，2 On_No_Unique_Tag。 |
| `MotoPersistenceSaveParams` (350) | `SaveConfiguration`、`SaveTagData`、`SaveTagEventData`：u1。 |
| `MotoCustomCommandOptions` (466) | `EnableNXPSetAndResetQuietCommands:u1`；默认关闭。 |
| `MotoDefaultSpec` (102) | `UseDefaultSpecForAutoMode:u1`；一个 `ROSpec`；`AccessSpec` 列表 0-N。 |

### 4.2 过滤和标签事件

| 参数 | 字段 |
|---|---|
| `MotoFilterRule` (254) | `RuleType:u8`（0 Inclusive，1 Exclusive，2 Continue）；可选 `MotoFilterRSSIRange`、`MotoFilterTimeRange`、`MotoFilterTagList`。至少需要 RSSI 或时间范围，规则可用 Continue 串联。 |
| `MotoFilterTimeOfDay` (251) | `Microseconds:u64`，自当天 00:00 起。 |
| `MotoUTCTimestamp` (250) | `Microseconds:u64`，UTC epoch。 |
| `MotoFilterTimeRange` (252) | `TimeFormat:u8`（0 time-of-day，1 UTC）；`Match:u8`（0 Within，1 Outside，2 大于下界，3 小于上界）；两个时间边界，边界类型由 `TimeFormat` 决定。 |
| `MotoFilterRSSIRange` (253) | `Match:u8` 同上；标准 `PeakRSSI` 两个边界。 |
| `MotoFilterTagList` (258) | `Match:u8`（0 Inclusive，1 Exclusive）；标签 EPC 数据列表。PDF 该节标题错误地写成 `MotoFilterRSSIRange`。 |
| `MotoFilterList` (255) | `UseFilter:u1`；`MotoFilterRule` 列表 0-N，PDF 限制最多 10 条。 |
| `MotoTagEventSelector` (121) | 三个事件选择 u8（Never/Immediate/Moderated）及相应的三个超时 u16（毫秒）。 |
| `MotoTagReportMode` (122) | `ReportFormat:u8`：0 No reporting，1 Report Notification，2 Report events。 |
| `MovingStationaryTagReport` (126) | `ReportMovingTag:u1`、`StrayTagModeratedTimeout:u16`。PDF 语义页把 subtype 错写成 122；二进制页 206 明确是 **126**。 |
| `MotoTagEventList` (123) | `MotoTagEventEntry` 列表 0-N。 |
| `MotoTagEventEntry` (124) | `EventType:u8`（0 Unknown，1 New Tag Visible，2 Tag Not Visible，3 Visibility Changed，4 Moving，5 Stationary）；`Microseconds:u64` UTC timestamp。 |
| `MotoROReportTrigger` (125) | `MotoReportTrigger:u8`：0 None，1 Upon_N_Seconds_Or_End_Of_AISpec，2 Upon_N_Seconds_Or_End_Of_ROSpec。仅当标准 `ROReportTriggerType` 为 none 时使用。 |

### 4.3 C1G2 / NXP / Gen2v2

| 参数 | 字段摘要 |
|---|---|
| `MotoC1G2ExtendedPC` (450) | `XPC:u16v`，XPC1 在前。仅在标准 `C1G2MemorySelector` 启用时出现在 TagReportData。 |
| `MotoC1G2Recommission` (451) | `OpSpecID:u16`、`KillPassword:u32`、`Operation:u8`（1–7）；已废弃。结果 452 为 `Result:u8`、`OpSpecID:u16`。 |
| `MotoC1G2BlockPermalock` (453) | `OpSpecID:u16`、`AccessPassword:u32`、`MB`、`ReadLock:u1`、`BlockPointer:u16`、`ReadBlockRange:u16`、`Mask:u16v`；结果 454 为 `Result:u8`、`OpSpecID:u16`、`Status:u16v`。`MB` 的 wire 宽度需按二进制页确认。 |
| `MotoNXPChangeEAS` (455) | `OpSpecID:u16`、`AccessPassword:u32`、`EASState:u1`；结果 456 为结果码和 `OpSpecID`。 |
| `MotoNXPSetQuiet`/`ResetQuiet` (457/459) | `OpSpecID:u16`、`AccessPassword:u32`；结果 458/460 为结果码和 `OpSpecID`。两项默认关闭，需先启用 subtype 466。 |
| `MotoNXPCalibrate` (461) | `OpSpecID:u16`、`AccessPassword:u32`；结果 462 另带 `ReadData:u16v`（前 512 bit）。 |
| `MotoNXPEASAlarmSpec` (463) | `AntennaIDs:u16v`，0 表示 AISpec 的全部天线；结果通过 `MotoNXPEASAlarmNotification` (464) 异步回报。 |
| `MotoNXPEASAlarmNotification` (464) | `EASAlarmCode:u64`（PDF 语义写 unsigned long integer，二进制页为 64 bit）、可选标准 `AntennaID`。 |
| `MotoNXPChangeConfig` (485) | `OpSpecID:u16`、`AccessPassword:u32`、`NXPChangeConfigWord:u16`；结果 486 为结果码、`OpSpecID` 和成功时的当前 word。 |
| `MotoImpinjQT` (487) | `OpSpecID:u16`、`AccessPassword:u32`、`QT_Write:u1`、`QT_Persist:u1`、可选 `QTData`；`QTData` (488) 为 `QT_Control:u16`。结果 489 复用 QT 字段。 |
| `MotoC1G2Authenticate` (490) | `OpSpecID`、`AccessPassword`、`SenResp`、`IncRespLen`、`CSI` 和 bit-string `Message`；结果 491 为结果码、`OpSpecID`、`DataBits`。精确 bit 宽度以 PDF 二进制页 225–226 为准。 |
| `MotoC1G2ReadBuffer` (492) | `OpSpecID`、`AccessPassword`、`WordPtr`、`BitCount`；结果 493 为结果码、`OpSpecID`、`DataBits`。 |
| `MotoC1G2Untraceable` (494) | `OpSpecID`、`AccessPassword`、`U`、EPC 显示/长度位、`TID`、`User`、`Range`；结果 495 为结果码、`OpSpecID`、`DataBits`。 |
| `MotoC1G2Crypto` (496) | `OpSpecID`、`AccessPassword`、`KeyID`、3 个 32-bit word 的 `IChallenge`、`CustomData:u1`、`Profile:u4`、`Offset:u12`、`BlockCount:u4`、`ProtMode:u4`；结果 497 为结果码、`OpSpecID`、`DataBits`。 |

### 4.4 报告、天线和 Zebra 触发器

| 参数 | 字段摘要 |
|---|---|
| `MotoTagGPS` (1000) | `longitude`、`latitude`、`altitude` 三个 32-bit wire 行；PDF 未说明符号、缩放和单位，需抓包/厂商 API 进一步确认后才能定最终 YAML 类型。 |
| `MotoAntennaConfig` (703) | 可选 `MotoAntennaStopCondition`、`MotoAntennaPhysicalPortConfig`、`MotoAntennaQueryConfig` 各 0-1；挂在 `C1G2InventoryCommand`，因为标准 `AntennaConfiguration` 不接受 custom extension。 |
| `MotoAntennaStopCondition` (704) | `AntennaStopTrigger:u8`（0 Dwell_Time，1 Number_Inventory_Cycles）、`AntennaStopConditionValue:u16`。 |
| `MotoAntennaPhysicalPortConfig` (705) | `PhysicalTransmitPort:u16`、`PhysicalReceivePort:u16`。 |
| `MotoTagReportContentSelector` (708) | `EnableZoneID`、`EnableZoneName`、`EnableAntennaPhysicalPortConfig`、`EnablePhase`、`EnableGPS`、`EnableMLTReport`：u1。 |
| `MotoTagPhase` (709) | `Phase:s16`；0x8000 表示 -π，0x7fff 表示 +π（接近 +π）。 |
| `MotoAntennaQueryConfig` (710) | `EnableSLAll:u1`、`EnableABFlip:u1`。 |
| `NXPBrandIDCheckConfig` (711) | `BrandID:u8`：0 Fail，1 Pass；挂在 `C1G2InventoryCommand`，仅适用于支持 BrandID 的 NXP UCode-8+。 |
| `BrandIDCheckStatus` (712) | `BrandID:u8`：0 Fail，1 Pass；PDF 文字误称为 `NXPBrandIDCheckConfig`。 |
| `ZebraROTriggerSpec` (801) | 可选 start/stop 两个子参数，各 0-1。父上下文未在 PDF 明确声明，见下文待确认项。 |
| `ZebraROSpecStartTrigger` (802) | 可选 `ZebraTimelapseStart`、`ZebraDistance`，各 0-1。 |
| `ZebraTimelapseStart` (803) | `TimeOfDay:utf8v`，格式 `HH:MM:SS`，空串等价于午夜；`Period` 为秒。二进制图显示为 32-bit 数值行，建议先按 `u32` 建模并用抓包验证。 |
| `ZebraDistance` (804) | `Value` 为 GPS 距离阈值；二进制图显示 32-bit 数值行，实际单位/缩放未在 PDF 说明。 |
| `ZebraROSpecStopTrigger` (805) | 可选 `ZebraTimelapseStop` 0-1。 |
| `ZebraTimelapseStop` (806) | `TotalDuration`、`PeriodicDuration`，单位秒；二进制图显示各 32-bit 数值行，建议按 `u32` 建模并验证。 |

## 5. allowedIn 建模清单

`allowedIn` 是后续真机互操作的关键，不能只按参数名字推断。根据 PDF 语义页，目前可以直接确定的关系如下：

| 扩展参数 | `allowedIn`（1.0.1 标准父项） |
|---|---|
| `MotoGeneralRequestCapabilities` | `GET_READER_CAPABILITIES` |
| `MotoGeneralCapabilities`、`MotoAutonomousCapabilities`、`MotoTagEventsGenerationCapabilities`、`MotoFilterCapabilities`、`MotoPersistenceCapabilities`、`MotoAdvancedCapabilities`、`MotoC1G2LLRPCapabilities` | `GET_READER_CAPABILITIES_RESPONSE` |
| `MotoGeneralGetParams` | `GET_READER_CONFIG` |
| `MotoAutonomousState`、`MotoRadioPowerState`、`MotoRadioTransmitDelay`、`MotoFilterList`、`MotoPersistenceSaveParams`、`MotoDefaultSpec`、`MotoCustomCommandOptions` | `SET_READER_CONFIG`；读回出现在 `GET_READER_CONFIG_RESPONSE` |
| `MotoVersionList` | `GET_READER_CONFIG_RESPONSE`；`MotoVersion` 只作为其子项 |
| `MotoTagEventSelector`、`MotoTagReportMode`、`MovingStationaryTagReport`、`MotoTagReportContentSelector`、`MotoROReportTrigger` | `ROReportSpec` |
| `MotoTagEventList` | `TagReportData`；`MotoTagEventEntry` 只作为其子项 |
| `MotoC1G2ExtendedPC`、`MotoTagGPS`、`MotoTagPhase`、`BrandIDCheckStatus` | `TagReportData` |
| `MotoAntennaConfig`、`MotoAntennaStopCondition`、`MotoAntennaPhysicalPortConfig`、`MotoAntennaQueryConfig`、`NXPBrandIDCheckConfig` | `C1G2InventoryCommand` |
| `MotoNXPEASAlarmSpec` | `InventoryParameterSpec` |
| `MotoNXPEASAlarmNotification`、`MotoConnectionFailureReason` | `ReaderEventNotificationData` |
| C1G2/NXP/Gen2v2 请求项 | 标准 C1G2 OpSpec 容器（最终以 1.0.1 XML 中对应 `allowedIn` 形状实现） |
| C1G2/NXP/Gen2v2 结果项 | `TagReportData` |

`ZebraROTriggerSpec` 以及其 802–806 子项在 PDF 语义章节没有给出父参数声明。先不要在 YAML 中强行写成 `ROSpec`、`ROReportSpec` 或 `ROBoundarySpec`；应通过真机抓包、Zebra API 示例或厂商补充资料确认后再固定。

## 6. 生成前必须处理的 PDF 歧义

1. **MovingStationaryTagReport subtype**：语义页 155 写成 122，二进制页 206 写成 **126**；采用 126。
2. **`MotoPersistenceCapabilities` 重复行**：Table 4 页 139–140 重复出现，定义一次即可，wire identity 仍为 300。
3. **`MotoFilterTagList` 标题错误**：页 151 的小节标题写成 `MotoFilterRSSIRange`，正文、subtype 258 和产品矩阵都指向 `MotoFilterTagList`。
4. **`MotoRadioPowerState` 标题错误**：正文参数名是 `MotoRadioPowerState` (500)，小节内部出现 `MobileRadioPowerState`；按表格和二进制页使用 `MotoRadioPowerState`。
5. **`BrandIDCheckStatus` 字段名错误**：正文把字段写成 `NXPBrandIDCheckConfig`，二进制页 231 和语义意图都是 `BrandID`。
6. **能力字段拼写**：`CanSupportNXPCuxtomCommands`、`CanSupportFujitsuCuxtomCommands` 含 `Cuxtom` 拼写错误；在 YAML 中是否保留原拼写要与仓库的命名兼容策略一起决定，不能无记录地改名。
7. **GPS 和 Zebra 时间/距离数值语义不完整**：二进制图能确认字段行和大致宽度，但 PDF 没有完整说明符号、缩放或单位；当前 YAML 先按 32-bit wire 行落地，生成前应使用抓包/SDK 文档验证。
8. **`MOTO_PURGE_TAGS.Data`**：PDF 引用 Data，但没有在该节给出完整 payload 形状；当前 YAML 按 `CUSTOM_MESSAGE` 的 bytes-to-end 保留，避免误建成标准 TLV 参数。
9. **生成器 bit-width 边界**：仓库当前定义模型没有 `u4/u6/u12`，因此 `MotoC1G2Untraceable` 和 `MotoC1G2Crypto` 在 YAML 中暂以 raw `Data:bytesToEnd` 保留；补齐这些字段类型后再结构化。
10. **`MotoRadioTransmitDelay` 二进制/语义不一致**：页 199 的 wire 图标为 `Type`/`Time`，语义页只给 `RadioTransmitDelay`；当前 YAML 保留语义字段并标注待抓包确认。

## 7. 建议的 YAML 落地顺序

1. `definitions/extensions/zebra.yml` 已建立 `vendors: Zebra/vendorId: 161` 和 4 个 FX9600 可用的事件消息（subtype 2–5）；先用生成器验证并保持该文件为唯一手工输入。
2. 先生成并测试能力、配置、过滤和标签事件参数；它们能覆盖能力探测、`SET/GET_READER_CONFIG` 和异步事件的基本闭环。
3. 再实现 `C1G2InventoryCommand` 的天线扩展、TagReportData 的报告扩展和 NXP/G2V2 OpSpec。
4. 在确认 `ZebraROTriggerSpec.allowedIn` 和 GPS/距离字段编码后，再加入 801–806。
5. 生成 `V1_0_1` 协议资产后，补充 registry/module、编解码 round-trip 测试和 FX9600 真机证据；没有真机证据时，不要把 `docs/acceptance/reader-interoperability.md` 中的计划行当成已验证事实。
6. 对 PDF 标为 `N` 的项目，即使协议层保留定义，也应在 FX9600 能力/配置层标记不可用，避免发送后得到 `M_UnsupportedParameter`。

## 7.1 真机实测证据(FX9600,固件 3.32.37.0,2026-08-14)

> 证据来源:`tools/LlrpSdk.LiveSmoke --zebra` 对 `192.168.40.88`(Manufacturer 161 / Model 96008)的
> `GET_READER_CAPABILITIES(All)` 响应抓包。固件省略了 PDF 二进制页中能力参数尾部的 24 位 reserved,
> 并将两个 PDF 标为 reserved 的位实际置 1;`zebra.yml` 已按实测修正(注释引用本节)。

| 参数(subtype) | 实测字节(数据段) | 与 PDF 的差异 |
|---|---|---|
| `MotoGeneralCapabilities` (1) | `00000001 9C` | Version=1;标志字节 `0x9C` = 6 标志 + reserved **2**(PDF 写 26) |
| `MotoAutonomousCapabilities` (100) | `00000001 80` | 标志字节 `0x80` + reserved **7**(PDF 写 31) |
| `MotoTagEventsGenerationCapabilities` (120) | `00000001 E0` | 标志字节 `0xE0` + reserved **5**(PDF 写 29) |
| `MotoFilterCapabilities` (200) | `00000001 F0` | 标志字节 `0xF0`:第 4 位被置 1,PDF 未文档化 → 落为 `DeviceSetCapabilityBit4` |
| `MotoPersistenceCapabilities` (300) | `00000001 E0` | 标志字节 `0xE0` + reserved **5**(PDF 写 29) |
| `MotoAdvancedCapabilities` (110) | `00000001 B2` | 标志字节 `0xB2` + reserved **1**(PDF 写 25) |
| `MotoC1G2LLRPCapabilities` (400) | `00000001 96` | 标志字节 `0x96`:第 1 位被置 1,PDF 未文档化 → 落为 `DeviceSetCapabilityBit1` |
| `MotoLocationCapabilities` (130) | `00000001 0000000000`(13 字节) | PDF 标 FX9600 N,但设备仍返回;当前定义未包含 → 解码为 `RawCustomParameter`,不阻塞 |

规律:能力参数线格式 = `Version(u32)` + 单标志字节(标志 + 字节边界补齐的 reserved);
PDF 二进制页的 reserved 计数全部多算 24 位。

配置参数(`GET_READER_CONFIG_RESPONSE` 抓包,同设备同日):

| 参数(subtype) | 实测 | 处理 |
|---|---|---|
| `MotoAutonomousState` (101) | 1 字节 | reserved 31 → 7 |
| `MotoPersistenceSaveParams` (350) | 1 字节(`0x60`) | reserved 29 → 5 |
| `MotoRadioPowerState` (500) | 1 字节(`0x80`) | reserved 31 → 7 |
| `MotoRadioTransmitDelay` (511) | 2 字节 | reserved 24 → 8 |
| `MotoDefaultSpec` (102) | 标志 1 字节 + 嵌套 ROSpec/AccessSpec | reserved 31 → 7 |
| `MotoTagReportMode` (122) | 1 字节 | 删 reserved |
| `MotoTagEventSelector` (121) | 9 字节(3 u8 + 3 u16) | 删 reserved |
| `MotoAntennaStopCondition` (704) | 3 字节(u8 + u16) | 删 reserved |
| `MotoAntennaQueryConfig` (710) | 2 字节 | reserved 30 → 14 |
| `MotoFilterRule` (254) | RuleType 之后是内联裸 `PeakRSSI` TV,非嵌套自定义 TLV | 尾部降级为 `bytesToEnd`,待 PDF 二进制页复核 |
| `MotoCustomCommandOptions` (466) | 4 字节全宽(`0x80000000`) | 保持 reserved 31 |
| `MotoTagReportContentSelector` (708) | 4 字节全宽 | 保持 reserved 26 |

配置查询闭环证据:`UseZebra()` 连接成功、7 个能力参数强类型解码、`zebra.configuration`
设置 contributor 真机往返(radioPower=True / transmitDelay=0 / autonomous=False /
persistence=False,True,True / nxpQuiet=True)。报告与盘点扩展参数(TagReportData 内的
Phase/GPS/XPC 等)仍需带标签盘点抓包验证。

## 8. 来源页索引

| 内容 | PDF 印刷页 |
|---|---:|
| FX 系列支持 LLRP 1.0.1 与 custom extensions | 19–20 |
| 自定义消息和消息语义 | 135–138 |
| FX9600 参数支持矩阵 | 139–142 |
| 参数语义定义 | 143–191 |
| 参数/消息二进制布局 | 192–233 |
| `MovingStationaryTagReport` subtype 126 的二进制依据 | 206 |
| GPS、天线、报告和 Zebra 触发器二进制布局 | 229–233 |
