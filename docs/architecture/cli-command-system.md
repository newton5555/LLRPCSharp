# CLI 命令系统架构

> 基准日期：2026-07-28

## 已确定的边界

`LlrpCli` 有两个入口，但只有一个在线操作入口：

```text
llrp inspect | decode | validate | encode ──► 离线 Protocol Codec

llrp ──► Live Shell ──► LiveSessionContext ──► LlrpReader SDK ──► Reader
                              │
                              ├─ connect / disconnect / status / caps
                              ├─ settings / inventory / tag
                              ├─ rospec / accessspec / raw / sync
                              └─ monitor / frames
```

- 根 Spectre CLI 只注册无需设备连接的 `inspect`、`decode`、`validate`、`encode`。
- 运行 `llrp` 进入 Live Shell；所有连接、配置、盘点、标签访问、资源管理和帧监控都在当前会话中完成。
- `LiveSessionContext` 是在线状态的唯一所有者：Reader、Frame Observer、连接信息、托管资源状态及盘点草稿均不跨会话泄漏。
- 连接后的 Frame Observer 默认立即渲染全部非标签 TX/RX 报文；`RO_ACCESS_REPORT` 由 Live 标签聚合消费，只有显式 `monitor frames` 才按原始帧显示标签报告。这样协议调试不会遗漏控制、配置或异常报文，也不会让标签数据淹没终端。

这样避免“一次性命令自行创建 Reader”与 Live Shell 复用 Reader 两种生命周期并存而产生的参数、确认语义和清理行为漂移。

## 分层职责

| 层 | 责任 | 不负责 |
|---|---|---|
| 根 CLI | 离线编解码、参数校验、退出码 | 建立读写器连接或执行业务操作 |
| Live Shell | 交互输入、帮助、补全、会话生命周期、帧展示 | 复制 SDK 的协议业务逻辑 |
| Live Handler | 将命令输入转换为 SDK 请求，渲染结果与确认提示 | 直接编码标准/厂商报文 |
| `LlrpReader` | 连接协商、标准/厂商扩展、托管 ROSpec/AccessSpec、高层 API | 终端会话草稿与 UI 状态 |

## 命令与会话规则

- `connect` 建立一次连接并执行版本/厂商策略；`disconnect` 或 `exit` 负责停止后台任务并关闭会话。
- `settings`、`inventory`、`session`、`tag`、`rospec`、`accessspec`、`raw`、`sync` 都要求已连接。
- Live Shell 维护完整的 `DesiredSettings: ReaderSettings` 草稿及纯 CLI 来源元数据。草稿只能显式从 `settings draft defaults`（SDK 的 Reader Profile）、`from-reader`（设备事实）、`generic`（通用基线）或文件初始化；这些来源均不写设备。`inventory` 只启动、停止或显示读写器已部署的 Inventory；运行中的 `CurrentInventorySettings` 与设备查询结果都不自动覆盖草稿。`session inventory <file>` 只使用文件中的 Inventory 子域并保证结束时清理资源，适合临时示例工作负载。
- `tag` 操作复用当前 Reader；若没有托管盘点，SDK 负责其临时 ROSpec/AccessSpec 生命周期。任何写入、擦除、锁定、销毁及含此类操作的序列均须 `--yes`。
- `settings get|defaults|draft|export|validate|apply` 是唯一的高层设置入口：`get` 读取设备事实；`defaults` 通过 SDK 解析当前 Reader Profile；`draft` 管理 CLI 应用意图；`apply <path> --yes` 或 `draft apply --yes` 会按是否包含 Inventory 决定仅写配置或独占接管盘点资源。Apply 后资源保持 Disabled，只有 `inventory start` 会启动盘点。

## 自动化的后续边界

不要重新引入各个在线命令的一次性根入口。将来需要非交互自动化时，应设计独立的 `run <script-or-yaml>` 批处理宿主：它复用 Live Handler/SDK 请求模型，但显式定义连接、确认、超时和退出码策略。该能力尚未实现。
