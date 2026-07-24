# LLRP C# SDK 整体规划

> 文档状态：持续实施（M1 至 M5b 已完成；M6/M7/M8 已建立基础设施，正继续实现）
> 迁移日期：2026-07-23  
> 项目根目录：`F:\Projects\LLRP\LLRPCSharp`  
> 当前范围：SDK 为核心，CLI 为唯一应用层与测试入口  
> 支持目标：LLRP 1.0.1、1.1、2.0，以及厂商和客户自定义扩展

### 已确定的工程基线

- Target Framework：`net10.0`；
- Visual Studio Solution：`LLRPCSharp.slnx`；
- SDK 选择：`global.json` 固定 .NET 10.0.100 feature band并滚动到同 band 最新补丁；
- 公共编译设置：`Directory.Build.props`；
- NuGet 集中包管理：`Directory.Packages.props`，项目内不写版本号；
- C# 文件编码：由 `.editorconfig` 强制 `utf-8-bom`。

## 0. 当前资料状态

项目当前已有以下输入，后续直接基于它们核验和导入，不再等待重复提供：

- `definitions/imports/xml/llrp-1.0.1/` 下的 Core XML/XSD；
- `definitions/imports/xml/extensions/impinj/` 下的 Impinj XML/XSD；
- `references/standards/llrp-1.0.1/` 下的 1.0.1 标准 PDF；
- `references/standards/llrp-1.1/` 下的 1.1 标准与 Conformance PDF；
- `references/standards/llrp-2.0/` 下的 Release 2.0（Jan 2021）标准 PDF。

Core XML 文件头声明 Apache License 2.0；当前 Impinj 定义文件头声明 confidential/proprietary，因此在确认授权和再分发范围之前仅作为本地资料，并由 `.gitignore` 排除。标准与定义的来源、版本映射和 SHA-256 已记录在 [`protocol-source-inventory.md`](protocol-source-inventory.md)。真机原始帧、设备型号/固件和已知兼容问题尚待补充。

## 1. 项目定位

本项目定位为一套现代化的 .NET LLRP 开发套件，而不只是二进制编解码库。

核心产品是面向应用程序的 `LlrpSdk.LlrpReader`：一个代表单台 RFID 读写器，管理连接、协议协商、配置、盘点、标签访问、扩展和状态恢复的设备会话对象。

```text
CLI（当前唯一应用层、诊断与测试入口）
                    │
                    ▼
              LlrpSdk.LlrpReader
       ┌────────────┼──────────────┐
       │            │              │
 常用能力直接方法   进阶资源入口      原始协议入口
 Start / Stop    RoSpecs           Protocol
 Settings        AccessSpecs
 Tag Access      Extensions
       │
       ▼
 LlrpNet Core + Protocol + 扩展协议模块
       │
       ▼
 TCP / LLRP 二进制协议 / 真实读写器
       │
       ▼
 后续：Virtual Reader、场景、抓包与回放
```

### 1.1 项目目标

- 为 C# 应用提供稳定、异步、线程安全的读写器 SDK。
- 保持与现有 LLRP 1.0.1 设备及代码的兼容，并逐步支持 LLRP 1.1 和 2.0。
- 将标准 LLRP、厂商扩展与客户客制报文纳入统一编解码、注册和扩展体系。
- 让普通业务使用高级 API，让协议测试和诊断能够使用 ROSpec、AccessSpec 与原始报文。
- 让 CLI 成为 SDK 的首个真实使用者、集成测试入口和现场诊断工具。
- 为虚拟读写器、场景测试、抓包重放和 CI 互操作测试预留稳定边界。

### 1.2 当前边界

当前阶段以 SDK 与 CLI 为重点，不建设图形化上位机。CLI 可用于真实设备控制、协议测试和回归验证，但不复制 SDK 内部的通信、编解码和资源管理逻辑。

第一阶段优先实现标准盘点路径和可验证的协议基础设施；LLRP 2.0 完整覆盖、厂商全量扩展、虚拟设备和图形化应用按后续里程碑推进。

## 2. 核心设计原则

### 2.1 `LlrpReader` 是设备会话根对象

一个 `LlrpReader` 对应一台读写器。它组合底层会话与服务，不继承底层 TCP Client，也不向应用泄漏内部 Manager。

`LlrpReader` 负责：

- 建立和释放连接；
- 版本协商、初始化和能力查询；
- 维护连接状态、运行状态和期望状态；
- 提供高级、进阶与原始三个能力层次；
- 管理扩展的协议注册、匹配和激活；
- 处理 Keepalive、超时、断线重连及配置恢复。

### 2.2 能力分层，入口按使用频率命名

SDK 提供三个能力层次，但不强制设计成三个形式化容器。最高频的标准能力直接放在 `LlrpReader`；低频且成组的资源操作使用明确命名的服务；原始消息通过 `Protocol` 暴露。

| 层次 | 公开入口 | 用途 |
|---|---|---|
| 标准高级能力 | `StartAsync`、`StopAsync`、`ApplySettingsAsync`、标签访问方法 | 普通应用与常规 CLI，隐藏 ROSpec/AccessSpec 细节 |
| 进阶资源能力 | `RoSpecs`、`AccessSpecs` | CLI、协议测试和资源生命周期测试 |
| 原始协议能力 | `Protocol` | 精确收发 LLRP 消息、边界测试和未封装功能 |

```csharp
await reader.ApplySettingsAsync(settings);
await reader.StartAsync();
await reader.RoSpecs.StartAsync(roSpecId);
await reader.Protocol.TransactAsync<V101AddRoSpecResponse>(request);
var vendor = reader.Extensions.Get<VendorAReaderExtension>();
```

`Advanced` 这类仅表达层级的中间入口不进入公共 API。CLI 在线操作通过 `LlrpReader` 暴露的稳定服务完成；离线编码、解码、校验和检查可直接使用协议层。

### 2.3 各层接口与版本类型可见性

版本化的生成协议模型是 `LlrpNet.Protocol` 的正式 API；不保留旧手写 PascalCase 类型名的长期兼容层。不同层的调用者看到的类型范围必须不同，不能为了便利而把 LLRP Message 泄漏到普通业务 API。

| 层次 | 主要调用者 | 当前/目标入口 | 可否直接使用 `Messages.Vx_y_z` | 责任 |
|---|---|---|---|---|
| 应用能力层 | 普通应用、常规 CLI | `LlrpReader` 的 `ConnectAsync`、`ApplySettingsAsync`、`StartAsync`、`InventoryAsync`、标签访问方法 | 否 | 面向设备意图、设置和业务结果；SDK 负责消息编排、资源 ID 与协议版本 |
| 进阶资源层 | 集成开发、资源管理 CLI、协议测试 | `reader.RoSpecs`、后续 `reader.AccessSpecs` | 仅参数模型可见，且仅在确有精确资源需求时 | 管理 ROSpec/AccessSpec 生命周期；不得要求调用者构造 `ADD_ROSPEC` 等 Message |
| 原始协议层 | 协议专家、诊断工具、未封装厂商功能 | `reader.Protocol` 的 `TransactAsync`、`SendAsync`、`TransactRawAsync`、`SendRawAsync` | 是 | 精确收发版本化 Message 或原始帧；调用者承担协议语义和状态同步责任 |
| 协议库层 | 离线工具、厂商 Protocol Module、SDK 内部 | `LlrpCodecRegistry`、`Vx_y_zProtocolModule`、`ILlrpMessage`、`ILlrpParameter` | 是 | 生成模型、编码、解码、注册和 Unknown/Raw 保留，不承担设备会话业务 |
| Core 层 | SDK/Protocol 内部 | `LlrpSession`、Transport、Frame Decoder、`ILlrpFrameObserver` | 否 | TCP、帧、事务、日志与诊断；不引用任何版本或厂商协议类型 |

普通业务调用的目标形态如下；这些高级模型属于 M3/M4，不以生成的 LLRP 类型作为方法参数或返回值：

```csharp
await reader.ConnectAsync(cancellationToken);
await reader.ApplySettingsAsync(settings, cancellationToken);

await foreach (TagReport report in reader.InventoryAsync(cancellationToken: cancellationToken))
{
    Consume(report);
}
```

进阶资源层当前已有 `IRoSpecService`。其 `AddAsync(ILlrpParameter)` 是 1.0.1 基线阶段的协议感知接口：实现会验证参数的 wire type 为 ROSpec，并在当前版本下发送对应消息。M3/M6 继续演进时，版本适配器负责把该资源操作映射到目标版本；普通应用不应经由此入口构造盘点流程。

仅原始协议层需要明确版本类型。调用者可在单个文件内使用局部别名，避免歧义：

```csharp
using V101AddRoSpec = LlrpNet.Protocol.Messages.V1_0_1.ADD_ROSPEC;
using V101AddRoSpecResponse = LlrpNet.Protocol.Messages.V1_0_1.ADD_ROSPEC_RESPONSE;

V101AddRoSpec request = CreateExactRequest();
V101AddRoSpecResponse response = await reader.Protocol
    .TransactAsync<V101AddRoSpecResponse>(request, cancellationToken: cancellationToken);
```

`GlobalAliases.cs` 只是在 SDK/CLI 迁移期间让内部旧代码编译的临时桥接文件：它只绑定 1.0.1，既不导出给 NuGet 使用者，也不得成为新的公共 API 或新代码的依赖。后续内部代码改为显式版本命名空间或文件级别 alias 后删除该桥接。

## 3. 解决方案与模块划分

```text
src/
├─ LlrpNet.Core/
├─ LlrpNet.Protocol/
├─ LlrpNet.ProtocolModel/
├─ LlrpNet.ProtocolGenerator/
├─ LlrpSdk/
├─ LlrpSdk.Extensions.Abstractions/
├─ LlrpSdk.Extensions.Impinj/         # 首个厂商扩展示例，名称可调整
├─ LlrpCli/
└─ LlrpVirtualReader/                  # 后续阶段

definitions/
├─ imports/xml/
├─ extensions/
├─ llrp-common.yaml
├─ llrp-1.0.1.yaml
├─ llrp-1.1.yaml
└─ llrp-2.0-delta.yaml

tests/
├─ LlrpNet.Core.Tests/
├─ LlrpNet.Protocol.Tests/
├─ LlrpSdk.Tests/
├─ LlrpCli.Tests/
└─ Interop.Tests/
```

### 3.1 `LlrpNet.Core`

与具体 LLRP 版本和厂商无关，负责可靠通信基础设施：TCP 生命周期、半包/粘包和帧切分、公共 Message Header、Message ID、请求/响应事务匹配、超时和取消、Keepalive、原始帧诊断。

该层不包含 `LlrpReader`、ROSpec 业务管理或 UI 逻辑。

### 3.2 `LlrpNet.Protocol`

负责 LLRP 1.0.1、1.1、2.0 的消息、参数、枚举、TV/TLV Header、Codec、多版本类型映射、标准注册模块、Unknown/Raw 类型，以及调试 JSON、Hex Dump 和协议校验辅助。

### 3.3 `LlrpNet.ProtocolModel` 与 `LlrpNet.ProtocolGenerator`

```text
XML / YAML 定义
       ↓
Definition Loader / Importer
       ↓
ProtocolModel
       ↓
Validator
       ↓
C# Generator
       ↓
Message、Parameter、Enum、Codec、Registry、校验代码
```

### 3.4 `LlrpSdk`

负责 `LlrpReader`、Reader 状态机、Settings/Capabilities/Identity、高级盘点和标签访问、ROSpec/AccessSpec 进阶服务、Settings Compiler、TagReport Pipeline、自动重连、缓存失效、状态同步及扩展生命周期。

### 3.5 扩展包

每个厂商或稳定的大型客户协议包独立发布，核心 SDK 不反向依赖具体厂商。扩展包可包含 `Protocol`、`Reader`、`Settings` 和 `Reports` 四部分。

### 3.6 `LlrpCli`

CLI 是当前唯一应用层，同时是 SDK 的真实消费方、回归工具和协议诊断工具。在线设备操作依赖 `LlrpSdk`；离线 encode/decode/validate/inspect 依赖 `LlrpNet.Protocol`。CLI 不引用内部的 Session、Manager 或 Frame Decoder。

### 3.7 `LlrpVirtualReader`

后续作为状态化 LLRP Server，用于无硬件开发、CI 和故障测试。它复用 Protocol 与 Codec，但不依赖 CLI。

## 4. LLRP 版本兼容策略

需兼容 LLRP 1.0.1、1.1 和 2.0。LLRP 2.0 不只是 Header 版本号变化；新增或修改的消息、参数、枚举、容器约束和 Gen2v2 能力必须由独立协议模型表达。

完成 M6 后，`ConnectAsync()` 内部完成连接、Reader Event、共同版本选择、Registry 切换、Capabilities/Identity/Configuration 查询和扩展激活，业务代码不自行处理协商。当前 M2 的 1.0.1 Reader 基线固定使用 `Version101`；这不是多版本协商的临时失败，而是 M6 的明确工作范围。

```text
建立 TCP → 接收 Reader Event → 查询支持版本 → 选择最高共同版本
→ 设置版本并切换 Registry → 初始化设备 → 匹配并激活扩展 → Ready
```

SDK 通过 `ILlrpProtocolAdapter` 屏蔽版本差异，计划实现：

```text
Llrp101ProtocolAdapter
Llrp11ProtocolAdapter
Llrp20ProtocolAdapter
```

业务层始终面对同一个 `LlrpReader` 和统一的高级模型。

## 5. 协议定义与源码生成

LLRP 类型包含大量位字段、TV/TLV、嵌套参数、Choice、可选/重复参数和版本差异。源码生成用于确保模型、编码、解码、长度计算、验证和注册表与协议定义一致。

| 格式 | 定位 |
|---|---|
| XML | 导入旧 LTK 定义和兼容历史资产 |
| YAML | 人工维护的新协议源定义、版本差异和大型扩展 |
| JSON | 内部标准化模型、调试输出和 Schema 校验 |
| C# | 少量、快速迭代的客户 Custom 报文 |

生成链以 C# Loader、ProtocolModel、Validator 和 Generator 为中心，不沿用旧式 XML+XSLT 作为核心。

LLRP 2.0 推荐采用“1.1 基础定义 + 2.0 Delta → 完整 2.0 ProtocolModel”。PDF 可提取候选定义，但类型编号、位宽、保留位、字段顺序、TV/TLV、Choice 和基数必须人工核验并自动校验。

### 5.1 手写与生成边界

手写：Header、TV/TLV Header、Bit/Buffer Reader/Writer、帧切分、Session、Transaction、Registry 运行时、Unknown/Raw、`LlrpReader`、状态机、高级服务和 Settings Compiler。

生成：消息、参数、枚举、Codec、注册模块、固定长度与引用校验，以及可选的调试输出、协议索引和测试骨架。

### 5.2 Validator 最低要求

- 同版本 Message Type、Parameter Type 和枚举值唯一；
- 字段、参数、枚举、响应类型与 Choice 引用完整；
- 固定长度、TV/TLV 最小长度和位宽合法；
- 版本依赖与容器关系合法；
- `VendorId + Subtype + 类型类别` 不冲突；
- 生成后通过二进制 `Decode → Encode` 往返测试。

## 6. Codec 与 Registry

Codec 负责 C# 协议对象与 LLRP 网络大端序二进制之间的转换，必须正确处理位字段、保留位、TV/TLV、嵌套参数、Choice 和 Custom 类型。

Registry 按以下键定位 Codec：

```text
标准 Message：Protocol Version + Message Type
标准 Parameter：Protocol Version + Parameter Type
Custom Message/Parameter：Vendor ID + Subtype + 类型类别
```

未知类型不能导致整条消息失败，应保留为 `UnknownMessage`、`UnknownParameter` 或 `RawCustomParameter`，以支持诊断和往返编码。

## 7. 厂商与客户扩展架构

扩展包分为两个生命周期：

| 分层 | 作用 | 时机 |
|---|---|---|
| Protocol Module | 注册 Custom Message/Parameter、Codec 和类型映射 | 连接前 |
| Reader Extension | 设备匹配、初始化、能力封装、设置贡献和报告解析 | 识别设备后 |

Codec 在连接前注册，因为初始设备消息可能包含 Custom Parameter；Reader 功能扩展只有识别设备后才激活。

### 7.1 注册、匹配与激活

```text
创建 Reader → 注册全部已配置扩展的 Codec → 连接并完成标准初始化
→ 按 Vendor/Model/Firmware/Capabilities 匹配 → 按互斥组选择
→ 扩展初始化 → 激活非冲突扩展
```

A、B 等主厂商扩展归入 `reader-vendor` 互斥组，同组只能激活一个；Diagnostics、Customer Metrics 等非冲突功能扩展可以叠加。若两个模块注册相同的 `VendorId + Subtype + 类型类别`，构建阶段立即失败，禁止静默覆盖。

少量客户报文使用 C# 类型、手写 Codec 和运行时注册；大型稳定协议使用 YAML、生成代码和独立扩展包。

## 8. 厂商额外寻卡数据

厂商在 TagReport 中返回的 Phase、Frequency、Doppler 或质量指标按以下管道处理：

```text
RO_ACCESS_REPORT 二进制
  → Registry/厂商 Codec
  → 标准参数 + 强类型 Custom Parameter
  → TagReportPipeline
  → 标准解析器 + 已激活 TagReportContributor
  → TagData.Extensions 中的强类型厂商数据
```

标准 `TagData` 不硬编码厂商字段。应用通过 `tag.Extensions.Get<T>()` 获取厂商数据。

| 情况 | 行为 |
|---|---|
| 未安装协议模块 | 保留 `RawCustomParameter`，标准 Tag 数据正常返回 |
| 已安装模块但未激活 Reader Extension | 可解码强类型参数，不填充高级扩展数据 |
| 已激活 Reader Extension | Contributor 填充 `TagData.Extensions` |

未知 Custom Parameter 在任何情况下都不能导致整个 `RO_ACCESS_REPORT` 失败。

## 9. `ReaderSettings` 与扩展贡献

标准设置不无限增加厂商属性，而是提供扩展数据容器。扩展通过受控 Contributor 向 Reader Config、ROSpec、Inventory Command、Report Spec 或 AccessSpec 贡献参数，不直接篡改完整消息，也不绕过 SDK 状态管理。

```text
VendorInventorySettings → Settings Contributor → ROSpec/ReportSpec Custom Parameter
→ 设备返回额外数据 → TagReportContributor → TagData.Extensions
```

## 10. `LlrpReader` 公共 API 草案

```csharp
public sealed class LlrpReader : IAsyncDisposable
{
    public ReaderConnectionState ConnectionState { get; }
    public ReaderOperationState OperationState { get; }
    public bool IsConnected { get; }
    public LlrpProtocolVersion NegotiatedVersion { get; }
    public ReaderIdentity? Identity { get; }
    public ReaderCapabilities? Capabilities { get; }
    public ReaderSettings? CurrentSettings { get; }

    public IRoSpecService RoSpecs { get; }
    public IAccessSpecService AccessSpecs { get; }
    public IReaderProtocolAccess Protocol { get; }
    public IReaderExtensionCollection Extensions { get; }

    public event EventHandler<TagReportEventArgs>? TagsReported;
    public event EventHandler<ReaderConnectionChangedEventArgs>? ConnectionChanged;
    public event EventHandler<ReaderErrorEventArgs>? ErrorOccurred;

    public Task ConnectAsync(CancellationToken cancellationToken = default);
    public Task DisconnectAsync(CancellationToken cancellationToken = default);
    public Task ReconnectAsync(CancellationToken cancellationToken = default);
    public Task<ReaderSettings> QuerySettingsAsync(CancellationToken cancellationToken = default);
    public Task ApplySettingsAsync(ReaderSettings settings, CancellationToken cancellationToken = default);
    public Task StartAsync(CancellationToken cancellationToken = default);
    public Task StopAsync(CancellationToken cancellationToken = default);
    public IAsyncEnumerable<TagReport> InventoryAsync(ReaderSettings? settings = null,
        CancellationToken cancellationToken = default);
    public Task<TagAccessResult> ExecuteTagAccessAsync(TagAccessRequest request,
        CancellationToken cancellationToken = default);
    public Task<ReadTagResult> ReadTagMemoryAsync(ReadTagRequest request,
        CancellationToken cancellationToken = default);
    public Task<WriteTagResult> WriteTagMemoryAsync(WriteTagRequest request,
        CancellationToken cancellationToken = default);
    public Task SynchronizeStateAsync(CancellationToken cancellationToken = default);
}
```

连接、配置、Start/Stop、盘点流及常用标签访问直接放在设备根对象上。ROSpec/AccessSpec 通过稳定服务暴露。第一阶段标签访问逐步覆盖 Read、Write、Lock、Kill；组合操作统一走 `ExecuteTagAccessAsync`；LLRP 2.0 Authenticate/Untraceable 后续加入。

截至 2026-07-24，已实现的 M3/M4 基线为：`StartAsync(ReaderSettings)`、`StopAsync()`、`InventoryAsync(ReaderSettings?)`、`ReadTagReportsAsync()`、`TagsReported`、`CurrentSettings`、`RoSpecs`、`AccessSpecs`、`IsManagedStateSynchronized` 和 `SynchronizeStateAsync()`。异步流与事件使用同一份已翻译的 TagReport；订阅者异常只写入日志，不中断收包。`ApplySettingsAsync`、`QuerySettingsAsync`、标签访问与 Extension 集合仍为后续 API；它们不能被表中的草案签名误解为当前已可调用。

## 11. Managed / Raw 与状态同步

高级方法、`RoSpecs` 和 `AccessSpecs` 属于 Managed 模式，SDK 管理资源 ID、设备状态、配置缓存和期望状态。`Protocol` 属于 Raw 模式，调用者可以发送任何消息，但 SDK 无法保证缓存仍有效。

- Raw 发送可能改变设备状态的消息后，相关缓存标记为 Dirty；
- 下一次 Managed 操作前自动或显式执行 `SynchronizeStateAsync()`；
- 无法可靠查询的状态明确标记为未知，不能假装缓存仍有效；
- CLI 的原始消息命令遵守相同规则。

当前实现采用保守策略：`reader.Protocol` 的成功 Typed/Raw 发送或事务完成后，将 `IsManagedStateSynchronized` 置为 `false` 并清除本地托管盘点状态；后续 `StartAsync` 要求先调用 `SynchronizeStateAsync()`。同步会查询 ROSpec 与 AccessSpec，但不会猜测或自动恢复之前的高级盘点意图；应用必须明确建立下一份期望配置。

## 12. CLI 规划与依赖边界

| 命令类别 | 入口 | 示例 |
|---|---|---|
| 常规设备控制 | `LlrpReader` 直接方法 | `inventory`、`tag read`、`config get` |
| 资源测试 | `reader.RoSpecs` / `reader.AccessSpecs` | `rospec add/start/stop` |
| 原始在线报文 | `reader.Protocol` | `message send` |
| 离线协议工具 | `LlrpNet.Protocol` | `decode`、`encode`、`validate`、`inspect` |
| 厂商功能 | `reader.Extensions.Get<T>()` | 厂商专用子命令 |

输出至少支持 `table`、`json`、`jsonl`、`csv` 和 `raw`。CLI 必须复用 SDK 服务，不维护另一套盘点、资源或配置逻辑。

## 13. 状态、可靠性与错误模型

连接状态计划包含 `Disconnected`、`Connecting`、`Negotiating`、`Initializing`、`Ready`、`Reconnecting`、`Disconnecting`、`Faulted`；操作状态包含 `Idle`、`Starting`、`Inventorying`、`Stopping`、`Accessing`。TCP 已连接不等于设备可操作，只有 `Ready` 后允许高级操作。

自动重连保存“设备当前状态”和“应用期望状态”：重新连接后重新协商、查询能力、激活扩展、应用期望配置并重建 SDK 管理的 ROSpec/AccessSpec；若期望状态仍为 Inventorying，则恢复盘点。显式 `StopAsync()` 清除恢复盘点的期望。

事件和异步流共用单一 `TagReportPipeline`，避免相同报告被重复解析或表现不一致。

## 14. 里程碑

### M1：协议运行时与 1.0.1 兼容

Header、Bit/Buffer Reader/Writer、TV/TLV、Frame Decoder、Session、Transaction、Registry、Raw/Unknown 类型以及 1.0.1 回归测试。

截至 2026-07-24：M1 已完成。Header、Buffer/Bit Reader/Writer、TV/TLV Header、流式 Frame Decoder、Message ID、Transaction、TCP Transport、Session、Registry、Unknown/Raw 与 Custom 注册均已实现。底层采用 `Microsoft.Extensions.Logging` 抽象，并通过 `ILlrpFrameObserver` 保留完整收发帧观测；即使应用只使用 `LlrpReader` 高级入口，底层报文日志/抓包仍可由 Builder 注入并持续工作。边界和内存所有权见 ADR 0001。1.0.1 手写模型已替换为生成模型；少量旧 Protocol 测试仍待按新模型同步，不阻塞当前 SDK/CLI 构建，但不能据此宣称 Protocol 测试套件已全绿。

### M2：`LlrpReader` 会话框架

Builder、连接生命周期、状态机、版本协商基础、Capabilities、Identity、Protocol 入口和扩展注册容器。

截至 2026-07-24：M2 已完成 1.0.1 Reader 会话基线。Builder、不可变 Options、连接状态机、Raw/Typed Protocol 入口、自定义 Registry 回调、Keepalive 自动应答、断线传播、重连，以及 `GetReaderCapabilities` 初始化均已实现；`Ready` 严格表示能力查询成功。实现已从手写 1.0.1 模型迁移到生成模型。跨版本协商与 Reader Extension 激活不属于 M2 完成条件，分别在 M6 与 M7 推进。

### M3：核心盘点路径

ReaderSettings、Settings Compiler、ROSpec Managed 服务、Start/Stop/Inventory、RO_ACCESS_REPORT、TagData、统一报告管道和 1.0.1 真机验证。

### M4：进阶资源与 CLI

AccessSpec Managed 服务、CLI 连接/配置/盘点/资源/Raw 命令、离线协议工具，以及 Managed/Raw 缓存失效与同步。

### M5：协议定义与生成系统

旧 XML Importer、YAML Loader、ProtocolModel、Validator、C# / Codec / Registry Generator，并将 1.0.1 定义接入回归。

截至 2026-07-24：M5a（标准 XML 生成链）和 M5b（定义/生成工作流）均已完成。LTK XML Importer、YAML Loader、ProtocolModel、Validator、C# / Codec / Registry Module Generator 与独立命令行工具均已实现；标准 1.0.1 的 42 个消息、111 个参数、42 个枚举和 14 个 Choice 已物化为受 Git 跟踪的 `.g.cs` 源码。生成器已覆盖参数引用、Choice、Custom 类型、长度计算、枚举值验证与依赖定义；YAML schema、`--verify` 和可重复执行的扩展定义命令已纳入仓库。1.1/2.0 的权威定义与 Delta 合成仍属于 M6 的后续输入。

### M6：LLRP 1.1 与 2.0

1.1 定义和适配器、2.0 Delta 与适配器、版本协商互操作，随后扩展 Gen2v2 能力。

截至 2026-07-24：已建立 `ILlrpProtocolAdapter` 边界，1.0.1 Adapter 负责标准 Codec 注册、盘点编译、报告翻译，以及 ROSpec/AccessSpec 的所有协议请求与响应映射。`RoSpecs` 和 `AccessSpecs` 服务不再直接引用 1.0.1 消息；加入 1.1/2.0 Adapter 后可在同一服务入口接管这些资源操作。共同版本选择和新版本 Adapter 仍依赖尚未导入并核验的 1.1/2.0 权威定义。

### M7：厂商扩展

首个 Protocol Module、Reader Extension、设备匹配与互斥组、Settings/TagReport Contributor、CLI 子命令和 Raw 未知扩展回归。

截至 2026-07-24：已完成连接前的 `ILlrpProtocolModule` 契约与
`LlrpReaderBuilder.UseProtocolModule(...)` 注册路径。模块在标准 1.0.1 Registry 之后、连接前注册；重复
module ID 和 Codec wire identity 冲突都会在创建 Reader 时失败。工作区内的 Impinj XML 是可用的本地增量输入
（Vendor ID 25882），但其声明为 confidential/proprietary 且被 Git 忽略；在获得再分发许可前，不提交该定义、
生成物或发布 Impinj 扩展。Reader 的厂商识别、互斥组和激活仍待许可范围、真实型号/固件规则及真机样本一并完成。

### M8：可靠性与虚拟设备

自动重连、配置/盘点恢复、诊断与抓包、Virtual Reader、场景和故障注入，以及真实/虚拟设备互操作测试。

截至 2026-07-24：已建立可直接启动的本地 LLRP 1.0.1 TCP Virtual Reader；它完成能力查询以及 ROSpec 的新增、查询、启用、启动、停止、禁用和删除状态机。报告生成、AccessSpec、可配置场景/故障注入、SDK 自动恢复和真实/虚拟互操作回归仍在后续范围。

## 15. 验收标准

### 15.1 Protocol/Core

- 支持类型可完成 `Decode → Encode` 二进制往返；
- 截断、非法长度、错误嵌套和未知类型行为明确、可诊断；
- 正确处理多消息同包和单消息跨包；
- 未知 Custom Parameter 不影响标准报告；
- Registry 冲突在注册时识别。

### 15.2 SDK/CLI

- 连接真实 1.0.1 设备并进入 `Ready`；
- 查询能力和配置、应用 Settings、启动和停止盘点；
- 事件和异步流获得一致 TagReport；
- CLI 常规能力复用 Reader 高级 API；
- CLI 通过进阶服务测试 ROSpec/AccessSpec 生命周期；
- Raw 修改使缓存失效并可同步恢复；
- 断线后按期望状态恢复配置和盘点。

### 15.3 扩展

- A/B Protocol Module 可同时注册且 Custom 类型不冲突；
- 同一 `reader-vendor` 互斥组只能激活一个主厂商扩展；
- 非冲突功能扩展可以叠加；
- 厂商 Tag 数据进入 `TagData.Extensions`；
- 未安装或未激活扩展时原始 Custom 数据仍保留。

## 16. 当前不做

- WPF、Avalonia、Web 管理平台和图形化配置工具；
- 一次性覆盖所有 LLRP 2.0 类型；
- 完整密码学实现和全部 Gen2v2 场景；
- 所有厂商扩展；
- RF 物理传播、碰撞和法规仿真；
- 官方一致性认证；
- 以运行时反射作为核心 Codec；
- 一开始建设大而全的通用协议生成平台。

## 17. 开工前待确认项

以下决定不阻塞目录和 Core 基础设计，但应在创建正式项目文件前冻结：

1. NuGet/程序集命名是否最终确定为 `LlrpNet.*`、`LlrpSdk`、`LlrpCli`。
2. 是否兼容旧 `LLRPSdk` 的公共 API，以及兼容层的范围。
3. 开源许可证或内部项目属性。
4. 首批真机品牌、型号、固件和已知兼容问题。
5. 核验现有 LLRP PDF/XML 的来源和版本映射，并补充可回归原始帧。

## 18. 结论

本项目的核心不是简单暴露底层 LLRP Message，而是通过 `LlrpSdk.LlrpReader` 提供一台可控、可诊断、可扩展、可恢复的 RFID 读写器会话。

```text
LlrpNet Core                 可靠通信、帧处理、事务与 Registry
LlrpNet Protocol/Generator   多版本协议模型、Codec 与生成资产
LlrpSdk.LlrpReader           应用如何使用一台读写器
Extensions                   厂商识别、功能封装、设置贡献和报告增强
LlrpCli                      SDK 的第一应用、诊断工具和测试入口
```

该边界既服务普通上位机开发，也满足 CLI 和研发团队对 ROSpec、AccessSpec、原始报文及厂商扩展的深度测试需求。
