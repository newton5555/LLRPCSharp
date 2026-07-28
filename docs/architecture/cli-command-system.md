# CLI 命令系统与交互提示链规划

> 状态：分阶段落地中
>
> 基准日期：2026-07-27
>
> 目的：记录 `LlrpCli` 外层命令与 Live Shell 的当前结构、长期边界和分阶段重构顺序。

## 目标

`LlrpCli` 同时承担三类职责：

1. 一次性执行的外层命令，例如 `llrp config get <HOST>`；
2. 保持 Reader 会话的 Live Shell，例如连接后连续执行 `inventory`、`rospec` 和 `raw`；
3. 不连接设备的协议诊断，例如 `inspect`、`decode`、`validate` 和 `encode`。

长期目标不是把三类入口合并成同一种 UI，而是让它们共享命令定义、参数语义和业务处理器，避免外层 CLI 与 Live Shell 功能逐渐分叉。

## 当前结构

### 外层 CLI

入口为 `LlrpCliApplication`，使用 `Spectre.Console.Cli.CommandApp` 注册命令：

```text
LlrpCliApplication
└─ Spectre CommandApp
   ├─ connect
   ├─ monitor
   ├─ inspect / decode / validate / encode
   └─ config
      ├─ get
      └─ apply
```

外层命令具备成熟的参数绑定、严格解析、`--help` 和退出码处理。在线命令通常自行创建 `LlrpReader`，执行一次操作后断开连接。

### Live Shell

Live Shell 是 `CommandApp` 的默认命令，但进入交互环境后使用独立实现：

```text
LiveCommand
├─ TerminalLineEditor
├─ CommandCatalog
├─ LiveCommandParser
├─ switch (verb)
└─ HandleXxxAsync / HandleXxx
```

Live Shell 持有当前 `_reader`、帧观察器、盘点任务和监控状态，适合连续执行依赖同一连接的命令。

### 当前提示链

输入提示链如下：

```text
键盘输入
  ↓
TerminalLineEditor.ReadLine(prompt, assistProvider)
  ↓
CommandCatalog.Assist(text, cursor, isConnected)
  ↓
候选项 Candidates + 灰色 GhostSuffix + Hint
  ↓
TerminalLineEditor.Redraw
  ↓
Tab 循环候选 / → 接受 Ghost / Enter 提交
  ↓
LiveCommandParser.Tokenize
  ↓
switch 分发到 HandleXxx
```

`TerminalLineEditor` 还负责：

- 上下方向键历史记录；
- 本地历史文件持久化；
- 光标移动、插入和删除；
- `Ctrl+C` 取消当前输入；
- 重定向输入输出时退化为普通 `Console.ReadLine()`。

## 当前问题

### 1. 两套命令描述重复

外层 CLI 的命令结构定义在 `LlrpCliApplication` 和各 `CommandSettings` 中；Live Shell 的命令名称、Usage、连接要求和别名定义在 `CommandCatalog` 中，详细帮助又在 `LiveCommand.RenderHelp()` 中维护。

新增或修改命令时容易只更新其中一处。例如外层 `config get/apply` 已存在，但 Live Shell 不会自动获得这些命令。

### 2. 两套参数解析不一致

外层 CLI 使用 Spectre 的严格解析；Live Shell 使用自己的 `Tokenize` 和各 Handler 内部解析。两者可能在以下方面产生差异：

- 选项顺序；
- 引号和转义；
- 必填参数；
- 默认值；
- 错误消息；
- `--help` 内容。

### 3. 提示候选由硬编码分支产生

当前只有 `rospec`、`accessspec`、`inventory`、`encode` 和 `frames` 等少量命令拥有子命令候选。选项名、枚举值、动态资源 ID 和型号相关能力尚未进入统一提示模型。

### 4. LiveCommand 责任过重

`LiveCommand` 同时负责：

- 连接生命周期；
- 命令解析和路由；
- 业务操作；
- 表格渲染；
- 帧监控；
- 盘点报告聚合；
- 帮助文本。

继续向其中直接增加配置、标签访问和厂商命令会显著增加维护成本。

### 5. 终端层与命令层耦合

外层命令主要依赖 `IAnsiConsole`，但 `TerminalLineEditor` 直接访问进程级 `Console`。这符合交互终端需求，却使提示链、光标行为和 Live 命令集成测试更困难。

## 目标架构

### 1. 保留两个宿主

继续保留：

- `SpectreCliHost`：一次性外层命令、参数绑定、帮助和退出码；
- `LiveShellHost`：保持 Reader 连接、交互输入、历史和提示。

不要求 Live Shell 完全复用 Spectre 的解析器，因为交互式部分输入和动态提示具有不同需求。

### 2. 共享命令定义

建立与宿主无关的命令描述模型，概念结构如下：

```csharp
public sealed record CliCommandDefinition(
    string Name,
    IReadOnlyList<string> Path,
    string Description,
    bool RequiresConnection,
    IReadOnlyList<CliArgumentDefinition> Arguments,
    IReadOnlyList<CliOptionDefinition> Options,
    IReadOnlyList<string> Aliases);
```

命令定义成为以下内容的共同来源：

- 外层命令注册；
- Live Shell 路由；
- Usage 和 Help；
- 输入候选和枚举值提示；
- 连接状态可用性；
- 命令对齐测试。

### 3. 共享业务处理器

把业务操作从两个 UI 宿主中提取出来：

```text
Command Definition
       ↓
Command Handler / Operation Service
       ↓
LlrpReader / Protocol / Offline Codec
       ↓
Command Result
       ↓
Spectre Renderer 或 Live Renderer
```

例如配置操作应由共享服务承担：

```text
ReaderConfigurationOperations
├─ QueryAsync(reader)
├─ BuildPatch(options)
└─ ApplyAsync(reader, patch)
```

外层 `config get/apply` 可以创建临时 Reader；Live Shell 的 `config get/apply` 则复用当前 Reader。配置映射和验证逻辑只保留一份。

### 4. 引入 LiveSessionContext

将 Live 会话状态从 `LiveCommand` 字段中收拢为独立上下文：

```text
LiveSessionContext
├─ Reader
├─ Host / Port
├─ ProtocolVersionPolicy
├─ VendorExtensionMode
├─ FrameObserver
├─ InventoryTask
├─ MonitoringState
├─ DesiredInventorySettings (下一次盘点草稿，不是运行中快照)
└─ CancellationScopes
```

Handler 通过上下文访问当前连接，Live Shell 负责上下文生命周期，不让普通命令直接管理全局字段。

### 5. 盘点意图不是设备配置

Live Shell 的 `config` 命令组映射 `ReaderConfiguration`：它查询或显式写入设备的物理/事件配置。盘点参数则属于 `ReaderSettings`（后续规范名为 `InventorySettings`），由 SDK 编译到 ROSpec 与必要的托管资源；两者不能共用一个 CLI 配置对象。

目标命令形态为：

```text
config get | defaults | apply ... --yes
inventory settings show | set | load | save | reset
inventory start [--antennas <id,id|all>]
inventory stop | status
```

`inventory settings` 操作 `LiveSessionContext.DesiredInventorySettings`；`inventory start` 将其不可变快照传给 `reader.StartAsync(snapshot)`。`reader.CurrentSettings` 只用于 `inventory status` 显示当前运行参数，停止盘点后不保留为草稿；status 同时明确提示运行快照是否与下一次草稿不同。厂商扩展设置不得以 `Dictionary<string, object?>` 的默认 JSON 形式保存，必须经过其 Extension 的强类型 Profile 序列化。

### 6. 重构提示链

保留 `TerminalLineEditor` 的交互体验，但把提示计算抽象为 `IInputAssistProvider`：

```text
Partial Input
  ↓
Partial Command Parser
  ↓
Command Definition + LiveSessionContext
  ↓
Suggestion Providers
  ├─ Command / Alias
  ├─ Subcommand
  ├─ Option Name
  ├─ Enum Value
  ├─ Resource ID
  └─ Reader Capability-Aware Value
  ↓
InputAssist
```

`InputAssist` 继续输出：

- `Candidates`：Tab 候选；
- `GhostSuffix`：灰色预测后缀；
- `Hint`：当前命令 Usage、参数说明或状态提示。

提示计算失败不应中断输入，但应允许写入诊断日志，避免所有错误被永久静默吞掉。

### 6. 提示与执行使用同一解析结果

部分输入解析和最终执行解析应共享 Token/Option 规则。目标是避免提示链认为命令有效，而执行阶段使用另一套规则拒绝。

引号、空格、选项顺序和转义规则需要形成单独测试矩阵。

## 命令分类与对齐原则

| 命令类型 | 外层 CLI | Live Shell | 共享要求 |
|---|---|---|---|
| `inspect/decode/validate/encode` | 支持 | 支持 | 共享解析与渲染输入模型 |
| `connect/monitor` | 临时连接 | 会话连接 | 共享连接选项和 Vendor/LLRP 策略 |
| `config get/apply` | 支持 | 支持 | 共享配置操作、变更解析与校验；Live 复用连接 |
| `inventory/rospec/accessspec` | 部分或无 | 支持 | `rospec add` 仅创建 SDK 默认 Disabled ROSpec；其余 Handler 面向已有 Reader |
| `raw/sync/frames` | 诊断入口 | 支持 | 保留明确的状态与安全提示 |
| `tag read/write` | 实施中 | 实施中 | `tag read` 仅执行标准非破坏性读取；`tag write` 先只支持 dry-run/inspect，不连接设备、不写标签 |

并非所有命令都必须同时暴露在两个宿主中，但差异必须由命令定义显式声明，不能因为漏注册而产生。

## 安全规则

- `raw send/transact` 继续要求显式确认；
- `config apply` 在 Live 中先显示变更摘要，支持 `--dry-run`，写入必须显式携带 `--yes`；
- 标签写入、锁定、Kill 等操作必须使用更高等级确认；
- 提示链可以展示危险级别，但不能替代执行阶段校验；
- 厂商扩展模式和协议版本必须显示在连接状态与提示上下文中。

## 分阶段实施

### C1：连接选项对齐

- 统一 `auto|impinj|none` Vendor 模式解析；
- 外层连接、Monitor、Live 启动和 Live `connect` 使用同一语义；
- 更新 Usage、Help 和提示候选。

当前状态：已落地基础实现。外层 `connect` / `monitor` 与 Live Shell `connect` 共享 `CliConnectionOptions` 和 `LiveCommandParser.ParseConnect`，`CommandCatalog` 已集中维护连接选项和常用子命令候选。

### C2：共享命令定义

- 将 `CommandCatalog` 升级为结构化命令树；
- 从定义生成 Live Help、Usage 和候选；
- 增加命令路径、参数、选项和可用状态模型。

当前状态：Live Shell 已完成这一边界。`CommandCatalog` 为每个命令集中保存规范名称、别名、Usage、描述、连接要求、候选值和 `LiveCommandRoute`；提示链、`help <command>` 与 Live 分发均读取该目录。`help` 安全渲染目录中的 Usage，避免方括号选项被 Spectre Markup 误解析。外层 Spectre 的强类型注册仍保持独立，通用业务 Handler 与 `LiveSessionContext` 则属于后续 C3/C4。

### C3：拆分 LiveSessionContext 与 Handler

- 已将连接、帧观察、盘点任务、监控状态和当前端点从 `LiveCommand` 提取到 `LiveSessionContext`。
- 无会话依赖的 `inspect`、`decode`、`validate` 与 `encode` 由 `LiveProtocolDiagnostics` 处理；`LiveInventoryHandler` 负责 SDK 托管盘点及报告泵，`LiveMonitorHandler` 负责被动帧和实时标签表，`LiveConnectionHandler` 负责连接、帧观察器、断开和退出释放。
- `LiveCommand` 现为交互宿主与路由层，保留提示链、帮助、状态渲染及尚未抽取的资源/配置命令；现有命令行为和输出保持兼容。

### C4：配置命令对齐

- 提取 `ConfigGetCommand` / `ConfigApplyCommand` 的共享操作；
- Live Shell 已增加 `config get/apply`，两者复用相同的配置映射与参数完整性校验；
- 外层和 Live `config apply` 均具备基于当前设备配置的变更摘要与 `--dry-run`；Live 写入必须显式使用 `--yes`。

### C5：标签访问命令

- SDK Tag Access API 已完成；当前增加标准 `tag read`，仅针对明确 EPC 目标执行非破坏性 Memory Read；
- `tag write` 当前仅生成并显示请求计划，明确不连接设备、不创建 AccessSpec 且不调用 SDK 写入 API；
- 提示链提供 Memory Bank 和访问选项候选；真实写入、Lock、Kill 与厂商访问操作留待后续独立安全设计。

### C6：测试与兼容性

- 已覆盖 `tag` 命令目录与连接状态门控、Live 输入候选、dry-run 不连接设备、以及 EPC/写入数据非法十六进制输入；
- 命令定义与两个宿主的对齐测试；
- Partial Parser 和 InputAssist 的完整引号、选项顺序矩阵仍待补齐；
- 引号、选项顺序、别名和错误提示测试；
- 使用可控 Transport 或 Virtual Reader 的 Live Handler 集成测试；
- 重定向输入输出和不支持 ANSI 的终端回退测试。

## 当前结论

短期继续保留 Spectre 外层命令和现有 Live Shell，不进行整体重写。新增功能优先提取共享业务操作，再分别接入两个宿主。

下一步推进 C5：在已稳定的 SDK Tag Access API 上提供安全的 `tag read`，`tag write` 先只提供 dry-run/inspect 计划；之后以 C6 补齐两个宿主、部分输入和 Live Handler 的兼容测试。
