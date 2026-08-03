# LLRP C# SDK

[English](README.md)

[![LLRPCSharp Architecture and Capabilities](docs/images/llrpcsharp_infographic.png)](docs/showcase.zh.md)

面向 .NET 的现代化 LLRP SDK。项目以 `LlrpSdk.LlrpReader` 为应用层设备会话根对象，提供 LLRP 读写器连接、协议协商、资源管理、盘点、报文诊断和协议扩展能力。

当前真实实现状态见 [docs/status.md](docs/status.md)，下一步开发顺序见 [docs/roadmap.md](docs/roadmap.md)。长期架构边界见 [docs/architecture/overview.zh.md](docs/architecture/overview.zh.md)。

## 当前能力

- LLRP 1.0.1 与 1.1 的 SDK/CLI 基线。
- 自动协商 1.1，并可按策略强制 1.0.1 或 1.1。
- `LlrpReader` 连接状态机、能力初始化、Keepalive 自动应答、Raw/Typed Protocol 入口。
- 高层 `ReaderSettings`、轻量 Settings Builder、无副作用 `ValidateSettingsAsync`、`StartInventoryAsync` / `InventorySession`，以及连接级 `ReadTagReportsAsync` / `TagsReported` 观察 API。
- ROSpec 与 AccessSpec 进阶资源服务。
- `Microsoft.Extensions.Logging` 日志抽象和 `ILlrpFrameObserver` 原始 TX/RX 帧观测。
- LTK XML / YAML 协议定义导入、校验和 C# 源码生成链。
- Spectre.Console CLI，支持 Live Shell、面向 Agent 的一次性 `inventory`，以及离线 inspect/decode/encode。
- Impinj 扩展注册架构、`UseImpinj()` 入口与强类型 Codec 生成资产。
- 最小 1.0.1 Virtual Reader，用于能力查询和 ROSpec 生命周期测试。

## 快速开始

```powershell
dotnet build LLRPCSharp.slnx --no-restore
dotnet test LLRPCSharp.slnx --no-build
```

连接读写器：

```csharp
await using LlrpReader reader = LlrpReader.CreateBuilder("192.0.2.10")
    .WithLoggerFactory(loggerFactory)
    .WithFrameObserver(frameObserver)
    .Build();

await reader.ConnectAsync();
```

对收到高版本协商报文即断开的旧设备，可显式跳过自动探测：

```csharp
await using LlrpReader reader = LlrpReader.CreateBuilder("192.0.2.10")
    .WithProtocolVersionPolicy(LlrpProtocolVersionPolicy.Force101)
    .Build();

await reader.ConnectAsync();
```

CLI 对应选项：

```powershell
dotnet run --project src/LlrpCli
# 然后在 Live Shell 中输入：
connect 192.0.2.10 --llrp auto
monitor 30
```

面向 Agent 或脚本的一次性限时寻卡默认输出 JSON：

```powershell
dotnet run --project src/LlrpCli -- inventory 192.0.2.10 --duration 10 --yes
```

离线协议诊断不需要连接设备：

```powershell
dotnet run --project src/LlrpCli -- inspect "043E0000000A01020304"
dotnet run --project src/LlrpCli -- decode "043E0000000A01020304"
dotnet run --project src/LlrpCli -- encode get-rospecs --message-id 1
```

## 目录

```text
definitions/   机器可读协议定义及扩展定义
docs/          状态、路线图、架构、决策记录和资料来源
references/    标准原文、抓包与旧项目参考（大部分不提交 Git）
src/           产品源码
tests/         单元、集成与互操作测试
tools/         定义导入、生成、校验和测试辅助工具
```

## 文档入口

- [当前状态](docs/status.md)：已经实现、尚未实现、当前阻塞。
- [路线图](docs/roadmap.md)：下一步开发顺序。
- [架构与能力图谱](docs/showcase.zh.md)：项目架构、能力边界和展示图。
- [文档索引](docs/README.md)：架构、ADR、资料来源。
- [Agent Guide](AGENTS.md)：给自动化 coding agent 的仓库规则。
- [协议定义说明](definitions/README.md)：XML/YAML 定义与生成命令。
