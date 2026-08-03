# LLRPCSharp CLI 工具链与 Studio 使用指南

本文档面向 RFID 现场工程师、测试人员及系统集成开发者，介绍 `LLRPCSharp` CLI 工具链（`LlrpCli`）的交互式 Live Shell、一次性寻卡命令与离线协议诊断工具。

---

## 一、CLI 设计原则与分层架构

`LlrpCli` 提供三个稳定入口；在线入口共用同一个 SDK 与托管设置工作流，不复制配置、校验或资源管理逻辑：

```text
                               命令行输入 (CLI Command)
                                          │
    ┌───────────────────────────────┬───────────────────────────────┐
    ▼                               ▼                               ▼
【Live Shell】                 【一次性 inventory】             【离线 Protocol Codec】
 长连接交互与诊断               Agent/脚本友好                  无需连接 Reader
 settings / inventory          connect -> apply -> inventory    inspect / decode
 tag / resources / raw         -> stop -> clear                 validate / encode
    │                               │
    └──────── ManagedSettingsWorkflow ────────► LlrpReader SDK
```

---

## 二、运行模式

### 1. 交互式 Studio 模式 (Live Shell) —— **推荐**

无需指定命令行子命令，直接运行 `LlrpCli` 即可进入高亮交互式终端环境：

```powershell
dotnet run --project src/LlrpCli
```

**特点**：
- **实时 Prompt**：动态显示当前连接设备 IP、端口及会话状态（如 `📡 llrp (192.168.1.100:5084) >`）。
- **智能提示与自动补全**：空 Prompt 会显示推荐的下一步；输入时底部提示线直接列出匹配的命令、子命令或标志。`Tab` 接受建议或循环候选，`Shift+Tab` 反向循环。
- **报文自动渲染**：连接后自动高亮渲染全部非标签收发 LLRP 帧（TX/RX）；`RO_ACCESS_REPORT` 进入标签聚合，只有 `monitor frames` 才将标签报告也按原始帧打印。
- **命令历史**：使用 `↑` / `↓` 方向键浏览历史输入记录。

### 2. 一次性在线命令

根级 `inventory` 适合 Agent、脚本和人工快速验收。它使用与 Live Shell 相同的 Settings 加载、校验和 Apply 代码路径：

```powershell
dotnet run --project src/LlrpCli -- inventory 192.168.1.100 --duration 10 --yes
dotnet run --project src/LlrpCli -- inventory 192.168.1.100 --settings warehouse.json --duration 30 --output json --yes
```

### 3. 离线命令行模式

离线命令不连接读写器，可安全用于脚本或 CI/CD：

```powershell
# 单发解码
dotnet run --project src/LlrpCli -- decode 043E0000000A01020304

# 单发验证
dotnet run --project src/LlrpCli -- validate 043E0000000A01020304
```

---

## 三、全量指令与语法手册

### 连接与会话

| 命令 | 完整语法 | 说明 |
|---|---|---|
| `connect` | `connect [host] [port] [--llrp auto\|1.0.1\|1.1] [--vendor auto\|impinj\|seuic\|none]` | 连接目标读写器并执行握手。<br>例：`connect 192.168.1.100` |
| `disconnect` | `disconnect` | 停止后台盘点流并优雅断开当前 TCP 连接。 |
| `status` | `status` | 显示连接状态、设备厂商、固件版本及累积捕获帧数。 |
| `caps` | `caps` | 显示能力与 Tx/Rx 索引到实际 dBm 的映射；修改功率、灵敏度或频道前先执行。 |
| `caps` | `caps` | 显示读写器硬件能力快照（天线数、灵敏度表、UTC 时钟等）。 |

---

### 托管盘点（inventory）

`inventory` 只控制读写器上已经部署的托管 Inventory；它不编辑草稿，也不会临时覆盖天线或其他参数。先用完整的 `ReaderSettings` 文档通过 `settings apply <file> --yes` 部署声明式意图；该操作保持资源 Disabled，再用 `inventory start` 启动：

```
inventory start [--monitor live|frames|none] [--monitor-duration <seconds>]
inventory stop
inventory status
```

`inventory status` 显示读写器实际托管的 `CurrentInventorySettings`。没有已部署的 Inventory 时会明确提示先执行 `settings apply <file> --yes`。

| 参数 | 类型 | 说明 |
|---|---|---|
| `--monitor` | `live\|frames\|none` | 本次启动的前台监控方式；默认 `live`。Ctrl+C 只退出监控，不停止盘点。 |
| `--monitor-duration` | 正整数秒 | 仅可搭配 `live` 或 `frames`；计时到期后退出监控并返回 Prompt，盘点继续运行。 |

**示例**：

```
# 先部署完整的 ReaderSettings 文档；文件中的 Inventory 决定天线、Session、Filters、AttachedData 与厂商扩展
settings apply warehouse.json --yes

# 默认显示聚合标签表；Ctrl+C 回到 Prompt，盘点继续运行
inventory start

# 连标签报告也显示为底层 TX/RX LLRP 报文；Ctrl+C 回到 Prompt
inventory start --monitor frames

# 监控 30 秒：时间到后退出表格，盘点继续运行
inventory start --monitor live --monitor-duration 30

# 只启动盘点，不进入前台监控
inventory start --monitor none

```

### 一次性寻卡（inventory）

```text
llrp inventory <HOST> [--port 5084] [--settings FILE] [--duration 10]
          [--output json|table] [--llrp auto|1.0.1|1.1]
          [--vendor auto|impinj|seuic|none] --yes
```

`inventory` 连接 Reader 后加载指定的完整 `ReaderSettings`；没有 `--settings` 时使用 SDK 针对当前设备解析的推荐 Defaults。命令会把 Inventory 强制为立即启动、无 Reader 侧停止触发，再按 `--duration` 限定采集时间。默认输出稳定 JSON，便于 Agent 读取；人工观察可指定 `--output table`。

该命令会应用完整 Settings。包含 Inventory 时，SDK 会清除全部 AccessSpec/ROSpec 并创建托管资源，因此必须显式给出 `--yes`。结束、取消或异常时会停止盘点并清除 SDK 托管 Inventory 资源；已经应用的 Reader 全局 Configuration 保留在设备上。退出码为：成功 `0`、参数或缺少确认 `2`、Settings 校验失败 `3`、连接或运行失败 `1`。

#### 其他盘点命令

```
inventory stop              # 停止当前托管盘点
inventory status            # 查看盘点运行状态（OperationState + 当前 Settings 摘要）
```

---

### 托管设置（settings）

| 命令 | 完整语法 | 说明 |
|---|---|---|
| `settings show` | `settings show [reader|draft|defaults] [--json]` | 只读查看设备事实、本地草稿或 SDK 推荐 Defaults；默认显示摘要，`--json` 显示完整文档。 |
| `settings edit` | `settings edit [--from defaults|reader|generic]` | 从明确来源建立副本并进入分组编辑器；只修改本地草稿。省略 `--from` 时编辑现有草稿，没有草稿则从 Defaults 开始。 |
| `settings validate` | `settings validate [file]` | 校验文件或当前草稿，不发送写报文。 |
| `settings apply` | `settings apply [file] --yes` | 应用文件或当前草稿；这是 `settings` 下唯一写设备的命令。 |
| `settings load` | `settings load <file>` | 将完整 Settings JSON 加载为本地草稿，不写设备。 |
| `settings save` | `settings save <file> [--source draft|reader|defaults]` | 保存指定来源；默认保存当前草稿。 |
| `settings discard` | `settings discard` | 丢弃本地草稿，不改变设备。 |

**示例**：

```
# 新设备：从当前型号/能力匹配的 Defaults 开始编辑
settings edit --from defaults
settings show draft
settings save warehouse-draft.json
settings validate
settings apply --yes
inventory start
```

草稿具有明确来源；来源只用于 CLI 显示和审计，不作为协议数据写入 Reader：

| 起点 | 命令 | 适用情况 |
|---|---|---|
| 当前 Reader Profile | `settings edit --from defaults` | 新设备或采用 SDK 的厂商/能力推荐值；Seuic 等扩展会解析实际天线与 RF 索引。 |
| 设备当前事实 | `settings edit --from reader` | 生产设备先读取后做最小修改。 |
| 通用 LLRP 基线 | `settings edit --from generic` | 准备可移植模板。 |
| JSON 文档 | `settings load <file>` | 使用已有的完整 `ReaderSettings` 文档。 |

编辑器按天线与 RF、Singulation、报告、Filters、启动/停止触发器、Attached Data、Reader Configuration 和厂商扩展分组。常用设置可直接交互编辑；高级用户仍可保存 JSON 后直接编辑完整 record 字段，再通过同一个 `validate/apply` 路径部署。

标准字段始终可序列化。厂商字段必须由已激活扩展的 `IReaderSettingsSerializationContributor` 提供强类型、版本化映射；未知扩展字段会明确失败，绝不静默丢失。启用 `.UseImpinj()` 的 Live CLI 支持 `impinj.configuration`、只读的 `impinj.facts` 和 Inventory 报告扩展；Facts 仅用于记录和核对，不是可写配置。

`settings apply` 的资源影响取决于内容：`Inventory == null` 时只写配置；包含 Inventory 时清除全部 AccessSpec/ROSpec 并重建 SDK 托管盘点，但保持 Disabled。因此始终要求 `--yes`；只有随后执行 `inventory start` 才开始 RF 盘点。`show/edit/load/save/discard/validate` 都不会写设备。

---

### 标签读写（tag）

| 命令 | 完整语法 | 说明 |
|---|---|---|
| `tag read` | `tag read <epc> --bank <bank> --word <addr> --count <n> [--antenna <id>] [--password <hex>] [--timeout <sec>]` | 读取指定 EPC 标签的内存 Bank 数据。 |
| `tag write` | `tag write <epc> --bank <bank> --word <addr> --data <hex> [--antenna <id>] [--password <hex>] [--yes]` | 写入标签内存；省略 `--yes` 时只预览 OpSpec，实写必须确认。 |
| `tag lock` | `tag lock <epc> --privilege <lock\|unlock\|perma-lock\|perma-unlock\|no-change> [--target all\|epc\|tid\|user\|access-pwd\|kill-pwd] [--antenna <id>] [--password <hex>] --yes` | 锁定/解锁标签内存区域，必须确认。 |
| `tag kill` | `tag kill <epc> [--kill-pwd <hex>] [--antenna <id>] --yes` | 永久销毁标签，必须确认。 |
| `tag erase` | `tag erase <epc> --bank <bank> --word <addr> --count <n> [--antenna <id>] [--password <hex>] --yes` | 块擦除（Block Erase）标签内存区域，必须确认。 |
| `tag sequence` | `tag sequence <epc> --op <operation> ...` | 在一个 AccessSpec 内执行多个同 EPC/天线目标的标准操作；含非读取操作时必须给出 `--yes`。 |

**Bank 别名**：`epc`(1) / `tid`(2) / `user`(3) / `reserved`(0)

**示例**：

```
# 读取 EPC 为 E2801171 的标签 User Bank 前 2 个字
tag read E2801171 --bank user --word 0 --count 2

# 写入（dry-run 预览）
tag write E2801171 --bank user --word 0 --data CAFEBABE

# 锁定 User Bank（需要访问密码）
tag lock E2801171 --privilege lock --target user --password 00000000

# 解锁所有区域
tag lock E2801171 --privilege unlock --target all --password 00000000

# 永久锁定 EPC Bank
tag lock E2801171 --privilege perma-lock --target epc --password 00000000

# 销毁标签
tag kill E2801171 --kill-pwd DEADBEEF

# 块擦除 User Bank 前 4 个字
tag erase E2801171 --bank user --word 0 --count 4 --password 00000000
```

`tag sequence` 的 `--op` 可重复使用：`read:<bank>:<word>:<count>`、`write:<bank>:<word>:<hex>`、`erase:<bank>:<word>:<count>`、`lock:<target>:<privilege>` 或 `kill:<password>`。例如：

```
# 先读 TID 两个字，再写入 User Memory；两项在同一个 AccessSpec 内执行
tag sequence E2801171 --op read:tid:0:2 --op write:user:0:1234 --password 00000000 --yes
```

序列中的 write/erase/lock/kill 与单操作一样会修改或销毁标签；Live Shell 必须显式添加 `--yes`，并应先在测试标签上验证。

---

### 资源操控（ROSpec / AccessSpec）

| 命令 | 语法 | 说明 |
|---|---|---|
| `rospec list` | `rospec list` | 列出设备上所有 ROSpec。 |
| `rospec enable` | `rospec enable [id]` | 启用 ROSpec。 |
| `rospec disable` | `rospec disable [id]` | 禁用 ROSpec。 |
| `rospec start` | `rospec start [id]` | 手动触发 ROSpec。 |
| `rospec stop` | `rospec stop [id]` | 停止 ROSpec。 |
| `rospec delete` | `rospec delete [id]` | 删除 ROSpec。 |
| `accessspec list` | `accessspec list` | 列出设备上所有 AccessSpec。 |
| `accessspec enable` | `accessspec enable [id]` | 启用 AccessSpec。 |
| `accessspec disable` | `accessspec disable [id]` | 禁用 AccessSpec。 |
| `accessspec delete` | `accessspec delete [id]` | 删除 AccessSpec。 |

所有上述写命令都必须先执行 `resources manual enter`；`rospec list` 与 `accessspec list` 则可在任何已连接模式查询。手动模式不得使用 SDK 保留 ID `14150`（ROSpec）和 `14151`（AttachedData AccessSpec）。若 Reader 保留 SDK 托管配置，先使用 `resources clear` 释放它，再进入手动模式。执行 `resources manual exit` 会显式删除全部 AccessSpec 与 ROSpec 并返回空闲状态。

---

### 被动监控（monitor / frames）

```
monitor [seconds]              # 实时抓取原始 LLRP 帧，按 Ctrl+C 停止
monitor 30                     # 抓取 30 秒后自动停止
frames [count]                 # 显示内存缓冲区中最近 N 条收发帧（默认 10）
```

---

### 逃生舱（raw / sync）

```
raw send <hex-frame> --yes                                 # 发送原始 LLRP 字节帧
raw transact <hex-frame> --response-type <type> --yes      # 发送并等待响应
sync                                                       # 同步 ROSpec/AccessSpec 状态
```

> `raw` 执行后，SDK 托管状态标记为"未同步"，需执行 `sync` 恢复托管能力。

---

### 离线协议工具（无需连接读写器）

| 命令 | 语法 | 说明 |
|---|---|---|
| `inspect` | `inspect <hex>` | 解析 LLRP 帧 Header（MessageType、Length、MessageId）。 |
| `decode` | `decode <hex>` | 完整解码 LLRP 帧并输出 JSON 参数树。 |
| `validate` | `validate <hex>` | 校验帧的长度规范与结构完整性。 |
| `encode` | `encode <msg-name> [--message-id <id>] [--rospec-id <id>]` | 将标准 LLRP 消息名序列化为 Hex 字节流。 |

---

### 终端实用

```
clear / cls                    # 清屏并重新绘制 Studio Banner
help [command]                 # 显示全量帮助或指定命令的参数说明
help ?                         # 与 help 相同
exit / quit / q               # 断开连接并退出
```

---

## 四、常见问题与调试建议

**1. 如何在 Live Shell 中即时看到底层收发的 LLRP 报文？**
- 连接后，控制台自动渲染所有非标签收发的原始 LLRP 帧；标签报告默认进入汇总。使用 `monitor frames` 可连同标签报告一起按原始帧查看。
- 也可随时输入 `frames 10` 查看最近 10 条报文，或使用 `monitor` 开启实时抓包。

**2. 如何避免误操作写坏设备？**
- `settings apply` 必须显式提供 `--yes`；应用前可先 `settings validate`，并以 `settings save reader-backup.json --source reader` 保存当前可识别设置。

**3. 如何批量管理盘点配置？**
- 将完整 `ReaderSettings` 草稿保存到 JSON 文件（如 `settings save warehouse.json`），新会话用 `settings load warehouse.json` 恢复。
- 如需改变个别字段，编辑完整 `ReaderSettings` JSON 后执行 `settings validate <file>` 与 `settings apply <file> --yes`。托管盘点没有一次性参数覆盖，以保证读写器实际资源与设置文档一致。

**4. `tag write` 如何防止意外覆盖标签数据？**
- 在已连接的 Live Shell 中先执行 `tag write <epc> ...`，CLI 会打印完整的 OpSpec 计划（包含 Bank、Word Pointer、待写数据）且不写标签；确认无误后在同一命令加 `--yes` 才会实际执行。访问受保护标签时再加 `--password`。

**5. `tag lock perma-lock` 操作是不可逆的，请谨慎使用！**
- `perma-lock` 将标签对应区域设为永久只读/永久不可访问，**无法撤销**。
