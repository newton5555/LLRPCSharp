# 协议定义

这里保存项目维护的机器可读协议定义。官方或旧 LTK XML 先放入 `imports/`，经过导入和校验后形成项目的标准化 `ProtocolModel`；人工维护的版本差异与大型扩展优先使用 YAML。

计划结构：

```text
imports/xml/
├─ llrp-1.0.1/      LLRP 1.0.1 XML/XSD 导入源
└─ extensions/
   └─ impinj/       Impinj XML/XSD 导入源（本地资料）
extensions/         厂商与客户扩展定义
llrp-common.yaml
llrp-1.0.1.yaml
llrp-1.1.yaml
llrp-2.0-delta.yaml
```

在定义格式和导入器完成前，不创建占位的伪协议数据。

`imports/` 保存外部格式的原始输入；经过导入、规范化和校验后，由本项目维护的定义才进入顶层 YAML 或 `extensions/`。当前 Impinj 输入声明为 confidential/proprietary，在确认授权前由 `.gitignore` 排除。

当前输入文件的版本、SHA-256 与使用约束记录在 [`docs/protocol-source-inventory.md`](../docs/protocol-source-inventory.md)。
