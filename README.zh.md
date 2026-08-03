# LLRPCSharp

[English](README.md)

LLRPCSharp 是面向 RFID 读写器的 .NET LLRP 实现。项目按三层组织，使用者可以
根据目标直接选择协议层、托管 SDK 层或 CLI 操作层，不需要先了解整个仓库。

LLRPCSharp 是对传统 LTK.NET 思路的现代化改造：保留定义驱动的协议模型和线级
兼容性，同时将生成协议资产、Codec、传输、Reader 状态和应用工作流拆成可以
独立演进的层次。

## 1. LlrpNet：协议与网络基础层

`LlrpNet` 是底层 LLRP 协议与网络基础层，负责线级协议能力，并与面向应用的
Reader SDK 保持解耦。

适合以下场景：

- 直接处理强类型 LLRP Message 和 Parameter；
- 在不连接读写器的情况下编码、解码和检查原始协议帧；
- 通过 Codec Registry 组合标准协议和厂商协议；
- 实现协议 Adapter 或厂商协议扩展；
- 根据经过校验的 LTK XML/YAML 定义生成协议代码。

这一层的亮点是定义驱动的代码生成、明确的 Codec 注册机制，以及可以脱离
协议模型独立测试的传输/会话层。`LlrpNet.ProtocolModel` 负责定义校验；协议
生成器生成 Message、Parameter、Enum、Codec 和 Registry Module；
`LlrpNet.Protocol` 保存标准协议资产；`LlrpNet.Protocol.Impinj` 保存独立的
厂商线级协议资产。

`LlrpNet.Core` 提供 TCP 传输和事务基础。普通应用不需要直接依赖这一层，
优先使用 `LlrpSdk`。

## 2. LlrpSdk：托管 Reader SDK

`LlrpSdk` 是面向应用的 SDK。核心对象是 `LlrpSdk.LlrpReader`，它负责一个
读写器连接，并提供托管的 Reader 工作流：

- 连接并协商 LLRP 1.0.1 或 1.1；
- 查询和应用 `ReaderSettings`；
- 启动、观察、停止和清理托管盘点；
- 接收翻译后的 `TagReport`；
- 执行标准标签存储区读写等操作；
- 通过 `UseImpinj()` 使用厂商扩展。

普通应用可以从下面的托管流程开始：

```csharp
await using LlrpReader reader = LlrpReader.CreateBuilder("192.0.2.10")
    .Build();

await reader.ConnectAsync();
ReaderSettings settings = (await reader.GetDefaultSettingsAsync()).Settings;
await reader.ApplySettingsAsync(settings);

await using InventorySession session = await reader.StartInventoryAsync();
await foreach (TagReport report in session.ReadReportsAsync())
{
    Console.WriteLine(report.Epc);
}
```

大多数应用只需要托管 API。Raw Protocol、专家级 ROSpec/AccessSpec 服务和
Contributor 扩展接口仍然可用，但属于深入使用场景，放在独立文档中说明。

## 3. LlrpCli：读写器操作工具

`LlrpCli` 面向人工操作、脚本和 Agent。它复用同一套 `LlrpSdk` 托管工作流，
不会维护第二套 Reader 配置模型。

启动 Live Shell：

```powershell
dotnet run --project src/LlrpCli
```

### Live Shell 常用流程

```text
connect 192.0.2.10
settings edit --from generic
settings show draft
settings apply --yes
inventory start
inventory status
inventory stop
```

操作含义如下：

1. `connect <host>` 连接读写器并协商协议版本。
2. `settings edit` 创建或修改本地 Settings 草稿。
3. `settings show draft` 查看草稿，不写入读写器。
4. `settings apply --yes` 应用托管配置，应用后盘点保持停止。
5. `inventory start|status|stop` 控制和查看托管盘点。

Live Shell 中还可以执行：

```text
status
caps
tag read <epc> --bank user --word 0 --count 2
tag write <epc> --bank user --word 0 --data <hex-data> --yes
disconnect
```

Live Shell 是主要的交互入口。面向 Agent 和脚本的一次性 `inventory` 命令
复用相同的 Settings 和 SDK 流程：

```powershell
dotnet run --project src/LlrpCli -- inventory 192.0.2.10 --duration 10 --yes
```

`inspect`、`decode`、`validate`、`encode` 是不需要连接读写器的离线协议诊断
命令，属于辅助工具，不展开说明。

## 构建与测试

```powershell
dotnet build LLRPCSharp.slnx --no-restore
dotnet test LLRPCSharp.slnx --no-build
```

## 仓库结构

```text
definitions/   机器可读协议定义及扩展定义
docs/          状态、路线图、架构、ADR 和用户指南
references/    标准原文、抓包与旧项目参考
src/LlrpNet/   协议、传输、编解码和生成器项目
src/LlrpSdk/   托管 Reader SDK 和厂商扩展
src/LlrpCli/   Live Shell 与命令行工具
tests/         单元、集成与互操作测试
tools/         定义导入、生成、校验和测试辅助工具
```

## 文档入口

- [当前状态](docs/status.md)：已经实现的能力和已知缺口。
- [CLI 用户指南](docs/guides/cli-user-guide.md)：核心命令语法。
- [SDK API 指南](docs/guides/sdk-api-guide.md)：托管 SDK API 说明。
- [路线图](docs/roadmap.md)：计划工作和开发顺序。
- [架构说明](docs/architecture/overview.zh.md)：长期架构边界。
- [协议定义说明](definitions/README.md)：定义与代码生成流程。
