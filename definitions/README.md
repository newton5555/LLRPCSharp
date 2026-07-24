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

每个 YAML 文件应聚焦于单个协议版本或单个扩展。版本 Delta 使用可重复的 `--base` 显式合成：
第一个基线是完整定义，后续基线按顺序作为 Delta 合并；再将输入 Delta 合成为一个完整的
`ProtocolDefinition` 后校验和生成。
新增项若与基线同名会被拒绝，避免静默改变 wire identity；替换既有定义必须使用后续加入的显式 override 格式。

可重复生成入口：

```powershell
dotnet run --project src/LlrpNet.ProtocolGenerator.Tool -- `
  --input definitions/my-extension.yaml --output src/MyExtension.Protocol `
  --root-namespace MyExtension.Protocol --version-namespace V1_0_1 `
  --protocol-version 1 --dependency definitions/imports/xml/llrp-1.0.1/llrp-1x0-def.xml `
  --dependency-root-namespace LlrpNet.Protocol `
  --registry-module-name MyExtensionProtocolModule --codecs --verify
```

2.0 以 1.0.1 XML 与 1.1 Delta 的合成模型为基线：

```powershell
dotnet run --project src/LlrpNet.ProtocolGenerator.Tool -- `
  --input definitions/llrp-2.0-delta.yaml `
  --base definitions/imports/xml/llrp-1.0.1/llrp-1x0-def.xml `
  --base definitions/llrp-1.1.yaml `
  --output src/LlrpNet.Protocol --root-namespace LlrpNet.Protocol `
  --version-namespace V2_0 --protocol-version 3 --registry-module-name Llrp20StandardModule --codecs
```

1.1 这类标准增量以 1.0.1 XML 为基线：

```powershell
dotnet run --project src/LlrpNet.ProtocolGenerator.Tool -- `
  --input definitions/llrp-1.1.yaml --base definitions/imports/xml/llrp-1.0.1/llrp-1.0.1.xml `
  --output src/LlrpNet.Protocol --root-namespace LlrpNet.Protocol `
  --version-namespace V1_1 --protocol-version 2 --registry-module-name Llrp11ProtocolModule --codecs
```

不带 `--verify` 时只写入缺失或内容变化的 `.g.cs` 文件，并使用 UTF-8 BOM；`--verify` 不写文件，适合 CI 检查生成资产已提交。
扩展定义以基础协议作为 `--dependency`，只生成扩展自身的类型；当扩展输出到独立程序集时，
`--root-namespace` 指向扩展程序集，`--dependency-root-namespace` 指向基础协议程序集的根命名空间。
扩展仍须指定不与标准模块冲突的 `--registry-module-name`。

项目内 Impinj 1.0.1 本地输入来自 LTK Impinj Definition Files 10.58.0；原始 XML 按 `.gitignore`
保持本地，生成的 `.g.cs` 进入 `LlrpSdk.Extensions.Impinj` 并与源码一同提交。该输入当前包含 4 条
Custom Message、104 个 Custom Parameter 和 49 个 Custom Enumeration：

```powershell
dotnet run --project src/LlrpNet.ProtocolGenerator.Tool -- `
  --input definitions/imports/xml/extensions/impinj/Impinjdef.xml `
  --dependency definitions/imports/xml/llrp-1.0.1/llrp-1x0-def.xml `
  --dependency-root-namespace LlrpNet.Protocol `
  --output src/LlrpSdk.Extensions.Impinj --root-namespace LlrpSdk.Extensions.Impinj `
  --version-namespace V1_0_1 --protocol-version 1 `
  --registry-module-name ImpinjProtocolModule --codecs --verify
```

在定义格式和导入器完成前，不创建占位的伪协议数据。

`imports/` 保存外部格式的原始输入；经过导入、规范化和校验后，由本项目维护的定义才进入顶层 YAML 或 `extensions/`。
当前 Impinj 输入声明为 confidential/proprietary，故原始 XML 继续由 `.gitignore` 排除；仅生成的 C# 资产进入 Git。

当前输入文件的版本、SHA-256 与使用约束记录在 [`docs/references/protocol-source-inventory.md`](../docs/references/protocol-source-inventory.md)。
