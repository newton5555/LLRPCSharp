# LlrpSdk 项目架构

> 范围:仅 `src/LlrpSdk` 项目;协议层(`LlrpNet.Protocol` 生成代码、`LlrpNet.Core` 传输)只在 §2 作为依赖说明。
> 标记:`①中立` = 零版本类型引用;`②切片` = 单版本文件(`Llrp101*`/`Llrp11*`);`③边界` = 跨版本接线组件;`④契约` = 接口/抽象。
> 本文 §5 中的 3.0 **仅作示例,不是规划中的版本**。
## 1. 项目本身的结构

### 1.1 文件树(全部文件,按文件夹)

```text
src/LlrpSdk/
├── Reader/                    ①中立 —— 门面与公共 API 宿主
│   ├── LlrpReader.cs          门面:连接状态机/消息泵/事件发布/托管资源生命周期;零版本引用(ArchitectureGuardTests 强制)
│   ├── LlrpReaderBuilder.cs   公共流式构建器(逐项透传 OptionsBuilder)
│   ├── LlrpReaderOptions.cs   不可变连接选项(record)
│   ├── LlrpReaderOptionsBuilder.cs  选项构建器(真正实现)
│   ├── LlrpReaderExceptions.cs 公共异常族;状态名由版本边界解析后传入(异常自身中立)
│   ├── ReaderEvents.cs        全部公共事件参数类型(GpiChanged/AntennaChanged/ReaderException/... 各 EventArgs)
│   ├── ReaderMetadata.cs      ReaderIdentity / ReaderCapabilities / ReaderMetadataSnapshot
│   ├── ReaderConnectionState.cs / ReaderOperationState.cs / ReaderResourceMode.cs  三个状态枚举
│   ├── LlrpAutomaticReconnectOptions.cs  自动重连策略
│   └── ReaderEventProjection.cs  中立事件投影记录(边界投影器 → 门面发布的传递物)
├── Settings/                 ①中立 —— 配置域模型
│   ├── ReaderSettings.cs      领域根:Configuration + Inventory? + Extensions;另含 ReaderSettingsSnapshot / ManagedRoSpecSnapshot / InventoryRuntimeState
│   ├── ReaderConfiguration.cs 设备配置模型(天线/keepalive/GPI/GPO/事件通知)
│   ├── ReaderSettingsBuilder.cs / ReaderSettingsDefaults.cs  流式助手 / 默认档案(Generic、Reader Profile)
│   ├── ReaderSettingsSerializer.cs  JSON 序列化(contributor 扩展感知)
│   ├── ReaderSettingsValidation.cs  校验器 + SettingsValidationResult / 诊断
│   ├── IReaderSettingsContributor.cs  ④契约  配置 contributor(查询/应用自定义参数)
│   ├── IReaderSettingsSerializationContributor.cs  ④契约  序列化 contributor
│   └── TranslatedReaderConfiguration.cs  适配器配置翻译产物(配置 + 厂商自定义参数)
├── Inventory/                ①中立(模型/会话/组装)+ ②切片(两个编译器)
│   ├── InventorySettings.cs   盘点意图模型:天线/过滤器/触发器/报告/singulation/attached-data(版本无关核心)
│   ├── InventorySettingsBuilder.cs / InventorySettingsSerializer.cs  流式助手 / JSON(仅标准字段)
│   ├── InventorySettingsNormalizer.cs  天线 ID 0 → 按能力展开
│   ├── InventorySession.cs    盘点会话(隔离报告流,与门面互斥所有权)
│   ├── IInventorySettingsContributor.cs  ④契约  反解析 contributor(+ ContributionContext/ExtensionBuilder)
│   ├── IInventoryContributor.cs  ④契约  编译 contributor
│   ├── InventoryCustomItems.cs  厂商自定义参数收集器(报告/命令两处)
│   ├── ParsedManagedRoSpec.cs  反解析中间产物(版本解析器 → 组装器)
│   ├── ManagedInventoryStateAssembler.cs  中立组装器:contributor 查询管道唯一实现 + FromUtcMicroseconds
│   ├── Llrp101InventoryCompiler.cs  ②切片  InventorySettings → 1.0.1 ROSpec
│   └── Llrp11InventoryCompiler.cs   ②切片  InventorySettings → 1.1 ROSpec
├── Resources/                ①中立 —— ROSpec/AccessSpec 专家服务(手动资源模式用)
│   ├── IRoSpecService.cs / RoSpecService.cs
│   └── IAccessSpecService.cs / AccessSpecService.cs
├── TagAccess/                ①中立(模型)+ ②切片(两个编译器)
│   ├── TagAccess.cs           请求/结果模型:Read/Write/Lock/Kill/BlockErase/Sequence + TagSelection
│   ├── Llrp101TagAccessCompiler.cs  ②切片  TagAccessRequest → 1.0.1 AccessSpec
│   └── Llrp11TagAccessCompiler.cs   ②切片  TagAccessRequest → 1.1 AccessSpec
├── Reports/                  ①中立(模型)+ ②切片(两个翻译器)
│   ├── TagReport.cs           标签观测(中立 record,含 EpcHex/PcBits/Extensions)
│   ├── TagTimestamp.cs / TagReportEventArgs.cs
│   ├── ITagReportContributor.cs  ④契约  报告 contributor
│   ├── TranslatedTagReport.cs   翻译产物(中立报告 + 厂商自定义参数)
│   ├── Llrp101TagReportTranslator.cs  ②切片  RO_ACCESS_REPORT → TranslatedTagReport
│   └── Llrp11TagReportTranslator.cs   ②切片  同上
├── Protocol/                 版本边界总部(②切片 + ③边界 + ④契约)
│   ├── ILlrpProtocolAdapter.cs  ④契约  26 成员:版本切片的唯一出口(前向编译/翻译 + 反向解析 + CRUD + 配置)
│   ├── Llrp101ProtocolAdapter.cs  ②切片  1.0.1 全向实现
│   ├── Llrp11ProtocolAdapter.cs   ②切片  1.1 全向实现
│   ├── Llrp101ManagedRoSpecParser.cs  ②切片  反向读线:1.0.1 ROSpec → ParsedManagedRoSpec
│   ├── Llrp11ManagedRoSpecParser.cs   ②切片  反向读线:1.1 ROSpec → ParsedManagedRoSpec
│   ├── Llrp101EventProjector.cs  ②切片  1.0.1 READER_EVENT_NOTIFICATION → 中立投影记录
│   ├── Llrp11EventProjector.cs   ②切片  1.1 同上
│   ├── ReaderEventProjector.cs   ③边界  事件分派(门面唯一入口;按消息版本选投影器)
│   ├── LlrpProtocolMessageFactory.cs  ③边界  KEEPALIVE_ACK/CLOSE_CONNECTION_RESPONSE/ENABLE_EVENTS_AND_REPORTS 构造 + ERROR_MESSAGE 分类(按版本枚举 switch)
│   ├── LlrpVersionNegotiator.cs  ③边界  连接前 1.1 探测(GET_SUPPORTED_VERSION / SET_PROTOCOL_VERSION)
│   ├── LlrpWireBits.cs        ①中立  位向量双向转换(ToBits / BitsToBytes 唯一实现)
│   ├── ReaderProtocolAccess.cs / IReaderProtocolAccess.cs  raw 协议入口(发送/收发原始帧)
│   └── LlrpProtocolVersionPolicy.cs  Auto / Force101 / Force11
└── Extensions/
    └── ReaderExtensionCollection.cs  ①中立  连接后激活的 Reader 扩展集合
```

### 1.2 分层是怎么运转的(技术说明)

项目把“版本翻译”这件事全部挤进 ②切片 与 ③边界,门面和领域模型完全不认识版本类型。这样设计的理由:

- **门面只做中立编排**:连接状态机、未请求帧泵、事件发布、托管资源生命周期。它拿到设备报文后不判断版本,
  只把消息交给边界组件处理;边界组件产出的永远是中立值(`TagReport`、`ManagedRoSpecSnapshot`、投影记录)。
- **②切片 各管一个版本、互相零引用**:改 1.1 不可能误伤 1.0.1;加新版本=加一组切片文件,旧切片一个字节不动。
- **③边界 是固定接线点**:全项目只有 5 处“知道版本存在”的开关(§5 列出),加版本时按清单改完即接线完成。
- **机器强制**:`ArchitectureGuardTests` 扫描 `LlrpReader.cs`,出现版本类型引用直接编译期测试失败。

三条主干数据流:

```text
正向(编译部署):StartInventoryAsync → 门面 CompileDefaultInventoryRoSpec(中立)
  → GetProtocolAdapter().CompileInventory → Llrp101/11InventoryCompiler → 生成 ROSpec → Codec → TCP
反向(反解析):QuerySettingsAsync → RoSpecs/AccessSpecs 拉取(中立列表)
  → GetProtocolAdapter().ParseManagedRoSpec → Llrp101/11ManagedRoSpecParser(读线 → ParsedManagedRoSpec)
  → ManagedInventoryStateAssembler(中立,contributor 管道) → ManagedRoSpecSnapshot
事件(推送):泵解码(中立 ILlrpMessage) → ReaderEventProjector.Project(分派)
  → Llrp101/11EventProjector(版本消息 → 中立投影记录) → 门面 HandleEventProjection → Publish* 事件
```

## 2. 与协议层(LlrpNet)的关系

### 2.1 依赖与用途(技术说明)

```text
LlrpSdk
  ├── 引用 LlrpNet.Protocol   (生成的版本类型 + LlrpCodecRegistry + 模块注册)
  ├── 引用 LlrpNet.Core       (LlrpSession/ILlrpTransport/帧解码/MessageId/LlrpProtocolVersion 枚举/帧观察)
  └── 引用 LlrpSdk.Extensions.Abstractions  (IReaderExtension / ILlrpProtocolModule / IReaderConnection)
```

| 协议层资产 | 谁在用 | 怎么用/约束 |
|---|---|---|
| 生成的版本类型(`LlrpNet.Protocol.{Messages,Parameters,Enumerations,Choices}.V1_0_1/V1_1`) | 仅 ②切片 与 ③边界组件 | ①中立文件一律禁止(守护测试扫 LlrpReader.cs) |
| `LlrpCodecRegistry` | 门面持有;连接时各适配器 `RegisterStandardCodecs` 注册本版本 Codec;厂商模块经 `UseProtocolModule` 注册 | 解码按帧头 Version 自动分派;编码必须显式传版本 |
| `LlrpSession` / `ILlrpTransport` / 帧解码 / `LlrpMessageIdGenerator` | 门面(收发、事务、未请求帧泵) | 中立,无版本概念 |
| `LlrpProtocolVersion` 枚举 | ①中立核心唯一允许的版本概念 | 加版本先在此加成员(协议层项目) |
| `ILlrpFrameObserver`(诊断) | 门面连接选项挂接 | 旁路观察,不影响数据通路 |

关键机制:门面把“字节流 ↔ 强类型对象”全部外包给协议层——`_registry.DecodeMessage(frame)` 按帧头版本解出对应版本类型,
`_registry.EncodeMessage(version, message)` 按协商版本编码;门面自己从不 new 版本类型(构造由 §3 内部面交给边界组件)。

### 2.2 版本 ↔ 命名约定(加版本必须沿用)

| 版本 | 生成命名空间 | 文件名前缀 | 别名约定 | 示例 |
|---|---|---|---|---|
| 1.0.1 | `...V1_0_1` | `Llrp101*` | `V101*` | `Llrp101InventoryCompiler` |
| 1.1 | `...V1_1` | `Llrp11*` | `V11*` | `Llrp11InventoryCompiler` |
| 2.0 | `...V2_0` | `Llrp20*` | `V20*` | `Llrp20InventoryCompiler` |
| 3.0(仅示例,非规划) | `...V3_0` | `Llrp30*` | `V30*` | `Llrp30InventoryCompiler` |

## 3. LlrpReader 暴露给外面的接口

### 3.1 公共面清单

**构建** `LlrpReader.CreateBuilder(host)` → 流式 builder(`WithPort/WithProtocolVersionPolicy/UseImpinj/UseReaderExtension/UseProtocolModule/WithFrameObserver/WithAutomaticReconnect/WithKeepaliveTimeout/...`) → `Build()`

**生命周期** `ConnectAsync` / `DisconnectAsync` / `ReconnectAsync` / `DisposeAsync`;属性 `ConnectionState` / `IsConnected` / `ConnectionId` / `NegotiatedVersion` / `OperationState` / `ResourceMode` / `IsManagedStateSynchronized` / `Options`

**盘点** `StartInventoryAsync(settings)`(部署+启动,独占资源)/ `StartInventoryAsync()`(启动已部署)/ `StopAsync` / `ClearManagedSettingsAsync` / `SynchronizeStateAsync` / `CurrentInventorySettings`

**配置** `QuerySettingsAsync` → `ReaderSettingsSnapshot` / `GetDefaultSettingsAsync` / `ValidateSettingsAsync` / `ApplySettingsAsync` / `SetGpoAsync`

**标签访问** `ReadTagMemoryAsync` / `WriteTagMemoryAsync` / `LockTagMemoryAsync` / `KillTagAsync` / `BlockEraseTagMemoryAsync` / `ExecuteTagAccessAsync` / `ExecuteTagAccessSequenceAsync` / `GetTagReportsAsync`

**专家资源与 raw** `RoSpecs` / `AccessSpecs` / `EnterManualResourceModeAsync` / `ExitManualResourceModeAsync` / `Protocol`(raw 收发)/ `TranslateTagReports` / `ReadMessagesAsync` / `ReadTagReportsAsync` / `Registry` / `RefreshCapabilitiesAsync`

**元数据** `Identity` / `Capabilities` / `Extensions`

**事件(11 个)** `ConnectionChanged` / `ErrorOccurred` / `TagsReported` / `GpiChanged` / `AntennaChanged` / `KeepaliveReceived` / `KeepaliveTimedOut` / `ReportBufferOverflow` / `ReportBufferWarning` / `ReaderExceptionOccurred` / `TagReportsDropped`

**内部面(只供版本边界组件,外部不可用)** `CompileDefaultInventoryRoSpec` / `TransactAsync` / `TransactSessionAsync` / `TransactDuringInitializationAsync` / `TransactDuringExtensionInitializationAsync` / `SendAsync` / `TransactRawAsync` / `SendRawAsync` / `NextMessageId` / `SelectProtocolAdapter` / `Logger` / `StartAsync(settings)`

### 3.2 门面机制(改它之前必须懂)

- **三个状态正交**:`ConnectionState`(Disconnected→Connecting→Negotiating→Initializing→Ready)管连接;
  `OperationState`(Idle/Starting/Inventorying/Stopping)管盘点;`ResourceMode`(Idle/HighLevelConfigured/HighLevelRunning/ManualResources/StateUnknown)管资源所有权。
- **报告出口互斥**:`TagsReported` 事件、`ReadTagReportsAsync()`、`InventorySession.ReadReportsAsync()` 三选一;
  同一盘点生命周期内先消费的出口取得所有权,其余立即抛 `InvalidOperationException`。
- **托管资源独占**:高层盘点使用固定 ROSpec 14150 / AccessSpec 14151,部署前删除设备上全部 ROSpec/AccessSpec
  (LLRP id=0 语义)。raw 或手动资源操作后 `IsManagedStateSynchronized=false`,须 `SynchronizeStateAsync` 或带 Inventory 的
  `ApplySettingsAsync` 强制接管。
- **版本在连接时锁定**:`Auto`(默认)先探测 1.1(`GET_SUPPORTED_VERSION`),支持则 `SET_PROTOCOL_VERSION` 后切换 1.1 适配器;
  `Force101` 不探测;`Force11` 失败即连接失败不静默回退。

## 4. Impinj 扩展 SDK 项目(LlrpSdk.Extensions.Impinj)的内部技术结构

### 4.1 文件树与各文件职责

```text
src/LlrpSdk.Extensions.Impinj/          (7 个手写文件,依赖 LlrpSdk + Abstractions + LlrpNet.Protocol.Impinj)
├── Registration/ImpinjLlrpExtension.cs   483 行,扩展包的心脏:两个入口类 + 五个 contributor 实现 + 序列化
│     ├── ImpinjProtocolModule : ILlrpProtocolModule     阶段一入口:转发注册生成的 Impinj Codec
│     ├── ImpinjReaderExtension : IReaderExtension + 5 个 contributor 接口  阶段二入口:身份匹配/初始化/全管道
│     └── UseImpinj() 扩展方法:builder.UseProtocolModule(...) + UseReaderExtension(...) 两步合一
├── Settings/ImpinjReaderSettings.cs      高层配置模型:ImpinjReaderConfiguration(ExtKey="impinj.configuration",可写)
│                                         + ImpinjReaderFacts(ExtKey="impinj.facts",只读,如区域/温度)
├── Inventory/ImpinjInventoryControlOptions.cs   盘点控制模型(ExtKey="impinj.inventoryControl":定频/低占空/搜索模式/
│                                         人口估计/Gen2X/端点校验/爬坡功率) + ImpinjInventoryControlConfigurator(编译侧)
├── Inventory/ImpinjInventoryReportOptions.cs    报告选项模型(ExtKey="impinj.inventoryReport":SerializedTID/RFPhaseAngle/
│                                         PeakRSSI/GPS/Doppler/TxPower/XPC/CRHandle/...) + ImpinjInventoryReportConfigurator(编译侧)
├── Inventory/ImpinjInventoryCapabilities.cs     能力目录:ImpinjInventoryCapabilityCatalog.Get(设备身份) →
│                                         该型号/固件支持哪些扩展字段(真机证据驱动,如 R420 6.4.1 不支持人口估计)
├── Inventory/ImpinjInventorySettingsBuilder.cs  typed builder:IncludeSerializedTid/IncludeRfPhaseAngle/... ,
│                                         Build 时把 Report/Control 写入 InventorySettings.Extensions(经核心 SetExtension)
└── Reports/ImpinjTagReportExtensions.cs         报告扩展值模型(ImpinjGpsCoordinates/ImpinjBitVector 等)+
                                          TagReport 便捷扩展 GetSerializedTidHex()/SerializedTidHex
```

### 4.2 两阶段入口的机制

- **阶段一(连接前,Codec)**:`ImpinjProtocolModule.Register(registry)` 转发到生成的 `LlrpNet.Protocol.Impinj` 模块,
  把 Impinj 自定义消息(`IMPINJ_ENABLE_EXTENSIONS` 等)与参数(ParameterType=327 下的各 Subtype)的编解码器
  注册进 `LlrpCodecRegistry`。没有这一步,Impinj 报文只会被降级成 `RawCustomParameter`。
- **阶段二(连接后,身份)**:连接初始化时门面拿到 `ReaderIdentity` 后执行 `ActivateReaderExtensions`——
  `ImpinjReaderExtension.Matches` 判定 `ManufacturerId==25882 && ProtocolVersion==Version101` 即激活;
  随后 `InitializeConnectionAsync` 发 `IMPINJ_ENABLE_EXTENSIONS` 事务,成功后才继续拉全量能力
  (此时响应里的 Impinj Custom 参数已能被阶段一的 Codec 强类型解码)。互斥组 `reader-vendor` 保证同组只激活一个。

**锚定与增量(与标准协议的同构关系)**:标准协议是每版本全量实现(`Llrp101*`/`Llrp11*` 切片);厂商扩展是
**锚定单一版本的增量**——Impinj 生成包命名空间带 `V1_0_1`、自定义参数只注册进 1.0.1 版本键、`Matches` 要求
`Version101`,因此 1.1 连接上扩展不激活。扩展只向固定槽位(CustomItems、`Extensions` 字典)塞值;
厂商若要支持第二个版本,需要第二套生成包与扩展实例,而不是让现有扩展跨版本。

**“加”与“替”的边界(报文层面)**:只加不替——Configurator 从不删除/改写标准参数,标准参数树与厂商参数树在报文中并列挂载(CustomItems 槽位,注册键空间不相交),互不覆盖;
语义冲突时扩展**显式抛错**(如 TruncatedReply 非空掩码与标准过滤器并存)。但**设备语义层存在“扩展接管标准
行为”**——定频覆盖标准跳频表、动态人口估计接管静态 `TagPopulation`、搜索模式与状态感知盘点语义重叠、
TagFilterVerification 改变标准 Select 的应用方式等:标准参数仍在报文中,优先级由设备固件决定,SDK 刻意不仲裁。
部分重叠组合目前无冲突校验(仅 TruncatedReply 有),是否补校验属扩展层策略。

### 4.3 五个 contributor 的机械原理(扩展值如何进出核心模型)

核心通道只有一条:**中立模型上的 `Extensions` 字典 + 稳定字符串键**。核心 SDK 不认识这些键,只负责把
“键-值”带过管道;Impinj 扩展认领自己名下的键,在正确的一端做类型转换。

| Contributor | 挂接点(中立管道) | 机械原理 | 方向 |
|---|---|---|---|
| `IInventoryContributor.Contribute` | 门面 `BuildInventoryCustomItems`(编译前) | 读 `InventorySettings.Extensions["impinj.inventoryControl"]` → `ImpinjInventoryControlConfigurator.BuildCustomItems(身份,选项,过滤器数)` 产出 `ImpinjGen2XInventoryConfig` 等自定义参数 → `AddC1G2InventoryCommandCustomItem`(将进 `C1G2InventoryCommand.CustomItems`);["impinj.inventoryReport"] 同理走 `AddRoReportSpecCustomItem`(将进 `ROReportSpec.CustomItems`) | 意图 → 线协议 |
| `IInventorySettingsContributor.ContributeQuery` | `ManagedInventoryStateAssembler.Assemble`(反解析时) | 从 `ParsedManagedRoSpec` 的 `ReportCustomItems`/`CommandCustomItems` 里 `OfType` 出 11 种 Impinj 自定义参数,逐字段还原 `ImpinjInventoryControlOptions` / `ImpinjInventoryReportOptions` 写回 `InventorySettings.Extensions`;`ImpinjInventoryCapabilityCatalog` 决定 `Supports*` 门控与 `AllowUnverifiedFields` 标记 | 线协议 → 意图 |
| `ITagReportContributor.Contribute` | 门面 `ApplyTagReportContributors`(翻译器产出后) | 遍历 `TranslatedTagReport.CustomItems`,switch 匹配 `ImpinjSerializedTID`/`ImpinjRFPhaseAngle`/`ImpinjPeakRSSI`/`ImpinjGPSCoordinates`/`ImpinjRFDopplerFrequency`/`ImpinjTxPower`/`ImpinjXPCWords`/... → 写 `TagReport.Extensions["impinj.*"]` | 线协议 → 报告 |
| `IReaderSettingsContributor`(三个方法) | 门面配置管道 | `BuildQueryParameters` → 把 `ImpinjRequestedData(All_Configuration)` 塞进 `GET_READER_CONFIG` 的 CustomItems;`ContributeQuery` → 从响应 CustomItems 还原 `ImpinjReaderConfiguration`/`ImpinjReaderFacts` 到 `ReaderConfiguration.Extensions`;`BuildApplyParameters` → 反向把配置编译成 `ImpinjGPIDebounceConfiguration` 等塞进 `SET_READER_CONFIG` | 双向 |
| `IReaderSettingsSerializationContributor` | `ReaderSettingsSerializer` | `CanHandle(scope,key)` 认领 4 个键(按 Configuration/Inventory scope);`Serialize` 产出 `{version:1, value:<typed JsonNode>}`;`Deserialize` 按 (scope,key) 反序列化回强类型,保证厂商值随 Settings JSON 无损持久化 | 双向 |

(另有 `IReaderSettingsDefaultsContributor`——连接后产出 Reader 默认档案;当前 Impinj 扩展未实现,Seuic 扩展实现了它。)

### 4.4 同一功能的两种机制:协调责任在哪一层(架构判定)

同一功能常同时存在标准机制与扩展机制(定频 vs 标准跳频表、动态人口估计 vs 静态 `TagPopulation`、
搜索模式 vs 状态感知盘点)。责任划分是固定的:

| 责任 | 层 | 现状 |
|---|---|---|
| 机制本身(编译/反解析/投影) | SDK/扩展层 | ✅ 已实现(Configurator、contributor 管道),应用层永不碰报文 |
| 客观校验(能力门控、硬冲突报错) | SDK/扩展层 | 🟡 能力目录 ✅、TruncatedReply+标准过滤器 ✅;缺:动态人口估计 vs 静态 TagPopulation、SearchMode vs 状态感知 |
| 语义优先级/路线选择(用标准还是用扩展实现) | **应用层** | ✅ 双通道独立暴露,SDK 不自动选边 |

判定理由:核心零感知原则禁止 SDK 内做厂商语义决策;厂商策略无法跨厂商复用;设备固件才是最终裁判,
SDK 能诚实做的是显式冲突报错而非静默消解。可选地,将来可在扩展层提供 Impinj 协调 facade
(一条意图 → 标准+扩展一致性组合),但不进核心。

### 4.5 程序集边界与依赖方向

```text
LlrpNet.Protocol.Impinj       生成的厂商线协议类型 + Codec + 注册模块
                              (只依赖 LlrpNet.Protocol/Core;禁止依赖 LlrpSdk —— 保证线协议层可独立使用)
LlrpSdk.Extensions.Impinj     手写:两阶段入口 + 高层选项模型 + Configurator/能力目录 + typed builder + 报告便捷方法
                              (依赖 LlrpSdk + LlrpSdk.Extensions.Abstractions + LlrpNet.Protocol.Impinj)
```

写一个新厂商扩展 = 实现 Abstractions 的两个契约 → 有私有线协议时先做生成扩展包 → 手写选项模型与 contributor →
加 `UseXxx()`。核心 SDK 与其它厂商扩展零改动。

`LlrpSdk.Extensions.Zebra` 已按同一结构实现最小子集(UseZebra + 设置/报告选项/相位·GPS·XPC 投影,无能力目录,
待真机证据)。

## 5. 增加一个新协议版本:项目结构树中的改动(以 3.0 为例;3.0 仅示例、非规划)

> 假设场景:未来要支持 LLRP 3.0。前提(协议层,不在本项目):`definitions/` 有 3.0 定义;`LlrpNet.Protocol` 已生成
> `V3_0` 类型集与 `Llrp30StandardModule`;`LlrpProtocolVersion.Version30` 已加。

### 5.1 改动树

```text
src/LlrpSdk/
├── Reader/
│   └── LlrpReader.cs                 【修改·1 行】适配器注册数组加 new Llrp30ProtocolAdapter()
├── Settings/
│   └── InventorySettings.cs 等        【修改·按需】3.0 新概念加 init 属性(默认值=旧行为)
├── Inventory/
│   └── Llrp30InventoryCompiler.cs    【新增】抄 Llrp11InventoryCompiler;核对 3.0 过滤器/触发器/报告结构差异
├── TagAccess/
│   └── Llrp30TagAccessCompiler.cs    【新增】抄 Llrp11TagAccessCompiler;3.0 新操作类型在此加分支
├── Reports/
│   └── Llrp30TagReportTranslator.cs  【新增】抄 Llrp11TagReportTranslator;3.0 新报告字段在此投影
└── Protocol/
    ├── Llrp30ProtocolAdapter.cs      【新增】抄 Llrp11ProtocolAdapter;实现 ILlrpProtocolAdapter 全部 26 成员
    ├── Llrp30ManagedRoSpecParser.cs  【新增】抄 Llrp11ManagedRoSpecParser;核对 3.0 AISpec/InventoryParameterSpec 形状与枚举映射
    ├── Llrp30EventProjector.cs       【新增】抄 Llrp11EventProjector;核对 3.0 ReaderEventNotificationData 字段
    ├── ReaderEventProjector.cs       【修改·1 行】加 3.0 类型分派
    ├── LlrpProtocolMessageFactory.cs 【修改·6 处】IsKeepalive/IsCloseConnection/CreateKeepaliveAck/CreateCloseConnectionResponse/CreateEnableEventsAndReports/TryCreateOperationException 各加 Version30 分支
    └── LlrpVersionNegotiator.cs      【修改·按需】若 3.0 有版本协商,加探测分支
```

### 5.2 为什么是这个形状(技术说明)

- **新增 6 个文件 = 6 个 ②切片**,全部从 `Llrp11*` 同名模板改写:适配器管“全向翻译”,解析器管“读线”,投影器管“事件”,
  编译器/翻译器管“域内正向”。接口契约(`ILlrpProtocolAdapter`)是唯一硬约束,抄模板即可满足。
- **修改点全部集中在 5 个接线点**(§1.2 的 ③边界):门面 1 行注册、分派器 1 行、消息工厂 6 处 switch、
  协商器按需、领域模型按需。**门面逻辑、1.0.1/1.1 切片、中立核心零改动。**
- **新旧版本永不互相引用**:3.0 的差异只写在自己的 6 个文件里;若某处两版逻辑必须相同,靠等价测试钉住,
  而不是靠共享基类(1.1 已证明“新版不是旧版超集”——BlockPermalock/Recommission 只存在于 1.1)。

### 5.3 测试配套(照单补)

- [ ] `RoundTrip_30_CompileThenParse_ReproducesDomainIntent` + 全状态映射(编译↔反解析闭环;漏写任一侧立即失败)
- [ ] `Translate_30` / `ProjectEvent_30` 等价测试(与既有版本同结构时)
- [ ] `Compile_30And11_ProduceIdenticalRospecWireBytes`(线字节等价,若结构相同)
- [ ] 守护测试自动生效(LlrpReader.cs 出现版本类型即失败)
- [ ] 1.0.1/1.1 全部既有测试零改动通过;真机验收记录进 [../acceptance/reader-interoperability.md](../acceptance/reader-interoperability.md)
- [ ] 文档同步:`../status.md` 支持矩阵、本文件 §1.1 树、覆盖率文档