# CLI 命令与 SDK 逻辑及 LLRP 报文对应关系规格计划 (Specification)

- 日期：2026-07-27
- 依据：[ADR 0005](../adr/0005-llrp101-reader-first-delivery.md) & [2026-07-27-reader-first-delivery.md](../plans/2026-07-27-reader-first-delivery.md)
- 目的：明确所有 CLI 命令与 SDK API、底层 LLRP 报文及帧观察器之间的 1:1 映射规格，作为 CLI 重构与验证的规格书。

---

## 一、 设计原则与双轨架构

CLI 旨在提供双轨视角：
1. **Live Shell 高层托管 API 视口**：展示 `LlrpReader` SDK 的公开能力，CLI 严禁包含私有协议业务逻辑；
2. **底层 Wire-Level 观察视口**：借助 SDK 的 `ILlrpFrameObserver` 实时捕获、解析并渲染 SDK 与设备之间收发的原始 LLRP 报文。

```text
命令行输入 (CLI Command)
   │
   ├─► 在线业务路由 ──► LlrpReader 公开 SDK API ──► 发送标准 / Impinj 报文
   │                                                    │
   └─► 离线诊断路由 ──► Protocol Codec / Parser         │ (Frame Observer 拦截捕获)
                                                       ▼
                                            终端渲染 & 报文树结构化展示
```

---

## 二、 CLI 命令与 SDK API / LLRP 报文全量映射规格表

| CLI 命令/子命令 | 对应 SDK 公开 API / 属性 | 底层 LLRP 报文 (标准 LLRP / Impinj Custom) | 观察器捕捉与行为描述 |
|---|---|---|---|
| `connect [host] [port] [--llrp auto\|1.0.1\|1.1] [--vendor auto\|impinj\|seuic\|none]` | `LlrpReader.CreateBuilder(host)`<br>`builder.Build()`<br>`reader.ConnectAsync()` | `GET_SUPPORTED_VERSION`<br>`SET_PROTOCOL_VERSION`<br>`GET_READER_CAPABILITIES(Discovery)`<br>`IMPINJ_ENABLE_EXTENSIONS`<br>`GET_READER_CAPABILITIES(All)` | 拦截并渲染握手过程中的 5 步收发报文；若连接成功更新控制台标题，失败则清空会话。 |
| `disconnect` | `reader.DisconnectAsync()`<br>`reader.DisposeAsync()` | `CLOSE_CONNECTION`<br>`CLOSE_CONNECTION_RESPONSE` | 优雅停止后台盘点泵，发送关闭连接报文，重置 CLI 为离线状态。 |
| `status` | `reader.ConnectionState`<br>`reader.NegotiatedVersion`<br>`reader.Identity`<br>`reader.IsManagedStateSynchronized` | - (只读 SDK 内存状态) | 打印当前连接状态、版本、厂商 ID、型号、固件版本以及托管同步标记。 |
| `caps` | `reader.Capabilities` | - (只读 SDK 内存状态) | 格式化展示天线数、GPI/GPO 限制、UTC 时钟及接收灵敏度全量快照。 |
| `config get` | `reader.QueryConfigurationAsync()` | `GET_READER_CONFIG`<br>`ImpinjRequestedData` (All_Configuration) | 查询读写器当前配置，并将 Impinj 延伸配置渲染在 `impinj.InventorySettings` 下。 |
| `config defaults` | `reader.GetDefaultConfigurationResult()` | - (SDK 本地基线，不发报文) | 展示 SDK 针对当前设备型号推荐的安全配置基线及 Provider 来源。 |
| `config apply [options] [--dry-run] --yes` | `reader.ApplyConfigurationAsync(config)` | `SET_READER_CONFIG` | 将命令行或 YAML 转化为 `ReaderConfigurationPatch` 并应用；使托管同步标记失效 (`IsManagedStateSynchronized = false`)。 |
| `inventory start [antenna]` | `reader.StartAsync(settings)` | `ADD_ROSPEC`<br>`ENABLE_ROSPEC`<br>`START_ROSPEC`<br>`ImpinjTagReportContentSelector` | 编译默认 ROSpec 14150；启动后台标签接收泵；挂载 Impinj 标签选择器。 |
| `inventory stop` | `reader.StopAsync()` | `STOP_ROSPEC`<br>`DISABLE_ROSPEC` | 停止盘点并保留 SDK 托管 ROSpec；`resources clear` 调用 `ClearManagedSettingsAsync()` 才删除它。 |
| `inventory status` | `reader.CurrentInventorySettings`<br>`reader.OperationState` | - (只读 SDK 内存状态) | 查看当前托管盘点配置、已接收标签统计与运行状态。 |
| `tag read <epc> --bank <bank> --word <addr> --count <cnt>` | `reader.ReadTagMemoryAsync(req)` | `ADD_ACCESSSPEC`<br>`ENABLE_ACCESSSPEC`<br>`RO_ACCESS_REPORT` (TagReport)<br>`DISABLE_ACCESSSPEC`<br>`DELETE_ACCESSSPEC` | 创建临时 AccessSpec (ID 24000+)；等待带有 C1G2ReadOpSpecResult 的标签报告；自动清理临时 AccessSpec。 |
| `tag write <epc> ... [--yes]` | `reader.WriteTagMemoryAsync(req)` | `ADD_ACCESSSPEC` ➔ `ENABLE_ACCESSSPEC` ➔ `RO_ACCESS_REPORT` ➔ 清理 | Live Shell 省略 `--yes` 时仅预览请求；显式确认后使用当前 Reader 执行标准 C1G2 写。 |
| `tag sequence <epc> --op ... [--yes]` | `reader.ExecuteTagAccessSequenceAsync(req)` | 一个 `AccessSpec` 内多个 C1G2 OpSpec ➔ `RO_ACCESS_REPORT` ➔ 清理 | Live Shell 的组合操作；纯读无需确认，任何写/擦/锁/销毁操作要求 `--yes`。 |
| `tag lock\|kill\|erase <epc> --yes` | `reader.LockTagMemoryAsync` / `KillTagAsync` / `BlockEraseTagMemoryAsync` | 对应标准 C1G2 OpSpec + 临时 AccessSpec 生命周期 | 均为高层 SDK 调用；所有修改或不可逆操作必须给出 `--yes`。 |
| `rospec add\|list\|enable\|disable\|start\|stop\|delete` | `reader.RoSpecs.*` | `ADD_ROSPEC`<br>`DELETE_ROSPEC`<br>`ENABLE_ROSPEC`<br>`DISABLE_ROSPEC`<br>`START_ROSPEC`<br>`STOP_ROSPEC`<br>`GET_ROSPECS` | 显式管理设备上的 ROSpec 资源，直接向读写器下发协议指令。 |
| `accessspec list\|enable\|disable\|delete` | `reader.AccessSpecs.*` | `ADD_ACCESSSPEC`<br>`DELETE_ACCESSSPEC`<br>`ENABLE_ACCESSSPEC`<br>`DISABLE_ACCESSSPEC`<br>`GET_ACCESSSPEC` | 显式管理设备上的 AccessSpec 资源。 |
| `raw send\|transact <hex> --yes` | `reader.Protocol.SendAsync`<br>`reader.Protocol.SendRawAsync`<br>`reader.Protocol.TransactRawAsync` | 任意自定义 Hex 帧 | 发送原始 LLRP 报文；发送后将 SDK 标记为 `IsManagedStateSynchronized = false`。 |
| `sync` | `reader.SynchronizeStateAsync()` | `GET_ROSPECS`<br>`GET_ACCESSSPEC` | 重新向设备拉取 ROSpec / AccessSpec 列表，恢复托管状态同步 (`IsManagedStateSynchronized = true`)。 |
| `monitor [seconds]` | `session.FrameObserver` | - (只读观测流) | 实时解包并彩色打印所有传输 (TX) 与接收 (RX) 的 LLRP 帧。 |
| `frames [count]` | `session.FrameObserver.CapturedFrames` | - (只读内存环形缓冲区) | 打印最近收发的 N 条 LLRP 帧历史日志。 |
| `inspect <hex>` | Protocol Header Decoder | - (离线分析) | 解析十六进制 LLRP 头部：Message Type, Length, Message ID, Spec Version。 |
| `decode <hex>` | Protocol Tree Decoder | - (离线分析) | 将十六进制 Hex 递归解包为包含所有 Parameter / Sub-parameter 的可读树状图。 |
| `validate <hex>` | Protocol Validator | - (离线分析) | 校验 Hex 数据包的长度、CRC 与结构完整性。 |
| `encode <msg-name>` | Protocol Encoder | - (离线构建) | 将消息名称及参数模板序列化为标准 LLRP 十六进制 Hex。 |
| `clear` / `cls` | Console Utility | - | 清屏并重新渲染 Studio Header。 |
| `help [command]` / `?` | Command Catalog | - | 动态展示可用命令列表及映射关系说明。 |
| `quit` / `exit` / `q` | Session Lifecycle | `CLOSE_CONNECTION` (若连通) | 退出交互式 Live Shell。 |

---

## 三、 典型业务序列的报文收发图解

### 1. `connect` 连接与双阶段初始化序列

```text
CLI                          SDK / LlrpReader                   LLRP Reader
 │                                   │                               │
 ├─ connect 192.168.1.100 ──────────►│                               │
 │                                   ├─ GET_SUPPORTED_VERSION ──────►│
 │                                   │◄─ GET_SUPPORTED_VERSION_RESP ─┤ (协商协议版本)
 │                                   ├─ GET_READER_CAPABILITIES ────►│ (Stage 1 Identity)
 │                                   │◄─ GET_READER_CAPABILITIES_RESP┤
 │                                   ├─ IMPINJ_ENABLE_EXTENSIONS ───►│ (激活 Impinj 扩展)
 │                                   │◄─ IMPINJ_ENABLE_EXTENSIONS_R ─┤
 │                                   ├─ GET_READER_CAPABILITIES(All)►│ (Stage 2 全量能力)
 │                                   │◄─ GET_READER_CAPABILITIES_RESP┤
 │◄─ [✔ Connected successfully] ─────┤                               │
```

### 2. `tag read` 临时 AccessSpec 读标签序列

```text
CLI                          SDK / LlrpReader                   LLRP Reader
 │                                   │                               │
 ├─ tag read E280... --bank 3 ──────►│                               │
 │                                   ├─ ADD_ACCESSSPEC (ID 24001) ──►│
 │                                   │◄─ ADD_ACCESSSPEC_RESPONSE ────┤
 │                                   ├─ ENABLE_ACCESSSPEC (24001) ──►│
 │                                   │◄─ ENABLE_ACCESSSPEC_RESPONSE ─┤
 │                                   │◄─ RO_ACCESS_REPORT (OpSpecRes)┤ (读取目标数据)
 │                                   ├─ DISABLE_ACCESSSPEC (24001) ─►│
 │                                   │◄─ DISABLE_ACCESSSPEC_RESPONSE┤
 │                                   ├─ DELETE_ACCESSSPEC (24001) ──►│
 │                                   │◄─ DELETE_ACCESSSPEC_RESPONSE ─┤
 │◄─ [打印读取到的 Memory Data] ─────┤                               │
```

### 3. `raw` 逃生透传与 `sync` 状态同步序列

```text
CLI                          SDK / LlrpReader                   LLRP Reader
 │                                   │                               │
 ├─ raw transact <HEX> ─────────────►│                               │
 │                                   ├─ [Transact Raw Frame] ───────►│
 │                                   │◄─ [Raw Response Frame] ───────┤
 │                                   │ (置 IsManagedStateSynchronized = false)
 ├─ sync ───────────────────────────►│                               │
 │                                   ├─ GET_ROSPECS ────────────────►│
 │                                   │◄─ GET_ROSPECS_RESPONSE ───────┤
 │                                   ├─ GET_ACCESSSPECS ────────────►│
 │                                   │◄─ GET_ACCESSSPECS_RESPONSE ───┤
 │                                   │ (重置 IsManagedStateSynchronized = true)
 │◄─ [✔ State synchronized] ────────┤                               │
```

---

## 四、 实施与对齐要求

1. **零私有逻辑校验**：`LlrpCli.Tests` 中需验证所有在线命令只能通过调用 `LlrpReader` 的公开方法或属性实现，禁止直接拼接字节发往 TCP 套接字。
2. **观测拦截无遗漏**：所有由 SDK 自动发送的控制报文（如 `IMPINJ_ENABLE_EXTENSIONS`）以及由应用发起的报文，必须完整经由 `FrameObserver` 记录，保证 `monitor` 与 `frames` 命令能精准抓取全量线缆报文。
