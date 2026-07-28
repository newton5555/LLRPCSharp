# LLRPCSharp CLI 工具链与 Studio 使用指南

本文档面向 RFID 现场工程师、测试人员及系统集成开发者，介绍 `LLRPCSharp` CLI 工具链（`LlrpCli`）的交互式 Live Shell 与离线协议诊断工具。

---

## 一、CLI 设计原则与双轨架构

`LlrpCli` 旨在为 RFID 硬件与 SDK 调试提供双轨视口：

```text
                               命令行输入 (CLI Command)
                                          │
    ┌─────────────────────────────────────┴─────────────────────────────────────┐
    ▼                                                                           ▼
【在线托管业务视口 (Live Shell)】                                    【离线协议诊断工具箱 (Protocol Codec)】
 严格对接 LlrpReader SDK 公开能力                                    不需要连接读写器即可使用
 ├─ connect / disconnect / status / caps                             ├─ inspect <hex>  (Header 解包)
 ├─ inventory start / stop / status                                  ├─ decode <hex>   (Tree 报文树)
 ├─ tag read / write / lock / kill / erase / sequence               ├─ validate <hex> (完整性校验)
 ├─ config get / defaults / apply                                    └─ encode <msg>   (序列化生成)
 └─ rospec / accessspec / raw / sync
```

---

## 二、两种运行模式

### 1. 交互式 Studio 模式 (Live Shell) —— **推荐**

无需指定命令行子命令，直接运行 `LlrpCli` 即可进入高亮交互式终端环境：

```powershell
dotnet run --project src/LlrpCli
```

**特点**：
- **实时 Prompt**：动态显示当前连接设备 IP、端口及会话状态（如 `📡 llrp (192.168.1.100:5084) >`）。
- **智能提示与自动补全**：按 `Tab` 键自动补全指令、子命令及标志参数，底部独创智能提示线（Hint）。
- **报文自动渲染**：连接后自动高亮渲染全部非标签收发 LLRP 帧（TX/RX）；`RO_ACCESS_REPORT` 进入标签聚合，只有 `monitor frames` 才将标签报告也按原始帧打印。
- **命令历史**：使用 `↑` / `↓` 方向键浏览历史输入记录。

### 2. 离线命令行模式

根命令只提供不连接读写器的协议分析、验证与编码，可安全用于脚本或 CI/CD：

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

盘点意图保存在当前 Live Shell 会话的本地草稿中；编辑草稿不需要连接读写器。`inventory settings`、`inventory settings show` 和 `inventory settings get` 都显示草稿；`inventory settings set --antennas 1` 与简写 `inventory settings --antennas 1` 都会更新草稿。`inventory start` 只消费该草稿，并可接受一次性的天线和监控覆盖：

```
inventory settings show
inventory settings set [options]
inventory settings load <path.json>
inventory settings save <path.json>
inventory settings reset
inventory start [--antennas <id,id|all>] [--monitor live|frames|none]
```

`inventory status` 显示 SDK 的运行中 `CurrentSettings`，并明确提示它是否已与本会话的下一次盘点草稿不同；草稿变化永远不会修改正在运行的盘点。

| 参数 | 类型 | 说明 |
|---|---|---|
| `--antennas` | `id,id\|all` | 草稿或本次启动使用的天线；`all` 映射为 LLRP 全部天线（ID 0）。 |
| `--monitor` | `live\|frames\|none` | 本次启动的前台监控方式；默认 `live`。Ctrl+C 只退出监控，不停止盘点。 |
| `--session` | 0..3 | C1G2 单例化会话号（默认 0）。 |
| `--population` | ushort | 标签数量估计（默认 32）。 |
| `--mode` | ushort | ModeIndex（RF 模式索引）。 |
| `--tari` | ushort | Tari 值（纳秒）。 |
| `--attach-bank` | epc\|tid\|user\|reserved\|none | 附加读取内存 Bank；设置 Bank 自动启用 AttachedData，`none` 关闭它。 |
| `--attach-ptr` | ushort | 附加读取字偏移（Word Pointer）。 |
| `--attach-len` | ushort | 附加读取字数（Word Count）。 |
| `--attach-pwd` | hex | 附加读取访问密码（8 位十六进制）。 |

**示例**：

```
# 将草稿设为天线 1、会话 2、估计 64 标签
inventory settings set --antennas 1 --session 2 --population 64

# 让草稿附带读取 TID 前 6 个字，并按草稿启动
inventory settings set --attach-bank tid --attach-len 6
inventory start

# 不改动草稿，仅在本次启动改用天线 1 和 3
inventory start --antennas 1,3

# 默认显示聚合标签表；Ctrl+C 回到 Prompt，盘点仍然运行
inventory start

# 连标签报告也显示为底层 TX/RX LLRP 报文；Ctrl+C 回到 Prompt
inventory start --monitor frames

# 只启动盘点，不进入前台监控
inventory start --monitor none

# 添加一个 Disabled 默认 ROSpec；把参数编译为 AISpec 内的 C1G2 RF / singulation 参数
rospec add --id 14151 --antennas 1,2 --mode 1000 --tari 25000 --session 2 --population 64
rospec enable 14151
rospec start 14151
```

使用 `inventory settings load <path>` 或 `save <path>` 导入、导出 JSON 格式的完整 `ReaderSettings` 草稿。之后仍可通过 `settings set` 只修改个别字段。

**ReaderSettings JSON 文件格式示例**（`my-settings.json`）：

```json
{
  "antennaIds": [1, 2],
  "session": 2,
  "tagPopulationEstimate": 64,
  "modeIndex": 1,
  "tari": 12500,
  "attachedData": {
    "enabled": true,
    "memoryBank": 2,
    "wordPointer": 0,
    "wordCount": 6,
    "accessPassword": "00000000"
  }
}
```

> `memoryBank`：0=Reserved, 1=EPC, 2=TID, 3=User

#### 其他盘点命令

```
inventory stop              # 停止当前托管盘点
inventory status            # 查看盘点运行状态（OperationState + 当前 Settings 摘要）
```

---

### 设备配置（config）

| 命令 | 完整语法 | 说明 |
|---|---|---|
| `config get` | `config get <host> [--llrp ...] [--vendor auto\|impinj\|none]` | 查询设备当前运行配置快照，在终端渲染配置面板。 |
| `config defaults` | `config defaults <host> [--llrp ...] [--vendor auto\|impinj\|none]` | 显示 SDK 针对当前设备推荐的安全配置基线（不下发）。 |
| `config apply` | `config apply <host> [--llrp ...] [--vendor auto\|impinj\|none] [options] [--dry-run]` | 调整设备配置。 |

`config apply` 支持的参数：

| 参数 | 说明 |
|---|---|
| `--antenna <ID>` | 天线端口 |
| `--tx-power <INDEX>` | 发射功率索引 |
| `--rx-sens <INDEX>` | 接收灵敏度索引 |
| `--channel <INDEX>` | 信道索引 |
| `--keepalive-type none\|periodic` | 心跳类型 |
| `--keepalive-interval <ms>` | 心跳间隔（毫秒） |
| `--gpo-port <PORT>` | GPO 引脚端口号 |
| `--gpo-data true\|false` | GPO 引脚输出值 |
| `--dry-run` | 仅预览变更计划，不实际写入设备 |
| `--yes` | 确认执行（非 dry-run 时必填） |

**示例**：

```
config apply --antenna 1 --tx-power 10 --dry-run     # 预览功率调整
config apply --antenna 1 --tx-power 10 --yes         # 实际写入
config apply --keepalive-type periodic --keepalive-interval 5000 --yes
```

`config get` 会显示每根天线的 Tx/Rx/Channel 索引、GPIO、全部事件开关及已启用的 Impinj 配置摘要。`--tx-power`、`--rx-sens` 与 `--channel` 接受的是设备能力表索引，不是 dBm、mW 或频率；先运行 `caps`，再把对应索引传给 `config apply`。CLI 会拒绝当前设备未报告的 Tx/Rx 索引。

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

**2. `config apply` 如何避免误操作写坏设备？**
- 使用 `--dry-run` 参数（如 `config apply --antenna 1 --tx-power 12 --dry-run`），SDK 会计算变更并渲染 Preview 面板，**绝对不会向读写器发送任何写报文**。

**3. 如何批量管理盘点配置？**
- 将常用草稿保存到 JSON 文件（如 `inventory settings save warehouse.json`），新会话用 `inventory settings load warehouse.json` 恢复。
- 如需改变个别字段，执行 `inventory settings set --session 0`；如只想临时换天线，使用 `inventory start --antennas 1`。

**4. `tag write` 如何防止意外覆盖标签数据？**
- 在已连接的 Live Shell 中先执行 `tag write <epc> ...`，CLI 会打印完整的 OpSpec 计划（包含 Bank、Word Pointer、待写数据）且不写标签；确认无误后在同一命令加 `--yes` 才会实际执行。访问受保护标签时再加 `--password`。

**5. `tag lock perma-lock` 操作是不可逆的，请谨慎使用！**
- `perma-lock` 将标签对应区域设为永久只读/永久不可访问，**无法撤销**。
