# LlrpCli 目标指令集（已实现基线）

> 状态：已实现（归档）。本文保留设计阶段的完整命令目录和行为决策；当前可用命令以
> [CLI 用户指南](../guides/cli-user-guide.md)、`LlrpCli` 命令目录和测试为准。

## 0. 设计原则

- 一段式优先,两段式保留(部署与激活可一步或分步)。
- 状态门控收敛到统一入口,报错给出下一步命令。
- 版本/厂商全覆盖(1.0.1 / 1.1 / 2.0;Impinj / Seuic / Zebra)。
- 离线工具版本感知,不再硬编码 1.0.1。

## 1. 连接层

```
connect <host> [port] [--llrp auto|1.0.1|1.1|2.0] [--vendor auto|impinj|seuic|zebra|none]
disconnect
status [--full]
caps [--raw|--json]
```

**变更**:
- `--llrp` 增加 `2.0`(修 `"2"→Force11` 错映射;别名 `101`/`11`/`20`)。
- `--vendor` 增加 `zebra`。
- banner 版本号不再硬编码 `v1.0.1`。

## 2. 配置层(settings 7 → 5)

```
settings show     [--json|--raw]
settings validate <file>
settings apply    [--defaults|<file>] --yes [--json]
settings edit     [--from reader|defaults|<file>]
settings save     <file>
```

**行为**:
- `validate` = 载入+校验+渲染诊断,零副作用(显式"先检查"步骤)。
- `apply` 语义单一:校验(守卫)→ 下发,`--yes` 是下发确认;`--defaults` 吸收原 `settings defaults`。
- `load` 折叠进 `validate`(load 本义即"载入+校验",多余一步删除)。
- `edit` 保留并增强(E1-E7,见 plan 5.3);`--from` 缺省为 `reader`(当前设备现状)。
- `save` 不变(导出当前配置 JSON)。

**双校验消除**:handler 只校验一次;`ManagedSettingsWorkflow.ApplyAsync` 拆出"已校验后直接下发"变体。

## 3. 盘点层

```
inventory start [--defaults|--settings <file>] [--monitor live|frames|none] [--monitor-duration s]
inventory stop
inventory status [--refresh]
monitor [live|frames|keepalive] [duration-sec] [--type MessageName]
```

**变更**:
- `inventory start` 无参 = 启动已部署(两段式第二段,现状保留);
- `inventory start --defaults` / `--settings <file>` = 一段式(部署+启动),合并 one-shot 逻辑,消除 P1/P5 双实现;
- `monitor` 与 `inventory start --monitor` 参数语法统一(P4/D5)。

## 4. 标签访问层

```
tag read   <epc> [--bank user|tid|epc|reserved] [--word N] [--count N] [--antenna N] [--password 8hex] [--timeout s]
tag write  <epc> [--bank ..] [--word N] [--data 4hex..] [--antenna N] [--password 8hex] [--timeout s] [--dry-run] [--yes]
tag lock   <epc> [--target user|epc|tid|access-pwd|kill-pwd|all] [--privilege unlock|perma-unlock|lock|perma-lock|no-change] [--yes]
tag kill   <epc> [--kill-pwd 8hex] [--yes]
tag erase  <epc> [--bank ..] [--word N] [--count N] [--yes]
tag sequence <epc> --read bank:word:count [--write bank:word:data] [--erase bank:word:count] [--lock target:privilege] [--kill pwd] [--yes]
```

**变更**:
- 单操作(read/write/lock/kill/erase)已结构化,保留;
- `sequence` 的 `--op read:...` 冒号字符串改为结构化 `--read/--write/--erase/--lock/--kill` 旗标,补全可提示(P6)。

## 5. 专家协议控制面(简化)

```
rospec add|list|enable|disable|start|stop|delete [id]
accessspec list|enable|disable|delete [id]
sync
clear
raw send|transact <hex> [--response-type N] --yes
```

**变更**:
- 删除旧的资源模式命令；`rospec`、`accessspec` 与 `raw` 连接 Ready 后直接执行。
- 专家写入后标记 `IsManagedStateSynchronized=false`，保留 DesiredState；`sync`/托管接管入口不变。

## 6. 离线工具层(版本感知)

```
inspect  <hex>
decode   <hex> [--output text|json] [--llrp auto|1.0.1|1.1|2.0]
validate <hex> [--llrp auto|1.0.1|1.1|2.0]
encode   <msg> [--llrp auto|1.0.1|1.1|2.0] [--message-id N] [--rospec-id N] [--requested-data ..]
frames   [count]
```

**变更**:
- `decode/validate/encode` 增加 `--llrp`(encode 不再硬编码 `Version101`);
- 底层 `Helpers.CreateRegistry()` 全版本/全厂商化(A1);
- `LlrpFrameAnalyzer` 状态提取去 1.0.1 硬编码(A6);
- `encode` 消息目录单源化,消除两处漂移(D4)。

## 7. 一次性命令(非交互,脚本用)

```
llrp inventory <host> [--port N] [--settings <file>] [--duration s] [--output json|table] [--llrp ..] [--vendor ..] --yes
```

**变更**:与交互式 `inventory start` 共用同一部署/启动工作流(消除 P5 双实现);`--output json` 保持脚本友好。

## 8. 变更总表(current → target)

| 现状 | 目标 | 备注 |
|---|---|---|
| `settings defaults` | `settings apply --defaults` | 折叠 |
| `settings load` | 删除 | `validate` 已覆盖"载入+校验" |
| `settings validate` | 保留 | 显式校验,零副作用 |
| `settings apply` | `settings apply [--defaults\|<file>] --yes` | 语义单一:校验守卫→下发 |
| `settings edit` | `settings edit`(增强) | E1-E7,不删 |
| `inventory start`(仅两段式) | `inventory start [--defaults\|--settings]` | 一段式 |
| 资源模式切换 + `sync` 前置 | `rospec`/`accessspec` 直接操作 | 两个控制面，不提供额外模式命令 |
| `resources clear` | `clear` | 改名,不保留别名 |
| `--llrp auto\|1.0.1\|1.1` | 增加 `2.0` | 修 `"2"` 错映射 |
| `--vendor impinj/seuic/none` | 增加 `zebra` | |
| `decode/validate/encode` 无 `--llrp` | 增加 `--llrp` | 版本感知 |
| `tag sequence --op read:...` | `--read/--write/...` 结构化 | P6 |

## 9. 残留待拍板

无 —— 全部已定(见 plan §8/§9/§11,可推翻)。
