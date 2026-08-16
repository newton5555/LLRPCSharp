# 路线图

> 基准日期：2026-08-17

本文档只保留未完成事项；当前实现事实见 [status.md](status.md)。

## 已交付基线

- LLRP 1.0.1/1.1 的 SDK、CLI、协议 Codec 和设备互操作基线。
- LLRP 2.0 协议层与 SDK Adapter 基线；真实设备验收仍未完成。
- 报文级 Virtual Reader Core：共享 `LlrpNet` accepted-TCP transport、Session、Codec
  Registry 和 Frame Observer；显式 1.0.1/1.1 版本 profile；初始化、Reader Event、能力/
  配置、ROSpec、AccessSpec、KEEPALIVE、TagReport、标准 C1G2 读写和确定性故障注入。
- Virtual Reader Manager：注册式 `PresetCatalog`、标准/严格/Tag Access/1.1 预设、故障
  预设（丢响应、状态错误、主动断开），以及进程内多实例
  `create/start/stop/restart/delete/list/status` API。
- `LlrpCli virtual-reader` 与独立 Manager 入口；兼容的 `src/LlrpVirtualReader` 启动入口
  保留。
- `LlrpVirtualReader.Core.Tests`、`LlrpVirtualReader.Manager.Tests` 和 `Interop.Tests` 的
  endpoint、生命周期、版本协商、报告、扩展 Handler、SDK 端到端和故障场景覆盖。

## 近期

1. 完成 LLRP 1.0.1 目标设备的最终实机验收，并补充失败场景记录。
2. 补充 LLRP 1.1 目标 Reader 的实机互操作验收，覆盖版本协商、标准 Settings、Inventory、
   TagReport 和 Tag Access，并记录具体型号/固件证据。
3. 体系化扩充 `LlrpSdk.Hardware.Tests`：多天线配置、真实标签 Memory Bank 读写、高并发稳定性
   和厂商扩展字段校验。
4. 根据真实设备证据扩充 Impinj/Zebra 及其他型号/固件能力目录；继续标定 Zebra 定义中与
   固件抓包不一致的 reserved 位和字段宽度。
5. 继续完善 CLI Settings 编辑器和 Agent 输出能力，保持已有命令语义稳定。
6. 继续完善 2.0 Adapter，并对支持的真实设备做互操作验收。

## Virtual Reader 后续工作

本专项决策来源为 [ADR 0006](adr/0006-preset-driven-virtual-reader-manager.md)。它只负责
报文级 TCP/LLRP 虚拟设备；`LlrpReaderPlatform` 的进程内 Session 替身仍属于平台仓库，
不与本运行时共享依赖。

### 当前边界

```text
LlrpVirtualReader.Manager
  ├─ Preset Catalog + instance identity/lifecycle
  └─ one VirtualReaderHost per instance
       ├─ accepted TCP + LlrpSession
       ├─ version-explicit Codec/Handler dispatch
       ├─ canonical ROSpec/AccessSpec/device state
       └─ configurable reports, tag memory, events, faults
```

Core 只维护一个虚拟 Reader 的设备状态和报文行为；Manager 负责实例目录和多 Host 编排。
端口只表示监听位置，绑定失败不会自动换端口。普通客户端不需要 Virtual Reader 专用分支。

### 尚未完成

- 跨进程实例持久化、恢复和结构化存储；当前 Manager 目录只在进程内有效。
- 更完整的 Reader 配置快照、动态配方编辑和全部标准/厂商消息覆盖；当前 Core 明确覆盖
  SDK 互操作所需的标准闭环。
- 延迟、吞吐、连接重置等更多一次性故障预设，以及 CLI/Manager 的帧日志导出和运行摘要。
- `LlrpVirtualReader.Extensions.Impinj`、`LlrpVirtualReader.Extensions.Zebra` 等厂商设备端
  模块；接口已经落地，但必须有协议资料或真机抓包证据后才能发布预设。
- LLRP 2.0 Virtual Reader 的独立协议验收；Core 保留 V2 Codec/translation path，但目前
  Manager 只公开经过测试的 1.0.1/1.1 预设。
- Reader Studio 图形化项目；不是当前 SDK 仓库阶段目标。

### 约束

- 新的托管能力优先进入 `LlrpSdk`，CLI 只负责输入、展示和流程编排。
- Virtual Reader Manager 只暴露经过自动化验收的稳定 `PresetId`；新增预设必须带专项测试。
- 多 Host 的创建、启停、重启、删除和状态编排只属于 Manager；Core 不维护实例目录。
- 报文设备端 Core 不依赖客户端 `LlrpSdk`；厂商虚拟模块不依赖客户端厂商扩展包。
- 生成协议代码只能通过 `definitions/`、生成器或生成脚本修改，禁止手工编辑 `.g.cs`。
