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

## Native YAML format

`llrp-definition.schema.yaml` 是 YAML 定义的完整字段示例；它是格式说明，不是待生成的协议。
新定义使用 `typeNumber` 与 `cardinality`，其中 cardinality 只能是 `1`、`0-1`、`1-N` 或 `0-N`。
YAML Loader 与 XML Importer 都输出同一个 `ProtocolDefinition`，后续 Validator 与 Generator 不因输入格式分支。

每个 YAML 文件应聚焦于单个协议版本或单个扩展。后续的版本 Delta 合成会显式传入依赖定义做符号解析，禁止静默合并重复的 wire identity。

可重复生成入口：

```powershell
dotnet run --project src/LlrpNet.ProtocolGenerator.Tool -- `
  --input definitions/my-extension.yaml --output src/MyExtension.Protocol `
  --root-namespace LlrpNet.Protocol --version-namespace V1_0_1 `
  --protocol-version 1 --dependency definitions/llrp-1.0.1.xml `
  --registry-module-name MyExtensionProtocolModule --codecs --verify
```

不带 `--verify` 时只写入缺失或内容变化的 `.g.cs` 文件，并使用 UTF-8 BOM；`--verify` 不写文件，适合 CI 检查生成资产已提交。
扩展定义以基础协议作为 `--dependency`，只生成扩展自身的类型；扩展应使用与基础协议相同的
`--root-namespace`，并指定不与标准模块冲突的 `--registry-module-name`。

在定义格式和导入器完成前，不创建占位的伪协议数据。

`imports/` 保存外部格式的原始输入；经过导入、规范化和校验后，由本项目维护的定义才进入顶层 YAML 或 `extensions/`。当前 Impinj 输入声明为 confidential/proprietary，在确认授权前由 `.gitignore` 排除。

当前输入文件的版本、SHA-256 与使用约束记录在 [`docs/protocol-source-inventory.md`](../docs/protocol-source-inventory.md)。
