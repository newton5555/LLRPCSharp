# Reader-first 交付计划：LLRP 1.0.1 与 Impinj 扩展

- 状态：Approved，实施中
- 决策依据：[ADR 0005](../adr/0005-llrp101-reader-first-delivery.md)
- 真实状态来源：[status.md](../status.md)

## 目标与非目标

目标是在真实 LLRP 1.0.1 设备上，使应用可以只通过 `LlrpReader` 完成标准读写器操作，并在 `UseImpinj()` 后获得经过证据门控的 Impinj 扩展能力。每项能力同时明确高层封装与高级资源接口的边界；CLI 仅调用这些接口。

本轮不以 LLRP 1.1、LLRP 2.0 或 Virtual Reader 的功能扩充作为完成条件。它们保留到最终阶段。

## 完成矩阵

| 域 | 高层 `LlrpReader` 接口 | 高级接口 | 当前验收原则 |
|---|---|---|---|
| 连接与初始化 | `ConnectAsync`、协商、身份、能力、断开 | `Protocol` 诊断 | R420/R700 连通、协商与错误可诊断 |
| 标准配置 | `QueryConfigurationAsync` / `ApplyConfigurationAsync`、默认配置/Profile、Patch 解析与显式 Apply | 原始配置报文 | 查询和写入边界分离；CLI 不重做配置语义 |
| ROSpec/盘点 | `StartAsync`、`StopAsync`、`InventoryAsync`、报告流 | `RoSpecs` Add/Get/Enable/Start/Stop/Delete | 默认资源可用、显式资源可控、所有权清楚 |
| AccessSpec/标签操作 | `ReadTagMemoryAsync`、`WriteTagMemoryAsync` | `AccessSpecs` 生命周期 | 最小读写、选择、密码、清理和失败结果正确 |
| TagReport | 版本无关 `TagReport` 流/事件 | 原始 Message/帧 | 标准字段与未知字段不丢失 |
| Impinj 配置 | 标准配置加 Extensions/Profile | Impinj 原始 Custom 参数 | 只实现有定义和型号/固件依据的字段 |
| Impinj 盘点/报告 | `ReaderSettings.Extensions`、`TagReport.Extensions` | Impinj Custom 参数与原始帧 | 默认拒绝；每个主动发送参数需实测证据 |

## 阶段 A：标准 LLRP 1.0.1 Reader API 补全

1. 为每个标准资源域盘点“高层入口 / 高级服务 / Raw 退路”三层是否齐备；缺高层入口时先补 SDK，不从 CLI 绕过。
2. 完成配置 API 的一致使用：`ReaderConfigurationPatch` 是部分变更模型，`QueryConfigurationAsync` 是设备状态，`GetDefaultConfigurationResult()` 是 SDK 基线，三者不得混用。
3. 清晰定义 SDK 托管 ROSpec、CLI 默认 ROSpec 和外部 ROSpec 的资源所有权，避免删除不属于调用者的资源。
4. 补足 Tag Access 的标准读写闭环、错误状态、超时、密码和清理语义；写入只在实机验收与明确确认后开放为 CLI 实际操作。
5. 建立每项 API 对 R420/R700 的非破坏性验收记录；破坏性写入使用可恢复的专用标签与明确步骤。

## 阶段 B：Impinj 1.0.1 扩展补全

1. 将 `definitions/imports/xml` 的 Impinj 定义映射为功能清单：配置、盘点、报告、标签操作、诊断五类。
2. 为每项建立“定义存在 / 官方资料 / ItemTest 抓包 / 实机 SDK 验证 / 已开放发送”的证据状态；只有最后一项才允许默认发送。
3. 扩展型号/固件能力表，先覆盖 R420/R700 的实际固件，再考虑其他机型。
4. 将确认的设置读取投影到 `ReaderConfiguration.Extensions`；设置写入通过显式 Patch/Profile 与恢复策略实现，不能根据读取结构反推。
5. 将确认的报告选择器编译到正确标准位置，并把响应投影到 `TagReport.Extensions`；未知/未批准字段保留原始数据。

## 阶段 C：CLI 与 SDK 对齐

1. 每个在线 CLI 命令列出其唯一的 Reader API 映射；没有映射的业务命令先停止扩展。
2. 将 `config apply` 从 CLI 私有完整对象合并逐步收敛到 `ReaderConfigurationPatch` → `ResolveConfigurationPatchAsync` / `ApplyConfigurationPatchAsync`。
3. 将 `ReaderSettings` 规范为盘点意图（目标名称 `InventorySettings`），并把 `CurrentSettings` 规范为运行中快照（目标名称 `CurrentInventorySettings`）。
4. Live Shell 在 `LiveSessionContext` 保存 `DesiredInventorySettings` 草稿；`inventory settings show|set|load|save|reset` 修改草稿，`inventory start [--antennas]` 只消费草稿快照并允许天线临时覆盖。
5. 标准盘点 Profile 可 JSON 读写；厂商 Extension 必须注册强类型、版本化的 Profile 映射，不能直接持久化 `Extensions<object>`。
6. 保持 `rospec`/`accessspec` 为高级接口的薄包装；默认 ROSpec 的创建规则与 SDK 盘点意图默认值保持一致。
7. 仅离线协议命令允许直接进入 Protocol 层；`raw` 操作后必须提示并强制下一次托管操作前同步。

## 阶段 D：CLI 生命周期管理（先规划，后实现）

### D1. 状态模型

`LiveSessionContext` 必须区分：连接状态、SDK 托管盘点状态、CLI 所有的报告泵、被动监控、Raw 后同步需求、待处理的断线/清理任务，以及仅用于下一次盘点的 `DesiredInventorySettings`。不得仅凭一个 `IsConnected` 或 `reader.CurrentSettings` 推断其余状态。

### D2. 所有权与清理

- 仅清理当前 CLI 命令创建的 ROSpec、AccessSpec、报告泵与监控任务；
- `disconnect`、连接失败和 Ctrl+C 走同一幂等清理路径；
- 临时 `tag read` 只停止它自己启动的托管盘点，绝不停止用户已在运行的盘点；
- `inventory start` 对草稿执行不可变快照；运行中 SDK 设置与之后继续编辑的草稿互不影响；
- Raw 操作不尝试猜测设备改变了什么，标为未同步并要求 `sync`。

### D3. 重连与错误

- 连接丢失时，取消 CLI 本地后台任务并清除本地“正在盘点”的显示；
- 不自动恢复 ROSpec、AccessSpec 或标签操作；恢复必须由用户显式发起；
- 命令执行前根据所需状态给出可行动提示，而不是让底层异常泄漏为终端错误。

### D4. 命令形态

- 外层一次性 CLI：创建 Reader、执行一个 SDK 操作、可靠清理；
- Live Shell：复用一个 Reader 和 Frame Observer，所有后台任务归 `LiveSessionContext`；
- 两种入口共享请求解析、业务 Handler 与渲染模型，不能各自实现设备生命周期。

### D5. 实施和验收

1. 将当前 `LiveCommand` 剩余的配置、ROSpec、AccessSpec 和 Raw 逻辑拆为职责明确的 Handler；
2. 为连接、盘点、临时标签读取、监控、Raw 后同步、断线和 Ctrl+C 建立状态转换测试；
3. 在 R420/R700 上验证顺序命令：连接 → 配置读取 → 默认 ROSpec → 盘点 → 标签读取 → 停止 → 断开；
4. 最后再用 Virtual Reader 覆盖故障注入和 LLRP 2.0 场景。

## 执行顺序

1. 阶段 A 的 API 矩阵和 R420/R700 实机缺口；
2. 阶段 B 的 Impinj 证据清单与已验证功能补全；
3. 阶段 C 的 CLI 到 SDK 映射收敛；
4. 阶段 D 的 CLI 生命周期实现与验收；
5. 最终阶段的 LLRP 2.0 和 Virtual Reader。
