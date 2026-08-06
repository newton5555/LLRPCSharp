# LlrpNet 协议层 & LlrpSdk API 层 架构分析报告

> 本文是 LLRPCSharp 的详细架构分析,聚焦底层协议层(`LlrpNet`)与高层 SDK(`LlrpSdk`)的
> 内部结构与协作机制。总览见 `docs/architecture/overview.md`;多版本代码约定见
> `AGENTS.md` "Multi-Version Protocol Code Convention"。

---

## 一、仓库分层总览

```
┌─────────────────────────────────────────────────────────────┐
│ 应用层  LlrpReaderStudio(WPF,独立仓库)/ 你的业务代码          │
├─────────────────────────────────────────────────────────────┤
│ API 层  LlrpSdk           设备抽象:领域模型 + 适配器 + 门面    │
│         LlrpSdk.Extensions.Impinj / .Seuic  厂商扩展          │
│         LlrpSdk.Extensions.Abstractions      扩展契约          │
├─────────────────────────────────────────────────────────────┤
│ 协议层  LlrpNet.Protocol  生成类型 + Registry + Codecs        │
│         LlrpNet.Core      传输/会话/事务/帧缓冲(传输无关核心)  │
├─────────────────────────────────────────────────────────────┤
│ 工具层  LlrpCli / LlrpVirtualReader / tools/LlrpSdk.LiveSmoke │
└─────────────────────────────────────────────────────────────┘
```

依赖方向**单向向下**:API 层依赖协议层,工具层可只依赖协议层(离线工具)或两者(实时命令)。

---

## 二、LlrpNet 协议层:协议精确、传输无关

### 2.1 项目结构

**`LlrpNet.Protocol`(协议模型与编解码)**

```
Messages/       生成消息类型,按版本分目录 V1_0_1/ 与 V1_1/(各自完整独立)
Parameters/     生成参数类型,同样按版本分目录
Enumerations/   生成枚举,按版本分目录
Choices/        生成联合类型(如 Timestamp 的 UTC/USec 选择)
Codecs/         每个生成类型的编解码器
Registry/       注册中心:标准模块 + LlrpCodecRegistry
```

关键设计:**每个协议版本是一套独立完整的生成类型集,无共享、无继承**。
`V1_0_1.KEEPALIVE` 与 `V1_1.KEEPALIVE` 是两个 class,命名空间隔离。这样
1.1 新增/变更的参数不会污染 1.0.1,也便于未来 2.0 再增一套。

**生成代码纪律**(见 `AGENTS.md`):所有 `*.g.cs` 由
`definitions/*.yaml`(LLRP 1.1 完整定义 + 2.0 delta)+ 生成器产出,
**禁止手改**;改协议走 `definitions/` → 重新生成。

**`LlrpNet.Core`(传输无关核心)**

| 子模块 | 职责 |
|---|---|
| `Transport/` | `ILlrpTransport` 抽象 + `LlrpTcpTransport` 实现(可替换传输) |
| `Session/` | `LlrpSession`(帧读写、背压 `LlrpSessionBackpressureException`、未请求帧溢出策略)、`LlrpResponseMatcher`(响应匹配) |
| `Transactions/` | `PendingTransactionManager`(请求-响应关联)+ `LlrpMessageIdGenerator` |
| `Frames/` | `LlrpFrameDecoder`、`LlrpFrame`(帧边界) |
| `Buffers/` | `LlrpBitReader/Writer`(位级读写,LLRP 参数按位对齐)、`LlrpBufferReader/Writer` |
| `Protocol/` | `LlrpMessageHeader`、`LlrpProtocolVersion`、`LlrpProtocolException` |
| `Diagnostics/` | `ILlrpFrameObserver` + `CompositeLlrpFrameObserver` + `LlrpFrameJournal`(帧级观察/抓包) |

Core **不包含任何版本化消息类型**——它只处理帧/传输/事务,是版本无关的底座。

### 2.2 编解码体系(协议层核心)

**`LlrpCodecRegistry`(LlrpNet.Protocol/Registry/)**:注册与分派中心,共 8 张索引:

```
_messageDecoders  (MessageWireKey: Version, MessageType) → codec
_messageEncoders  (ClrKey: Version, CLRType)             → codec
_parameterDecoders(ParameterWireKey: Version, Encoding, ParameterType)
_parameterEncoders(ClrKey: Version, CLRType)
+ 4 张自定义(厂商)消息/参数表
```

**解码自动分派**(关键机制):

```csharp
public ILlrpMessage DecodeMessage(LlrpMessageHeader header, ReadOnlySpan<byte> payload)
{
    // LLRP 帧头自带 Version 字段 → 按 (Version, MessageType) 双键查表
    MessageRegistration? registration = FindMessageDecoder(header.Version, header.MessageType);
    return registration.Codec.Decode(header, payload);
}
```

- **同一 MessageType(如 KEEPALIVE=30)在 1.0.1/1.1 下是不同键**,双版本注册无冲突
- 解码结果类型**由帧头 Version 决定**,天然正确
- 未注册 → `UnknownMessage`/`UnknownParameter` 兜底(鲁棒,不崩)

**模块注册**(每个版本/厂商一个静态注册入口):
`V1_0_1ProtocolModule`(生成)、`Llrp11StandardModule`(生成)、
`ImpinjProtocolModule`、Seuic 模块——`Register(registry)` 一次性注册全量 codec。

**编码**:按 `(Version, CLRType)` 查 encoder——类型自带版本信息,天然正确。

### 2.3 会话与事务

- `LlrpSession`:建立连接后读帧循环;请求帧走 `PendingTransactionManager`
  按 MessageId 匹配响应;未请求帧(报告/事件/keepalive)走独立队列供上层消费
- 背压:消费慢时 `LlrpSessionBackpressureException` / 溢出策略(`LlrpUnsolicitedFrameOverflowPolicy`)
- 帧边界由 `LlrpFrameDecoder` 依据帧头 MessageLength 切分

---

## 三、LlrpSdk API 层:设备抽象、版本无关

### 3.1 三层结构

```
领域层(版本无关)  InventorySettings / ReaderSettings / TagReport / ReaderCapabilities …
适配层(版本相关)  ILlrpProtocolAdapter ← Llrp101ProtocolAdapter / Llrp11ProtocolAdapter
门面层(公共 API)  LlrpReader(+ InventorySession / Builder / Options)
```

- **领域类型不引用任何协议类型**(审计确认:公共属性/枚举全部是 SDK 自有类型,
  如 `KeepaliveConfiguration.TriggerType` 用 SDK 枚举,适配器翻译为
  `V1_1.KeepaliveTriggerType`)
- **适配器是唯一版本边界**:编译(领域→协议)与翻译(协议→领域)全部收敛在适配器,
  `Llrp101InventoryCompiler` / `Llrp11InventoryCompiler` /
  `Llrp101TagAccessCompiler` / `Llrp11TagReportTranslator` 等按版本各一份

### 3.2 LlrpReader 公共 API 面

**生命周期**
```
ConnectAsync / DisconnectAsync / ReconnectAsync / SynchronizeStateAsync
```

**配置**
```
ApplySettingsAsync(ReaderSettings)     部署完整配置
QuerySettingsAsync → ReaderSettingsSnapshot   查询设备当前配置
GetDefaultSettingsAsync → ReaderSettingsDefaults  设备推荐默认值
ValidateSettingsAsync → SettingsValidationResult  校验(不下发)
ClearManagedSettingsAsync / StopAsync   清理托管资源
```

**盘点**
```
StartInventoryAsync(InventorySettings) → InventorySession(单实例)
InventorySession:ReadReportsAsync(按 RoSpecId 过滤的报告流)/ StopAsync
```

**标准 Tag Access**(读/写/锁/销毁/块擦除)
```
ReadTagMemoryAsync / WriteTagMemoryAsync / LockTagAsync / KillTagAsync / BlockEraseAsync
```

**资源模式**
```
EnterManualResourceModeAsync / ExitManualResourceModeAsync  (应用自管 ROSpec/AccessSpec)
```

**事件面(11 个)**
```
ConnectionChanged / ErrorOccurred / TagsReported / GpiChanged / AntennaChanged
KeepaliveReceived / KeepaliveTimedOut / ReportBufferOverflow / ReportBufferWarning
TagReportsDropped(SDK 连接级丢弃) / ReportBufferWarning
```

**版本策略**:`LlrpProtocolVersionPolicy.Auto | Force101 | Force11`(Builder 配置)。

### 3.3 核心机制

**(1) 版本协商(连接时锁定)**

```
SelectProtocolAdapter(Version101) → TCP 连接 → 版本协商 → InitializeReaderAsync
协商:默认发 GET_SUPPORTED_VERSION(1.1 消息)询问
    → 支持且策略允许 → SET_PROTOCOL_VERSION(1.1) → 切 Llrp11ProtocolAdapter
    → 不支持/Force101 → 保持 1.0.1;Force11 失败则连接失败(不静默回退)
```

**(2) 消息泵(唯一未请求帧消费者)**

```
UnsolicitedFrames → registry.DecodeMessage(按帧头版本自动解)
  ├─ KEEPALIVE(is V101 or V11)→ 事件 + 按协商版本回 KEEPALIVE_ACK
  ├─ READER_EVENT_NOTIFICATION → 双重载(各版本枚举),提取 ROSpec/GPI/Antenna/Buffer 事件
  └─ TagReport 翻译 → GetProtocolAdapter().TranslateTagReports(消息)
       → Contributor 管道 → 连接级通道 + TagsReported 事件
```

**(3) 报告路由(双流)**

- **连接级**:`ReadTagReportsAsync()` 读全部报告(含丢弃检测:通道满时 DropOldest + `TagReportsDropped` 事件)
- **会话级**:`InventorySession.ReadReportsAsync()` 按 `RoSpecId == ManagedInventoryRoSpecId`
  (+ AccessSpecId)过滤——这就是部署后**快照真实 RoSpecId** 的原因
  (部署前 id 未知,否则路由全 miss)

**(4) 盘点生命周期**

```
StartInventoryAsync(settings)
  → 校验 → 推断初始状态(StartTrigger=None → Running)
  → StartManagedInventoryCoreAsync:清旧资源 → 部署(ADD_ROSPEC 14150 + 可选 AccessSpec)→ 启动
  → new InventorySession(…, 部署后的真实 RoSpecId/AccessSpecId, …)
  → 返回 session;dispose/stop 时清理托管资源
```

**(5) 扩展机制(Contributor 管道)**

```
IReaderSettingsContributor / IInventorySettingsContributor / IInventoryContributor
ITagReportContributor / IReaderSettingsDefaultsContributor / IReaderSettingsSerializationContributor
```
厂商扩展(LlrpSdk.Extensions.Impinj / .Seuic)通过 contributor 注入
settings 默认值、inventory 自定义参数、tag report 附加字段(如
`impinj.serializedTid`),与核心协议解耦。

### 3.4 领域模型要点

- `InventorySettings` + `InventorySettingsBuilder`:**盘点意图模型**(非完整配置快照),
  含报告配置、select 过滤器、state-aware singulation、attached-data 读 TID 等
- `ReaderSettings` + `ReaderSettingsBuilder` + `ReaderSettingsSerializer`(JSON)+ 校验器
- `TagReport`:`ElectronicProductCode` + `Extensions`(厂商附加)+ `EpcHex`/TID 便捷成员
- TagAccess 请求族:`ReadTagRequest / WriteTagRequest / LockTagRequest / KillTagRequest /
  BlockEraseTagRequest`,结果 `TagAccessResult` 族

---

## 四、端到端数据流(一次盘点示例)

```
应用:  StartInventoryAsync(InventorySettings)
 SDK:   ValidateSettingsCore → Llrp101/11Adapter.CompileInventory → ILlrpParameter
协议:   registry.EncodeMessage(version, ADD_ROSPEC) → 字节帧
 Core:   PendingTransactionManager 发送 + 等待 ADD_ROSPEC_RESPONSE
设备:   返回响应 → 帧解码 → registry 按帧头版本解出响应类型 → 事务完成
设备:   推送 RO_ACCESS_REPORT(未请求帧)→ 消息泵
 SDK:   适配器 TranslateTagReports → TranslatedTagReport → Contributor → TagReport
应用:   session.ReadReportsAsync 按 RoSpecId 过滤收到 TagReport
```

版本在**每一跳**都被追踪:协商锁定 → 编码按版本 → 解码按帧头版本 → 翻译按协商适配器。

---

## 五、多版本支持现状(2026-08 重构后)

| 层 | 1.0.1 | 1.1 |
|---|---|---|
| LlrpNet 生成类型 | 完整 | 完整(独立类型集) |
| 编解码 registry | ✅ | ✅(双键共存) |
| SDK 适配器/协商 | ✅ | ✅ 可用基线(真实设备覆盖待验证) |
| CLI 实时命令(走 SDK) | ✅ | ✅(自动协商,`--llrp auto|1.0.1|1.1`) |
| CLI 离线工具(decode/encode) | ✅ | ⚠️ 仅注册 1.0.1(roadmap 待办) |
| 虚拟 reader | ✅ | ⚠️ 仅 1.0.1(roadmap 待办) |

**版本化代码约定**(`AGENTS.md`):双版本文件禁止裸版本 using,必须
`V101Messages/V101Parameters/V101Enumerations`(1.0.1)与 `V11*`(1.1)前缀引用;
禁止旧驼峰别名;禁止默认版本路径。单版本文件(文件名带版本标识)可裸引用。

---

## 六、质量保障与已知边界

- **测试分层**:协议层单测(`LlrpNet.Protocol.Tests`,按版本目录)→ SDK 单测
  (`LlrpSdk.Tests`,脚本化传输)→ 虚拟 reader 互操作(`Interop.Tests`)→
  真机硬件测试(`LlrpSdk.Hardware.Tests`,设备不可达自动跳过)
- **已知边界**(详见 `docs/status.md`):
  - 自动重连不恢复 ROSpec/AccessSpec/托管盘点状态
  - `InventorySettings` 是意图模型,非完整配置快照
  - LLRP 2.0 有定义 delta,无 `Llrp20ProtocolAdapter`
  - CLI 离线工具 1.1 支持为 roadmap 待办
