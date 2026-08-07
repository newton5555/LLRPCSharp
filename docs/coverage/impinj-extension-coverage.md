# Impinj 扩展完成度（LLRP 1.0.1,验收基准 Impinj R420）

> 审计基准:2026-08-07（dev 分支）
> 权威依据:`definitions/imports/xml/extensions/impinj/Impinjdef.xml`（Impinj LTK 1.50,厂商号 25882）
> 验收基准:Impinj R420,固件 6.4.1.240,IP 192.168.41.134（证据见
> [acceptance/reader-interoperability.md](../acceptance/reader-interoperability.md)）
> 范围:仅 Impinj 扩展;LLRP 1.0.1 标准完成度见
> [llrp101-sdk-coverage.md](llrp101-sdk-coverage.md)。

## 结论摘要

- **消息级 4/4 全覆盖**:`IMPINJ_ENABLE_EXTENSIONS`（连接时自动发送并校验响应）、
  `IMPINJ_ENABLE_EXTENSIONS_RESPONSE` 在初始化闭环使用;`IMPINJ_SAVE_SETTINGS`/
  `IMPINJ_SAVE_SETTINGS_RESPONSE` 已生成注册,SDK 无直接入口（R420 上无保存语义需求）。
- **参数级 47/104 有 SDK 路径（45%）**:配置域、报告选择器、报告数据、盘点控制
  的核心参数已接入托管管线;57 个参数仅停留在协议编解码层,无高层模型
  （多为 R420 不支持的定位/xArray/QT 等高级特性）。
- **R420 6.4.1.240 实测**（2026-08-04 真机 10/10 通过）:扩展激活、托管配置查询/
  同值回写、SerializedTID/RFPhaseAngle/PeakRSSI 报告投影端到端通过;
  `TagPopulationEstimation` 实测 `M_UnsupportedParameter`,已从能力目录排除。
- **主要覆盖缺口**:能力判断依赖静态目录（`ImpinjInventoryCapabilityCatalog`）
  而非 GET_READER_CAPABILITIES 动态解析;能力类参数（5 个）无强类型消费。

## 消息级完成度（4 个自定义消息,CUSTOM_MESSAGE 1023 通道）

| 消息 | 协议层 | SDK 接线 | 说明 |
|---|---|---|---|
| IMPINJ_ENABLE_EXTENSIONS | ✅ | ✅ | 连接初始化自动发送（`ImpinjLlrpExtension.cs:66-68`） |
| IMPINJ_ENABLE_EXTENSIONS_RESPONSE | ✅ | ✅ | 校验成功后才继续能力初始化;R420 实测成功 |
| IMPINJ_SAVE_SETTINGS | ✅ | ◐ | 已生成注册,SDK 无发送入口（非破坏性约束下不主动保存设备配置） |
| IMPINJ_SAVE_SETTINGS_RESPONSE | ✅ | ◐ | 同上 |

## 扩展注册与管线

- `UseImpinj()` = `UseProtocolModule(ImpinjProtocolModule)` + `UseReaderExtension(ImpinjReaderExtension)`。
- `ImpinjReaderExtension` 实现 5 个 Contributor 接口,按
  `ManufacturerId==25882 && Version101` 自动匹配（`MutualExclusionGroup=reader-vendor`）。
- 4 个扩展键,JSON v1 版本化序列化:配置域 `impinj.configuration`、`impinj.facts`;盘点域
  `impinj.inventoryReport`、`impinj.inventoryControl`。

## Settings / Inventory / TagReport 覆盖

| 域 | 已接入 | 模型 |
|---|---|---|
| Reader 配置（GET/SET_READER_CONFIG Custom） | ✅ | `ImpinjReaderConfiguration`:GpiDebounce、LinkMonitor、ReportBufferMode、AccessSpec（BlockWriteWordCount/OpSpecRetryCount/OrderingMode）、AdvancedGpos;`ImpinjReaderFacts`:RegulatoryRegion、Temperature |
| 盘点控制（C1G2InventoryCommand Custom） | ✅ | `ImpinjInventoryControlOptions`:FixedFrequency、ReducedPower、LowDutyCycle、InventorySearchMode、TagPopulationEstimation、TagFilterVerification、TruncatedReply、Gen2X、EndpointICVerification、RampUpPowerBoost 等 + AllowUnverifiedFeatures |
| 报告请求（ROReportSpec Custom） | ✅ | `ImpinjInventoryReportOptions`:12 项（SerializedTid/RfPhaseAngle/PeakRssi/GPS/OptimizedRead/RfDoppler/TxPower/XpcWords/CrHandle/Id/EnhancedIntegra/EndpointICVerification）+ OptimizedReads + AllowUnverifiedFields |
| 报告解析（TagReport.Extensions） | ✅ | 11 个键投影;扩展方法 `TagReport.GetSerializedTidHex()`（`SerializedTidHex` 属性因 C# 14 工具链禁用） |
| 构建入口 | ✅ | `ImpinjInventorySettingsBuilder.Impinj(...)` 类型化配置 |

## R420 能力目录（静态 profile,`ImpinjInventoryCapabilityCatalog`）

匹配 `ModelId==2001002 && Firmware.StartsWith("6.4.1.")` → `R420Firmware641`;其余 Unknown（全 false,不启用任何可选扩展）。

| 能力位 | R420 6.4.1 值 | 实测依据 |
|---|---|---|
| TagReportContentSelector / SerializedTid / RfPhaseAngle / PeakRssi / TagFilterVerification | ✅ true | 真机报告投影通过（`E2801171200003EEADD309A0`、相位 1276、RSSI -6700） |
| TagPopulationEstimation | ❌ false | ADD_ROSPEC 返回 `M_UnsupportedParameter`（2026-07-30 记录） |
| GPS / OptimizedRead / RfDoppler / TxPower / XPCWords / CRHandle / ID / EnhancedIntegra / EndpointICVerification / TruncatedReply / Gen2X / RampUpPowerBoost | ❌ false | 无 R420 通过证据,默认关闭;`AllowUnverified*` 可强行下发由设备自裁 |

## 参数级覆盖（104 个 Custom 参数）

**已消费 47 个**:配置域 11、报告选择器 13、报告数据 11、盘点控制 12。

**无 SDK 路径 57 个**,分类:

- **能力/版本类（5）**:`ImpinjHubVersions`、`ImpinjDetailedVersion`、`ImpinjFrequencyCapabilities`、
  `ImpinjArrayVersion`、`ImpinjxArrayCapabilities` —— GET_READER_CAPABILITIES 返回,
  仅停留于 `ReaderCapabilities.CustomItems`,无强类型解析。
- **QT/BlockPermalock/MarginRead/Authenticate OpSpec（14）**:`BlockPermalock`、
  `GetBlockPermalockStatus`、`SetQTConfig`、`GetQTConfig`、`MarginRead`、`Authenticate`、
  `TIDParity`、`BLEVersion` 及其 OpSpecResult 等。
- **GPS/NMEA（4）**:`LoopSpec`、`GPSNMEASentences`、`GGASentence`、`RMCSentence`。
- **定位/方向（16）**:`LISpec`、`LocationConfig`、`LocationReporting`、`DISpec`、
  `DirectionSectors`、`DirectionConfig`、`ExtendedTagInformation`、`DirectionReportData` 等。
- **天线/xArray/IAM/Beacon（12）**:`TiltConfiguration`、`BeaconConfiguration`、
  `AntennaConfiguration`、`IntelligentAntennaManagement`、`PolarizationControl`、
  `DisabledAntennas`、`xArrayDirectionCapabilities` 等。
- **Hub/诊断/杂项（5）**:`HubConfiguration`、`DiagnosticReport`、`PlacementConfiguration`、
  `InventoryConfiguration`、`RFPowerSweep`。

## 缺口清单

1. **能力动态解析（主要缺口）**:GET_READER_CAPABILITIES 的 Impinj Custom 参数（5 个能力类）
   未强类型解析;能力决策走静态目录,新固件/新型号需手工扩目录。
   建议:解析 `ImpinjFrequencyCapabilities`、`ImpinjHubVersions`、`ImpinjDetailedVersion`
   等并纳入匹配上下文。
2. **SAVE_SETTINGS 无入口**:如需"保存设备配置"能力可增加显式 API（非破坏性约束下
   默认不做）。
3. **57 个未覆盖参数**:除能力类 5 个外,其余（QT/定位/xArray 等）为 R420 不支持的高级
   特性,按"设备不支持不接线"原则维持现状;如需支持新型号再按需扩充。

## 验证状态

- 真机:2026-08-04 R420（6.4.1.240）`LlrpSdk.Hardware.Tests` 10/10 通过,
  覆盖扩展激活、盘点、SerializedTID 投影、配置回写（`acceptance/reader-interoperability.md`）。
- 本日（2026-08-07）R430 同固件线冒烟:连接、扩展注册、配置查询正常。
