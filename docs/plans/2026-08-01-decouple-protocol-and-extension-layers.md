# 架构解耦规划：底层协议扩展层与高层 SDK 插件层拆分

> 日期：2026-08-01  
> 状态：已实现（2026-08-03，dev 分支）
> 目标：解耦 `LlrpNet.Protocol`（协议层）与 `LlrpSdk`（托管层）对厂商扩展（以 Impinj 为代表）的物理依赖，建立可复用的协议引擎分层规范。

---

## 1. 背景与痛点分析

在当前项目结构中，Impinj 厂商扩展存放于 `src/LlrpSdk.Extensions.Impinj/`，内部混合了两类不同层次的代码：

1. **协议层代码**（由工具自动生成）：`Parameters/*.g.cs`、`Messages/*.g.cs`、`Codecs/*.g.cs`、`ImpinjProtocolModule.cs` 等纯二进制编解码与报文对象。
2. **SDK 托管层代码**（手写）：`ImpinjReaderExtension.cs`、`ImpinjReaderConfiguration.cs`、`ImpinjInventoryReportOptions.cs` 等高层门面模型与 Contributor 管道。

### 存在的问题
`LlrpSdk.Extensions.Impinj.csproj` 当前不得不强制引用 `LlrpSdk.csproj`。
结果导致：如果开发者或第三方应用只想使用底层协议引擎 (`LlrpNet.Protocol`) 对 Impinj 原始 Raw 报文进行编解码与收发，**也不得不被迫引用高层的 `LlrpSdk`**，破坏了底层协议引擎的独立性与纯洁性。

---

## 2. 目标架构方案

将厂商扩展彻底解耦为上下两层物理项目：

```text
┌───────────────────────────────────────────────────────────────────────────┐
│ 2. SDK 扩展插件层: LlrpSdk.Extensions.Impinj                             │
│    - 物理位置: src/LlrpSdk.Extensions.Impinj/                             │
│    - 包含内容: ImpinjReaderExtension, ImpinjReaderConfiguration,          │
│                Contributor 管道、带 Profile 来源的强类型配置映射           │
│    - 项目依赖: 引用 LlrpNet.Protocol.Impinj + LlrpSdk                       │
├───────────────────────────────────────────────────────────────────────────┤
│ 1. 协议扩展层: LlrpNet.Protocol.Impinj (独立协议引擎扩展)                  │
│    - 物理位置: src/LlrpNet/LlrpNet.Protocol.Impinj/                       │
│    - 包含内容: 生成的 Impinj 报文/参数 Codecs (*.g.cs)、ImpinjProtocolModule │
│    - 项目依赖: 仅引用 LlrpNet.Protocol + LlrpNet.Core (绝对不引用 LlrpSdk)    │
└───────────────────────────────────────────────────────────────────────────┘
```

---

## 3. 详细实施计划

### 步骤一：新建物理项目 `src/LlrpNet/LlrpNet.Protocol.Impinj`（已完成）
- 创建 `LlrpNet.Protocol.Impinj.csproj`，设置只引用 `LlrpNet.Protocol` 和 `LlrpNet.Core`。
- 将生成目录从 `LlrpSdk.Extensions.Impinj` 迁移至 `LlrpNet.Protocol.Impinj`:
  - `Codecs/`
  - `Enumerations/`
  - `Messages/`
  - `Parameters/`
  - `Registry/`
  - `ImpinjProtocolModule.cs`

### 步骤二：清理 `src/LlrpSdk.Extensions.Impinj`（已完成）
- 项目修改为引用 `LlrpNet.Protocol.Impinj` 和 `LlrpSdk`。
- 仅保留 `ImpinjReaderExtension.cs`、`ImpinjReaderConfiguration.cs`、`ImpinjReaderSettings.cs` 等高层 SDK 模型。

### 步骤三：更新工具链与生成脚本（已完成）
- 修改 [tools/Generate-ProtocolCode.ps1](file:///f:/Projects/LLRP/LLRPCSharp/tools/Generate-ProtocolCode.ps1)，将 Impinj 目标生成路径调整为 `src/LlrpNet/LlrpNet.Protocol.Impinj`。
- 更新 `definitions/README.md` 与生成命令文档。

### 步骤四：包边界（已完成基础拆分）
- `LlrpNet.Protocol.Impinj` 是独立协议包，可被只使用 Raw 协议的应用单独引用。
- `LlrpSdk.Extensions.Impinj` 依赖协议包并提供高层 SDK 映射。是否在发布流水线中将协议包作为高层包的传递依赖，由打包验证另行确认；源码项目不通过反向引用或复制 DLL 解决依赖。

---

## 4. 成本与风险评估

- **工程成本**：低-中（约 1-2 小时），纯物理结构重构，零算法/逻辑变更。
- **构建校验**：重构后执行全量 `dotnet build` 与 `dotnet test`，并增加协议包不引用 SDK 的独立回归测试。
- **长期收益**：建立了标准化的厂商扩展解耦范式，未来引入 Zebra、Alien 等其它厂商扩展时直接复用该物理分层。
