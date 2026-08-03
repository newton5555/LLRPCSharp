# ADR 0003：读写器默认配置 Profile 与厂商型号适配

- 状态：Accepted（第一阶段已实施）
- 日期：2026-07-27

> 实施说明：本文记录设计决策时使用的早期 API 名称。当前实现统一使用
> `ReaderSettingsDefaults`、`GetDefaultSettingsAsync()`、`QuerySettingsAsync()`
> 和 `ApplySettingsAsync()`；`ReaderConfigurationPatch` 与旧的
> `*ConfigurationAsync` 名称不属于当前公开 API。

## 背景

当前 SDK 通过 `QuerySettingsAsync()` 和 `ApplySettingsAsync()` 管理版本无关的
`ReaderSettings`；底层 `GET_READER_CONFIG` / `SET_READER_CONFIG` 由 SDK 内部
编译和执行。

但部分设备存在以下需求：

1. 设备的默认配置具有明显的厂商、型号或固件差异；
2. 设备的 `GET_READER_CONFIG` 可能返回不完整，或者不适合作为业务初始化基线；
3. 盘点和标签访问编译器需要一组安全、可预测的初始配置；
4. 厂商扩展不应把默认策略硬编码到核心 SDK，也不应要求应用层直接处理版本化协议参数。

因此需要一个“不查询设备当前配置、只根据身份和能力生成 SDK 默认配置”的能力。

## 决定

### 1. 默认配置与实际配置分离

新增默认配置入口，首选语义为：

```csharp
ReaderSettingsDefaults defaults = await reader.GetDefaultSettingsAsync();
```

该方法：

- 不发送 `GET_READER_CONFIG`；
- 使用连接初始化阶段已经获取的 `ReaderIdentity` 和 `ReaderCapabilities`；
- 根据当前协议版本、厂商、型号、固件和已激活扩展选择 Profile；
- 不自动调用 `ApplySettingsAsync()`；
- 只返回 SDK 推荐的配置基线，不代表设备当前真实状态。

`QuerySettingsAsync()` 继续专门表示设备实际托管状态，两者不能互相替代。

完全离线的 Profile 创建能力可以在后续增加，但必须由调用方显式提供厂商、型号、固件和协议版本等身份信息，不能从空的 `LlrpReader` 推断设备型号。

### 2. 使用 Profile Provider 扩展机制

核心 SDK 提供版本无关的 Profile 选择抽象，厂商包提供具体 Profile。概念结构如下：

```text
IReaderConfigurationDefaultsProvider
    └─ IReaderConfigurationProfile
```

Profile 的上下文至少包含：

- `ManufacturerId`；
- `ModelId`；
- `FirmwareVersion`；
- 协商后的 `LlrpProtocolVersion`；
- `ReaderCapabilities`；
- 已激活的 Reader Extension。

Impinj Profile 只放在 `LlrpSdk.Extensions.Impinj` 中。核心 SDK 只负责通用默认值、Profile 注册、匹配和合并，不硬编码 Impinj 型号判断。

### 3. 确定 Profile 匹配优先级

Profile 按以下优先级选择：

```text
精确型号 + 固件范围
    > 精确型号
    > 厂商系列
    > 厂商通用
    > LLRP 通用安全默认值
```

同一优先级匹配到多个 Profile 时必须报告冲突，不允许依赖注册顺序静默选择。

未知型号使用通用安全默认值，并记录诊断信息。对于发射功率、信道、接收灵敏度等具有现场风险的值，不根据猜测填充。

### 4. 明确配置合并顺序

默认配置的来源按照以下顺序合并：

```text
LLRP 通用安全默认值
    ↓
厂商默认值
    ↓
型号默认值
    ↓
固件/区域修正
    ↓
用户显式覆盖
```

最终生成的 `ReaderConfiguration` 应是可提交的完整配置。为了避免用户用部分对象覆盖设备配置，后续增加独立的 `ReaderConfigurationPatch` 或等价的变更模型：

```text
默认配置/实际配置
    + ReaderConfigurationPatch
    → 完整 ReaderConfiguration
    → 显式 Apply
```

### 5. 不自动写入设备

`GetDefaultSettingsAsync()` 不读取或修改 Reader 资源，也不自动 Apply。应用必须明确调用 `ApplySettingsAsync()` 才能修改设备。

这条约束适用于：

- 连接初始化；
- Impinj 等厂商扩展激活；
- SDK 盘点编译器；
- CLI 和 Live Shell。

### 6. 默认 Profile 不进入协议生成代码

协议定义和生成器只描述线上的消息、参数、字段和编解码规则。默认配置属于 SDK 策略，不放入 `*.g.cs`，也不修改协议生成模型。

第一阶段使用强类型 C# Profile 实现，待厂商型号数量增加后，再评估用 YAML 维护 Profile 数据；无论采用何种数据格式，最终都必须经过类型校验和安全范围校验。

## 后果

### 正面后果

- 业务层可以获得稳定的初始化配置基线；
- 厂商和型号差异被隔离在扩展包内；
- 未来增加 Zebra、Alien、ThingMagic 等厂商不需要修改核心协议适配器；
- 默认配置、设备实际配置、用户覆盖的语义清晰；
- 标签访问 API 和 Inventory Compiler 可以复用统一的配置基线。

### 代价与约束

- 需要维护 Profile 匹配、优先级和冲突诊断；
- 型号默认值必须基于厂商资料或实测，不能凭经验猜测；
- `ReaderConfiguration` 当前是完整配置模型，增加 Patch 模型后需要调整 CLI 和 Apply 流程；
- 设备未连接且未提供身份时，不能选择型号级 Profile；
- 默认值不是设备快照，文档和 API 命名必须避免混淆。

## 实施顺序

1. 已定义 `ReaderConfigurationProfileContext`、`IReaderConfigurationDefaultsProvider`、`ReaderConfigurationProfile` 和冲突诊断模型；
2. 已增加核心 LLRP 通用安全基线：不推测天线功率/信道/GPO，Keepalive 为 `None`；
3. 已增加 `LlrpReader.GetDefaultSettingsAsync()`；该 API 仅依赖已初始化的身份、能力和激活扩展，不发送 LLRP 请求；
   `ReaderSettingsDefaults` 可同时返回选中的 Provider/Profile 来源；
4. 在 Impinj 扩展包中增加厂商/型号 Profile；仅在厂商资料或实测能证明具体安全值时实施，当前不得猜测 R420/R700 的功率、信道或私有设置；
5. 已增加 `ReaderConfigurationPatch`、`ResolveConfigurationPatchAsync()` 与 `ApplyConfigurationPatchAsync()`；前者仅查询并合并，后者才明确写入；
6. 增加 Profile 冲突、未知型号和安全范围测试；
7. 最后接入 CLI、Live Shell 和 Inventory Compiler。

## 未决问题

- 型号 Profile 的数据源采用 C# 代码还是增量 YAML；
- 区域信息是否需要加入 `ReaderConfigurationProfileContext`；
- `GetDefaultConfiguration()` 是否只允许连接后调用，还是同时提供显式 ProfileKey 的离线 API；
- 默认配置来源是否需要作为公开诊断信息返回。
