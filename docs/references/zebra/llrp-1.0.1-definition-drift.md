# Zebra ICG 与 zebra.yml 定义偏差汇总 (definition drift)

> 状态:事实记录,非代码改动。本文只汇总"官方 PDF ↔ `definitions/extensions/zebra.yml` ↔ 实机字节"三方偏差,
> 作为后续逐参数严格验证的依据。来源:官方 ICG 72E-131718-13EN Rev A(2025-03) + FX9600 固件 3.32.37.0
> 实机抓包(192.168.40.88,Manufacturer 161/Model 96008,LLRP 1.0.1)。

---

## 0. 结论(一句话)

**官方 ICG 二进制页的 `reserved` 位数与固件实际字节存在系统性偏移(能力/配置参数整体多算 24 位,部分报告参数
多算 16/24 位);`zebra.yml` 已按抓包修正了一部分,但仍有大量报告/盘点参数缺乏实机字节级验证,不能把任何
单一来源(PDF / SDK / yml)当权威。**

---

## 1. 已实机字节级标定的参数(可信)

这些参数经 `tools/LlrpSdk.LiveSmoke --zebra` 抓包,`reserved`/字段宽度已按固件实际字节修正,`zebra.yml` 注释引用本节。

### 1.1 能力参数(GET_READER_CAPABILITIES 响应,规律:Version(u32)+单标志字节)

| subtype | 参数 | 实测数据段 | PDF reserved | 实测 reserved | 结论 |
|---|---|---|---|---|---|
| 1 | MotoGeneralCapabilities | `00000001 9C` | 26 | 2 | 已修 |
| 100 | MotoAutonomousCapabilities | `00000001 80` | 31 | 7 | 已修 |
| 120 | MotoTagEventsGenerationCapabilities | `00000001 E0` | 29 | 5 | 已修 |
| 200 | MotoFilterCapabilities | `00000001 F0` | 29 | 4(第4位实置1,PDF未文档化→DeviceSetCapabilityBit4) | 已修 |
| 300 | MotoPersistenceCapabilities | `00000001 E0` | 29 | 5 | 已修 |
| 110 | MotoAdvancedCapabilities | `00000001 B2` | 25 | 1 | 已修 |
| 400 | MotoC1G2LLRPCapabilities | `00000001 96` | 25 | 1(第1位实置1→DeviceSetCapabilityBit1) | 已修 |
| 130 | MotoLocationCapabilities | 13 字节 | PDF 标 N | 未定义(解码为 RawCustomParameter) | 未建模,不阻塞 |

### 1.2 配置参数(GET_READER_CONFIG_RESPONSE 抓包)

| subtype | 参数 | 实测 | 处理 |
|---|---|---|---|
| 101 | MotoAutonomousState | 1 字节 | reserved 31→7 |
| 350 | MotoPersistenceSaveParams | 1 字节(`0x60`) | reserved 29→5 |
| 500 | MotoRadioPowerState | 1 字节(`0x80`) | reserved 31→7 |
| 511 | MotoRadioTransmitDelay | 2 字节 | reserved 24→8 |
| 102 | MotoDefaultSpec | 标志1字节+嵌套 | reserved 31→7 |
| 122 | MotoTagReportMode | 1 字节 | 删 reserved |
| 121 | MotoTagEventSelector | 9 字节(3u8+3u16) | 删 reserved |
| 704 | MotoAntennaStopCondition | 3 字节(u8+u16) | 删 reserved |
| 710 | MotoAntennaQueryConfig | 2 字节 | reserved 30→14 |
| 466 | MotoCustomCommandOptions | 4 字节全宽(`0x80000000`) | 保持 reserved 31 |
| 708 | MotoTagReportContentSelector | 4 字节全宽 | 保持 reserved 26 |

### 1.3 报告参数(TagReportData 内,带标签盘点抓包,2026-08-14 补充)

| subtype | 参数 | 实测 | 处理 |
|---|---|---|---|
| 709 | MotoTagPhase | 2 字节(单 s16,如 `A0D4`=-24364) | 删 reserved 16 |
| 712 | BrandIDCheckStatus | 1 字节(单 u8,如 `FF`=255) | 删 reserved 24 |

报告闭环证据:开 `MotoTagReportContentSelector.EnablePhase` → 报告带 `MotoTagPhase` → SDK 投影 `zebra.phase`;
默认不开则 `(none)`。上述两参数是唯一经实机字节级验证的报告参数。

---

## 2. 尚未实机字节级验证的参数(存疑,仍信任 PDF)

以下参数 `zebra.yml` 中仍按 PDF 的 `reserved`/字段宽度定义,**未经抓包确认**,存在与已发现偏移同类的风险。

### 2.1 报告参数(最需优先验证)

| subtype | 参数 | 当前定义 | 风险点 |
|---|---|---|---|
| 450 | MotoC1G2ExtendedPC | XPC1(u16)+XPC2(u16),无 reserved | PDF 二进制页命名两 16-bit 字,但 XPC 本身是 1.1/2.0 能力,1.0.1 标准无——需确认 Zebra 扩展的实际编码 |
| 1000 | MotoTagGPS | longitude/latitude/altitude(u32×3) | PDF 显示三行 32-bit 但**未文档化符号/缩放**;字段宽度待抓包 |
| 123/124 | MotoTagEventList / MotoTagEventEntry | EventType(u8)+Microseconds(u64) | 未抓包;u64 时间戳编码待确认 |
| 708 其余标志 | MotoTagReportContentSelector 的 EnableZoneName/EnableAntennaPhysicalPortConfig/EnableGPS/EnableMLTReport | 各 u1 | 只验证了 EnablePhase/EnableZoneID 两路;其余标志触发对应报告参数时其编码未验证 |

### 2.2 尚未建模/未定线格式的参数

| subtype | 参数 | 状态 |
|---|---|---|
| 130 | MotoLocationCapabilities | PDF 标 FX9600 N 但设备仍返回;当前未定义,解码为 RawCustomParameter |
| — | MotoZoneInfo(zone 报告) | 设备 `CanSupportZone=true`,但需先在设备配置 zone 才会返回;zone 报告参数线格式未验证 |
| 254 | MotoFilterRule | RuleType 后内联裸 PeakRSSI TV 非嵌套自定义 TLV;尾部降级 bytesToEnd 待 PDF 复核 |

### 2.3 带 `reserved` 且无实测注释的参数(按 yml 行号)

`definitions/extensions/zebra.yml` 中仍有 `reserved` 且**无** `FX9600 capture` 注释的参数(即仍信任 PDF 原文),集中在:
- subtype 51 / 256 / 504 / 251 / 250 / 252 / 253 / 258 / 255 等过滤/持久化相关参数(行 388/399/443/445/456 等);
- subtype 451-462 / 485-497 / 801-806 等 NXP/G2V2 OpSpec 与结果参数(行 647/659/673/701/713/736/759/820/843/860 等);
- 这些参数**从未在 FX9600 上抓包**,其 `reserved` 位数完全按 PDF 转录,风险与已发现偏移同源。

---

## 3. 系统性偏移规律(用于预判)

1. **能力/配置参数**:PDF 二进制页的 `reserved` 计数整体多算 **24 位**(即 PDF 期望一个额外的 3 字节保留区,固件不发送)。
2. **部分参数**把 PDF 标为 reserved 的位实际置 1(如 `MotoFilterCapabilities` 第4位、`MotoC1G2LLRPCapabilities` 第1位),需按实机落在"未文档化标志位"字段。
3. **报告参数**(TagReportData 内)已发现两例 reserved 多算(MotoTagPhase 16 位、BrandIDCheckStatus 24 位),其余报告参数极可能同类偏移。
4. 结论:**任何未经抓包的 `reserved` 字段都不能当作已验证**,除非该参数已在本表 §1 出现。

---

## 4. 事实依据(抓包证据)

- 证据 1(`--vendor zebra` 握手抓包):8 个能力参数的数据段字节,见 `llrp-1.0.1-extensions.md` §7.1 表格。
- 证据 2(报告帧 `RO_ACCESS_REPORT`,67/94 字节):`03FF 000E 000000A1 000002C5 A0D4` = MotoTagPhase(subtype 709,data `A0D4`);
  `03FF 000D 000000A1 000002C8 FF` = BrandIDCheckStatus(subtype 712,data `FF`)。完整帧 hex 可经 `llrp decode <hex> --output text` 复核。
- 证据 3(固件版本):FX9600 固件 3.32.37.0(2026-04-14 release notes,`FX_ATR_3_32_37_Release_Notes.pdf`);
  实测 `GET_SUPPORTED_VERSION` 返回 `M_UnsupportedVersion(110)`,确认 LLRP 1.0.1(不支持 1.1)。
- 证据 4(XPC 归属):标准 LLRP 1.0.1 定义无 XPC(`definitions/imports/xml/llrp-1.0.1` 无 XPC 字样);
  `CanSupportXPC` 出现在 llrp-1.1.yaml / llrp-2.0-delta.yaml;`C1G2XPCW1/W2` 参数仅在 V2_0 生成。
  Zebra 的 `MotoC1G2ExtendedPC`(subtype 450)是厂商自定义,不是标准 XPC。

---

## 5. 待办(按优先级,不改代码仅记录)

1. 对 §2.1 报告参数(450/1000/123/124/708 其余标志)做带标签盘点抓包,逐参数标定 `reserved`/字段宽度,再改 `zebra.yml` 并 regenerate。
2. 对 §2.3 里带 `reserved` 且无实测注释的参数,逐个抓包(能力/配置/过滤/NXP OpSpec 等)标定。
3. 补 Zebra codec round-trip 测试(encode→decode 字节等价),作为回归防线。
4. 拿到更新的 Zebra ICG(如有 Rev B 或对应 1.1 的版本)时,对照本表重新核对。
