# 当前状态

> 基准日期：2026-07-24  
> 目的：作为仓库当前真实状态的事实源。README 面向使用者，长期规划面向设计；本文件回答“现在已经有什么、还缺什么、什么会阻塞开发”。

## 总结

当前源码已经超过旧 README 的 M3/M5 描述：M3/M4/M6/M7/M8 都已有部分实现。下一步扩功能前，应先恢复仓库可构建状态，并把现有 1.0.1/1.1 SDK 基线固定下来。

当前最高优先级：修复 `src/LlrpSdk.Extensions.Impinj` 中重复生成类型导致的构建失败。

## 支持矩阵

| 能力 | 当前状态 | 说明 |
|---|---|---|
| LLRP 1.0.1 | 可用基线 | 标准模型、Codec、Registry、Reader Adapter 和 CLI 路径已存在。 |
| LLRP 1.1 | 可用基线 | `GET_SUPPORTED_VERSION` / `SET_PROTOCOL_VERSION` 协商、`Force11` 和回退策略已接入。 |
| LLRP 2.0 | 定义已入库，Adapter 未完成 | `definitions/llrp-2.0-delta.yaml` 存在，但没有 `Llrp20ProtocolAdapter`、协商和互操作闭环。 |
| Impinj 扩展 | 架构存在，构建阻塞 | `UseImpinj()` 和扩展注册入口存在；当前重复生成类型阻塞构建。 |
| CLI | 可用诊断入口 | 支持在线连接、监控、Live Shell、离线 inspect/decode/encode。 |
| Virtual Reader | 最小 1.0.1 Server | 支持能力查询与 ROSpec 生命周期；TagReport、AccessSpec、故障注入仍待补。 |

## 已实现

### 盘点与资源服务

- `LlrpReader.StartAsync(ReaderSettings)`、`StopAsync()`、`InventoryAsync(ReaderSettings?)`。
- `ReadTagReportsAsync()` 与 `TagsReported`，并共用同一份已翻译的 `TagReport`。
- `ReaderSettings` 作为版本无关的盘点意图模型存在。
- `IRoSpecService` 提供 Add/Delete/Enable/Disable/Start/Stop/GetAll。
- `IAccessSpecService` 提供 Add/Delete/Enable/Disable/GetAll。
- Raw Protocol 操作后会使托管状态失效，并通过 `SynchronizeStateAsync()` 恢复可继续 Managed 操作的状态。

### 版本协商与 Adapter

- `Llrp101ProtocolAdapter` 与 `Llrp11ProtocolAdapter`。
- `LlrpProtocolVersionPolicy.Auto`、`Force101`、`Force11`。
- `ConnectAsync()` 内部执行 1.1 协商，并可在旧设备返回不支持时回退到 1.0.1。
- CLI 支持 `--llrp auto|1.0.1|1.1`。

### 扩展生命周期

- `ILlrpProtocolModule`、`UseProtocolModule(...)`。
- `IReaderExtension`、`UseReaderExtension(...)`、`reader.Extensions`。
- `UseImpinj()` 扩展入口。
- Reader Extension 基于 Manufacturer/Model/Firmware/ProtocolVersion 匹配，并检查互斥组冲突。

### 可靠性与诊断

- `LlrpAutomaticReconnectOptions` 和 `WithAutomaticReconnect(...)`。
- 意外断线后的有限自动重连。
- `LlrpFrameJournal` 诊断基线。
- `ILlrpFrameObserver` 可从底层 Transport/Session 注入完整 TX/RX 帧观测。

## 未完成

### Reader 配置查询与应用

规划中出现的 `QuerySettingsAsync` 与 `ApplySettingsAsync` 当前不是公开可用 API。`ReaderSettings` 目前主要用于编译托管盘点 ROSpec，不等同于完整 Reader Config 查询/应用模型。

### 标签访问 API

规划中的 `ExecuteTagAccessAsync`、`ReadTagMemoryAsync`、`WriteTagMemoryAsync` 当前不存在。现有 AccessSpec 服务是进阶资源生命周期操作，不是面向普通业务的标签访问封装。

### LLRP 2.0

仓库已有 2.0 Delta，但当前没有 `Llrp20ProtocolAdapter`，Reader 初始化 Adapter 列表也只有 1.0.1 与 1.1。

### 扩展 Contributor

文档描述过 Settings Contributor 与 TagReport Contributor 管道，但源码当前只具备协议模块注册和 Reader Extension 激活，还没有标准 Contributor 接口。

### Virtual Reader 场景覆盖

当前 Virtual Reader 支持能力查询和 ROSpec 生命周期，但不支持报告生成、AccessSpec、故障注入和脚本化场景。

## 当前构建阻塞

`dotnet build LLRPCSharp.slnx --no-restore` 当前失败在 `src/LlrpSdk.Extensions.Impinj`。错误集中表现为重复类型定义，例如：

- `ImpinjEnableEnhancedIntegraCodec`
- `ImpinjEnhancedIntegraReportCodec`
- `IMPINJ_ENABLE_EXTENSIONSCodec`
- `IMPINJ_ENABLE_EXTENSIONS_RESPONSECodec`
- `IMPINJ_SAVE_SETTINGSCodec`
- `IMPINJ_SAVE_SETTINGS_RESPONSECodec`
- `ImpinjEnhancedIntegraMode`
- `ImpinjEnhancedIntegraResultType`

初步判断：Impinj 生成输出中同名定义被写入了多组编号文件，可能来自原始 XML 重复定义、生成器去重策略不足，或旧生成文件未清理。

## 同步要求

- 改变公开能力时，同步本文件。
- 新增长期设计时，放入规划或 architecture 文档，不要把未来 API 写成本文件的已实现事实。
- 修复构建阻塞后，更新本文件的构建状态。
