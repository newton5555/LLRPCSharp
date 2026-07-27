# 草稿：LLRPCSharp、LTK .NET 与 Octane SDK 的关系

```text
LTK .NET
  = 标准 LLRP 与厂商 Custom Message / Parameter 的构造、收发、解析。

Octane SDK
  = 基于 LTK .NET 的 Impinj 厂商上层 SDK；向应用提供 ImpinjReader。

LLRPCSharp
  = 自己实现协议生成与收发解析能力，并在 LlrpReader 中提供跨厂商的基础业务能力；
    通过可选的 Impinj 扩展接入厂商协议，不依赖 LTK .NET 或 Octane SDK 运行时。
```

```text
LTK XML / Impinj Def ─> LTK .NET ─> Octane SDK ─> ImpinjReader ─> Impinj Reader

XML / YAML ─> LLRPCSharp Generator ─> Protocol + Impinj Extension ─> LlrpReader ─> 任意 LLRP Reader
```

| 维度 | LTK .NET | Impinj Octane SDK | 当前 LLRPCSharp |
|---|---|---|---|
| 定位 | 底层 LLRP 协议工具包 | Impinj 厂商上层 SDK | 跨厂商 LLRP 协议栈与 SDK |
| 主要入口 | Message / Parameter / Endpoint 类型 | `ImpinjReader` | `LlrpReader` |
| 标准与厂商报文 | 构造、收发、解析标准及 Custom Message / Parameter | 使用底层 LTK 能力 | `LlrpNet.Protocol` + 已激活的厂商扩展，负责收发与解析 |
| 业务能力 | 主要由应用自行组合 | Impinj 配置、盘点、型号与设备策略 | 连接、协议协商、获取/设置配置、开始/停止盘点、资源服务、扩展生命周期 |
| 厂商能力 | 协议定义层 | 内建 Impinj 设备知识 | `UseImpinj()` 激活协议扩展；型号默认值/Profile 等上层策略仍待补充 |
| 依赖关系 | Octane SDK 的协议基础 | 依赖/使用 LTK .NET | 独立实现；以 LTK XML 为定义输入和兼容性参照 |
| 适用对象 | 需要直接控制 LLRP 报文 | 只使用 Impinj Reader/Gateway | 需要标准 LLRP、跨厂商能力或可插拔厂商扩展 |
