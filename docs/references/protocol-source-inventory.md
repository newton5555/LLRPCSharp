# 协议资料清单

> 核验日期：2026-07-24
> 哈希算法：SHA-256

本清单记录当前工作区中协议定义与标准原文的精确版本。资料由用户放入项目；在补齐权威下载 URL、下载日期和再分发许可前，标准 PDF 只作为本地核验材料，不随源码发布。

| 版本/用途 | 工作区路径 | SHA-256 | 当前用途 |
|---|---|---|---|
| LLRP 1.0.1 LTK 定义 | `definitions/imports/xml/llrp-1.0.1/llrp-1x0-def.xml` | `53D07A1A8493E6540F8CA8E1DFD934A4C548A035065036138618B88D0E3C18EC` | 主要机器可读导入源；与标准 PDF 交叉核验 |
| Impinj LTK Definition Files 10.58.0 | `definitions/imports/xml/extensions/impinj/Impinjdef.xml` | `5AE82816476153B4BB3CA52EE5269886F4F4D917C339FB881252C0D6ED4E0BD2` | 本地生成输入；4 条 Custom Message、104 个 Custom Parameter、49 个 Custom Enumeration；原始文件不入 Git |
| LLRP 1.0.1 XML Schema | `definitions/imports/xml/llrp-1.0.1/llrp-1x0.xsd` | `2B07B257848F934C102E5048A2F9748D87DE85F37A663C04D7800AA09E7B74DF` | XML 表示校验；不单独作为二进制位宽真值源 |
| LLRP 1.0.1 Standard（2007-08-13） | `references/standards/llrp-1.0.1/llrp_1_0_1-standard-20070813.pdf` | `113C91782926B289286914CFFD743C2D7D623CA5CE255A4D0B7FE08B404D7264` | 1.0.1 字段、约束和二进制布局核验 |
| LLRP 1.1 Standard（2010-10-13） | `references/standards/llrp-1.1/llrp_1_1-standard-20101013.pdf` | `23C7BDFD382B7F76918A712EF86E8867FE6DB2262B62B7B4CB529B1B82F3F47C` | 1.1 定义和版本差异核验 |
| LLRP 1.1 Conformance（2010-10-13） | `references/standards/llrp-1.1/llrp_1_1-conformance-20101013.pdf` | `A2A09874FF0708C59B028D1E1DB2906A487D09D79209EAE0853F453B94E2B25D` | 一致性测试设计 |
| LLRP 2.0 Standard（2021-01-27） | `references/standards/llrp-2.0/LLRP_standard_i2_r_2021-01-27.pdf` | `C886D011086737EEAED3DBEFBCB472F5A7D6AE70B19BC26DE825D38761BBB7B1` | 2.0 Delta、Gen2v2 和版本协商核验 |

## 已知约束

- Core definition XML 当前可作为 1.0.1 导入主输入；其文件头声明 Apache License 2.0。
- XSD 与 definition XML 在消息数量和部分位宽表达上存在差异，生成二进制 Codec 时以 definition XML 加标准 PDF 的交叉核验结果为准。
- 当前本地 Impinj 定义声明 confidential/proprietary，继续保持 `.gitignore` 排除；本清单的哈希仅用于本地输入可追溯与生成一致性，不能视为再分发许可。
- 当前输入由用户提供的 `LTK_Impinj_Definition_Files_10_58_0.zip` 核验；后续更新时必须记录来源版本、更新哈希并经过协议回归测试。
