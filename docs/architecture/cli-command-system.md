# CLI 命令系统架构

> 基准日期：2026-08-03

## 已确定的边界

`LlrpCli` 有三个入口，两个在线入口共用同一业务工作流：

```text
llrp inspect | decode | validate | encode ──► 离线 Protocol Codec

llrp inventory <host> ──► InventoryCommand ──┐
                                   ├─► ManagedSettingsWorkflow ──► LlrpReader SDK ──► Reader
llrp ──► Live Shell ──► Handler ───┘

Live Shell ──► LiveSessionContext
                              │
                              ├─ connect / disconnect / status / caps
                              ├─ settings / inventory / tag
                              ├─ rospec / accessspec / raw / sync
                              └─ monitor / frames
```

- 根 Spectre CLI 注册一次性在线 `inventory` 和离线 `inspect`、`decode`、`validate`、`encode`。
- 运行 `llrp` 进入 Live Shell；所有连接、配置、盘点、标签访问、资源管理和帧监控都在当前会话中完成。
- 根级 `inventory` 是面向 Agent 和脚本的有界工作负载：连接、加载 Defaults 或 Settings 文件、校验、Apply、盘点、Stop 和 Clear。它不复制 Settings 业务逻辑；Live Shell 里的 `inventory start|stop|status` 仍管理当前连接。
- `LiveSessionContext` 是在线状态的唯一所有者：Reader、Frame Observer、连接信息、托管资源状态及盘点草稿均不跨会话泄漏。
- 连接后的 Frame Observer 默认立即渲染全部非标签 TX/RX 报文；`RO_ACCESS_REPORT` 由 Live 标签聚合消费（盘点期间通过 `InventorySession.ReadReportsAsync()`），只有显式 `monitor frames` 才按原始帧显示标签报告。SDK 报告出口互斥，CLI 不会同时排空 Session 和 Reader 级报告流。

`ManagedSettingsWorkflow` 统一 Settings 的来源解析、序列化、校验和 Apply。Live Handler 与根级 `inventory` 只负责各自的输入、连接生命周期和输出格式，避免两条实现随版本漂移。

## 分层职责

| 层 | 责任 | 不负责 |
|---|---|---|
| 根 CLI | 离线编解码；编排一次性 `inventory` 的连接、超时、结构化输出与退出码 | 实现 Settings 校验、协议编译或资源业务规则 |
| Live Shell | 交互输入、帮助、补全、会话生命周期、帧展示 | 复制 SDK 的协议业务逻辑 |
| Live Handler | 将命令输入转换为 SDK 请求，渲染结果与确认提示 | 直接编码标准/厂商报文 |
| `LlrpReader` | 连接协商、标准/厂商扩展、托管 ROSpec/AccessSpec、托管 Reader API | 终端会话草稿与 UI 状态 |

## 命令与会话规则

- `connect` 建立一次连接并执行版本/厂商策略；`disconnect` 或 `exit` 负责停止后台任务并关闭会话。
- `settings`、`inventory`、`tag`、`rospec`、`accessspec`、`raw`、`sync` 都要求 Live Shell 已连接。
- Live Shell 维护可空的 `SettingsDraft: ReaderSettings?` 及纯 CLI 来源元数据。草稿只由 `settings edit` 或 `settings load` 建立，由 `settings discard` 清除；运行中的 Reader Settings 不自动覆盖草稿。`inventory` 只启动、停止或显示 Reader 已部署的 Inventory。
- `tag` 操作复用当前 Reader；若没有托管盘点，SDK 负责其临时 ROSpec/AccessSpec 生命周期。任何写入、擦除、锁定、销毁及含此类操作的序列均须 `--yes`。
- `settings show|edit|validate|apply|load|save|discard` 是稳定的托管设置入口。只有 `apply [file] --yes` 写设备；Apply 后 Inventory 保持 Disabled，只有 `inventory start` 启动盘点。Raw/手工资源操作后，带 Inventory 的 Apply 或 defaults apply 可直接强制接管；`sync` 只用于查询并采用设备现状。
- 根级 `inventory` 默认使用当前 Reader 的 SDK Defaults，也可加载完整 Settings 文件；它强制立即启动并按 duration 结束。因为 Apply 会接管 Inventory 资源，必须使用 `--yes`。结束时清除托管 Inventory 资源，但不回滚 Reader 全局 Configuration。

## 自动化的后续边界

不要为每个 Live 命令增加对应的一次性根入口。自动化的稳定入口保持为有界根级 `inventory`；将来若需要多步骤自动化，再设计独立的 `run <script-or-yaml>` 批处理宿主，并继续复用 SDK 与共享工作流。
