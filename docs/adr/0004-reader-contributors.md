# ADR 0004：读写器扩展 Contributor 管道

- 状态：Accepted（部分实施）
- 日期：2026-07-27

> 实施说明：本文中的 `QueryConfigurationAsync` / `ApplyConfigurationAsync`
> 是设计阶段名称。当前 Contributor 管道通过 `QuerySettingsAsync()` /
> `ApplySettingsAsync()` 接入 `ReaderSettings`。

## 决定

`ILlrpProtocolModule` 和 `IReaderExtension` 保持现有职责：前者在连接前注册 Codec，后者在读取标准身份后匹配并执行连接初始化。它们不直接承担托管配置、盘点编译和报告投影。

在已激活的 `IReaderExtension` 之上增加三类可选 Contributor：

```text
IReaderSettingsContributor
    查询、校验、合并、应用厂商配置片段

IInventoryContributor
    为版本 Adapter 编译出的 ROSpec / ReportSpec 添加厂商参数

ITagReportContributor
    将已解析的厂商结果参数投影到 TagReport.Extensions
```

核心 SDK 负责按已激活 Extension 的顺序调用 Contributor、检测冲突并保证标准配置或标准 TagReport 始终可用；厂商包负责具体字段、型号、固件和区域策略。

## 配置边界

`ReaderConfiguration` 保留版本无关的标准配置。厂商私有配置不通过派生 `ReaderConfiguration` 表达，而是作为带稳定 Contributor Id 的独立配置片段参与查询和应用：

```text
QuerySettingsAsync
  → 标准 ReaderConfiguration
  → 已激活 Settings Contributor 的配置片段

ApplySettingsAsync
  → 先校验和应用标准部分
  → 再按 Contributor 处理厂商片段
```

未安装或未激活的 Contributor 不应阻止标准配置查询和应用。未知厂商数据保留为 Raw 协议数据，不伪造为已知强类型配置。

## 调用顺序

```text
连接前：Protocol Module 注册 Codec
连接中：Reader Extension 匹配、主动初始化
连接后：Contributor 仅在所属 Extension 已激活时参与
```

同一 Contributor Id 只能注册一次；同一配置键由多个 Contributor 声明时必须失败，不允许以注册顺序覆盖。

## 后果

- `LlrpReader` 继续是跨厂商统一入口，不需要 `ImpinjReader : LlrpReader` 继承体系。
- `LlrpSdk.Extensions.Impinj` 可以先提供协议激活，后续再独立增加 Impinj Settings、Inventory 和 TagReport Contributor。
- `LlrpSdk.Extensions.Zebra` 可以复用同一模型。
- `ReaderConfigurationPatch`、默认 Profile 和厂商私有 Settings 继续遵循 ADR 0003，不进入生成的协议代码。

## 实施顺序

1. 已完成版本无关的标准 Tag Access API；
2. 已定义并接入 `ITagReportContributor`，让厂商报告字段可进入统一结果；
3. 已定义并接入 `IReaderSettingsContributor` 与配置片段模型；
4. 已在 Impinj 扩展中实现首个只读 Settings Contributor；
5. `IInventoryContributor` 的 ROReportSpec 管线已建立，并向 Contributor 提供初始化后的身份、能力与协商版本；Impinj 已接入首个报告选择器门控。所有厂商参数默认拒绝，只有型号/固件目录明确验证后才会写入 ROSpec。
6. R420 Model `2001002` Firmware `6.4.1.x` 的 ItemTest 抓包已验证支持 `ImpinjTagReportContentSelector`，条件是该 Custom 参数作为 `ROReportSpec` 的子项；SDK 已修正此前误放到 `AISpec` 的编译错误。
7. R420 已在空闲 ROSpec 槽位完成 SDK 端到端 Serialized TID、RF Phase Angle 与 Peak RSSI 报告验收，且停止后确认临时 ROSpec 清理；后续扩充经实机验证的型号/固件目录。厂商 Profile 与受控 Settings 写入仍待完成。
