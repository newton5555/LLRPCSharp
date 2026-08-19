# LLRP 协议扩展与定义准备指南

[English](protocol-extension-guide.md)

> **文档路径**：`docs/architecture/protocol-extension-guide.zh.md`  
> **适用对象**：项目开发者、集成商、客户技术团队  
> **核心原则**：XML 是固定历史基线；SDK 已内置 1.0.1 / 1.1 / 2.0 的版本化协议资产，
> 2.0 的 V2 Adapter 与协商基线已经接入，真实设备互操作仍单列验收；客户仅需为厂商扩展
> 及未来新协议编写 YAML 增量。

## 1. XML 基线与 YAML 增量

在 `LLRPCSharp` 中，协议定义文件的分工保持明确：历史既有 XML 作为固定基线，后续新增标准差异、厂商扩展和项目私有扩展优先使用 YAML 增量。

```text
┌────────────────────────────────────────────────────────────────────────┐
│                        1. 固定 XML 基础底座                            │
│  • 仅包含 1.0.1 标准读写器定义                                          │
│  • 1.0.1 Impinj 厂商扩展输入（本地保留）                                │
│  • 历史既有资产导入为标准化 ProtocolModel                               │
└──────────────────────────────────┬─────────────────────────────────────┘
                                   │
                                   ▼
┌────────────────────────────────────────────────────────────────────────┐
│                        2. 新场景统一使用 YAML 增量                      │
│  • SDK 提供的 LLRP 1.1 与 2.0 差量                                      │
│  • Zebra、Alien、国产读写器等新厂商扩展                                 │
│  • 项目私有报文、参数和未来新协议标准                                   │
└────────────────────────────────────────────────────────────────────────┘
```

## 2. 场景指南

### 场景 1：标准 1.0.1 / 1.1 / 2.0 读写器，或 Impinj 1.0.1 读写器

- **客户需要准备什么**：无需准备任何文件。
- **项目处理机制**：
  - 1.0.1 标准与 Impinj 1.0.1 扩展已基于本地 XML 预编译。当前 Impinj 输入为 LTK Definition Files 10.58.0，包含 4 条 Custom Message、104 个 Custom Parameter、49 个 Custom Enumeration。原始 XML 不随包分发；生成模型和 Codec 位于不依赖 SDK 的 `LlrpNet.Protocol.Impinj`，高层映射位于 `LlrpSdk.Extensions.Impinj`。
  - LLRP 1.1 已由 SDK 内置 `llrp-1.1.yaml` 并生成代码；LLRP 2.0 使用入库的
    `llrp-2.0-delta.yaml`，V2 Adapter 与协商路径已经接入，可作为协议/SDK 基线使用，
    但真实设备互操作仍待验收。

### 场景 2：接入第三方新厂商设备

- **客户需要准备什么**：
  1. 查阅目标厂商的 LLRP 扩展手册，获取官方 Vendor ID、私有参数/消息 Subtype 和字段定义。
  2. 在 `definitions/extensions/` 下新建增量 YAML，例如 `definitions/extensions/zebra.yaml`。

```yaml
# definitions/extensions/zebra.yaml
name: "ZebraExtension"
vendor_id: 10086
base_version: "1.0.1"

parameters:
  - name: "ZebraCustomFrequencySpec"
    subtype: 1
    type: "TLV"
    fields:
      - name: "ChannelHopRate"
        type: "U16"

parameter_extensions:
  - target_parameter: "ROReportSpec"
    allowed_custom_parameters:
      - "ZebraCustomFrequencySpec"
```

### 场景 3：客户项目私有扩展

- 在 `definitions/extensions/` 下新建项目 YAML，例如 `definitions/extensions/custom-project-a.yaml`。
- 无需修改 SDK 主体；使用代码生成工具生成独立扩展模块 DLL，例如 `LlrpSdk.Extensions.ProjectA`。

### 场景 4：未来更高标准协议版本

若未来出现 SDK 尚未内置支持的更高标准版本，可在 `definitions/` 下编写对应差量 YAML，例如 `definitions/llrp-3.0-delta.yaml`。

## 3. 工具链合成机制

无论增量 YAML 属于哪个场景，代码生成工具在构建期的合成流程一致。

![LLRPCSharp Extension Pipeline](../images/vendor_extension_infographic.png)

```text
[ 固定 XML 基线 ] (1.0.1 标准 / 本地 Impinj 输入)
         │
         ▼
[ 标准化 ProtocolModel ] <─── 合并 ─── [ YAML 增量 ]
         │
         ▼
[ 生成强类型 C# 模型、Codec 和 Registry Module ]
```

## 4. 速查表

| 场景需求 | 协议文件格式 | 维护主体 | 客户是否需要写 YAML？ |
|---|---|---|---|
| 1.0.1 标准设备 | XML | 本项目提供 | 否 |
| Impinj 1.0.1 设备 | 本地 XML | 本项目提供已编译扩展 DLL | 否 |
| LLRP 1.1 标准 | YAML 差量 | 本 SDK 提供 | 否 |
| LLRP 2.0 标准 | YAML 差量 | 本 SDK 提供，V2 Adapter 已接入 | 标准路径可用，真实设备互操作待验收 |
| 新厂商设备 | YAML 增量 | 客户或集成商 | 是 |
| 项目私有报文 | YAML 增量 | 客户项目团队 | 是 |
| 未来新协议标准 | YAML 差量 | 开发者或集成商 | 是 |

## 5. 远期规划：运行时动态 YAML 加载

> 当前状态：本节是远期规划，不是当前 SDK API。`WithDynamicYamlExtension(...)`、`DynamicCustomParameter` 和 `DynamicYamlCodec` 当前尚未实现；当前可用模式仍是静态生成扩展程序集并注册协议模块。

为满足“生产环境无需重新编译 DLL、直接投放 YAML 文件即可接入小众/私有设备”的需求，项目架构预留了动态解释加载方向。

```text
[ 外部 YAML 增量 ]
        │
        ▼
[ 运行时 ProtocolModel 字段元数据 ]
        │
        ▼
[ DynamicYamlCodec ]
        │
        ▼
[ 注册到 LlrpCodecRegistry 的 DynamicCustomParameter 字典模型 ]
```

| 维度 | 静态生成模式 | 动态加载模式 |
|---|---|---|
| 代码生成 | 编译期 C# 代码生成 | 零代码生成 |
| 部署流程 | 需要编译发布 DLL | 投放 YAML 文件 |
| 代码体验 | 强类型 C# 类和 IntelliSense | 字典或动态字段访问 |
| 运行性能 | 原生 C# Codec，性能最佳 | 动态解释，适合轻量场景 |
