# LLRPCSharp

[English](README.md)

![.NET 10.0](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square&logo=dotnet)
![C# 14](https://img.shields.io/badge/C%23-14.0-239120?style=flat-square&logo=c-sharp)
![Build & Tests](https://img.shields.io/badge/Build%20%26%20Tests-523%20Passed-10b981?style=flat-square)
![Protocol](https://img.shields.io/badge/LLRP-1.0.1%20%7C%201.1%20%7C%202.0-3b82f6?style=flat-square)

**LLRPCSharp** 是基于 .NET 10 / C# 14 的 RFID 读写器开发包。项目的主产品是客户端 LlrpSdk：为业务应用提供连接 LLRP 读写器、读取能力与设置、配置盘点、消费标签报告以及执行标准 C1G2 标签访问的托管 API。

仓库同时包含协议基础层和设备端虚拟读写器运行时。虚拟设备的完整说明放在本文末尾。

## 客户端 SDK

* **LlrpSdk** 是普通应用应使用的 API。LlrpReader 提供连接状态、ReaderCapabilities、ReaderSettings、InventorySession、标签报告和 Tag Access，不要求应用手工组装 ROSpec 或 AccessSpec 报文。
* **厂商扩展项目**提供强类型行为。Impinj 扩展提供 UseImpinj()、能力映射、盘点扩展和报告投影。
* **LlrpNet** 是协议层，包含 LLRP 1.0.1/1.1/2.0 生成类型、编解码器、注册表和异步 TCP 传输。需要精确控制线上的报文时，可使用 reader.Protocol 或直接使用 LlrpNet。
* **LlrpCli** 是基于该 SDK 构建的第一个客户端应用，遵循相同的连接、Settings、盘点、Tag Access 和 Raw 协议工作流。

客户端通常按以下流程工作：连接并协商版本；读取能力与设置；获取并编辑 ReaderSettings 默认值；应用设置；启动 InventorySession；消费 ReadReportsAsync；按需调用高层 Tag Access。

高层盘点会独占 SDK 保留的 ROSpec/AccessSpec 资源域。精确资源 ID、手动资源和未封装的厂商报文属于专家接口 RoSpecs、AccessSpecs 和 Protocol。Raw 或专家写操作会使托管状态失效；回到高层控制前应先同步，或显式执行一次新的高层接管。

### 对外 SDK 示例：LlrpSdk 客户端

```csharp
await using var reader = LlrpReader.CreateBuilder("192.168.1.100").Build();
await reader.ConnectAsync();
ReaderSettingsDefaults defaults = await reader.GetDefaultSettingsAsync();
await reader.ApplySettingsAsync(defaults.Settings); // 下发但不启动
await using InventorySession session = await reader.StartInventoryAsync();
await foreach (TagReport tag in session.ReadReportsAsync())
    Console.WriteLine(tag.EpcHex);
```

### Settings 与报告

* QuerySettingsAsync 在存在时识别 SDK 保留 ROSpec（14150）；其他手动资源保持专家数据。
* InventorySession.ReadReportsAsync 是会话独立报告流。连接级 TagsReported/ReadTagReportsAsync 是另一种观察方式，冲突的消费者会被拒绝。
* StopAsync 会移除 SDK 托管盘点资源。应用层持有的 Settings 仍可再次应用；读写器不会替应用保存草稿。
* Inventory = null 的 ApplySettingsAsync 只修改读写器全局配置。带 Inventory 时会接管资源域、重建 SDK ROSpec 和可选 AttachedData AccessSpec，并保持 ROSpec 为 Disabled。StartInventoryAsync(settings) 是一次部署并启动的入口。

---

## 系统架构

![LLRPCSharp 系统架构图](docs/images/architecture.zh.svg)

客户端侧是主要 SDK 使用面。设备端是独立的进程内虚拟读写器，用于测试、演示、UI 开发和外部客户端互操作。

    客户端侧
      LlrpSdk + LlrpSdk.Extensions.*  托管读写器 SDK
      LlrpNet                      协议编解码与传输
      LlrpCli                      客户端 CLI

    设备端
      LlrpDevice.Virtual.Hosting   虚拟设备 SDK 门面
      LlrpVirtualDevice.Cli        虚拟设备 CLI

---

## 客户端 CLI（LlrpCli）

LlrpCli 是客户端 SDK 的示例应用，不是虚拟设备服务端。启动命令是 dotnet run --project src/LlrpCli。典型命令包括 connect HOST、caps、settings show、settings edit --from defaults、settings apply --defaults --yes、inventory start --monitor live、inventory stop 和 raw send。客户端 CLI 与 LlrpSdk 使用同样的资源接管和报告流规则。

---

## 协议与厂商支持

| 协议 / 厂商 | 支持状态 | 说明 |
| :--- | :--- | :--- |
| **LLRP 1.0.1** | 可用 | 主客户端 SDK 路径和标准虚拟设备路径 |
| **LLRP 1.1** | SDK 基线 | 明确版本适配器和生成类型；更广泛实机覆盖待补充 |
| **LLRP 2.0** | 协议基线 | 已生成 V2_0 资产和适配器；实机验证待完成 |
| **Impinj 扩展** | 主线可用 | UseImpinj() 管道、强类型能力、盘点/报告扩展和 R420 路径 |
| **Zebra 扩展** | 扩展基线 | 线协议包和 UseZebra() 管道，部分投影已验证 |

---

## 仓库结构

下面的结构以客户端 SDK 为主；虚拟设备 SDK 与 CLI 是独立的设备端消费者，不是额外的客户端 API：

    src/
      LlrpSdk/                    主要托管客户端 SDK
      LlrpSdk.Extensions.Impinj/  Impinj 客户端扩展
      LlrpSdk.Extensions.Zebra/   Zebra 客户端扩展
      LlrpNet/                    协议模型、编解码、注册表与 TCP 传输
      LlrpCli/                    客户端交互式/脚本式 CLI

      LlrpDevice.Abstractions/    设备端合同
      LlrpDevice.Server/           通用 LLRP 设备端服务
      LlrpDevice.Virtual/          确定性的虚拟读写器
      LlrpDevice.Virtual.Hosting/  对外的虚拟设备 SDK 门面
      LlrpVirtualDevice.Cli/       虚拟设备服务 CLI

    docs/                         SDK、CLI 与虚拟设备指南
    tests/                        协议、SDK、虚拟设备、互操作和硬件测试

---

## 虚拟设备 SDK 与 CLI（设备端）

虚拟设备用于创建一个表现为读写器的 LLRP 端点，适合 SDK 开发、CI、协议检查、UI 开发和外部客户端互操作。它不是第二套客户端 API，不替代真实读写器，也不模拟真实 RF 波形。

公开入口是 LlrpDevice.Virtual.Hosting：

* IVirtualDeviceHost 管理一个端点，提供启动、停止、重启、端点信息、客户端连接、生命周期事件和解码报文事件。
* VirtualDeviceHostOptions 选择协议/profile、监听参数、报告节奏、确定性寻卡数据、RF 可观察场景以及宽松或严格的 ROSpec 生命周期校验。
* VirtualLlrpDevice 提供确定性的标签内存、标准 Tag Access、GPI/GPO 状态以及 static/moving/noisy 可观察行为。
* LlrpDevice.Server 负责 TCP、LLRP 版本分派、ROSpec/AccessSpec 状态、事件和报告投递。

默认 profile 是 llrp1.0.1_standard。内置 impinj.r420.llrp-1.0.1 profile 增加采集的 Impinj capability/configuration 参数和 Impinj 控制报文模块。启动前可以通过 VirtualDeviceHostOptions.Inventory 注入标签。

### 对外 SDK 示例：LlrpDevice.Virtual.Hosting

这是设备端 SDK，负责创建 LLRP 端点；外部的 LlrpSdk、LlrpCli、ItemTest 或其他 LLRP 客户端连接该端点。

```csharp
using LlrpDevice.Virtual.Hosting;

await using IVirtualDeviceHost host = VirtualLlrpDeviceHost.Create(
    new VirtualDeviceHostOptions
    {
        ProfileId = VirtualDeviceProfiles.Standard101Id,
        Port = 0,
        Inventory = new VirtualInventoryOptions
        {
            Tags =
            [
                new VirtualInventoryTag
                {
                    ElectronicProductCode = Convert.FromHexString("E28011710000020D056E9BEE")
                }
            ]
        }
    });

await host.StartAsync();
Console.WriteLine($"LLRP 端点：127.0.0.1:{host.BoundPort}");
// 客户端连接 host.BoundPort；使用完成后停止虚拟读写器。
await host.StopAsync();
```

独立设备端 CLI 使用同一个门面，生命周期命令为 server create --llrp 1.0.1、server start、server status、logs on、server stop 和 server destroy。run 用于启动配置好的前台服务，live 用于启动设备后进入交互 Shell。

虚拟设备 CLI 只观察外部 LLRP 客户端产生的请求。Impinj R420 profile 已验证可以接受 Impinj ItemTest 2.10.0 的 LLRP 客户端连接，并在 LLRP 1.0.1 下进入盘点启动路径。ItemTest 的非 LLRP 功能，包括 mDNS 发现和 rshell/SSH 服务，不属于本项目范围。

实现细节见 docs/guides/virtual-device-cli.md 和 docs/architecture/overview.md。

---

## 用户指南

* SDK API 使用指南：docs/guides/sdk-api-guide.md
* 客户端 CLI 使用指南：docs/guides/cli-user-guide.md
* Virtual Device SDK 与 CLI：docs/guides/virtual-device-cli.md

## 开源协议

[MIT License](LICENSE)
