# LlrpCli 优化规划

> 状态：已完成（归档）。WP1-WP8 以及 WP6 的 D1/D3 消重均已实施；本文件保留
> 接口盘点、差距分析和决策历史。当前命令行为以 [CLI 用户指南](../guides/cli-user-guide.md)、
> [当前状态](../status.md) 和源码为准，不再把本文的旧差距表当作待办清单。

## 1. 现状

CLI 有两个入口:

- **交互式 Live Shell**(`llrp`,默认命令):`LiveCommand` + 22 个 route,自带 Tab 补全 / ghost 提示 / 历史(`TerminalLineEditor`)。
- **一次性命令**:`inspect` / `decode` / `validate` / `encode` / `inventory`(Spectre.Cli `CommandApp`)。

交互式命令面按职责:连接(`connect/disconnect/status/caps`)、托管盘点(`inventory start|stop|status`、`monitor`)、配置(`settings` 7 子命令 + 475 行交互编辑器)、标签访问(`tag` 6 操作)、专家资源(`rospec/accessspec/resources/sync/raw`)、离线工具(`inspect/decode/validate/encode/frames`)。

## 2. 对齐差距(与当前 SDK)

门面 API 改动(删 `TranslateTagReports`、`Registry` 只读视图、builder 合并、`Options` 成员 internal 化、`GetTagReportsAsync` 纯拉取)对 CLI **零影响**——CLI 未引用任何被删/收窄的 API。

真正的缺口是**版本/厂商覆盖还停在 1.0.1 + Impinj**:

| # | 缺口 | 位置 |
|---|---|---|
| A1 | `Helpers.CreateRegistry()` 只注册 V1_0_1 + Impinj,缺 1.1 / 2.0 / Seuic / Zebra | `Helpers.cs:77-83` |
| A2 | `--llrp` 无 `Force20`,且 `"2"` 错映射到 `Force11` | `ProtocolVersionPolicyParser.cs:14` |
| A3 | `--vendor` 无 zebra | `VendorExtensionModeParser.cs`、`CliConnectionOptions.cs:39` |
| A4 | banner 硬编码 `v1.0.1` | `LiveCommand.cs:893` |
| A5 | `encode` 硬编码 `Version101`,消息目录 V1_0_1 类型,无 `--llrp` | `EncodeCommand.cs:58`、`LiveProtocolDiagnostics.cs:100` |
| A6 | `LlrpFrameAnalyzer.ExtractStatus` 1.0.1 类型匹配,1.1/2.0 状态提取不出 | `LlrpFrameAnalyzer.cs:47` |
| A7 | 覆盖不一致:live 会话经 builder 拿到 Impinj+Seuic,离线命令只有 1.0.1+Impinj | `CliConnectionOptions.cs:47-67` vs `Helpers.CreateRegistry()` |

## 3. 操作痛点

| # | 痛点 | 证据 |
|---|---|---|
| P1 | 托管盘点必须两段式(settings 部署 → inventory start);one-shot `inventory` 却一段式,两套实现 | `LiveInventoryHandler.cs:58-70` vs `InventoryCommand.cs:137-142` |
| P2 | 状态门控长文案重复 6+ 处 | `LiveInventoryHandler`/`LiveSettingsHandler`/`LiveCommand` 多处 |
| P3 | rospec/accessspec 双重仪式(先 sync 再 resources manual enter) | `LiveCommand.cs:642-643,802-810` |
| P4 | monitor 两条重叠入口,参数语法不一致 | `LiveMonitorHandler.cs:91-124` vs `LiveInventoryHandler.cs:159-214` |
| P5 | `inventory start` 与 one-shot `inventory` 双实现 | `LiveInventoryHandler` vs `InventoryCommand` |
| P6 | tag 命令 `--op read:bank:word:count` 冒号字符串,无补全校验 | `LiveTagAccessHandler.cs:142-228` |
| P7 | 连接后不引导,`inventory start` 无部署时只报错不提示 | `LiveInventoryHandler.cs:58-61` |

## 4. 重复逻辑

| # | 重复 | 位置 |
|---|---|---|
| D1 | 两套反射树遍历,深度/截断行为不同,同一报文渲染不同树 | `FrameRenderer.cs:171-267` vs `LlrpFrameAnalyzer.cs:97-153` |
| D2 | 两个 hex 解析器 | `Helpers.cs:12-43` vs `TagAccessCliRequest.cs:132` |
| D3 | standalone 与 live-shell 四对孪生命令 | `LiveProtocolDiagnostics.cs` vs `Inspect/Decode/Validate/EncodeCommand.cs` |
| D4 | encode 消息目录漂移(两处 9 消息,`--requested-data` 支持不一致) | `LiveProtocolDiagnostics.cs:83-98` vs `EncodeCommand.cs:70-82` |

## 5. settings 专项优化(重点)

`settings` 是命令面最重的一块:7 子命令 + 475 行交互编辑器(`SettingsEditor.cs`)。

### 5.1 现有子命令与问题

| 子命令 | 问题 |
|---|---|
| `load <file>` | ≈ `validate` + 一行提示,冗余 |
| `defaults --yes` | ≈ `apply` 换源,apply 逻辑重复两份 |
| `edit` | 13 area 逐区表单,终端操作累;`EditVendorExtensions` 硬编码 `is ImpinjReaderExtension`,Seuic/Zebra 进不了编辑 |
| `apply <file> --yes` | 与 handler 层各校验一次(双校验) |
| `show` | 状态门控长文案重复 |
| flags 不一致 | `show --json|--raw` / `defaults --json|--yes` / `apply --yes` 三种组合 |

### 5.2 目标形态(before → after)

| 现在 | 目标 |
|---|---|
| `settings show [--json|--raw]` | 保留 |
| `settings defaults [--json|--yes]` | 并入 `settings apply --defaults --yes` |
| `settings apply <file> --yes` | 保留,语义单一(下发);吸收 `defaults` |
| `settings validate <file>` | 保留(显式校验,零副作用) |
| `settings load <file>` | 删除(由 `validate` 承担,load≈载入+校验) |
| `settings save <file>` | 保留 |
| `settings edit` | 保留并增强(见 5.3) |

### 5.3 edit 保留并增强(交互编辑器是核心能力,不减重)

`settings edit` 的交互表单是高频使用的核心能力,不删除。减重从 5.2 的子命令合并(load/defaults 折叠)与双校验消除入手,编辑器本身走增强路线:

| # | 增强 | 说明 |
|---|---|---|
| E1 | 厂商扩展泛化 | `EditVendorExtensions` 现硬编码 `is ImpinjReaderExtension`(`SettingsEditor.cs:420`);改为按 `reader.Extensions` 动态发现活跃扩展,补 Seuic/Zebra 编辑片段 |
| E2 | 能力交叉引用 | 编辑 ModeIndex/Tari/TxPowerIndex/RxSensitivityIndex 时,就地显示 `reader.Capabilities` 对应表(如 Tx index 旁显示 dBm、Mode index 旁显示 DR/M/Tari 范围),免去来回切 `caps` |
| E3 | 变更 diff 预览 | `Review` 不只显示全量,高亮本会话改动字段(改前 → 改后) |
| E4 | 内联校验反馈 | 字段录入后立即局部校验(范围/格式),`Validate` 保留为全量编译校验 |
| E5 | 减少确认疲劳 | `EditReaderConfiguration` 的 10 个事件开关改多选/表格一次勾;天线逐根询问默认"所有同值",需要时才逐根覆盖 |
| E6 | 导航增强 | 主菜单编号直达、返回上一步、未保存变更提示 |
| E7 | JSON 融合 | 编辑过程支持 import/export JSON,打通表单与文件工作流 |

**实施优先级(已定)**:E2 → E1 → E4 → E3 → E5 → E6 → E7。

理由:E2(能力交叉引用)最直接解决"操作难受"——编辑时不用来回切 `caps`;E1(厂商泛化)是纯对齐缺口(硬编码 Impinj)且 E2 在厂商字段上也用得上;E4/E3 提升录入可信度;E5/E6 消疲劳与导航;E7(JSON 融合)最后做,打通文件流。

### 5.4 其余收口

- 消除双校验:`ManagedSettingsWorkflow.ApplyAsync` 拆出"已校验后直接下发"变体,handler 只校验一次。
- 状态门控文案收进单一 helper(与全局 P2 一起)。
- flags 统一 `--json` 输出与 `--yes` 确认语义。

## 6. 其它优化方向(分层)

**第一梯队(对齐 + 最高价值痛点)**
- D1:`inventory start` 支持 `--defaults` / `--settings <file>`,一段式部署+启动,合并 P5 双实现。
- D4:A1-A7 全对齐(`--llrp 2.0`、`--vendor zebra`、registry 全版本/厂商化、修 `"2"→Force11`、A5/A6 硬编码)。

**第二梯队(体验)**
- D2:状态门控文案收敛 + 下一步建议,可选 `--force`(保守)。
- D3:`rospec/accessspec/resources` 收成单一 manual 流程,消除双重仪式。

**第三梯队(消重 + 可选)**
- D5:monitor 入口合一(P4)。
- D6:tag 命令结构化(P6)。
- D7:消 D1-D4 重复(树遍历、hex 解析、孪生命令、encode 目录)。

## 7. 建议落地顺序

1. **A1-A7 对齐 + 2.0/zebra**(地基,顺带修 `"2"→Force11` 真 bug)。
2. **settings 专项(§5.2/§5.4)+ 一段式盘点(P1/D1)**(你最痛的流程与命令面)。
3. 其余按体验优先级跟进。

## 8. 已定决策(可推翻)

- `settings validate` 独立保留(只校验,不写设备);`settings apply` 语义单一(写设备,`--yes` 确认)。
- 一段式盘点用 `inventory start --defaults` 与 `--settings <file>` 两个旗标。
- 不引入 `--force`(保留 `--yes` + 显式接管语义)。
- `manual on/off/status` 命名确认;托管调用在 manual 模式时**自动 off**:设备有非托管 ROSpec/AccessSpec → 提示确认(默认否,显示将删除的数量),无资源 → 静默退出;显式 `manual off` 不额外确认。
- 非法 vendor×version 组合:**自动降级为标准 LLRP(丢弃厂商)+ 显式警告**,不拒绝连接(§11)。
- 旧命令(`resources`/`settings defaults`/`settings load`)**直接移除,不保留别名**(§15)。

## 9. 残留待拍板

无 —— 全部已定(见 §8 与 §11,可推翻)。

## 10. 实现依赖与分工(离线工具)

离线工具(`inspect/decode/validate/encode`)不重写报文解析:字节级解析由协议层生成的 codec 完成,CLI 只做"registry 装配 + 反射呈现"。

| 环节 | 谁做 | 现状 |
|---|---|---|
| wire → 强类型消息 | `LlrpNet.Protocol` codec(`registry.DecodeMessage`) | 已有,复用 |
| codec registry 装配 | CLI `Helpers.CreateRegistry()` | 只 1.0.1+Impinj(A1) |
| 反射树遍历 / 语义提取 / 渲染 | CLI(`FrameRenderer` / `LlrpFrameAnalyzer`) | 已有,重复两份(D1) |

离线 registry 目标模块清单(实施照此注册):
- 标准:`V1_0_1ProtocolModule`、`Llrp11StandardModule`、`Llrp20StandardModule`
- 厂商:`ImpinjProtocolModule`、`ZebraProtocolModule`
- Seuic **无 wire 包**(`SeuicReaderExtension` 仅标准 LLRP defaults 扩展,无 codec 可注册)

## 11. 版本 × 厂商组合矩阵(设计空白,需定义)

现状核实:三个厂商的 wire 包与 SDK 扩展**全部只支持 1.0.1**(Impinj/Zebra/Seuic 的 `Matches` 均锁定 `Version101`;Impinj/Zebra wire 包只有 `Registry/V1_0_1`)。**2.0 目前只有标准协议**。

已定规则:
1. 显式 `--vendor impinj|seuic|zebra` 且显式 `--llrp 1.1|2.0` → **自动降级为标准 LLRP(丢弃厂商)+ 显式警告**("该厂商扩展仅支持 1.0.1,已按标准 LLRP {version} 连接"),不拒绝连接;
2. `auto` 组合下,协商到 1.1/2.0 时厂商扩展不激活(标准模式),不报错;
3. 显式厂商 + `--llrp auto`,协商后扩展未激活 → 连接完成输出一条说明(不阻塞),`status` 的 Active Extensions 反映真相。

## 12. 代码结构规划(消重落点)

- **共享命令核**:standalone 与 live-shell 的 inspect/decode/validate/encode 合并为一个实现,两个入口只做参数适配(D3)。
- **盘点共用 workflow**:一段式/两段式/one-shot 共享"部署→启动→拉报表→清理"核心(P5)。
- **状态门控 helper**:单一入口,返回(错误文案 + 建议下一步命令),全部 handler 调用(P2)。
- **消重**:树遍历合并到一个 walker(D1);hex 解析统一 `Helpers.ParseHex`(D2);encode 消息目录单源(D4)。

## 13. 命令细节残留设计项

- `manual on`:需要时自动 `sync` + `EnterManualResourceModeAsync`(SDK 要求托管配置已清,否则报错);`manual off` 删除全部资源回 Idle;`manual status` 查询。托管调用(`inventory start --defaults/--settings`、`settings apply --yes` 带 Inventory)在 manual 模式时自动 off:设备有非托管资源 → 提示"将删除 N 个 ROSpec / M 个 AccessSpec"确认(默认否),无资源 → 静默退出。
- `inventory start`:同时给 `--defaults` 与 `--settings` → 报错;两者都不给 = 两段式第二段(现状)。
- `tag sequence`:结构化旗标 `--read bank:word:count` / `--write bank:word:data` / `--erase` / `--lock target:privilege` / `--kill pwd`,可重复、按出现顺序执行。
- `settings apply --json`:统一 JSON 输出形态(成功/失败结构,与 one-shot `inventory` 对齐)。
- banner 版本号取程序集版本,不再硬编码(A4)。
- **过时提示清理**:`caps` 的表头引用不存在的 `config apply --tx-power/--rx-sens`(`LiveCommand.cs:391,405`),改为指向 settings 流程或删除。

## 14. 测试与验收

- **单元/交互测试新增点**:版本解析器(`"2"`→2.0、别名)、vendor×version 组合校验、一段式盘点、apply/validate 语义、`manual on/off`、`tag sequence` 结构化、离线工具 `--llrp`。
- **实机验收**(按 AGENTS.md 记入 acceptance 表):
  - 2.0 协商:SDK 层已实现但**未实机验证**;CLI 接 2.0 后需在支持设备上验证;
  - Zebra:192.168.40.88(FX9600),CLI `--vendor zebra` 走它;
  - Impinj:192.168.40.87(R420);
  - Seuic:无设备,extension 行为仅单测。
- 现有 `LlrpCli.Tests` 27 个测试为基线,不得回归。

## 15. 文档 / 发布 / 迁移

- `docs/guides/cli-user-guide.md` 与 CommandCatalog 补全候选随命令改动同步。
- `docs/status.md` / `docs/roadmap.md` 记录 CLI 实施进展。
- **迁移**:`resources` / `settings defaults` / `settings load` 等旧命令**直接移除,不保留别名**(已定);one-shot `inventory` 参数保持向后兼容(外层入口,不在交互 shell 内)。

## 16. 可选新功能(不在主线,可后置)

- `gpo set <port> <state>`:SDK 已有 `SetGpoAsync`,CLI 未暴露便捷入口(复合读改写,见门面文档 §4.4)。
- `antenna set <id> --tx-power/--rx-sens` 快捷入口(对应 SET_ANTENNA_PROPERTIES)。
- 丢报/缓冲事件(`ReportBufferOverflow`/`TagReportsDropped`)的 monitor 展示。


## 17. 实施任务包(按序执行,可直接照做)

| 包 | 范围 | 涉及文件 | 验收标准 |
|---|---|---|---|
| WP1 对齐:版本/厂商覆盖(A1-A7) | 修 `"2"→Force20` 错映射与别名;`--vendor zebra`;`Helpers.CreateRegistry()` 注册全版本/厂商模块;banner 版本取程序集;`encode` 加 `--llrp` 且目录单源;`LlrpFrameAnalyzer` 状态提取去 1.0.1 硬编码 | `ProtocolVersionPolicyParser.cs`、`VendorExtensionModeParser.cs`、`CliConnectionOptions.cs`、`Helpers.cs`、`EncodeCommand.cs`、`LiveProtocolDiagnostics.cs`、`LlrpFrameAnalyzer.cs`、`LiveCommand.cs` | LlrpCli.Tests 新增:版本解析(`"2"`→2.0、别名)、decode/validate 1.1/2.0 帧、Zebra custom 帧解码、encode `--llrp 2.0` |
| WP2 settings 收缩(§5.2/§5.4) | 7→5:`show/validate/apply/edit/save`;`apply --defaults`;移除 `defaults/load`;双校验消除;门控文案收口;flags 统一 | `LiveSettingsHandler.cs`、`ManagedSettingsWorkflow.cs`、`CommandCatalog.cs` | `apply` 无 `--yes` = 校验+预览+停止(不写设备);`validate` 零副作用;现有 settings 测试迁移 |
| WP3 一段式盘点(P1/P5) | `inventory start --defaults\|--settings`;与 one-shot `inventory` 共用部署/启动/清理核心 | `LiveInventoryHandler.cs`、`InventoryCommand.cs`(抽共用 workflow) | 交互式一段式可跑;one-shot 行为不回归 |
| WP4 manual on/off(§8/§13) | `manual on/off/status`;托管调用自动 off + 非托管资源确认;移除 `resources`/`clear` 旧命令(不保留别名) | `LiveCommand.cs`、`CommandCatalog.cs`、`LiveInventoryHandler.cs`、`LiveSettingsHandler.cs` | manual 流程测试;自动 off 的确认/静默两分支;`rospec add` 前置自动 sync |
| WP5 monitor 合一 + tag sequence 结构化(P4/P6) | `monitor` 与 `inventory start --monitor` 参数统一;`tag sequence` 改 `--read/--write/--erase/--lock/--kill` 结构化旗标 | `LiveMonitorHandler.cs`、`LiveInventoryHandler.cs`、`LiveTagAccessHandler.cs`、`CommandCatalog.cs` | 参数解析测试;补全候选同步 |
| WP6 消重(D1-D4) | standalone 与 live-shell 四命令合体;树遍历合并;hex 解析统一 | `LiveProtocolDiagnostics.cs` + 四个 Command、`FrameRenderer.cs`、`LlrpFrameAnalyzer.cs`、`TagAccessCliRequest.cs` | 行为不变;同一报文渲染一致 |
| WP7 编辑器增强(§5.3 优先级 E2→E1→E4→E3→E5→E6→E7) | 逐项独立小步,每步带测试 | `SettingsEditor.cs`(可能拆文件) | 每项交付即验收 |
| WP8 收口(§14/§15) | cli-user-guide 同步、CommandCatalog 补全、status/roadmap 记录、实机验收矩阵(2.0 未验证/Zebra 192.168.40.88/Impinj 192.168.40.87) | 文档 + acceptance 表 | 文档与代码一致;实机记录进 `docs/acceptance/reader-interoperability.md` |

