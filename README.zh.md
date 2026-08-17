# LLRPCSharp

[English](README.md)

![.NET 10.0](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square&logo=dotnet)
![C# 14](https://img.shields.io/badge/C%23-14.0-239120?style=flat-square&logo=c-sharp)
![Build & Tests](https://img.shields.io/badge/Build%20%26%20Tests-486%20Passed-10b981?style=flat-square)
![Protocol](https://img.shields.io/badge/LLRP-1.0.1%20%7C%201.1%20%7C%202.0-3b82f6?style=flat-square)
![License](https://img.shields.io/badge/License-MIT-blue?style=flat-square)

**LLRPCSharp** 是基于 **.NET 10.0 / C# 14** 构建的 RFID 超高频 (UHF) 读写器 LLRP 协议开发包与命令行工具。

项目分为两层核心定位：
* **`LlrpNet`**：传统 **LTK.NET** 的现代化改造，负责 LLRP 1.0.1 / 1.1 / 2.0 协议编解码、类型定义与 TCP 异步传输。
* **`LlrpSdk`**：参考 **Impinj Octane SDK** 理念重新实现的托管 API，屏蔽底层 `ROSpec` 与 `AccessSpec` 复杂细节，面向业务应用提供连接、配置管理与标签上报流。

---

## 🏛️ 系统架构

![LLRPCSharp 系统架构图](docs/images/architecture.zh.svg)

---

## ⚡ 快速开始

### 1. 基础盘点示例 (获取并打印推荐默认配置)

```csharp
using LlrpSdk;

// 1. 创建并连接读写器
await using var reader = LlrpReader.CreateBuilder("192.168.1.100").Build();
await reader.ConnectAsync();

// 2. 查询设备推荐默认配置，打印配置信息并下发到读写器
ReaderSettingsDefaults defaults = await reader.GetDefaultSettingsAsync();
Console.WriteLine($"已加载设备默认配置 Profile: {defaults.ProfileId}");
Console.WriteLine($"默认盘点天线列表: {string.Join(", ", defaults.Settings.Inventory.AntennaIds)} (0代表全部天线)");

await reader.ApplySettingsAsync(defaults.Settings);

// 3. 启动盘点并消费标签数据
await using var session = await reader.StartInventoryAsync();
await foreach (TagReport tag in session.ReadReportsAsync())
{
    Console.WriteLine($"[天线 {tag.AntennaId}] EPC: {tag.EpcHex} | RSSI: {tag.PeakRssi} dBm");
}
```

### 2. 自定义配置 (参数 Mode / 功率索引由 `reader.Capabilities` 查询)

```csharp
using LlrpSdk;
using LlrpSdk.Extensions.Impinj;

await using var reader = LlrpReader.CreateBuilder("192.168.1.100")
    .UseImpinj()
    .Build();

await reader.ConnectAsync();

// 注：ModeIndex 与功率 Index 的具体速率/dBm 物理含义均存储在 reader.Capabilities 表中
ReaderSettings customSettings = ReaderSettings.Create(builder => builder
    .Inventory(inv => inv
        .Antennas(1, 2)
        .Mode(modeIndex: 1000)      // RF Mode 索引 (可通过 reader.Capabilities.RfModes 查询对应速率)
        .Session(2)                 // Gen2 Session (0~3)
        .Population(128)            // 预计标签数量
        .ReportEveryTag()
        .Impinj(imp => imp.IncludeSerializedTid())));

await reader.ApplySettingsAsync(customSettings);

await using var session = await reader.StartInventoryAsync();
await foreach (TagReport tag in session.ReadReportsAsync())
{
    Console.WriteLine($"EPC: {tag.EpcHex}, TID: {tag.GetSerializedTidHex()}");
}
```

---

## 🛠️ CLI 调试工具 (`LlrpCli`)

提供交互终端与单行脚本命令，便于现场设备调试与状态检查：

```powershell
# 启动交互终端 (Live Shell)
dotnet run --project src/LlrpCli
```

```text
connect 192.168.1.100   # 连接读写器
status                  # 查看当前状态与协议版本
caps                    # 查询读写器天线数量、功率表 (TransmitPowerIndex) 与 RF 模式表 (ModeIndex)
settings edit           # 交互式编辑配置并应用到读写器
settings show           # 查看当前读写器已应用的配置
inventory start         # 启动标签盘点
inventory stop          # 停止盘点
```

单行自动化脚本命令：

```powershell
# 执行 10 秒盘点并输出 JSON 标签数据
dotnet run --project src/LlrpCli -- inventory 192.168.1.100 --duration 10 --yes
```

### 报文级 Virtual Reader

启动真实 TCP LLRP 设备端点，用于 SDK/CLI 开发和无硬件 CI：

```powershell
dotnet run --project src/LlrpCli/LlrpCli.csproj -- virtual-reader `
  --port 5085 --llrp 1.1 --name ci-reader
```

显式加载版本化的本地读写器/寻卡预设：

```powershell
dotnet run --project src/LlrpCli/LlrpCli.csproj -- virtual-reader `
  --config config/virtual-readers.example.json --validate-config
dotnet run --project src/LlrpCli/LlrpCli.csproj -- virtual-reader `
  --config config/virtual-readers.example.json --instance reader-local-1
```

Virtual Reader 由通用 `LlrpDevice.Server` 和实现 `ILlrpDevice` 的
`VirtualLlrpDevice` 组合而成，提供确定性的 `static`、`moving-tags`、`noisy` 标签观察
场景。配置只会在显式命令下加载；进程重启后不会自动恢复活跃的 LLRP 资源。

独立 Manager 还提供注册式预设与进程内多实例生命周期 API，详见
[Virtual Reader Manager 指南](docs/guides/virtual-reader-manager.md)。

### 单台虚拟设备 SDK 与 CLI

设备端 SDK 通过 `LlrpDevice.Virtual.Hosting` 提供
`IVirtualLlrpDeviceHost`，这是单台虚拟设备的公开入口，内部组合一台
`VirtualLlrpDevice` 和一台 `LlrpDeviceServer`：

```powershell
dotnet run --project src/LlrpVirtualDevice.Cli/LlrpVirtualDevice.Cli.csproj -- `
  run --config config/virtual-device.example.json
```

独立的 `LlrpVirtualDevice.Cli` 与通用 `LlrpCli` 在 `src` 下平级。直接启动
且不带参数时进入交互 Shell，此时不会自动创建设备。使用
`server create`、`server start`、`server status`、`server stop`、
`server restart` 和 `server destroy` 管理一台设备；使用
`logs on|off|status` 控制生命周期、客户端以及解码后的 `RX`/`TX` 事件输出。
使用 `validate --config <PATH>` 校验单设备 JSON，或使用 `presets` 查看内置
预设。未来 UI 可以直接引用同一个 SDK 门面，不依赖 CLI 或兼容 Manager。

如果希望自动创建并启动设备后进入交互 Shell，可以使用 `live`：

```powershell
dotnet run --project src/LlrpVirtualDevice.Cli/LlrpVirtualDevice.Cli.csproj -- `
  live --config config/virtual-device.example.json
```

`live` 会进入同一个 Shell，并默认打开事件输出；`run` 则是安静的、非交互
前台服务模式。设备 CLI 只观察 LLRP 客户端产生的流量，不会自己充当第二个
客户端自动生成请求。

---

## 📋 协议与厂商支持

| 协议 / 厂商 | 支持状态 | 说明 |
| :--- | :--- | :--- |
| **LLRP 1.0.1** | 可用（实机验证） | 覆盖完整 SDK、CLI、虚拟读写器及标准 ROSpec / AccessSpec 操作 |
| **LLRP 1.1** | 可用基线 | 支持协议版本自动协商与 `Llrp11ProtocolAdapter` 适配器基线 |
| **LLRP 2.0** | 协议+适配器基线 | 已生成 `V2_0` 协议资产与 `Llrp20ProtocolAdapter`，支持协商接入；待实机验收 |
| **Impinj 扩展** | 主线可用 | 提供强类型 `UseImpinj()` 管道与 Contributor 模型（TID、相位、RSSI），R420/R430 实测通过 |
| **Zebra 扩展** | 扩展基线 | 提供线协议包与 `UseZebra()` 扩展，FX9600 实测通过能力参数与相位/Brand-ID 投影 |

---

## 📁 目录结构

```text
src/
  LlrpSdk/    托管高层 API (参考 Impinj Octane SDK)
  LlrpNet/    协议编解码与 TCP 传输 (LTK.NET 现代化实现)
  LlrpCli/    通用客户端命令行工具与 Live Shell
  LlrpVirtualDevice.Cli/  单台虚拟 LLRP 设备服务 CLI
docs/
  guides/     SDK、CLI 与 Virtual Device 指南
```

---

## 📚 用户指南

* [SDK API 使用指南](docs/guides/sdk-api-guide.md)
* [CLI 命令行工具指南](docs/guides/cli-user-guide.md)

---

## 📄 开源协议

[MIT License](LICENSE)
