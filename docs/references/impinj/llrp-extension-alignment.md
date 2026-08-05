# Impinj LLRP 扩展参数对齐文档(SDK ↔ 官方 Impinjdef.xml)

> 数据来源:官方 `Impinjdef.xml`(Impinj LTK 10.30.0.0,`definitions/imports/xml/extensions/impinj/Impinjdef.xml`)。
> 目的:核对 `LlrpSdk.Extensions.Impinj` 每个自定义参数的实际发送位置是否符合官方的 `allowedIn` 声明,定位真机报错(如 `M_UnsupportedParameter` / 错误码 398)的根因。
> 核对日期:2026-08-05

## 背景

LLRP 的 custom 参数(`allowedIn`)声明了该参数允许被嵌套的位置。把参数发送到不允许的位置时,读者会拒绝请求(错误码 398)。此前 `ImpinjInventorySearchMode` 被发在 `SET_READER_CONFIG` 顶层,而官方声明其 `allowedIn = C1G2InventoryCommand`,已按此修正(挪到 inventory 命令级)。

## 对齐总表

图例:✅ = SDK 发送位置与官方 `allowedIn` 一致;❌ = 不一致(需要修正);— = 参数在 SDK 中未使用(不影响)。

| 参数(subtype) | 官方 allowedIn | SDK 实际发送位置 | 判定 |
|---|---|---|---|
| `ImpinjInventorySearchMode` (23) | `C1G2InventoryCommand` | C1G2InventoryCommand(`impinj.inventoryControl` 扩展) | ✅ 已修(2026-08-05) |
| **`ImpinjFixedFrequencyList` (26)** | `C1G2InventoryCommand` | C1G2InventoryCommand(`impinj.inventoryControl` 扩展) | ✅ 已修(2026-08-05) |
| **`ImpinjReducedPowerFrequencyList` (27)** | `C1G2InventoryCommand` | C1G2InventoryCommand(`impinj.inventoryControl` 扩展) | ✅ 已修(2026-08-05) |
| **`ImpinjLowDutyCycle` (28)** | `C1G2InventoryCommand` | C1G2InventoryCommand(`impinj.inventoryControl` 扩展) | ✅ 已修(2026-08-05) |
| `ImpinjGPIDebounceConfiguration` (36) | `GET_READER_CONFIG_RESPONSE` / `SET_READER_CONFIG` | `SET_READER_CONFIG` 顶层(`impinj.configuration` 扩展) | ✅ |
| `ImpinjLinkMonitorConfiguration` (38) | `GET_READER_CONFIG_RESPONSE` / `SET_READER_CONFIG` | `SET_READER_CONFIG` 顶层(`impinj.configuration` 扩展) | ✅ |
| `ImpinjReportBufferConfiguration` (39) | `GET_READER_CONFIG_RESPONSE` / `SET_READER_CONFIG` | `SET_READER_CONFIG` 顶层(`impinj.configuration` 扩展) | ✅ |
| `ImpinjAccessSpecConfiguration` (40) | `GET_READER_CONFIG_RESPONSE` / `SET_READER_CONFIG` / `AccessSpec` | `SET_READER_CONFIG` 顶层(`impinj.configuration` 扩展) | ✅ |
| `ImpinjAdvancedGPOConfiguration` (64) | `GET_READER_CONFIG_RESPONSE` / `SET_READER_CONFIG` | `SET_READER_CONFIG` 顶层(`impinj.configuration` 扩展) | ✅ |
| `ImpinjEnableTagPopulationEstimationAlgorithm` (1587) | `C1G2InventoryCommand` | C1G2InventoryCommand(`impinj.inventoryControl` 扩展) | ✅ |
| `ImpinjTagFilterVerificationConfiguration` (1586) | `C1G2InventoryCommand` | C1G2InventoryCommand(`impinj.inventoryControl` 扩展) | ✅ |
| `ImpinjTruncatedReplyConfiguration` (1583) | `C1G2InventoryCommand` | C1G2InventoryCommand(`impinj.inventoryControl` 扩展) | ✅ |
| `ImpinjGen2XInventoryConfig` (1596) | `C1G2InventoryCommand` | C1G2InventoryCommand(`impinj.inventoryControl` 扩展) | ✅ |
| `ImpinjGen2XTagSelectionConfig` (1597) | `C1G2InventoryCommand` | C1G2InventoryCommand(`impinj.inventoryControl` 扩展) | ✅ |
| `ImpinjGen2XTagSelectionEpcLength` (1598) | `ImpinjGen2XTagSelectionConfig` | 嵌套在 `ImpinjGen2XTagSelectionConfig` 内 | ✅ |
| `ImpinjEndpointICVerificationConfig` (1593) | `C1G2InventoryCommand` | C1G2InventoryCommand(`impinj.inventoryControl` 扩展) | ✅ |
| `ImpinjRampUpPowerBoost` (1608) | `C1G2InventoryCommand` | C1G2InventoryCommand(`impinj.inventoryControl` 扩展) | ✅ |
| `ImpinjTagReportContentSelector` (50) | `ROReportSpec` | ROReportSpec(`impinj.inventoryReport` 扩展) | ✅ |
| `ImpinjRequestedData` (21) | `GET_READER_CAPABILITIES` / `GET_READER_CONFIG` | `GET_READER_CONFIG` 查询 | ✅ |
| `ImpinjSubRegulatoryRegion` (22) | `GET_READER_CONFIG_RESPONSE` / `SET_READER_CONFIG` | 查询响应读回(facts) | ✅ |
| `ImpinjReaderTemperature` (37) | `GET_READER_CONFIG_RESPONSE` | 查询响应读回(facts) | ✅ |

未使用参数(不参与发送,无影响):`ImpinjBeaconConfiguration`、`ImpinjHubConfiguration`、`ImpinjDirection*`、`ImpinjLocation*`、`ImpinjPlacementConfiguration`、`ImpinjPolarizationControl`、`ImpinjAntennaConfiguration`、`ImpinjC1G2*Config` 等。

## 发现的问题(已于 2026-08-05 修复)

以下 3 个参数曾被 SDK 发在 `SET_READER_CONFIG` 顶层(通过 `ImpinjReaderConfiguration` / `impinj.configuration` 扩展),而官方 `allowedIn` 声明它们属于 **`C1G2InventoryCommand`**(每天线 / AISpec 级):

1. `ImpinjFixedFrequencyList` (26)
2. `ImpinjReducedPowerFrequencyList` (27)
3. `ImpinjLowDutyCycle` (28)

**症状**:真机执行 `SET_READER_CONFIG` 返回 `M_UnsupportedParameter`(错误码 398)。

## 修复方式(已完成)

完全复用 `ImpinjInventorySearchMode` 的迁移模式(2026-08-05 已完成):

- 三个字段从 `ImpinjReaderConfiguration`(`impinj.configuration`,reader 配置级)移到 `ImpinjInventoryControlOptions`(`impinj.inventoryControl`,inventory 命令级);
- `ImpinjInventoryControlConfigurator` 把它们编进 `C1G2InventoryCommand`(随 ROSpec 的 AISpec 发送);
- inventory 层 `ContributeQuery` 从 `C1G2InventoryCommandCustomItems` 读回;
- `ImpinjInventorySettingsBuilder` 新增 `FixedFrequency(...)` / `ReducedPowerFrequency(...)` / `LowDutyCycle(...)` 方法;
- 官方文档要求:参数内容必须跨 AISpec 内所有启用天线一致(见 `Impinjdef.xml` 中 `ImpinjFixedFrequencyList` / `ImpinjLowDutyCycle` 描述)。

## 代码位置索引

- ReaderConfig 层(保留正确参数):`src/LlrpSdk.Extensions.Impinj/Registration/ImpinjLlrpExtension.cs` 的 `BuildApplyParameters` / `ContributeQuery`;模型:`src/LlrpSdk.Extensions.Impinj/Settings/ImpinjReaderSettings.cs` 的 `ImpinjReaderConfiguration`(仅 GpiDebounce / LinkMonitor / ReportBuffer / AccessSpec / AdvancedGpos)。
- inventory 命令级管道(发送与读回):同文件 `Contribute`(`AddC1G2InventoryCommandCustomItem`)与 `ContributeQuery`(`C1G2InventoryCommandCustomItems`);模型:`src/LlrpSdk.Extensions.Impinj/Inventory/ImpinjInventoryControlOptions.cs`。
- 对齐参考:`definitions/imports/xml/extensions/impinj/Impinjdef.xml`(Impinj LTK 10.30.0.0)。
