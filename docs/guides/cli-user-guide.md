# LLRPCSharp CLI 工具链与 Studio 使用指南 (CLI User Guide)

本文档面向 RFID 现场工程师、测试人员及系统集成开发者，详细介绍 `LLRPCSharp` CLI 工具链（`LlrpCli`）的功能架构、单发命令行模式与交互式 Studio 模式（Live Shell Terminal）的全量指令手册与诊断调试指南。

---

## 一、 CLI 设计原则与双轨架构

`LlrpCli` 旨在为 RFID 硬件与 SDK 调试提供双轨视口：

```text
                               命令行输入 (CLI Command)
                                          │
    ┌─────────────────────────────────────┴─────────────────────────────────────┐
    ▼                                                                           ▼
【在线托管业务视口 (Live Shell & One-shot)】                         【离线协议诊断工具箱 (Protocol Codec)】
 严格对接 LlrpReader SDK 公开能力                                    不需要连接读写器即可使用
 ├─ connect / disconnect / status / caps                             ├─ inspect <hex> (Header 解包)
 ├─ inventory start / stop / status                                  ├─ decode <hex> (Tree 报文树)
 ├─ tag read / tag write                                             ├─ validate <hex> (完整性校验)
 ├─ config get / defaults / apply                                    └─ encode <msg> (序列化生成)
 └─ rospec / accessspec / raw / sync
```

---

## 二、 两种运行模式

### 1. 交互式 Studio 模式 (Live Shell) —— **推荐**

无需指定命令行子命令，直接运行 `LlrpCli` 即可进入高亮交互式终端环境：

```powershell
dotnet run --project src/LlrpCli
```

**特点**：
- **实时 Prompt**：动态显示当前连接设备 IP、端口及会话状态（如 `📡 llrp (192.168.1.100:5084) >`）。
- **智能提示与自动补全**：按 `Tab` 键自动补全指令、子命令及标志参数（如 `--antenna`, `--tx-power` 等），底部独创智能提示线（Hint）。
- **报文自动渲染**：执行连接、配置调整、资源操控时，自动在控制台高亮渲染收发的底层二进制 LLRP 帧（TX/RX）。
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

## 三、 全量指令与语法手册

| 分组 | 命令 / 缩写 | 完整语法格式 | 说明与示例 |
|---|---|---|---|
| **连接与会话** | `connect` | `connect [host] [port] [--llrp auto\|1.0.1\|1.1] [--vendor auto\|impinj\|none]` | 连接目标读写器并执行握手。<br>例：`connect 192.168.1.100` |
| | `disconnect` | `disconnect` | 停止后台盘点流并优雅断开当前读写器 TCP 连接。 |
| | `status` | `status` | 查询显示当前会话状态、连接 ID、厂商 ID、型号、固件版本及累积捕获帧数。 |
| | `caps` | `caps` | 显示读写器硬件能力快照（最大天线数、灵敏度表、UTC 时钟等）。 |
| **设备配置** | `config get` | `config get` | 查询设备当前运行配置快照，并在终端渲染配置面板。 |
| | `config defaults` | `config defaults` | 显示 SDK 针对当前设备推荐的安全配置基线（不下发设备）。 |
| | `config apply` | `config apply [options] [--dry-run] --yes` | 调整设备配置。参数支持：<br>`--antenna <ID>` 天线端口<br>`--tx-power <INDEX>` 发射功率<br>`--rx-sens <INDEX>` 接收灵敏度<br>`--channel <INDEX>` 信道<br>`--keepalive-type none\|periodic` 心跳类型<br>`--keepalive-interval <ms>` 心跳间隔<br>`--gpo-port <PORT>` `--gpo-data true\|false` GPO引脚<br>例：`config apply --antenna 1 --tx-power 10 --yes` |
| **托管盘点** | `inventory start` | `inventory start [antenna-id]` | 启动 SDK 托管盘点流，控制台实时推流输出发现的标签 EPC、天线与 RSSI。<br>例：`inventory start 1` |
| | `inventory stop` | `inventory stop` | 停止当前托管盘点。 |
| | `inventory status` | `inventory status` | 查看托管盘点运行状态 (`OperationState`)。 |
| **被动推流** | `monitor` | `monitor [seconds] [--table \| --frames]` | 纯被动监听抓取原始 LLRP 帧。<br>`--table` 模式实时显示 EPC 统计表；`--frames` 模式输出 Hex 报文树。 |
| | `frames` | `frames [count]` | 输出内存缓冲区中最近收发的 N 条 LLRP 报文日志。<br>例：`frames 10` |
| **标签读写** | `tag read` | `tag read <epc> --bank <bank> --word <addr> --count <cnt>` | 针对指定 EPC 读取 C1G2 内存 Bank（`epc`, `tid`, `user`, `reserved`）。<br>例：`tag read E2801171 --bank user --word 0 --count 2` |
| | `tag write` | `tag write <epc> --bank <bank> --word <addr> --data <hex>` | 写入演练检查（Dry-run 校验模式，预览 OpSpec 计划，防止意外操作）。 |
| **资源操控** | `rospec` | `rospec add\|list\|enable\|disable\|start\|stop\|delete [id]` | 显式管理设备上的 ROSpec 资源。<br>例：`rospec list` / `rospec enable 1` |
| | `accessspec` | `accessspec list\|enable\|disable\|delete [id]` | 显式管理设备上的 AccessSpec 资源。 |
| **逃生与同步**| `raw` | `raw send\|transact <hex-frame> [--response-type type] --yes` | 发送原始 LLRP 字节帧，执行后触发 `IsManagedStateSynchronized = false`。 |
| | `sync` | `sync` | 重新向读写器同步 ROSpec/AccessSpec 状态，恢复托管同步标记。 |
| **离线工具** | `inspect` | `inspect <hex>` | 检查 16 进制 LLRP 报文的 Header（MessageType, Length, ID）。 |
| | `decode` | `decode <hex>` | 解码 16 进制报文并格式化输出结构化报文树及 JSON。 |
| | `validate` | `validate <hex>` | 校验 Hex 包的长度规范与结构完整性。 |
| | `encode` | `encode <msg-name> [--message-id ID] [--rospec-id ID]` | 将标准 LLRP 消息名序列化构建为 Hex 字节流。 |
| **终端实用** | `clear` / `cls` | `clear` 或 `cls` | 清屏并重新绘制标志性的 Studio Header Banner。 |
| | `help` / `?` | `help [command]` | 显示全量帮助大图或指定命令的参数说明。 |
| | `quit` / `exit` | `quit`, `exit`, 或 `q` | 断开连接并退出交互式 Shell。 |

---

## 四、 常见问题与调试建议

1. **如何在 Live Shell 中即时看到底层收发的二进制 Hex 报文？**
   - 执行 `connect`, `config get`, `config apply`, `rospec`, `sync` 等操作时，控制台会自动在表格上方渲染本次收发的原始 LLRP 帧。
   - 也可随时输入 `frames 10` 查看最近 10 条报文，或使用 `monitor` 开启实时抓包。

2. **`config apply` 如何避免误操作写坏设备？**
   - 使用 `--dry-run` 参数（如 `config apply --antenna 1 --tx-power 12 --dry-run`），SDK 会计算变更并渲染 Preview 面板，但绝对不会向读写器发送任何 `SET_READER_CONFIG` 写报文。
