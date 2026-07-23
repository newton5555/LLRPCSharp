# LLRP C# SDK

面向 .NET 的现代化 LLRP SDK。项目以 `LlrpSdk.LlrpReader` 为应用层设备会话根对象，逐步支持 LLRP 1.0.1、1.1、2.0，以及厂商和客户自定义扩展。

当前已完成 M1 协议运行时和 M2 Reader 会话基础，正在推进 M3 核心盘点路径与 M5 协议定义/生成链。整体目标、模块边界、兼容策略和里程碑见 [项目整体规划](docs/LLRP-CSharp-SDK-整体规划.md)。

## 当前实现

M1 协议运行时已经落地：

- 网络大端序 Buffer Reader/Writer；
- MSB-first Bit Reader/Writer；
- LLRP 1.0.1/1.1/2.0 公共消息头；
- TCP 半包、粘包和多段缓冲区的流式帧切分；
- TLV/TV 参数头及边界校验；
- 并发事务匹配、超时/取消、连接代次与断线传播；
- 标准、未知和 Custom Message/Parameter Registry；
- 1.0.1 Keepalive、Capabilities、LLRPStatus、ERROR_MESSAGE 与 ROSpec 管理消息 Codec；
- `LlrpReader` Builder、状态机、能力初始化、Typed/Raw Protocol 入口与 Keepalive 自动应答；
- `reader.RoSpecs` Add/Delete/Enable/Disable/Start/Stop/GetAll 进阶资源服务；
- LTK XML → ProtocolModel Core/Custom 导入、定义校验与 C# Generator 基础；
- Spectre.Console 交互式 CLI Live Shell（支持 `connect`、`status`、`caps`、`rospec`、`frames` 诊断及报文树状分析）。

日志通过 `Microsoft.Extensions.Logging` 抽象注入；完整 TX/RX 报文通过 `ILlrpFrameObserver` 注入。两者均位于底层 Transport/Session，因此使用 `LlrpReader` 高级 API 时仍能打印或采集底层报文。诊断边界见 [ADR 0001](docs/decisions/0001-structured-logging-and-frame-observation.md)。

```csharp
await using LlrpReader reader = LlrpReader.CreateBuilder("192.0.2.10")
    .WithLoggerFactory(loggerFactory)
    .WithFrameObserver(frameObserver)
    .Build();

await reader.ConnectAsync();
```

离线协议诊断不需要连接设备：

```powershell
dotnet run --project src/LlrpCli -- inspect "043E0000000A01020304"
dotnet run --project src/LlrpCli -- decode "043E0000000A01020304"
dotnet run --project src/LlrpCli -- encode get-rospecs --message-id 1
```

## 手写与代码生成边界

本 SDK 明确区分**手写核心架构**与**基于协议定义自动生成的源码**：

- **手写核心架构（Hand-written Core）**：
  - `LlrpSdk.LlrpReader`：设备会话状态机、能力初始化与高级服务。
  - `LlrpNet.Core`：网络大端序 IO、TCP 流式帧切分、`LlrpSession` 事务匹配与 `ILlrpFrameObserver` 报文监听。
  - `LlrpNet.ProtocolModel` & `LlrpNet.ProtocolGenerator`：包含 `LtkXmlDefinitionImporter` 定义导入器与代码生成器。
  - `LlrpCli`：基于 Spectre.Console 的终端 Shell、智能提示链与深层 LLRP 报文树状分析器。

- **自动生成源码（Generated Code via LTK XML）**：
  - `LlrpNet.Protocol` 中的 Message（消息）、Parameter（参数）、Enum（枚举）强类型 C# 类及其二进制 Codec 编解码器（由 `definitions/imports/xml/` 导入官方及厂商描述自动生成）。

## 目录

```text
definitions/   机器可读协议定义及扩展定义
docs/          架构、决策记录和项目计划
references/    标准原文、抓包与旧项目参考（大部分不提交 Git）
samples/       SDK 使用示例
src/           产品源码
testdata/      可提交的清洗后测试帧、场景和期望结果
tests/         单元、集成与互操作测试
tools/         定义导入、生成、校验和测试辅助工具
```

## 当前下一步

1. 完成 M3 所需的 ROSpec 参数图、Reader Settings 和 `RO_ACCESS_REPORT`/TagData 管道。
2. 将 C# 类型、Codec 与 Registry Generator 正式接入 1.0.1 生成回归。
3. 扩展 CLI 在线连接、盘点和进阶资源命令，全部复用 `LlrpReader` 服务。
4. 补充已脱敏真机帧、设备型号、固件及兼容问题。
5. 在确认授权前，仅本地使用 proprietary 厂商定义，不提交或再分发其内容。
