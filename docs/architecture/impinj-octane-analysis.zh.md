# Impinj Octane SDK 架构分析与 LLRPCSharp 设计对比参考

本文档记录 Impinj 官方 Octane SDK / LTK .NET 的架构设计、对象模型、配置文件格式与底层 LLRP 协议映射关系，并阐述 `LLRPCSharp` 现代化解耦设计的哲学依据与演进路线。

---

## 一、 Impinj LLRP 生态三层架构

在 Impinj 官方技术体系中，RFID 读写器软件栈分为两层，而 `LLRPCSharp` 实现了全栈替代与现代化升级：

```text
┌─────────────────────────────────────────────────────────────────────────────────┐
│                      应用层代码 (Application Code)                              │
└───────────────────────────────┬─────────────────────────────────────────────────┘
                                │
        ┌───────────────────────┴───────────────────────┐
        ▼                                               ▼
【Impinj Octane SDK 体系】                   【LLRPCSharp 现代化体系】
 ├─ ImpinjReader (上层管理句柄)               ├─ LlrpReader (现代 C# 核心入口)
 ├─ Settings (打包大对象)                     ├─ ReaderConfiguration (硬件配置)
 └─ 基于 LTK .NET 运行时                     ├─ InventorySettings (盘点意图)
        │                                     ├─ LlrpNet.Protocol (协议编解码)
        ▼                                     └─ UseImpinj() (厂商扩展管道)
【LTK .NET 底层】                                       │
 └─ 依赖生成报文/Endpoint 收发                            ▼
        │                                     【LLRP 二进制协议 / 网络套接字】
        └───────────────────────┬───────────────────────┘
                                │
                                ▼
                   【Impinj R420 / R700 / xArray 等读写器】
```

| 维度 | LTK .NET | Impinj Octane SDK | LLRPCSharp |
|---|---|---|---|
| **定位** | 底层 LLRP 二进制编解码与报文生成工具包 | Impinj 官方上层 C# 管理 SDK | 跨厂商现代 LLRP 协议栈与 SDK |
| **主要入口** | `LLRPClient` / `Message` / `Parameter` | `ImpinjReader` / `Settings` | `LlrpReader` |
| **报文处理** | 手工构造与收发原生的 Message / Parameter | 使用底层 LTK 隐式生成发给设备 | `LlrpNet.Protocol` 强类型编解码 + `UseImpinj()` 扩展管道 |
| **模型设计** | 严格贴合 LLRP 1.0.1 原生结构 | 将硬件配置、盘点意图与报告选择打成单一 `Settings` 大对象 | 将硬件配置 (`ReaderConfiguration`) 与盘点意图 (`InventorySettings`) 严格解耦 |
| **持久化格式** | LTK 原生 XML 报文dump | C# `Settings` 属性序列化 `.profile` XML | 现代强类型 JSON (`InventorySettingsSerializer`) |

---

## 二、 Octane SDK 的“打包 Settings”与底层 LLRP 报文映射

Impinj Octane SDK 导出的 `.profile` 配置文件（如 `Default.profile`）并非原生的 LLRP 协议 XML，而是 Impinj C# 类 `Impinj.OctaneSdk.Settings` 属性的 XML 序列化结果。

当调用 `ImpinjReader.ApplySettings(settings)` 时，Octane SDK 内部会将该单一大对象拆解并隐式下发两组 LLRP 报文：

```text
               ┌──► 发送 SET_READER_CONFIG ──► 应用天线功率/GPIO/心跳/Impinj扩展参数
Settings 大对象 ┤
               └──► 发送 ADD_ROSPEC + ENABLE_ROSPEC + START_ROSPEC ──► 下发盘点 Session/Mode/报告选择
```

### 属性拆解与 LLRP 报文映射表

| Octane SDK 节点 | 属性语义 | LLRP 底层报文与参数归属 | LLRPCSharp 领域划分 |
|---|---|---|---|
| `<Antennas>` | 端口号、`TxPowerInDbm` (30)、`RxSensitivityInDbm` (-90) | `SET_READER_CONFIG` / `AntennaConfiguration` | **`ReaderConfiguration.Antennas`** (硬件/物理配置) |
| `<Gpis>` | 端口号、`DebounceInMs` (20ms) | `SET_READER_CONFIG` / `ImpinjGpiDebounceSetting` | **`ReaderConfiguration.Extensions["impinj.readerSettings"]`** |
| `<Gpos>` | 端口号、Mode (Normal) | `SET_READER_CONFIG` / `GpoConfiguration` | **`ReaderConfiguration.Gpos`** (硬件端口配置) |
| `<Keepalives>` | 心跳使能、周期、`LinkMonitor` 阈值 | `SET_READER_CONFIG` / `KeepaliveSpec` + `LinkMonitor` | **`ReaderConfiguration.Keepalive`** & Impinj 扩展 |
| `<Session>` | C1G2 Session (如 2) | `ADD_ROSPEC` / `C1G2SingulationControl` | **`InventorySettings.Session`** (单次盘点意图) |
| `<SearchMode>` | 盘点目标 (DualTarget A/B) | `ADD_ROSPEC` / `C1G2TagInventoryStateAwareSingulation` | **`InventorySettings.StateAwareSingulation`** |
| `<TagPopulationEstimate>`| 估计标签数量 (如 100) | `ADD_ROSPEC` / `C1G2SingulationControl` | **`InventorySettings.TagPopulationEstimate`** |
| `<RfMode>` | 射频模式 (如 1002 - MaxThroughput) | `ADD_ROSPEC` / `C1G2RFControl` | **`InventorySettings.ModeIndex`** |
| `<AutoStart>` / `<AutoStop>` | 启动/停止触发器 | `ADD_ROSPEC` / `ROBoundarySpec` | **`InventorySettings.StartTrigger` / `StopTrigger`** |
| `<Report>` | RSSI, Channel, Antenna, FastId 等选择 | `ROReportSpec` / `ImpinjTagReportContentSelector` | **`InventorySettings` / Impinj Report Contributor** |
| `<SpatialConfig>` | xArray 天线阵列与空间定位模式 | Impinj xArray 专属 Custom Parameter | Impinj 专属 Extension |

---

## 三、 LLRPCSharp 现代化解耦设计的架构优势

`LLRPCSharp` 放弃了 Octane SDK 的“打包 Settings”模式，采用 `ReaderConfiguration` 与 `InventorySettings` 严格解耦的设计，具备以下核心架构优势：

1. **避免硬件频繁擦写与延迟**：
   在实际生产中，调整盘点逻辑（如切换 Session 或改变触发器）是高频动作，而天线功率和跳频频段属于静态硬件基线。解耦设计使得 `StartAsync` 只下发 ROSpec，避免每次改盘点参数都去重复擦写天线硬件功率。
2. **多厂商通用性 (Multi-Vendor Compatibility)**：
   标准 LLRP 协议天然分离 Config 与 ROSpec。解耦设计使得 SDK 核心既能完美驱动 Impinj (R420/R700/xArray)，又能驱动 Zebra、Seuic、Alien 等所有标准 LLRP 读写器。
3. **轻量强类型 JSON 序列化**：
   抛弃 Impinj 绑定的私有 XML `.profile` 格式，采用现代 JSON 格式 (`InventorySettingsSerializer`) 保存与加载盘点草稿。
4. **通过扩展管道兼顾 Impinj 专属能力**：
   通过 `builder.UseImpinj()` 激活扩展后，Serialized TID、RF Phase Angle、Peak RSSI、Link Monitor 等 Impinj 专属参数会自动通过 Contributor 无缝注入到解耦模型中，不破坏核心 SDK 的纯洁性。

---

## 四、 总结与迁移规划

1. **核心 SDK 策略**：不强行兼容 Impinj 废弃的 C# `.profile` XML 导出格式，坚持使用标准解耦领域模型与 JSON 序列化。
2. **工具链拓展（未来可选）**：若未来有大量旧 Octane SDK 项目迁移需求，可在 `LlrpSdk.Extensions.Impinj` 扩展库中提供可选的 `OctaneProfileConverter` 迁移工具，将 Octane XML 转换为 `ReaderConfiguration` 和 `InventorySettings`。
