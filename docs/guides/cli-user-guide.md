# LLRPCSharp CLI 工具链与 Studio 使用指南

本文档面向 RFID 现场工程师、测试人员及系统集成开发者，详细介绍 `LLRPCSharp` CLI 工具链（`LlrpCli`）的功能架构、单发命令行模式与交互式 Studio 模式（Live Shell Terminal）的全量指令手册与诊断调试指南。

---

## 一、CLI 设计原则与双轨架构

`LlrpCli` 旨在为 RFID 硬件与 SDK 调试提供双轨视口：

```text
                               命令行输入 (CLI Command)
                                          │
    ┌─────────────────────────────────────┴─────────────────────────────────────┐
    ▼                                                                           ▼
【在线托管业务视口 (Live Shell & One-shot)】                         【离线协议诊断工具箱 (Protocol Codec)】
 严格对接 LlrpReader SDK 公开能力                                    不需要连接读写器即可使用
 ├─ connect / disconnect / status / caps                             ├─ inspect <hex>  (Header 解包)
 ├─ inventory start / stop / status                                  ├─ decode <hex>   (Tree 报文树)
 ├─ tag read / write / lock / kill / erase                          ├─ validate <hex> (完整性校验)
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
- **报文自动渲染**：执行连接、配置调整、资源操控时，自动高亮渲染收发的底层 LLRP 帧（TX/RX）。
- **命令历史**：使用 `↑` / `↓` 方向键浏览历史输入记录。

### 2. 单发命令行模式 (One-shot Mode)

支持在脚本或 CI/CD 自动化中作为单发命令行工具使用：

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
| `connect` | `connect [host] [port] [--llrp auto\|1.0.1\|1.1] [--vendor auto\|impinj\|none]` | 连接目标读写器并执行握手。<br>例：`connect 192.168.1.100` |
| `disconnect` | `disconnect` | 停止后台盘点流并优雅断开当前 TCP 连接。 |
| `status` | `status` | 显示连接状态、设备厂商、固件版本及累积捕获帧数。 |
| `caps` | `caps` | 显示读写器硬件能力快照（天线数、灵敏度表、UTC 时钟等）。 |

---

### 托管盘点（inventory）

`inventory start` 支持两种参数组合方式，可单独使用或混合使用：

#### A. 内联参数

```
inventory start [antenna-id] [--session <0..3>] [--population <n>]
                [--mode <idx>] [--tari <nsec>]
                [--attach-bank epc|tid|user|reserved] [--attach-ptr <n>]
                [--attach-len <n>] [--attach-pwd <hex>]
```

| 参数 | 类型 | 说明 |
|---|---|---|
| `[antenna-id]` | ushort | 位置参数，天线 ID（0=全部天线）。向后兼容。 |
| `--session` | 0..3 | C1G2 单例化会话号（默认 0）。 |
| `--population` | ushort | 标签数量估计（默认 32）。 |
| `--mode` | ushort | ModeIndex（RF 模式索引）。 |
| `--tari` | ushort | Tari 值（纳秒）。 |
| `--attach-bank` | epc\|tid\|user\|reserved | 附加读取内存 Bank，设置后自动启用 AttachedData。 |
| `--attach-ptr` | ushort | 附加读取字偏移（Word Pointer）。 |
| `--attach-len` | ushort | 附加读取字数（Word Count）。 |
| `--attach-pwd` | hex | 附加读取访问密码（8 位十六进制）。 |

**示例**：

```
# 在天线 1 启动盘点，会话 2，估计 64 标签
inventory start 1 --session 2 --population 64

# 启动盘点并附带读取 TID 前 6 个字
inventory start --attach-bank tid --attach-len 6

# 全参数组合
inventory start 1 --session 2 --population 64 --mode 1 --attach-bank tid --attach-len 6
```

#### B. Settings 配置文件

通过 `--settings <path>` 或 `--config <path>` 加载 JSON 格式的完整 `ReaderSettings` 配置文件，**内联参数优先级高于文件**：

```
inventory start --settings my-settings.json
inventory start --settings my-settings.json --session 0   # 内联 --session 覆盖文件
```

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
| `config get` | `config get` | 查询设备当前运行配置快照，在终端渲染配置面板。 |
| `config defaults` | `config defaults` | 显示 SDK 针对当前设备推荐的安全配置基线（不下发）。 |
| `config apply` | `config apply [options] [--dry-run] --yes` | 调整设备配置。 |

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

---

### 标签读写（tag）

| 命令 | 完整语法 | 说明 |
|---|---|---|
| `tag read` | `tag read <epc> --bank <bank> --word <addr> --count <n> [--antenna <id>] [--password <hex>] [--timeout <sec>]` | 读取指定 EPC 标签的内存 Bank 数据。 |
| `tag write` | `tag write <epc> --bank <bank> --word <addr> --data <hex> [--antenna <id>] [--password <hex>] [--dry-run]` | 写入标签内存。`--dry-run` 模式仅预览 OpSpec，不实际写入。 |
| `tag lock` | `tag lock <epc> --privilege <lock\|unlock\|perma-lock\|perma-unlock\|no-change> [--target all\|epc\|tid\|user\|access-pwd\|kill-pwd] [--antenna <id>] [--password <hex>]` | 锁定/解锁标签内存区域。 |
| `tag kill` | `tag kill <epc> --kill-pwd <hex> [--antenna <id>]` | 永久销毁标签。 |
| `tag erase` | `tag erase <epc> --bank <bank> --word <addr> --count <n> [--antenna <id>] [--password <hex>]` | 块擦除（Block Erase）标签内存区域。 |

**Bank 别名**：`epc`(1) / `tid`(2) / `user`(3) / `reserved`(0)

**示例**：

```
# 读取 EPC 为 E2801171 的标签 User Bank 前 2 个字
tag read E2801171 --bank user --word 0 --count 2

# 写入（dry-run 预览）
tag write E2801171 --bank user --word 0 --data CAFEBABE --dry-run

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
- 执行 `connect`、`config get`、`config apply`、`rospec`、`sync` 等操作时，控制台自动渲染收发的原始 LLRP 帧。
- 也可随时输入 `frames 10` 查看最近 10 条报文，或使用 `monitor` 开启实时抓包。

**2. `config apply` 如何避免误操作写坏设备？**
- 使用 `--dry-run` 参数（如 `config apply --antenna 1 --tx-power 12 --dry-run`），SDK 会计算变更并渲染 Preview 面板，**绝对不会向读写器发送任何写报文**。

**3. `inventory start` 如何结合 settings 文件批量管理盘点配置？**
- 将常用配置保存到 JSON 文件（如 `warehouse.json`），然后每次运行 `inventory start --settings warehouse.json`。
- 如需临时覆盖个别参数，追加内联选项即可：`inventory start --settings warehouse.json --session 0`。

**4. `tag write` 如何防止意外覆盖标签数据？**
- 先执行 `tag write <epc> ... --dry-run`，CLI 会打印完整的 OpSpec 计划（包含 Bank、Word Pointer、待写数据），确认无误后去掉 `--dry-run` 并加 `--password` 实际执行。

**5. `tag lock perma-lock` 操作是不可逆的，请谨慎使用！**
- `perma-lock` 将标签对应区域设为永久只读/永久不可访问，**无法撤销**。
