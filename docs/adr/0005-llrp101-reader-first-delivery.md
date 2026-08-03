# ADR 0005：以 LLRP 1.0.1 Reader 能力为当前交付主线

- 状态：Accepted（实施中）
- 日期：2026-07-27

> 实施说明：本文保留决策形成时的历史模型名称。当前托管配置 API 是
> `QuerySettingsAsync()` / `ApplySettingsAsync()`，CLI 使用 `settings` 草稿
> 与 `inventory start|stop|status`，不再提供 `config` 命令组。

## 背景

项目同时拥有协议生成、多个 LLRP 版本、厂商扩展、SDK、CLI 和 Virtual Reader。若以 CLI 命令数量、生成类型数量或 Virtual Reader 行为数量衡量完成度，容易掩盖真实产品边界：应用实际依赖的是一个可操作真实读写器的 `LlrpReader`。

当前实机基线是 LLRP 1.0.1 的 Impinj R420/R700。LLRP 1.1、LLRP 2.0 和 Virtual Reader 仍有独立价值，但不应稀释当前 SDK 主线。

## 决定

### 1. 当前 SDK 完成度只按 LLRP 1.0.1 与 Impinj 1.0.1 扩展验收

当前阶段的完成度由以下两个矩阵决定：

1. LLRP 1.0.1 标准能力必须可通过 `LlrpReader` 操作真实设备；
2. Impinj 的 LLRP 1.0.1 扩展必须通过 `UseImpinj()` 和 `LlrpReader` 的扩展管道提供大部分有协议定义且具备厂商资料或实机依据的能力。

未实测、未有厂商资料支撑、或设备型号/固件未确认的 Impinj 功能保持默认拒绝，不以“理论支持”计入完成度。

### 2. 标准能力同时提供两种 Reader 层入口

每项适合托管的标准能力都必须先定义版本无关的托管 Reader API；需要完整资源控制的能力同时经由专家资源 API 暴露：

```text
应用 / CLI
    └─ LlrpReader
       ├─ 托管 Reader API：Connect、配置、Start/Stop/Inventory、Tag Access、报告
       └─ 专家资源 / 原始协议 API：RoSpecs、AccessSpecs、Protocol
```

- 托管 Reader API 隐藏版本化 Message/Parameter，负责默认值、资源所有权、清理、报告投影和恢复边界；
- 专家资源 API 保留标准 ROSpec/AccessSpec 的显式控制，不能为了托管 API 的易用性而消失；
- Raw `Protocol` 是诊断与尚未封装标准功能的逃生口，操作后必须使托管状态失效并要求同步；
- 新的托管能力必须建立在 Reader 服务之上，不能由 CLI 自己重做协议生命周期。

### 3. Impinj 不引入 `ImpinjReader` 继承树

`LlrpReader` 始终是跨厂商入口。Impinj 包通过 Protocol Module、Reader Extension 与 Contributors 扩展：

- 标准能力仍从同一个 `LlrpReader` 使用；
- 厂商设置、盘点编译选项和 TagReport 投影进入 `Extensions` 或版本无关请求模型；
- 型号/固件能力表决定哪些 Custom Parameter 可以被主动发送；
- 需要专属业务 Profile 时，作为显式 Provider/Extension 添加，而不是派生 Reader。

### 4. CLI 是 Reader SDK 调用者，不是第二套业务实现

在线 CLI 命令必须映射到公开的 Reader API。CLI 仅负责：

- 命令/文件参数解析、默认值和确认提示；
- 将输入映射为 `InventorySettings`、`ReaderConfigurationPatch`、Tag Access 请求或高级资源请求；
- 渲染结果、帧观察和交互会话体验。

离线 `encode`、`decode`、`inspect`、`validate` 可直接使用 Protocol 层；它们不属于 Reader 业务 API。

### 5. CLI 生命周期是独立工作包

Live Shell 必须清晰管理一个 Reader 会话的连接、托管资源所有权、后台报告泵、监控和清理。它不应假定断线、Raw 操作或外部设备变化后 Reader 状态仍然有效。

实施顺序与当前完成状态以 [`../roadmap.md`](../roadmap.md) 和 [`../status.md`](../status.md) 为准。

### 6. 设备配置、盘点意图与会话草稿必须分层

以下对象不能因为都含有“settings/configuration”而合并：

| 层 | SDK 对象 | 语义与生命周期 | CLI 归属 |
|---|---|---|---|
| 设备配置 | `ReaderConfiguration` / `ReaderConfigurationPatch` | `GET/SET_READER_CONFIG` 的设备状态与显式改动；可能持久在设备 | `config` 命令组 |
| 盘点意图 | 当前 `InventorySettings`（后续规范名为 `InventorySettings`） | 编译为 ROSpec 及相关托管资源；影响一次盘点启动 | `inventory settings` 命令组 |
| 运行中快照 | 当前 `CurrentInventorySettings`（后续规范名为 `CurrentInventorySettings`） | SDK 正在托管的盘点参数；停止后失效 | `inventory status` 只读显示 |
| 会话草稿 | `LiveSessionContext.DesiredInventorySettings` | 用户准备给下一次盘点使用的本地草稿；断开后丢弃 | Live Shell 内部状态 |

`CurrentInventorySettings` 不能承担 CLI 会话草稿：它只反映当前运行的 SDK 托管盘点。CLI 也不得自行编译 ROSpec 或管理 AttachedData 所需的 AccessSpec；它只将草稿快照传给 `LlrpReader.StartAsync(...)`。

`InventorySettings.Extensions` 不构成通用 JSON 持久化协议。标准盘点 Profile 可以序列化；厂商扩展必须由其 Extension 注册强类型 JSON 映射、版本和类型标识。未知扩展不得反序列化成不安全的 `object` 后再发送给设备。

## 后果

- 1.0.1 Reader API 的缺口优先于 1.1/2.0、Virtual Reader 和新的 CLI 花样；
- CLI 新命令没有对应 SDK API 时，先补 SDK API，再加 CLI 薄封装；
- SDK 配置 API 统一使用 `QuerySettingsAsync` / `ApplySettingsAsync`；盘点模型使用 `InventorySettings` / `CurrentInventorySettings`；
- Impinj 写入能力必须有精确能力证据与恢复/回滚策略，不能由读取投影自动推断；
- 每项完成项需要标准协议测试与 R420/R700 实机证据之一；厂商扩展还需要版本/型号证据；
- LLRP 2.0 及所有后续 Virtual Reader 扩展维持在最终阶段。
