# Impinj Octane SDK 架构与设计参考手册

本文档作为 Impinj 官方 Octane SDK 的独立技术参考手册，详细记录其架构设计、对象模型、`.profile` 配置文件 XML Schema，以及 Octane SDK 如何将上层高层 API 映射到底层 LLRP 协议报文。

---

## 一、 架构定位与核心对象模型

Impinj Octane SDK 是 Impinj 官方基于底层 LTK .NET 构建的 C# 面向对象 SDK，旨在为应用开发者隐去原始 LLRP 报文与参数细节，提供以 `ImpinjReader` 为核心的设备管理接口。

```text
               ┌──────────────────────────────────────────────┐
               │    应用层代码 (Application Code)             │
               └──────────────────────┬───────────────────────┘
                                      │
                                      ▼
               ┌──────────────────────────────────────────────┐
               │         Impinj.OctaneSdk.ImpinjReader        │
               │   (连接、断开、ApplySettings、Status、事件)   │
               └──────────────────────┬───────────────────────┘
                                      │
                                      ▼
               ┌──────────────────────────────────────────────┐
               │     Impinj.OctaneSdk.Settings 打包大对象     │
               │   (Antennas, Session, SearchMode, Report)    │
               └──────────────────────┬───────────────────────┘
                                      │
                                      ▼
               ┌──────────────────────────────────────────────┐
               │              LTK .NET 协议引擎               │
               │   (自动生成 SET_READER_CONFIG 与 ADD_ROSPEC) │
               └──────────────────────┬───────────────────────┘
                                      │
                                      ▼
               ┌──────────────────────────────────────────────┐
               │    LLRP 1.0.1 二进制协议 (TCP Port 5084)     │
               └──────────────────────────────────────────────┘
```

### 核心公开类说明：

1. **`ImpinjReader`**：设备的句柄主入口，负责 TCP 连接控制、`ApplySettings()`、`QueryStatus()`、`QueryTags()`、以及 `TagReported` / `GpiChanged` 事件监听。
2. **`Settings`**：单一大对象配置类。包含设备物理天线、GPIO、盘点模式、触发器条件以及标签报告过滤器等全部属性。
3. **`Status`**：设备运行状态查询对象，包含读写器物理连接状态、内部摄氏温度、网络与天线插拔状态等。
4. **`Tag` / `TagReport`**：标签观察结果投影类，包含 EPC、AntennaPortNumber、RSSI、Timestamp、PhaseAngle、FastId 等属性。

---

## 二、 `Settings` 配置大对象结构

Octane SDK 的核心特征是将硬件物理配置与盘点任务意图全部打包在 `Settings` 结构中。其主要属性分类如下：

### 1. 天线与射频参数 (`Antennas`)
* **`TxPowerInDbm`**：直接以 dBm 为单位（如 `30.0`）表示天线发射功率，隐藏了底层 LLRP 的功率索引表（Index）。
* **`RxSensitivityInDbm`**：直接以 dBm 为单位（如 `-90.0`）表示天线接收灵敏度。
* **`MaxTxPower` / `MaxRxSensitivity`**：布尔值，指示是否自动使用设备支持的最大功率或最高灵敏度。

### 2. 盘点与 Singulation 参数 (`Session` / `SearchMode` / `ReaderMode`)
* **`Session`**：Gen2 Session 编号 (0, 1, 2, 3)。
* **`SearchMode`**：Gen2 标签 Target A/B 状态翻转策略：
  * `SingleTarget` (Target A)
  * `DualTarget` (Target A/B 循环)
  * `SingleTargetReset`
  * `DualTargetWithReset`
* **`ReaderMode` / `RfMode`**：如 `MaxThroughput`、`DenseReaderM4`、`MaxMiller` 或数值索引（如 `1002`）。
* **`TagPopulationEstimate`**：预计场强内的标签数量（默认 32 或 100），用于初始化 Q 算法的时隙数。

### 3. 自动触发条件 (`AutoStart` / `AutoStop`)
* **`AutoStart`**：启动触发器模式 (`None`, `Immediate`, `Periodic`, `GpiPort`)。
* **`AutoStop`**：停止触发器模式 (`None`, `Duration`, `GpiPort`)。

### 4. 标签报告选择器 (`Report`)
用于配置读写器在 `RO_ACCESS_REPORT` 中上报哪些可选字段：
* `IncludeAntennaPortNumber` (天线端口号)
* `IncludeChannel` (射频信道/频点)
* `IncludeFirstSeenTime` / `IncludeLastSeenTime` (时间戳)
* `IncludePeakRssi` (峰值信号强度)
* `IncludePhaseAngle` (相位角)
* `IncludeFastId` (Impinj 专属 TID 快速读取)

### 5. GPIO 与高阶功能
* **`Gpis`**：GPI 端口启用与防抖控制 (`DebounceInMs`)。
* **`Gpos`**：GPO 端口模式与脉冲输出时长 (`GpoPulseDurationMsec`)。
* **`SpatialConfig`**：仅针对 xArray 阵列读写器的空间定位 (`Inventory`, `Location`, `Direction`) 参数。

---

## 三、 `.profile` 配置文件 XML Schema 结构

Impinj Octane SDK 提供了 `settings.Save("filename.profile")` 方法，用于导出读写器配置。其导出的文件本质上是 C# `Settings` 类属性经过 `XmlSerializer` 导出的 XML 树（外层包裹 JSON）：

```xml
<?xml version="1.0" encoding="utf-16"?>
<Settings xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema">
  <!-- 自动化启动/停止条件 -->
  <AutoStart>
    <Mode>None</Mode>
    <GpiPortNumber>0</GpiPortNumber>
    <FirstDelayInMs>0</FirstDelayInMs>
    <PeriodInMs>0</PeriodInMs>
  </AutoStart>
  <AutoStop>
    <Mode>None</Mode>
    <DurationInMs>0</DurationInMs>
  </AutoStop>

  <!-- 盘点与模式 -->
  <ReaderMode>MaxThroughput</ReaderMode>
  <RfMode>1002</RfMode>
  <SearchMode>DualTarget</SearchMode>
  <Session>2</Session>
  <TagPopulationEstimate>100</TagPopulationEstimate>

  <!-- 过滤条件 -->
  <Filters>
    <Mode>None</Mode>
    <TagFilter1>
      <MemoryBank>Epc</MemoryBank>
      <BitPointer>32</BitPointer>
      <BitCount>0</BitCount>
      <TagMask />
      <FilterOp>Match</FilterOp>
    </TagFilter1>
  </Filters>

  <!-- 标签报告配置 -->
  <Report>
    <IncludeAntennaPortNumber>true</IncludeAntennaPortNumber>
    <IncludeChannel>true</IncludeChannel>
    <IncludeFirstSeenTime>true</IncludeFirstSeenTime>
    <IncludeLastSeenTime>false</IncludeLastSeenTime>
    <IncludePeakRssi>true</IncludePeakRssi>
    <IncludePhaseAngle>false</IncludePhaseAngle>
    <IncludeFastId>false</IncludeFastId>
  </Report>

  <!-- 天线物理端口配置 -->
  <Antennas>
    <Antenna>
      <IsEnabled>true</IsEnabled>
      <PortNumber>1</PortNumber>
      <PortName>Antenna Port 1</PortName>
      <TxPowerInDbm>30</TxPowerInDbm>
      <RxSensitivityInDbm>-90</RxSensitivityInDbm>
    </Antenna>
    <!-- 天线端口 2..4 ... -->
  </Antennas>

  <!-- GPIO 端口配置 -->
  <Gpis>
    <Gpi><IsEnabled>false</IsEnabled><DebounceInMs>20</DebounceInMs><PortNumber>1</PortNumber></Gpi>
  </Gpis>
  <Gpos>
    <Gpo><Mode>Normal</Mode><GpoPulseDurationMsec>0</GpoPulseDurationMsec><PortNumber>1</PortNumber></Gpo>
  </Gpos>

  <!-- 心跳与链路监测 -->
  <Keepalives>
    <Enabled>false</Enabled>
    <PeriodInMs>0</PeriodInMs>
    <EnableLinkMonitorMode>false</EnableLinkMonitorMode>
  </Keepalives>
</Settings>
```

---

## 四、 Octane SDK 到底层 LLRP 协议报文的映射逻辑

当应用调用 `reader.ApplySettings(settings)` 时，Octane SDK 内部通过驱动将 `Settings` 对象拆解，并依次向设备发送以下标准的 LLRP 协议报文：

```text
ApplySettings(settings)
  │
  ├─► 1. 组装并发送 SET_READER_CONFIG
  │     ├─ AntennaConfiguration: 将 TxPowerInDbm / RxSensitivityInDbm 查表转为 TransmitPowerIndex / ReceiverSensitivityIndex
  │     ├─ GPIPortCurrentState / GPOWriteData: 写入 GPIO 端口初始电平与状态
  │     ├─ KeepaliveSpec: 写入心跳周期与使能状态
  │     └─ ImpinjCustomParameter: 写入 ImpinjGpiDebounceSetting 与 ImpinjLinkMonitorSettings
  │
  └─► 2. 组装并发送 ADD_ROSPEC (ID: 1)
        ├─ ROBoundarySpec: 将 AutoStart / AutoStop 组装为 ROSpecStartTrigger / ROSpecStopTrigger
        ├─ AISpec: 组装已使能天线端口列表 (AntennaIDs)
        ├─ InventoryParameterSpec:
        │    ├─ C1G2RFControl: 写入 ModeIndex (RfMode) 与 Tari
        │    ├─ C1G2SingulationControl: 写入 Session 与 TagPopulationEstimate
        │    └─ C1G2TagInventoryStateAwareSingulation: 将 SearchMode (DualTarget 等) 映射为 Target A/B 状态动作
        └─ ROReportSpec:
             ├─ TagReportContentSelector: 映射 EnableAntennaID, EnablePeakRSSI, EnableChannelIndex 等
             └─ ImpinjTagReportContentSelector: 若启用 IncludeFastId，在 Custom Items 中加入 FastID 列表项
  │
  ├─► 3. 发送 ENABLE_ROSPEC (ID: 1)
  │
  └─► 4. 发送 START_ROSPEC (ID: 1)  (若 AutoStartMode == None)
```
