# LLRP 协议扩展与定义准备指南：XML 基线与 YAML 增量

> **文档路径**：`docs/architecture/protocol-extension-guide.md`  
> **适用对象**：项目开发者、集成商、客户技术团队  
> **核心原则**：**XML 为固定历史基线；SDK 已内置支持 1.0.1 / 1.1，2.0 已提供 YAML 增量定义、待 Adapter 接入；客户仅需为“厂商扩展”及“未来新协议”编写 YAML 增量。**

---

## 1. 核心原则：XML 基线与 YAML 增量的分工

在 `LLRPCSharp` 项目中，协议定义文件的划分规则极其严格且明确：

```text
┌────────────────────────────────────────────────────────────────────────┐
│                        1. 固定 XML 基础底座 (Baseline)                 │
│  • 仅包含 1.0.1 标准读写器定义 (llrp-1.0.1.xml)                        │
│  • 1.0.1 Impinj 厂商扩展输入 (Impinjdef.xml；本地保留)                 │
│  • 定位：历史既有资产，直接导入使用，后续不再增加新的 XML。              │
└──────────────────────────────────┬─────────────────────────────────────┘
                                   │ 作为静态基线 (Base)
                                   ▼
┌────────────────────────────────────────────────────────────────────────┐
│                   2. 所有新场景统一使用 YAML 增量 (Delta)                │
│  • 本 SDK 提供：LLRP 1.1 与 2.0 标准 (SDK 将内置提供 YAML)         │
│  • 客户扩展场景：接入第三方新厂商读写器 (如 Zebra, Alien 等)           │
│  • 客户扩展场景：项目私有扩展报文与算法数据                             │
│  • 客户扩展场景：未来更高的协议标准版本 (如 LLRP 2.x+)                 │
│  • 定位：语法极简、无冗余标签，手写维护与 Code Review 的唯一标准。       │
└────────────────────────────────────────────────────────────────────────┘
```

---

## 2. 分场景操作与客户准备指南

当未来遇到不同的扩展需求时，客户与开发者需要准备的文件和操作如下：

### 场景 1：使用标准 1.0.1 / 1.1 读写器，或 Impinj 1.0.1 读写器
- **客户需要准备什么**：**无需准备任何文件**。
- **项目处理机制**：
  - 1.0.1 标准与 Impinj 1.0.1 扩展已基于本地 XML 预编译；当前 Impinj 输入为 LTK Definition Files 10.58.0（4 条 Custom Message、104 个 Custom Parameter、49 个 Custom Enumeration）。原始 XML 不随包分发，但其生成模型和 Codec 已包含在 `LlrpSdk.Extensions.Impinj`。
  - LLRP 1.1 已由 SDK 内置 `llrp-1.1.yaml` 并生成代码，客户开箱即用；LLRP 2.0 的 `llrp-2.0-delta.yaml` 已入库，待完成 V2 Adapter 与协商后才成为可用的 SDK 协议版本。

---

### 场景 2：接入第三方新厂商设备（如 Zebra, Alien 或国产读写器）
- **客户需要准备什么**：
  1. 查阅目标厂商的 LLRP 扩展手册，获取厂商的 **Vendor ID** 以及私有参数/消息的 **Subtype（子类型号）** 和字段定义。
  2. 在 `definitions/extensions/` 目录下新建一个增量 YAML 文件（例如 `definitions/extensions/zebra.yaml`）。
- **YAML 文件模板与示范**：

```yaml
# definitions/extensions/zebra.yaml
name: "ZebraExtension"
vendor_id: 10086                   # 厂商官方 Vendor ID
base_version: "1.0.1"              # 明确基于 1.0.1 基线扩展

# 1. 定义厂商私有参数
parameters:
  - name: "ZebraCustomFrequencySpec"
    subtype: 1                     # 厂商私有子类型号
    type: "TLV"
    fields:
      - name: "ChannelHopRate"
        type: "U16"

# 2. 将私有参数挂载到 LLRP 标准容器中（如盘点报告 ROReportSpec）
parameter_extensions:
  - target_parameter: "ROReportSpec"
    allowed_custom_parameters:
      - "ZebraCustomFrequencySpec"
```

---

### 场景 3：客户特定项目的私有定制扩展（特定私有报文/特定算法物理数据）
- **客户需要准备什么**：
  在 `definitions/extensions/` 目录下新建特定项目的 YAML 增量文件（例如 `definitions/extensions/custom-project-a.yaml`）。
- **处理机制**：
  无需修改 SDK 主体，使用代码生成工具针对该 YAML 生成独立的扩展模块 DLL（例如 `LlrpSdk.Extensions.ProjectA`），业务项目直接引用即可。

---

### 场景 4：未来更高的标准协议版本（如未来 LLRP 2.x+ / 3.0）
- **客户/开发者需要准备什么**：
  若未来出现 SDK 尚未内置支持的更高标准版本，可在 `definitions/` 目录下编写对应的差量 YAML 文件，例如 `definitions/llrp-3.0-delta.yaml`。

---

## 3. 工具链自动合成机制简述

无论增量 YAML 属于哪个场景，代码生成工具在构建期的合成流程完全一致：

```text
[ 固定 XML 基线 ] (1.0.1 标准 / Impinj；原始 Impinj XML 仅作本地生成输入)
         │
         ▼ (解析为内存 1.0.1 基础模型)
 [ 统一内存模型 (ProtocolModel) ] <─── 自动叠加 ─── [ 场景 2/3/4 的增量 YAML ]
         │
         ▼
 [ 一键生成 C# 强类型类与编解码 Codec ]
```

---

## 4. 总结速查表

| 场景需求 | 协议文件格式 | 维护主体与责任方 | 客户/集成商是否需要写 YAML？ |
|---|---|---|---|
| **1.0.1 标准设备** | **XML** | 本项目提供（已编译至 DLL） | ❌ **否**（运行时无需文件，开箱即用） |
| **Impinj 1.0.1 设备** | **本地 XML** | 本项目提供已编译的扩展 DLL；原始 XML 不随包分发 | ❌ **否**（引用 `LlrpSdk.Extensions.Impinj` 并调用 `UseImpinj()`） |
| **LLRP 1.1 标准** | **YAML (差量)** | 本 SDK 提供（编译至 DLL） | ❌ **否**（SDK 内置完成，开箱即用） |
| **LLRP 2.0 标准** | **YAML (差量)** | 本 SDK 提供（当前为待接入定义） | ❌ **否**（待 V2 Adapter 完成后开箱即用） |
| **新厂商设备 (Zebra/Alien等)** | **YAML (增量)** | 客户 / 厂商集成商 | ✅ **是**（按厂商手册写 YAML） |
| **客户项目私有定制报文** | **YAML (增量)** | 客户项目团队 | ✅ **是**（按项目需求写 YAML） |
| **未来未包含的新协议标准** | **YAML (差量)** | 开发者 / 集成商 | ✅ **是**（按新规范写 YAML） |

---

## 5. 远期扩展规划：运行时动态 YAML 解释加载机制

除了上述的静态编译模式外，为满足**“生产环境无需重新编译 DLL、直接放一个 YAML 文件即可动态接入小众/私有新设备”**的需求，项目架构预留了动态解释加载规划：

### 5.1 架构设计原理
```text
[ 外部轻量增量 YAML ]
        │
        ▼ (运行时加载解析)
[ 内存 ProtocolModel 字段元数据 ]
        │
        ▼
[ DynamicYamlCodec (通用解释型编解码器) ]
        │
        ▼ (动态注册到 LlrpCodecRegistry)
[ 运行时直接解包为 DynamicCustomParameter 字典模型 ]
```

### 5.2 核心技术组件
1. **`DynamicCustomParameter` 实体**：实现 `ILlrpParameter` 接口，内部使用 `IReadOnlyDictionary<string, object>` 存放动态解包出来的字段，无需预先生成 C# Class。
2. **`DynamicYamlCodec` 解释器**：依据 YAML 中的位宽与数据类型描述，直接调用 `LlrpNet.Core` 的 `MsbBitReader/Writer` 动态读写二进制帧。
3. **`LlrpReaderBuilder` API 扩展**：
   ```csharp
   await using LlrpReader reader = LlrpReader.CreateBuilder("192.0.2.10")
       .WithDynamicYamlExtension("definitions/extensions/vendor-dynamic.yaml") // 运行时动态装载 YAML
       .Build();
   ```

### 5.3 静态生成 vs 动态解释 对比

| 维度 | 静态生成模式 (主要模式) | 动态加载模式 (远期规划) |
|---|---|---|
| **代码生成** | 编译期 C# 代码生成 $\rightarrow$ 打包 DLL | 零代码生成，运行时直接解析 YAML |
| **部署流程** | 需编译发布 DLL 程序集 | 仅需在运行目录投放新 YAML 文件即可生效 |
| **代码体验** | 强类型 C# 类，带 IntelliSense 智能提示 | 动态字典/动态属性访问字段 |
| **运行性能** | 🚀 极致性能 (原生 C# 位运算) | ⚡ 动态解释执行 (性能略有下降，但足够轻量场景) |
